using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

public readonly record struct ExactQuerySignal(
    bool ExactIndexAvailable,
    bool HasMissingIndex,
    bool HasMissingTable,
    string? DegradedReason);

/// <summary>
/// Handles read/query operations against the database for search, symbols, and files.
/// 検索・シンボル・ファイル一覧などのDB読み取り操作を担当する。
/// </summary>
public partial class DbReader
{
    private readonly SqliteConnection _conn;
    private readonly bool _isReadOnly;
    private readonly HashSet<string> _fileColumns;
    private readonly HashSet<string> _symbolColumns;
    private readonly HashSet<string> _symbolIndexes;
    private readonly HashSet<string> _referenceIndexes;
    internal readonly bool _hasReferencesTable;
    internal readonly bool _hasIssuesTable;
    // #86: True when every symbols / symbol_references row has name_folded populated and
    // the Unicode fold path is safe to use for `--exact`. Legacy / partial-backfill DBs
    // read this as false and fall back to the ASCII-only `COLLATE NOCASE` path.
    // #86: name_folded 列が全行埋まっているか（fold 経路を使えるか）。
    internal readonly bool _foldReady;
    internal const string TestPathCondition = @"
        (
            lower(f.path) LIKE 'tests/%' OR
            lower(f.path) LIKE '%/tests/%' OR
            lower(f.path) LIKE 'test/%' OR
            lower(f.path) LIKE '%/test/%' OR
            lower(f.path) LIKE '%tests.%' OR
            lower(f.path) LIKE '%test.%' OR
            lower(f.path) LIKE '%_test.%' OR
            lower(f.path) LIKE '%.spec.%' OR
            lower(f.path) LIKE '%.test.%'
        )";
    private const string PathBucketOrder = @"
        CASE
            WHEN " + TestPathCondition + @" THEN 1
            WHEN lower(f.path) LIKE 'docs/%' OR lower(f.path) LIKE '%/docs/%' OR lower(f.path) LIKE 'readme%' OR lower(f.path) LIKE 'changelog%' OR lower(f.path) LIKE '%.md'
                THEN 2
            ELSE 0
        END";

    /// <summary>
    /// Visibility ranking: public symbols first, then protected, internal, private, unknown last.
    /// 可視性ランキング: public を最優先、次に protected、internal、private、不明は最後。
    /// </summary>
    internal string VisibilityOrder => $@"
        CASE lower({GetSymbolColumnSql("visibility")})
            WHEN 'public' THEN 0
            WHEN 'open' THEN 0
            WHEN 'pub' THEN 0
            WHEN 'export' THEN 0
            WHEN 'protected' THEN 1
            WHEN 'protected internal' THEN 1
            WHEN 'internal' THEN 2
            WHEN 'private protected' THEN 2
            WHEN 'private' THEN 3
            WHEN 'fileprivate' THEN 3
            ELSE 4
        END";
    private const string ExactSymbolMatchOrder = @"
        CASE
            WHEN EXISTS (
                SELECT 1
                FROM symbols sx
                WHERE sx.file_id = f.id
                  AND lower(sx.name) = lower(@rankingQuery)
            ) THEN 0
            ELSE 1
        END";
    private const string PrefixSymbolMatchOrder = @"
        CASE
            WHEN EXISTS (
                SELECT 1
                FROM symbols sx
                WHERE sx.file_id = f.id
                  AND lower(sx.name) LIKE lower(@rankingQueryPrefix) ESCAPE '\'
            ) THEN 0
            ELSE 1
        END";
    private const string PathTextMatchOrder = @"
        CASE
            WHEN instr(lower(f.path), lower(@rankingQuery)) > 0 THEN 0
            ELSE 1
        END";
    private const string ChunkTextMatchOrder = @"
        CASE
            WHEN instr(lower(c.content), lower(@rankingQuery)) > 0 THEN 0
            ELSE 1
        END";
    public DbReader(SqliteConnection connection, bool isReadOnly = false)
    {
        _conn = connection;
        _isReadOnly = isReadOnly;
        _fileColumns = LoadColumns("files");
        _symbolColumns = LoadColumns("symbols");
        _symbolIndexes = LoadIndexes("symbols");
        int userVersion;
        using (var v = _conn.CreateCommand())
        {
            v.CommandText = "PRAGMA user_version";
            var raw = v.ExecuteScalar();
            userVersion = raw is long l ? (int)l : (raw is int i ? i : 0);
        }
        _hasReferencesTable = HasTable("symbol_references") && (userVersion & DbContext.GraphReadyFlag) != 0;
        _hasIssuesTable = HasTable("file_issues") && (userVersion & DbContext.IssuesReadyFlag) != 0;
        _referenceIndexes = LoadIndexes("symbol_references");
        // #86/#97: require the FoldReady bit plus matching fold metadata before trusting
        // folded columns. version guards intentional NameFold changes; fingerprint guards
        // runtime ICU / invariant-casing drift across .NET upgrades. Missing metadata on
        // legacy / read-only DBs degrades safely to NOCASE until rebuild/backfill restamps current.
        // #86/#97: FoldReadyFlag に加え fold metadata 一致時のみ fold 経路を trusted とみなす。
        // version mismatch や fingerprint mismatch、未記録は NOCASE fallback に降格させる。
        var foldBitSet = (userVersion & DbContext.FoldReadyFlag) != 0
                         && _symbolColumns.Contains("name_folded");
        var storedFoldVersion = foldBitSet ? ParseFoldVersion(connection) : -1;
        var storedFoldFingerprint = foldBitSet ? ParseFoldFingerprint(connection) : null;
        _foldReady = foldBitSet
            && storedFoldVersion == NameFold.Version
            && string.Equals(storedFoldFingerprint, NameFold.Fingerprint(), StringComparison.Ordinal);
        // NOTE: row presence is intentionally NOT used as a fallback. A legacy DB or an
        // interrupted first-time / partial backfill can have one row while the rest of the
        // repo is untouched, which would flip trust on prematurely. Only an explicit
        // end-of-run readiness bit counts. Pre-upgrade DBs need a `cdidx index` re-run to
        // get stamped — degradation is safer than silent false-clean zeroes.
        // 行存在のフォールバックは意図的に採用しない。途中までのデータでも trusted に見えてしまうため。
    }

    private HashSet<string> LoadIndexes(string tableName)
    {
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!HasTable(tableName))
            return indexes;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA index_list('{tableName.Replace("'", "''")}')";
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            if (!reader.IsDBNull(1))
                indexes.Add(reader.GetString(1));
        }
        return indexes;
    }

    private static int ParseFoldVersion(SqliteConnection conn)
    {
        var raw = TryGetMetaString(conn, "fold_key_version");
        if (raw is string s && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return -1;
    }

    private static string? ParseFoldFingerprint(SqliteConnection conn)
    {
        return TryGetMetaString(conn, "fold_key_fingerprint");
    }

    private static string? TryGetMetaString(SqliteConnection conn, string key)
    {
        // Inline the codeindex_meta lookup to avoid creating a DbContext here.
        // codeindex_meta を直接引く（DbContext を new しない）。
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // codeindex_meta missing (legacy DB / read-only where migration skipped)
            // → treat as missing metadata so the reader falls back to NOCASE.
            return null;
        }
    }

    // Reference-count subquery that gracefully degrades to 0 when symbol_references is absent
    // (legacy read-only DBs where TryMigrateForRead could not create the table).
    // symbol_references が無いレガシー read-only DB では 0 にフォールバックする。
    private string ReferenceCountByFileSubquery =>
        _hasReferencesTable
            ? "(SELECT COUNT(*) FROM symbol_references WHERE file_id = f.id)"
            : "0";

    private bool HasTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    private bool HasSymbolIndex(string indexName) => _symbolIndexes.Contains(indexName);
    private bool HasReferenceIndex(string indexName) => _referenceIndexes.Contains(indexName);

    private bool SymbolNameExactIndexAvailable =>
        _foldReady
            ? HasSymbolIndex("idx_symbols_name_folded")
            : HasSymbolIndex("idx_symbols_name_nocase");

    private bool SymbolNameExactGraphIndexAvailable =>
        _foldReady
            ? HasReferenceIndex("idx_symbol_refs_symbol_name_folded")
            : HasReferenceIndex("idx_symbol_refs_name_nocase");

    private bool ContainerNameExactGraphIndexAvailable =>
        _foldReady
            ? HasReferenceIndex("idx_symbol_refs_container_name_folded")
            : HasReferenceIndex("idx_symbol_refs_container_nocase");

    private string BuildExactGraphIndexReason(IEnumerable<string> missingIndexes)
    {
        var missing = missingIndexes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0)
            return string.Empty;

        var scope = _isReadOnly ? "read-only legacy index" : "legacy index";
        return missing.Count == 1
            ? $"{missing[0]} missing on {scope}"
            : $"{string.Join(", ", missing)} missing on {scope}";
    }

    private ExactQuerySignal BuildExactGraphSignal(bool available, params string[] missingIndexes)
    {
        if (!_hasReferencesTable)
            return new(false, HasMissingIndex: false, HasMissingTable: true, "symbol_references table missing in this index");
        if (available)
            return new(true, HasMissingIndex: false, HasMissingTable: false, null);
        return new(false, HasMissingIndex: true, HasMissingTable: false, BuildExactGraphIndexReason(missingIndexes));
    }

    private ExactQuerySignal BuildExactSymbolSignal(bool available, params string[] missingIndexes)
    {
        if (available)
            return new(true, HasMissingIndex: false, HasMissingTable: false, null);
        return new(false, HasMissingIndex: true, HasMissingTable: false, BuildExactGraphIndexReason(missingIndexes));
    }

    private static ExactQuerySignal CombineExactSignals(params ExactQuerySignal?[] signals)
    {
        var participating = signals.Where(signal => signal.HasValue).Select(signal => signal!.Value).ToList();
        if (participating.Count == 0)
            return new(true, HasMissingIndex: false, HasMissingTable: false, null);

        if (participating.All(signal => signal.ExactIndexAvailable))
            return new(true, HasMissingIndex: false, HasMissingTable: false, null);

        var reasons = participating
            .Where(signal => !signal.ExactIndexAvailable && !string.IsNullOrWhiteSpace(signal.DegradedReason))
            .Select(signal => signal.DegradedReason!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new(
            ExactIndexAvailable: false,
            HasMissingIndex: participating.Any(signal => signal.HasMissingIndex),
            HasMissingTable: participating.Any(signal => signal.HasMissingTable),
            DegradedReason: reasons.Count == 0 ? null : string.Join("; ", reasons));
    }

    public ExactQuerySignal GetSymbolsExactQuerySignal() =>
        BuildExactSymbolSignal(SymbolNameExactIndexAvailable,
            _foldReady ? "idx_symbols_name_folded" : "idx_symbols_name_nocase");

    public ExactQuerySignal GetDefinitionExactQuerySignal() =>
        GetSymbolsExactQuerySignal();

    public ExactQuerySignal GetReferencesExactQuerySignal() =>
        BuildExactGraphSignal(SymbolNameExactGraphIndexAvailable,
            _foldReady ? "idx_symbol_refs_symbol_name_folded" : "idx_symbol_refs_name_nocase");

    public ExactQuerySignal GetCallersExactQuerySignal() =>
        BuildExactGraphSignal(SymbolNameExactGraphIndexAvailable,
            _foldReady ? "idx_symbol_refs_symbol_name_folded" : "idx_symbol_refs_name_nocase");

    public ExactQuerySignal GetCalleesExactQuerySignal() =>
        BuildExactGraphSignal(ContainerNameExactGraphIndexAvailable,
            _foldReady ? "idx_symbol_refs_container_name_folded" : "idx_symbol_refs_container_nocase");

    public ExactQuerySignal GetAnalyzeSymbolExactQuerySignal(bool includeGraphSignal = true)
    {
        return CombineExactSignals(
            GetDefinitionExactQuerySignal(),
            includeGraphSignal ? BuildAnalyzeGraphExactQuerySignal() : null);
    }

    internal bool HasGraphApplicableFiles(string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT 1
            FROM files f
            WHERE 1=1";
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphApplicableLang")}";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " LIMIT 1";

        cmd.CommandText = sql;
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return cmd.ExecuteScalar() != null;
    }

    private ExactQuerySignal BuildAnalyzeGraphExactQuerySignal()
    {
        if (!_hasReferencesTable)
            return new(false, HasMissingIndex: false, HasMissingTable: true, "symbol_references table missing in this index");

        var missing = new List<string>();
        if (!SymbolNameExactGraphIndexAvailable)
            missing.Add(_foldReady ? "idx_symbol_refs_symbol_name_folded" : "idx_symbol_refs_name_nocase");
        if (!ContainerNameExactGraphIndexAvailable)
            missing.Add(_foldReady ? "idx_symbol_refs_container_name_folded" : "idx_symbol_refs_container_nocase");

        return missing.Count == 0
            ? new(true, HasMissingIndex: false, HasMissingTable: false, null)
            : new(false, HasMissingIndex: true, HasMissingTable: false, BuildExactGraphIndexReason(missing));
    }

    internal static string EscapeLikeQuery(string input)
    {
        return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    private static string BuildGraphSupportedLanguagePredicate(SqliteCommand cmd, string fileAlias, string parameterPrefix)
    {
        var supportedLanguages = ReferenceExtractor.GetSupportedLanguages()
            .OrderBy(lang => lang, StringComparer.Ordinal)
            .ToList();
        if (supportedLanguages.Count == 0)
            return "1 = 0";

        var parameterNames = new List<string>(supportedLanguages.Count);
        for (int i = 0; i < supportedLanguages.Count; i++)
        {
            var parameterName = $"@{parameterPrefix}{i}";
            parameterNames.Add(parameterName);
            cmd.Parameters.AddWithValue(parameterName, supportedLanguages[i]);
        }

        return $"{fileAlias}.lang IN ({string.Join(", ", parameterNames)})";
    }

    /// <summary>
    /// List indexed files, optionally filtered by name pattern and language.
    /// インデックス済みファイルを一覧（名前パターン・言語でフィルタ可能）。
    /// </summary>
    public List<FileResult> ListFiles(string? query = null, int limit = 20, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null)
    {
        using var cmd = _conn.CreateCommand();

        var sql = $@"
            SELECT f.path, f.lang, f.size, f.lines,
                   (SELECT COUNT(*) FROM symbols WHERE file_id = f.id) AS symbol_count,
                   {ReferenceCountByFileSubquery} AS reference_count,
                   {GetFileColumnSql("checksum")} AS checksum,
                   {GetFileColumnSql("modified")} AS modified,
                   {GetFileColumnSql("indexed_at")} AS indexed_at
            FROM files f
            WHERE 1=1";

        if (query != null)
            sql += " AND f.path LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, f.path LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<FileResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new FileResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                Size = reader.GetInt64(2),
                Lines = reader.GetInt32(3),
                SymbolCount = reader.GetInt32(4),
                ReferenceCount = reader.GetInt32(5),
                Checksum = GetNullableString(reader, 6),
                Modified = GetNullableDateTime(reader, 7),
                IndexedAt = GetNullableDateTime(reader, 8),
            });
        }
        return results;
    }

    /// <summary>
    /// Search indexed references such as call sites.
    /// 呼び出し箇所などのインデックス済み参照を検索する。
    /// </summary>
    public List<ReferenceResult> SearchReferences(string? query = null, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return new List<ReferenceResult>();
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.symbol_name, r.reference_kind, r.line, r.column_number,
                   r.context, r.container_kind, r.container_name
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE 1=1";
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (query != null)
        {
            // --exact: Unicode-aware equality when FoldReady (#86), else ASCII COLLATE NOCASE.
            // Fold path: r.symbol_name_folded = @qFolded (indexed), query pre-folded in .NET.
            // Fallback: r.symbol_name = @q COLLATE NOCASE (indexed by idx_symbol_refs_name_nocase).
            // --exact: FoldReady なら Unicode 折り畳み経路、未 ready なら ASCII NOCASE へ fallback。
            if (exact && _foldReady)
                sql += " AND r.symbol_name_folded = @query";
            else if (exact)
                sql += " AND r.symbol_name = @query COLLATE NOCASE";
            else
                sql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        }
        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY CASE WHEN @preferExactCase = 1 AND r.symbol_name = @rawQuery THEN 0 ELSE 1 END, {PathBucketOrder}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, CASE WHEN lower(r.symbol_name) LIKE lower(@rankingQueryPrefix) ESCAPE '\\' THEN 0 ELSE 1 END, f.path, r.line LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
        {
            string queryParam;
            if (!exact)
                queryParam = $"%{EscapeLikeQuery(query)}%";
            else if (_foldReady)
                queryParam = NameFold.Fold(query) ?? query;
            else
                queryParam = query;
            cmd.Parameters.AddWithValue("@query", queryParam);
            cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
            cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(query.Trim())}%");
        }
        else
        {
            cmd.Parameters.AddWithValue("@rankingQuery", "");
            cmd.Parameters.AddWithValue("@rankingQueryPrefix", "%");
        }
        cmd.Parameters.AddWithValue("@preferExactCase", exact && query != null ? 1 : 0);
        cmd.Parameters.AddWithValue("@rawQuery", exact && query != null ? query : string.Empty);
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<ReferenceResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new ReferenceResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                SymbolName = reader.GetString(2),
                ReferenceKind = reader.GetString(3),
                Line = reader.GetInt32(4),
                Column = reader.GetInt32(5),
                Context = reader.GetString(6),
                ContainerKind = GetNullableString(reader, 7),
                ContainerName = GetNullableString(reader, 8),
            });
        }
        return results;
    }

    public int CountSearchReferences(string? query = null, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return 0;
        using var cmd = _conn.CreateCommand();

        var innerSql = @"
            SELECT 1
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE 1=1";
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (query != null)
        {
            if (exact && _foldReady)
                innerSql += " AND r.symbol_name_folded = @query";
            else if (exact)
                innerSql += " AND r.symbol_name = @query COLLATE NOCASE";
            else
                innerSql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        }
        if (referenceKind != null)
            innerSql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        innerSql += " LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({innerSql})";
        if (query != null)
        {
            var value = !exact
                ? $"%{EscapeLikeQuery(query)}%"
                : _foldReady
                    ? NameFold.Fold(query) ?? query
                    : query;
            cmd.Parameters.AddWithValue("@query", value);
        }
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    /// <summary>
    /// Find callers for a referenced symbol.
    /// 指定シンボルを呼び出している呼び出し元を探す。
    /// </summary>
    public List<CallerResult> GetCallers(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return new List<CallerResult>();
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += " AND r.reference_kind IN ('call', 'instantiate')";
        if (exact && _foldReady)
            sql += " AND r.symbol_name_folded = @query";
        else if (exact)
            sql += " AND r.symbol_name = @query COLLATE NOCASE";
        else
            sql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name ORDER BY CASE WHEN @preferExactCase = 1 AND r.symbol_name = @rawQuery THEN 0 ELSE 1 END, {PathBucketOrder}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, f.path, first_line LIMIT @limit";

        cmd.CommandText = sql;
        string callersQueryParam;
        if (!exact)
            callersQueryParam = $"%{EscapeLikeQuery(query)}%";
        else if (_foldReady)
            callersQueryParam = NameFold.Fold(query) ?? query;
        else
            callersQueryParam = query;
        cmd.Parameters.AddWithValue("@query", callersQueryParam);
        cmd.Parameters.AddWithValue("@preferExactCase", exact ? 1 : 0);
        cmd.Parameters.AddWithValue("@rawQuery", exact ? query : string.Empty);
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<CallerResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new CallerResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                CallerKind = GetNullableString(reader, 2),
                CallerName = GetNullableString(reader, 3),
                CalleeName = reader.GetString(4),
                FirstLine = reader.GetInt32(5),
                ReferenceCount = reader.GetInt32(6),
            });
        }
        return results;
    }

    public int CountCallers(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return 0;
        using var cmd = _conn.CreateCommand();

        var innerSql = @"
            SELECT 1
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            innerSql += " AND r.reference_kind = @referenceKind";
        else
            innerSql += " AND r.reference_kind IN ('call', 'instantiate')";
        if (exact && _foldReady)
            innerSql += " AND r.symbol_name_folded = @query";
        else if (exact)
            innerSql += " AND r.symbol_name = @query COLLATE NOCASE";
        else
            innerSql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        innerSql += " GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({innerSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    /// <summary>
    /// Find callees used by a caller/container symbol.
    /// 呼び出し元シンボルが使っている呼び出し先を探す。
    /// </summary>
    public List<CalleeResult> GetCallees(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return new List<CalleeResult>();
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   r.reference_kind, MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += " AND r.reference_kind IN ('call', 'instantiate')";
        if (exact && _foldReady)
            sql += " AND r.container_name_folded = @query";
        else if (exact)
            sql += " AND r.container_name = @query COLLATE NOCASE";
        else
            sql += " AND r.container_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.reference_kind ORDER BY CASE WHEN @preferExactCase = 1 AND r.container_name = @rawQuery THEN 0 ELSE 1 END, {PathBucketOrder}, CASE WHEN lower(r.container_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, f.path, first_line LIMIT @limit";

        cmd.CommandText = sql;
        string calleesQueryParam;
        if (!exact)
            calleesQueryParam = $"%{EscapeLikeQuery(query)}%";
        else if (_foldReady)
            calleesQueryParam = NameFold.Fold(query) ?? query;
        else
            calleesQueryParam = query;
        cmd.Parameters.AddWithValue("@query", calleesQueryParam);
        cmd.Parameters.AddWithValue("@preferExactCase", exact ? 1 : 0);
        cmd.Parameters.AddWithValue("@rawQuery", exact ? query : string.Empty);
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<CalleeResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new CalleeResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                CallerKind = GetNullableString(reader, 2),
                CallerName = GetNullableString(reader, 3),
                CalleeName = reader.GetString(4),
                ReferenceKind = reader.GetString(5),
                FirstLine = reader.GetInt32(6),
                ReferenceCount = reader.GetInt32(7),
            });
        }
        return results;
    }

    public int CountCallees(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return 0;
        using var cmd = _conn.CreateCommand();

        var innerSql = @"
            SELECT 1
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            innerSql += " AND r.reference_kind = @referenceKind";
        else
            innerSql += " AND r.reference_kind IN ('call', 'instantiate')";
        if (exact && _foldReady)
            innerSql += " AND r.container_name_folded = @query";
        else if (exact)
            innerSql += " AND r.container_name = @query COLLATE NOCASE";
        else
            innerSql += " AND r.container_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        innerSql += " GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.reference_kind LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({innerSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    /// <summary>
    /// Resolve a user-provided symbol name to its actual indexed casing via definition lookup.
    /// Prefers exact-case match, then falls back to case-insensitive. Only considers
    /// graph-supported languages. Returns the original input if no match is found.
    /// ユーザ入力のシンボル名を定義検索で実際のインデックス済みケーシングに解決する。
    /// 完全一致を優先し、なければ大文字小文字無視でフォールバック。graph 対応言語のみ対象。
    /// 見つからなければ元の入力をそのまま返す。
    /// </summary>
    private string ResolveSymbolName(string symbolName, string? lang)
    {
        // Exact lookup mirrors the leaf `--exact` readers: folded equality when FoldReady,
        // ASCII `COLLATE NOCASE` fallback on legacy / partial-backfill DBs.
        // No path/test filters — definitions outside caller scope must still be found.
        // Only considers graph-supported languages to avoid resolving to unsupported ones.
        // FoldReady なら folded equality、legacy DB では ASCII `COLLATE NOCASE` にフォールバック。
        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "resolveLang");
        var nameCondition = _foldReady
            ? "s.name_folded = @nameFolded"
            : "s.name = @name COLLATE NOCASE";
        cmd.CommandText = @"SELECT s.name FROM symbols s JOIN files f ON s.file_id = f.id
                            WHERE " + nameCondition + @"
                              AND " + supportedLangFilter + @"
                            ORDER BY CASE WHEN s.name = @name THEN 0 ELSE 1 END LIMIT 1";
        cmd.Parameters.AddWithValue("@name", symbolName);
        if (_foldReady)
            cmd.Parameters.AddWithValue("@nameFolded", NameFold.Fold(symbolName) ?? symbolName);
        using var reader = cmd.ExecuteTrackedReader();
        return reader.TrackedRead() ? reader.GetString(0) : symbolName;
    }

    /// <summary>
    /// Find exact-match callers for BFS traversal. Uses per-row case sensitivity
    /// and filters to graph-supported languages only (preventing stale edges from
    /// unsupported languages leaking into results on pre-upgrade databases).
    /// BFS 走査用の完全一致 caller 検索。行ごとの case sensitivity 判定、
    /// かつ graph 対応言語のみにフィルタ（アップグレード前 DB の古いエッジ漏れを防止）。
    /// </summary>
    private List<CallerResult> GetCallersExact(string symbolName, int limit, int offset = 0, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        if (!_hasReferencesTable) return new List<CallerResult>();
        using var cmd = _conn.CreateCommand();

        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "callerLang");

        // Exact caller matching mirrors the leaf `--exact` readers: folded equality when
        // FoldReady, ASCII `COLLATE NOCASE` fallback on legacy / partial-backfill DBs.
        // ResolveSymbolName() already normalizes the root symbol first, so this catches
        // caller rows whose stored callee casing differs from the resolved definition.
        // caller 側も leaf `--exact` と同じく FoldReady なら folded equality、legacy DB では
        // `COLLATE NOCASE` fallback。definition と caller 行の casing 差もここで吸収する。
        var nameCondition = _foldReady
            ? @"
              AND r.symbol_name_folded = @symbolNameFolded"
            : @"
              AND r.symbol_name = @symbolName COLLATE NOCASE";

        var sql = $@"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL
              AND r.reference_kind IN ('call', 'instantiate')
              AND {supportedLangFilter}
              {nameCondition}";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name ORDER BY {PathBucketOrder}, reference_count DESC, f.path, COALESCE(r.container_name, ''), COALESCE(r.container_kind, ''), r.symbol_name, first_line LIMIT @limit OFFSET @offset";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@symbolName", symbolName);
        if (_foldReady)
            cmd.Parameters.AddWithValue("@symbolNameFolded", NameFold.Fold(symbolName) ?? symbolName);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var results = new List<CallerResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new CallerResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                CallerKind = GetNullableString(reader, 2),
                CallerName = GetNullableString(reader, 3),
                CalleeName = reader.GetString(4),
                FirstLine = reader.GetInt32(5),
                ReferenceCount = reader.GetInt32(6),
            });
        }
        return results;
    }

    /// <summary>
    /// Compute transitive callers of a symbol using BFS with exact matching.
    /// Returns each unique caller in the call chain with its depth from the root symbol.
    /// Truncation is signaled via the Truncated property in results.
    /// 完全一致の BFS でシンボルの推移的呼び出し元を算出。各呼び出し元とルートシンボルからの深さを返す。
    /// 結果が切り詰められた場合は Truncated フラグで通知する。
    /// </summary>
    public (List<ImpactResult> Results, bool Truncated) GetTransitiveCallers(string symbolName, int maxDepth = 5, int limit = 50, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        // Resolve the symbol name through definitions first so case-mismatched queries
        // like "run" find the actual "Run" symbol. Falls back to user input if not found.
        // 定義を通じてシンボル名を解決し、"run" → "Run" のようなケース違いを補正する。
        // 見つからなければユーザ入力をフォールバック使用。
        var resolvedName = ResolveSymbolName(symbolName, lang);

        var results = new List<ImpactResult>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Symbol, int Depth)>();
        queue.Enqueue((resolvedName, 0));
        visited.Add(resolvedName);
        var truncated = false;
        // Safety cap to prevent infinite loops on pathological graphs / 病的グラフでの無限ループ防止
        const int maxFetchIterations = 1000;

        while (queue.Count > 0 && results.Count < limit)
        {
            var (currentSymbol, depth) = queue.Dequeue();
            if (depth > maxDepth)
                break;

            // Fetch callers in pages, filtering out already-visited before counting toward limit.
            // This prevents diamond graphs from hiding reachable callers behind visited duplicates.
            // ページングで caller を取得し、visited フィルタ後にカウント。
            // ダイヤモンド型グラフで到達可能な caller が visited 重複に隠れるのを防止。
            var needed = limit - results.Count;
            var offset = 0;
            const int pageSize = 200;
            var fetchIterations = 0;

            while (results.Count < limit && fetchIterations < maxFetchIterations)
            {
                fetchIterations++;
                var page = GetCallersExact(currentSymbol, pageSize, offset, lang, pathPatterns, excludePathPatterns, excludeTests);

                if (page.Count == 0)
                    break; // No more callers for this symbol / このシンボルの caller は尽きた

                foreach (var caller in page)
                {
                    if (results.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }

                    var callerName = caller.CallerName ?? "<top-level>";
                    var key = $"{caller.Path}:{callerName}";

                    if (!visited.Add(key))
                        continue;

                    results.Add(new ImpactResult
                    {
                        Path = caller.Path,
                        Lang = caller.Lang,
                        CallerKind = caller.CallerKind,
                        CallerName = caller.CallerName,
                        CalleeName = caller.CalleeName,
                        Depth = depth + 1,
                        FirstLine = caller.FirstLine,
                        ReferenceCount = caller.ReferenceCount,
                    });

                    if (caller.CallerName != null && depth + 1 < maxDepth)
                        queue.Enqueue((caller.CallerName, depth + 1));
                }

                offset += page.Count;

                // If this page was full, there might be more — continue paging
                // ページが満杯なら、まだある可能性 — ページングを継続
                if (page.Count < pageSize)
                    break;
            }

            // If fetch iteration cap was hit, mark as truncated / フェッチ反復上限に達した場合も truncated
            if (fetchIterations >= maxFetchIterations)
                truncated = true;
        }

        if (queue.Count > 0 && results.Count >= limit)
            truncated = true;

        return (results, truncated);
    }

    /// <summary>
    /// Reconstruct a file excerpt from indexed chunks.
    /// インデックス済みチャンクからファイル抜粋を再構成する。
    /// </summary>
    public FileExcerptResult? GetExcerpt(string path, int startLine, int endLine, int before = 0, int after = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (startLine <= 0)
            startLine = 1;
        if (endLine < startLine)
            endLine = startLine;
        if (before < 0)
            before = 0;
        if (after < 0)
            after = 0;

        using var fileCmd = _conn.CreateCommand();
        fileCmd.CommandText = "SELECT lang, lines FROM files WHERE path = @path";
        fileCmd.Parameters.AddWithValue("@path", path);

        using var fileReader = fileCmd.ExecuteTrackedReader();
        if (!fileReader.TrackedRead())
            return null;

        var lang = GetNullableString(fileReader, 0);
        var totalLines = fileReader.GetInt32(1);
        var requestedStart = Math.Max(1, startLine - before);
        var requestedEnd = Math.Min(totalLines, endLine + after);

        using var chunkCmd = _conn.CreateCommand();
        chunkCmd.CommandText = @"
            SELECT c.start_line, c.end_line, c.content
            FROM chunks c
            JOIN files f ON c.file_id = f.id
            WHERE f.path = @path
              AND c.end_line >= @startLine
              AND c.start_line <= @endLine
            ORDER BY c.start_line, c.chunk_index";
        chunkCmd.Parameters.AddWithValue("@path", path);
        chunkCmd.Parameters.AddWithValue("@startLine", requestedStart);
        chunkCmd.Parameters.AddWithValue("@endLine", requestedEnd);

        var lineMap = new SortedDictionary<int, string>();
        using var chunkReader = chunkCmd.ExecuteTrackedReader();
        while (chunkReader.TrackedRead())
        {
            var chunkStartLine = chunkReader.GetInt32(0);
            var chunkEndLine = chunkReader.GetInt32(1);
            var chunkLines = chunkReader.GetString(2).Split('\n');
            var lineCount = chunkEndLine - chunkStartLine + 1;

            for (int i = 0; i < chunkLines.Length && i < lineCount; i++)
            {
                var absoluteLine = chunkStartLine + i;
                if (absoluteLine < requestedStart || absoluteLine > requestedEnd)
                    continue;
                if (!lineMap.ContainsKey(absoluteLine))
                    lineMap[absoluteLine] = chunkLines[i];
            }
        }

        if (lineMap.Count == 0)
            return null;

        var selectedLines = Enumerable.Range(requestedStart, requestedEnd - requestedStart + 1)
            .Where(lineMap.ContainsKey)
            .ToList();

        if (selectedLines.Count == 0)
            return null;

        return new FileExcerptResult
        {
            Path = path,
            Lang = lang,
            StartLine = selectedLines[0],
            EndLine = selectedLines[^1],
            Content = string.Join("\n", selectedLines.Select(line => lineMap[line])),
        };
    }

    /// <summary>
    /// Get one indexed file by exact path.
    /// 完全一致パスでインデックス済みファイルを1件取得する。
    /// </summary>
    public FileResult? GetFileByPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT f.path, f.lang, f.size, f.lines,
                   (SELECT COUNT(*) FROM symbols WHERE file_id = f.id) AS symbol_count,
                   {ReferenceCountByFileSubquery} AS reference_count,
                   {GetFileColumnSql("checksum")} AS checksum,
                   {GetFileColumnSql("modified")} AS modified,
                   {GetFileColumnSql("indexed_at")} AS indexed_at
            FROM files f
            WHERE f.path = @path";
        cmd.Parameters.AddWithValue("@path", path);

        using var reader = cmd.ExecuteTrackedReader();
        if (!reader.TrackedRead())
            return null;

        return new FileResult
        {
            Path = reader.GetString(0),
            Lang = GetNullableString(reader, 1),
            Size = reader.GetInt64(2),
            Lines = reader.GetInt32(3),
            SymbolCount = reader.GetInt32(4),
            ReferenceCount = reader.GetInt32(5),
            Checksum = GetNullableString(reader, 6),
            Modified = GetNullableDateTime(reader, 7),
            IndexedAt = GetNullableDateTime(reader, 8),
        };
    }

    /// <summary>
    /// Get database statistics.
    /// データベースの統計情報を取得する。
    /// </summary>
    public StatusResult GetStatus()
    {
        var files = ExecuteScalar("SELECT COUNT(*) FROM files");
        var chunks = ExecuteScalar("SELECT COUNT(*) FROM chunks");
        var symbols = ExecuteScalar("SELECT COUNT(*) FROM symbols");
        var references = _hasReferencesTable ? ExecuteScalar("SELECT COUNT(*) FROM symbol_references") : 0L;
        var freshness = GetWorkspaceFreshness();

        // Language breakdown / 言語別内訳
        var langs = new Dictionary<string, long>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT lang, COUNT(*) FROM files WHERE lang IS NOT NULL GROUP BY lang ORDER BY COUNT(*) DESC";
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            langs[reader.GetString(0)] = reader.GetInt64(1);

        return new StatusResult
        {
            Files = files,
            Chunks = chunks,
            Symbols = symbols,
            References = references,
            IndexedAt = freshness.IndexedAt,
            LatestModified = freshness.LatestModified,
            Languages = langs,
            GraphTableAvailable = _hasReferencesTable,
            IssuesTableAvailable = _hasIssuesTable,
            FoldReady = _foldReady,
        };
    }

    /// <summary>
    /// Delegate to RepoMapBuilder for repo-level overview generation.
    /// RepoMapBuilderに委譲してリポジトリ俯瞰情報を生成する。
    /// </summary>
    public RepoMapResult GetRepoMap(int limit = 10, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        var builder = new RepoMapBuilder(_conn, _fileColumns, _hasReferencesTable);
        return builder.Build(limit, lang, pathPatterns, excludePathPatterns, excludeTests, GetWorkspaceFreshness);
    }

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Return a lightweight freshness hint for zero-result MCP responses.
    /// 0件MCPレスポンス向けの軽量な鮮度ヒントを返す。
    /// </summary>
    public FreshnessHintResult GetFreshnessHint()
    {
        var freshnessAvailable = _fileColumns.Contains("indexed_at");
        var fileCount = ExecuteScalar("SELECT COUNT(*) FROM files");
        var indexedAt = ExecuteNullableDateTime(
            freshnessAvailable ? "SELECT MAX(indexed_at) FROM files" : null);
        return new FreshnessHintResult
        {
            FileCount = fileCount,
            IndexedAt = indexedAt,
            FreshnessAvailable = freshnessAvailable,
            FreshnessDegradedReason = freshnessAvailable ? null : "files.indexed_at column missing in this index",
        };
    }

    private (DateTime? IndexedAt, DateTime? LatestModified) GetWorkspaceFreshness()
    {
        return (
            ExecuteNullableDateTime(_fileColumns.Contains("indexed_at") ? "SELECT MAX(indexed_at) FROM files" : null),
            ExecuteNullableDateTime(_fileColumns.Contains("modified") ? "SELECT MAX(modified) FROM files" : null)
        );
    }

    private DateTime? ExecuteNullableDateTime(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        if (value == null || value is DBNull)
            return null;

        return ParseDateTimeValue(value);
    }

    private static string BuildGraphSupportReason(string? graphLanguage, bool? graphSupported)
    {
        return ReferenceExtractor.BuildGraphSupportReason(graphLanguage, graphSupported)
            ?? "Call-graph support could not be determined because no language filter or matching definition was available.";
    }

    private HashSet<string> LoadColumns(string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            columns.Add(reader.GetString(1));

        return columns;
    }

    private string GetSymbolColumnSql(string columnName, string? fallbackSql = null)
    {
        if (_symbolColumns.Contains(columnName))
        {
            // Older binaries added the column but may have left existing rows with NULL.
            // Coalesce to the fallback so queries don't crash on legacy indexes.
            // 古いバイナリがカラムだけ追加して既存行を NULL のまま残しているケースに備え、
            // fallback と COALESCE してレガシーインデックスでクラッシュしないようにする。
            return fallbackSql != null
                ? $"COALESCE(s.{columnName}, {fallbackSql})"
                : $"s.{columnName}";
        }

        return fallbackSql ?? "NULL";
    }

    internal string GetFileColumnSql(string columnName, string? fallbackSql = null)
    {
        if (_fileColumns.Contains(columnName))
            return $"f.{columnName}";

        return fallbackSql ?? "NULL";
    }

    /// <summary>
    /// Compute file-level dependency edges: which files reference symbols defined in which other files.
    /// ファイル間の依存関係エッジを算出: どのファイルがどのファイルで定義されたシンボルを参照しているか。
    /// </summary>
    public List<FileDependencyResult> GetFileDependencies(int limit = 50, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool reverse = false)
    {
        if (!_hasReferencesTable) return new List<FileDependencyResult>();
        using var cmd = _conn.CreateCommand();
        // Use a subquery to find distinct (reference_file, definition_file, symbol) triples,
        // avoiding inflated counts from same-name symbols across multiple files.
        // サブクエリで (参照ファイル, 定義ファイル, シンボル) の重複を排除し、
        // 同名シンボルによるカウント膨張を防ぐ。
        var filterAlias = reverse ? "dst" : "src";
        var innerSql = @"
                SELECT DISTINCT src.path AS source_path, dst.path AS target_path,
                       r.symbol_name AS symbol_name
                FROM symbol_references r
                JOIN files src ON r.file_id = src.id
                JOIN symbols s ON r.symbol_name = s.name AND s.file_id != r.file_id
                JOIN files dst ON s.file_id = dst.id
                WHERE src.path != dst.path";
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "src", "depsLang")}";
        if (lang != null)
            innerSql += " AND src.lang = @lang";
        if (pathPatterns is { Count: > 0 })
        {
            // OR together multiple --path values / 複数の --path 値を OR で結合
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"{filterAlias}.path LIKE @pathPattern{i} ESCAPE '\\'");
            innerSql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                innerSql += $" AND {filterAlias}.path NOT LIKE @excludePath{i} ESCAPE '\\'";
        }
        if (excludeTests)
            innerSql += $" AND NOT {TestPathCondition.Replace("f.path", $"{filterAlias}.path")}";

        var sql = $@"
            SELECT source_path, target_path,
                   COUNT(*) AS reference_count,
                   GROUP_CONCAT(symbol_name) AS symbols
            FROM ({innerSql}) edges
            GROUP BY source_path, target_path ORDER BY reference_count DESC LIMIT @limit";

        cmd.CommandText = sql;
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (pathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < pathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@pathPattern{i}", $"%{EscapeLikeQuery(pathPatterns[i])}%");
        }
        if (excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@excludePath{i}", $"%{EscapeLikeQuery(excludePathPatterns[i])}%");
        }
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<FileDependencyResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new FileDependencyResult
            {
                SourcePath = reader.GetString(0),
                TargetPath = reader.GetString(1),
                ReferenceCount = reader.GetInt32(2),
                Symbols = reader.GetString(3),
            });
        }
        return results;
    }

    internal static void AppendPathFilters(ref string sql, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (pathPatterns != null && pathPatterns.Count > 0)
        {
            // Multiple --path values are OR'd together / 複数の --path 値は OR で結合する
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"f.path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }

        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND f.path NOT LIKE @excludePathPattern{i} ESCAPE '\\'";
        }

        if (excludeTests)
            sql += $" AND NOT {TestPathCondition}";
    }

    internal static void AddPathFilterParameters(SqliteCommand cmd, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns)
    {
        if (pathPatterns != null)
        {
            for (int i = 0; i < pathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@pathPattern{i}", $"%{EscapeLikeQuery(pathPatterns[i])}%");
        }

        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@excludePathPattern{i}", $"%{EscapeLikeQuery(excludePathPatterns[i])}%");
        }
    }

    internal static DateTime? GetNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        return ParseDateTimeValue(reader.GetValue(ordinal));
    }

    // Nullable-column reader helpers. Older DBs migrated in place may leave some
    // symbol columns nullable, so every read path has to guard. Centralizing this
    // avoids the #58/#60 class of bug where a single missed IsDBNull crashes queries.
    // 旧DBをその場移行すると一部カラムがNULL可のまま残るため、全読み取り経路でガードが必要。
    // #58/#60 のような IsDBNull 漏れによるクラッシュを構造的に防ぐためヘルパーに集約する。
    internal static string? GetNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    internal static int? GetNullableInt32(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    internal static int GetInt32OrFallback(SqliteDataReader reader, int ordinal, int fallbackOrdinal)
        => reader.IsDBNull(ordinal) ? reader.GetInt32(fallbackOrdinal) : reader.GetInt32(ordinal);

    private static DateTime? ParseDateTimeValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            string text when DateTime.TryParse(text, out var parsed) => parsed.Kind == DateTimeKind.Utc ? parsed : DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
            _ => null,
        };
    }

    /// <summary>
    /// Get all file validation issues from the index.
    /// インデックスから全ファイル検証問題を取得する。
    /// </summary>
    public List<Models.FileIssue> GetIssues(string? kind = null, IReadOnlyList<string>? pathPatterns = null)
    {
        if (!_hasIssuesTable) return new List<Models.FileIssue>();
        using var cmd = _conn.CreateCommand();
        var sql = @"
            SELECT f.path, i.kind, i.line, i.message
            FROM file_issues i
            JOIN files f ON i.file_id = f.id
            WHERE 1=1";
        if (kind != null)
            sql += " AND i.kind = @kind";
        if (pathPatterns is { Count: > 0 })
        {
            // OR multiple path filters / 複数パスフィルタを OR で結合
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"f.path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        sql += " ORDER BY f.path, i.line";

        cmd.CommandText = sql;
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (pathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < pathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@pathPattern{i}", $"%{EscapeLikeQuery(pathPatterns[i])}%");
        }

        var results = new List<Models.FileIssue>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new Models.FileIssue
            {
                Path = reader.GetString(0),
                Kind = reader.GetString(1),
                Line = reader.GetInt32(2),
                Message = reader.GetString(3),
            });
        }
        return results;
    }
}

// Result DTOs are in Models/QueryResults.cs / 結果DTOは Models/QueryResults.cs に分離
