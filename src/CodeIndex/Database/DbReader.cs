using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace CodeIndex.Database;

public readonly record struct ExactQuerySignal(
    bool ExactIndexAvailable,
    bool HasMissingIndex,
    bool HasMissingTable,
    string? DegradedReason);

public readonly record struct HotspotFamilySignal(
    bool Ready,
    bool Relevant,
    string? DegradedReason);

/// <summary>
/// Handles read/query operations against the database for search, symbols, and files.
/// 検索・シンボル・ファイル一覧などのDB読み取り操作を担当する。
/// </summary>
public partial class DbReader
{
    private static readonly Regex ImpactSignatureIdentifierRegex = new(@"[\p{L}_][\p{L}\p{Nd}_]*", RegexOptions.Compiled);
    private readonly SqliteConnection _conn;
    private readonly bool _isReadOnly;
    private readonly HashSet<string> _fileColumns;
    private readonly HashSet<string> _symbolColumns;
    private readonly HashSet<string> _symbolIndexes;
    private readonly HashSet<string> _referenceIndexes;
    private readonly HashSet<string> _indexedHotspotFamilyLanguages;
    internal readonly bool _hasReferencesTable;
    internal readonly bool _hasIssuesTable;
    internal readonly bool _hasChunksTable;
    // #86: True when every symbols / symbol_references row has name_folded populated and
    // the Unicode fold path is safe to use for `--exact`. Legacy / partial-backfill DBs
    // read this as false and fall back to the ASCII-only `COLLATE NOCASE` path.
    // #86: name_folded 列が全行埋まっているか（fold 経路を使えるか）。
    internal readonly bool _foldReady;
    internal readonly bool _csharpSymbolNameContractCurrent;
    // Tracks which languages have authoritative cross-file hotspot family semantics.
    // Mixed legacy/update states can therefore degrade only the affected language instead of
    // globally disabling families for unrelated marker types.
    // authoritative な hotspot family semantics を保持する言語集合。
    internal readonly HashSet<string> _hotspotFamilyReadyLanguages;
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
    private const string InvokeReferenceKindsSql = "('call', 'instantiate')";
    // Reference kinds that participate in the call-graph (callers/callees/hotspots). Metadata
    // kinds such as `attribute` / `annotation` are excluded so they do not inflate the graph
    // with non-call edges (issue #293).
    // call-graph (callers/callees/hotspots) に参加する reference kind。`attribute` / `annotation`
    // のようなメタデータ kind は非呼び出しエッジなのでここから除外する (issue #293)。
    internal const string CallGraphReferenceKindsSql = "('call', 'instantiate', 'subscribe')";

    // Reference kinds that represent compile-time type/member references (e.g. C# `nameof(X)`,
    // `typeof(T)`, Java `T.class`). They are intentionally excluded from default `callers` /
    // `callees` results because they are not invocation edges, but they remain discoverable
    // via `references` and explicit `--kind type_reference` queries. See issue #253.
    // コンパイル時の型・メンバ参照（C# の nameof/typeof、Java の `.class` 等）。
    // 呼び出しエッジではないため既定の callers/callees からは除外するが、
    // references や `--kind type_reference` 経由では引き続き参照できる。issue #253 参照。
    private const string NonInvocationReferenceKindsExclusion =
        " AND r.reference_kind != 'type_reference'";

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
    private static string GetLogicalReferenceKindSql(string referenceKindSql)
        => $"CASE WHEN {referenceKindSql} IN {InvokeReferenceKindsSql} THEN 'invoke' ELSE {referenceKindSql} END";

    private static string GetPreferredReferenceKindSql(string referenceKindSql)
        => $"CASE WHEN SUM(CASE WHEN {referenceKindSql} = 'instantiate' THEN 1 ELSE 0 END) > 0 THEN 'instantiate' ELSE MIN({referenceKindSql}) END";

    private static string GetGroupedCallerReferenceKindSql(string referenceKindSql)
        => $"CASE WHEN SUM(CASE WHEN {referenceKindSql} = 'instantiate' THEN 1 ELSE 0 END) > 0 THEN 'instantiate' " +
           $"WHEN SUM(CASE WHEN {referenceKindSql} = 'subscribe' THEN 1 ELSE 0 END) > 0 THEN 'subscribe' " +
           $"ELSE MIN({referenceKindSql}) END";

    private static string GetPathBucketOrderSql(string pathSql)
        => PathBucketOrder.Replace("f.path", pathSql, StringComparison.Ordinal);

    public DbReader(SqliteConnection connection, bool isReadOnly = false)
    {
        _conn = connection;
        DbContext.RegisterConnectionFunctions(_conn);
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
        _hasChunksTable = HasTable("chunks");
        _hasReferencesTable = HasTable("symbol_references") && (userVersion & DbContext.GraphReadyFlag) != 0;
        _hasIssuesTable = HasTable("file_issues") && (userVersion & DbContext.IssuesReadyFlag) != 0;
        _referenceIndexes = LoadIndexes("symbol_references");
        _indexedHotspotFamilyLanguages = LoadIndexedHotspotFamilyLanguages();
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
        _csharpSymbolNameContractCurrent = string.Equals(
            TryGetMetaString(_conn, DbContext.CSharpSymbolNameContractVersionMetaKey),
            DbContext.CSharpSymbolNameContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        _hotspotFamilyReadyLanguages = LoadHotspotFamilyReadyLanguages(connection);
        // NOTE: row presence is intentionally NOT used as a fallback. A legacy DB or an
        // interrupted first-time / partial backfill can have one row while the rest of the
        // repo is untouched, which would flip trust on prematurely. Only an explicit
        // end-of-run readiness bit counts. Pre-upgrade DBs need a `cdidx index` re-run to
        // get stamped — degradation is safer than silent false-clean zeroes.
        // 行存在のフォールバックは意図的に採用しない。途中までのデータでも trusted に見えてしまうため。
    }

    internal HotspotFamilySignal GetHotspotFamilySignal(string? lang)
    {
        if (lang != null)
        {
            if (!FileIndexer.SupportsHotspotFamilyMarkerLanguage(lang) || !_indexedHotspotFamilyLanguages.Contains(lang))
                return new HotspotFamilySignal(Ready: true, Relevant: false, DegradedReason: null);

            var ready = _hotspotFamilyReadyLanguages.Contains(lang);
            return new HotspotFamilySignal(
                Ready: ready,
                Relevant: true,
                DegradedReason: ready
                    ? null
                    : $"cross-file hotspot family grouping for '{lang}' is degraded; run `cdidx index <projectPath>` to restamp authoritative hotspot families.");
        }

        var relevantLanguages = _indexedHotspotFamilyLanguages
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        if (relevantLanguages.Count == 0)
            return new HotspotFamilySignal(Ready: true, Relevant: false, DegradedReason: null);

        var unreadyLanguages = relevantLanguages
            .Where(language => !_hotspotFamilyReadyLanguages.Contains(language))
            .ToList();
        if (unreadyLanguages.Count == 0)
            return new HotspotFamilySignal(Ready: true, Relevant: true, DegradedReason: null);

        return new HotspotFamilySignal(
            Ready: false,
            Relevant: true,
            DegradedReason: $"cross-file hotspot family grouping is degraded for: {string.Join(", ", unreadyLanguages)}; run `cdidx index <projectPath>` to restamp authoritative hotspot families.");
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

    private HashSet<string> LoadIndexedHotspotFamilyLanguages()
    {
        var langs = new HashSet<string>(StringComparer.Ordinal);
        var hotspotFamilyLangs = FileIndexer.GetHotspotFamilyMarkerLanguages();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT DISTINCT lang
            FROM files
            WHERE lang IN ({string.Join(",", hotspotFamilyLangs.Select((_, i) => $"@hfl{i}"))})";
        for (int i = 0; i < hotspotFamilyLangs.Count; i++)
            cmd.Parameters.AddWithValue($"@hfl{i}", hotspotFamilyLangs[i]);
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            if (!reader.IsDBNull(0))
                langs.Add(reader.GetString(0));
        }
        return langs;
    }

    private HashSet<string> LoadHotspotFamilyReadyLanguages(SqliteConnection conn)
    {
        var readyLangs = new HashSet<string>(StringComparer.Ordinal);
        if (!_symbolColumns.Contains("family_key") || !_symbolColumns.Contains("container_qualified_name"))
            return readyLangs;

        foreach (var lang in FileIndexer.GetHotspotFamilyMarkerLanguages())
        {
            var raw = TryGetMetaString(conn, DbContext.GetHotspotFamilyVersionMetaKey(lang));
            var fingerprint = TryGetMetaString(conn, DbContext.GetHotspotFamilyMarkerFingerprintMetaKey(lang));
            if (raw is string s
                && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version)
                && version == DbContext.HotspotFamilyVersion
                && !string.IsNullOrWhiteSpace(fingerprint))
            {
                readyLangs.Add(lang);
            }
        }

        return readyLangs;
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

    private ExactQuerySignal? GetCSharpCanonicalNameExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false,
        DateTime? since = null)
    {
        if (_csharpSymbolNameContractCurrent)
            return null;

        if (!ScopeMayIncludeCSharpFiles(lang, pathPatterns, excludePathPatterns, excludeTests, since))
            return null;

        return new(
            ExactIndexAvailable: false,
            HasMissingIndex: false,
            HasMissingTable: false,
            DegradedReason: "csharp_symbol_name_ready=false (canonical C# operator / conversion operator / indexer / verbatim identifier names are stale in this DB)");
    }

    private bool ScopeMayIncludeCSharpFiles(
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests,
        DateTime? since)
    {
        if (lang != null && !string.Equals(lang, "csharp", StringComparison.OrdinalIgnoreCase))
            return false;

        using var cmd = _conn.CreateCommand();
        var sql = "SELECT 1 FROM files f WHERE f.lang = 'csharp'";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " LIMIT 1";

        cmd.CommandText = sql;
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        return cmd.ExecuteScalar() != null;
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

    public ExactQuerySignal GetSymbolsExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false,
        DateTime? since = null)
        => CombineExactSignals(
            BuildExactSymbolSignal(SymbolNameExactIndexAvailable,
                _foldReady ? "idx_symbols_name_folded" : "idx_symbols_name_nocase"),
            GetCSharpCanonicalNameExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests, since));

    public ExactQuerySignal GetDefinitionExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false,
        DateTime? since = null)
        => GetSymbolsExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests, since);

    public ExactQuerySignal GetReferencesExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false)
        => CombineExactSignals(
            BuildExactGraphSignal(SymbolNameExactGraphIndexAvailable,
                _foldReady ? "idx_symbol_refs_symbol_name_folded" : "idx_symbol_refs_name_nocase"),
            GetCSharpCanonicalNameExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests));

    public ExactQuerySignal GetCallersExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false)
        => CombineExactSignals(
            BuildExactGraphSignal(SymbolNameExactGraphIndexAvailable,
                _foldReady ? "idx_symbol_refs_symbol_name_folded" : "idx_symbol_refs_name_nocase"),
            GetCSharpCanonicalNameExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests));

    public ExactQuerySignal GetCalleesExactQuerySignal(
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false)
        => CombineExactSignals(
            BuildExactGraphSignal(ContainerNameExactGraphIndexAvailable,
                _foldReady ? "idx_symbol_refs_container_name_folded" : "idx_symbol_refs_container_nocase"),
            GetCSharpCanonicalNameExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests));

    public ExactQuerySignal GetAnalyzeSymbolExactQuerySignal(
        bool includeGraphSignal = true,
        string? lang = null,
        IReadOnlyList<string>? pathPatterns = null,
        IReadOnlyList<string>? excludePathPatterns = null,
        bool excludeTests = false)
    {
        return CombineExactSignals(
            GetDefinitionExactQuerySignal(lang, pathPatterns, excludePathPatterns, excludeTests),
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

    private static QueryCountResult ExecuteCountSummary(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteTrackedReader();
        return reader.TrackedRead()
            ? new QueryCountResult(reader.GetInt32(0), reader.GetInt32(1))
            : new QueryCountResult(0, 0);
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
            cmd.Parameters.AddWithValue("@since", since.Value);
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

    public QueryCountResult CountListFiles(string? query = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT COUNT(*), COUNT(DISTINCT path)
            FROM (
                SELECT f.path AS path
                FROM files f
                WHERE 1=1";

        if (query != null)
            sql += " AND f.path LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += ")";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return ExecuteCountSummary(cmd);
    }

    /// <summary>
    /// Search indexed references such as call sites.
    /// 呼び出し箇所などのインデックス済み参照を検索する。
    /// </summary>
    public List<ReferenceResult> SearchReferences(string? query = null, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false, int maxLineWidth = LineWidthFormatter.DefaultMaxLineWidth)
    {
        if (!_hasReferencesTable) return new List<ReferenceResult>();
        maxLineWidth = LineWidthFormatter.ClampMaxLineWidth(maxLineWidth);
        using var cmd = _conn.CreateCommand();
        var sql = referenceKind == null
            ? $@"
            WITH logical_references AS (
                SELECT f.path, f.lang, r.symbol_name,
                       {GetPreferredReferenceKindSql("r.reference_kind")} AS reference_kind,
                       r.line, r.column_number,
                       MIN(r.context) AS context,
                       CASE WHEN COUNT(DISTINCT COALESCE(r.container_kind, '')) = 1 THEN MIN(r.container_kind) ELSE NULL END AS container_kind,
                       CASE WHEN COUNT(DISTINCT COALESCE(r.container_name, '')) = 1 THEN MIN(r.container_name) ELSE NULL END AS container_name
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE 1=1
                  AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}"
            : @"
            SELECT f.path, f.lang, r.symbol_name, r.reference_kind, r.line, r.column_number,
                   r.context, r.container_kind, r.container_name
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE 1=1";

        if (referenceKind != null)
            sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        var referencesSuffixAlias = ComputeCSharpAttributeSuffixAlias(query, lang, referenceKind);
        // When the alias fires without an explicit `lang` / `--kind` scope we still need
        // to keep it from bleeding into non-C# rows or non-attribute rows. The SQL guard
        // clamps the alias disjunct to `f.lang = 'csharp' AND r.reference_kind = 'attribute'`
        // so unscoped `references FooAttribute` only picks up real C# attribute sites.
        // alias が `--lang` / `--kind` スコープなしで発火するときも、C# 以外の行や
        // attribute 以外の行を拾わないように、SQL 側で `f.lang = 'csharp' AND
        // r.reference_kind = 'attribute'` に限定する。
        var referencesAliasScope = referencesSuffixAlias != null
            ? " AND f.lang = 'csharp' AND r.reference_kind = 'attribute'"
            : string.Empty;
        const string sqlLeafReferenceScope = " AND f.lang = 'sql'";
        if (query != null)
        {
            // --exact: Unicode-aware equality when FoldReady (#86), else ASCII COLLATE NOCASE.
            // Fold path: r.symbol_name_folded = @qFolded (indexed), query pre-folded in .NET.
            // Fallback: r.symbol_name = @q COLLATE NOCASE (indexed by idx_symbol_refs_name_nocase).
            // When the query ends with C# attribute suffix `Attribute`, also OR against the
            // suffix-stripped alias so `references FooAttribute --exact` reaches the idiomatic
            // `[Foo]` reference site stored with `symbol_name = "Foo"`. In substring mode we
            // still LIKE-match `%FooAttribute%` and add only the exact stripped alias to avoid
            // overmatching unrelated names (e.g. `FooAuditLog`) that share the stripped prefix.
            // The alias disjunct is scoped to C# attribute rows to avoid false positives.
            // --exact: FoldReady なら Unicode 折り畳み経路、未 ready なら ASCII NOCASE へ fallback。
            // C# の `Attribute` suffix が付いたクエリは、suffix を外した別名とも照合する。
            // 部分一致モードでは `%FooAttribute%` をそのまま使い、別名側は exact 照合だけを OR
            // することで `FooAuditLog` など無関係な名前を巻き込まないようにする。
            // 別名節は C# の attribute 行に限定し、誤一致を避ける。
            if (exact && _foldReady)
                sql += referencesSuffixAlias != null
                    ? $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @queryAttributeAlias{referencesAliasScope}) OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafReferenceScope}))"
                    : $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafReferenceScope}))";
            else if (exact)
                sql += referencesSuffixAlias != null
                    ? $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{referencesAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafReferenceScope}))"
                    : $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafReferenceScope}))";
            else
                sql += referencesSuffixAlias != null
                    ? $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{referencesAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafReferenceScope}))"
                    : $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafReferenceScope}))";
        }
        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
        {
            sql += @"
                GROUP BY f.path, f.lang, r.file_id, r.symbol_name, r.line, r.column_number, " + GetLogicalReferenceKindSql("r.reference_kind") + @"
            )
            SELECT path, lang, symbol_name, reference_kind, line, column_number,
                   context, container_kind, container_name
            FROM logical_references r";
        }
        sql += $" ORDER BY CASE WHEN @preferExactCase = 1 AND r.symbol_name = @rawQuery THEN 0 ELSE 1 END, {(referenceKind == null ? GetPathBucketOrderSql("r.path") : PathBucketOrder)}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, CASE WHEN lower(r.symbol_name) LIKE lower(@rankingQueryPrefix) ESCAPE '\\' THEN 0 ELSE 1 END, {(referenceKind == null ? "r.path" : "f.path")}, r.line LIMIT @limit";

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
            cmd.Parameters.AddWithValue("@aliasQuery", query);
            cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
            if (referencesSuffixAlias != null)
            {
                // Exact-match alias value is used both in --exact paths (folded / NOCASE)
                // and in the substring path (COLLATE NOCASE exact OR to bypass LIKE noise).
                // In the folded --exact branch the alias is pre-folded; the substring branch
                // uses the raw stripped form because the OR clause is a literal `=` comparison.
                // exact 用の別名値は --exact 経路（folded / NOCASE）と部分一致経路（LIKE ノイズを
                // 避けるための COLLATE NOCASE の等値 OR）の両方で使う。folded 経路だけは事前に
                // 折りたたみ、部分一致経路は生の stripped 形をそのまま使う。
                var aliasParam = exact && _foldReady
                    ? NameFold.Fold(referencesSuffixAlias) ?? referencesSuffixAlias
                    : referencesSuffixAlias;
                cmd.Parameters.AddWithValue("@queryAttributeAlias", aliasParam);
            }
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
            var column = reader.GetInt32(5);
            var context = reader.GetString(6);
            var clampedContext = LineWidthFormatter.ClampLine(context, maxLineWidth, column, query?.Length ?? 1);
            results.Add(new ReferenceResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                SymbolName = reader.GetString(2),
                ReferenceKind = reader.GetString(3),
                Line = reader.GetInt32(4),
                Column = column,
                Context = clampedContext.Text,
                ContextTruncated = clampedContext.Truncated,
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

        var countSuffixAlias = ComputeCSharpAttributeSuffixAlias(query, lang, referenceKind);
        var countAliasScope = countSuffixAlias != null
            ? " AND f.lang = 'csharp' AND r.reference_kind = 'attribute'"
            : string.Empty;
        const string sqlLeafCountScope = " AND f.lang = 'sql'";
        if (query != null)
        {
            if (exact && _foldReady)
                innerSql += countSuffixAlias != null
                    ? $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @queryAttributeAlias{countAliasScope}) OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafCountScope}))"
                    : $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafCountScope}))";
            else if (exact)
                innerSql += countSuffixAlias != null
                    ? $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{countAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafCountScope}))"
                    : $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafCountScope}))";
            else
                innerSql += countSuffixAlias != null
                    ? $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{countAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafCountScope}))"
                    : $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafCountScope}))";
        }
        if (referenceKind != null)
            innerSql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            innerSql += $" GROUP BY r.file_id, r.symbol_name, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
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
            cmd.Parameters.AddWithValue("@aliasQuery", query);
            cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
            if (countSuffixAlias != null)
            {
                var aliasParam = exact && _foldReady
                    ? NameFold.Fold(countSuffixAlias) ?? countSuffixAlias
                    : countSuffixAlias;
                cmd.Parameters.AddWithValue("@queryAttributeAlias", aliasParam);
            }
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

    public QueryCountResult CountSearchReferencesTotal(string? query = null, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable)
            return new QueryCountResult(0, 0);

        using var cmd = _conn.CreateCommand();

        var innerSql = @"
            SELECT path
            FROM (
                SELECT f.path AS path, r.file_id, r.symbol_name, r.line, r.column_number, " + GetLogicalReferenceKindSql("r.reference_kind") + @" AS logical_reference_kind
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE 1=1";
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        var totalSuffixAlias = ComputeCSharpAttributeSuffixAlias(query, lang, referenceKind);
        var totalAliasScope = totalSuffixAlias != null
            ? " AND f.lang = 'csharp' AND r.reference_kind = 'attribute'"
            : string.Empty;
        const string sqlLeafTotalScope = " AND f.lang = 'sql'";
        if (query != null)
        {
            if (exact && _foldReady)
                innerSql += totalSuffixAlias != null
                    ? $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @queryAttributeAlias{totalAliasScope}) OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafTotalScope}))"
                    : $" AND (r.symbol_name_folded = @query OR (r.symbol_name_folded = @aliasQueryLeafFolded{sqlLeafTotalScope}))";
            else if (exact)
                innerSql += totalSuffixAlias != null
                    ? $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{totalAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafTotalScope}))"
                    : $" AND (r.symbol_name = @query COLLATE NOCASE OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafTotalScope}))";
            else
                innerSql += totalSuffixAlias != null
                    ? $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = @queryAttributeAlias COLLATE NOCASE{totalAliasScope}) OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafTotalScope}))"
                    : $" AND (r.symbol_name LIKE @query ESCAPE '\\' OR (r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE{sqlLeafTotalScope}))";
        }
        if (referenceKind != null)
            innerSql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            innerSql += $" GROUP BY f.path, r.file_id, r.symbol_name, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
        innerSql += ")";

        cmd.CommandText = $"SELECT COUNT(*), COUNT(DISTINCT path) FROM ({innerSql})";
        if (query != null)
        {
            var value = !exact
                ? $"%{EscapeLikeQuery(query)}%"
                : _foldReady
                    ? NameFold.Fold(query) ?? query
                    : query;
            cmd.Parameters.AddWithValue("@query", value);
            cmd.Parameters.AddWithValue("@aliasQuery", query);
            cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
            if (totalSuffixAlias != null)
            {
                var aliasParam = exact && _foldReady
                    ? NameFold.Fold(totalSuffixAlias) ?? totalSuffixAlias
                    : totalSuffixAlias;
                cmd.Parameters.AddWithValue("@queryAttributeAlias", aliasParam);
            }
        }
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return ExecuteCountSummary(cmd);
    }

    /// <summary>
    /// Find callers for a referenced symbol.
    /// 指定シンボルを呼び出している呼び出し元を探す。
    /// </summary>
    public List<CallerResult> GetCallers(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return new List<CallerResult>();
        using var cmd = _conn.CreateCommand();

        var sql = referenceKind == null
            ? $@"
            WITH logical_references AS (
                SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                       {GetGroupedCallerReferenceKindSql("r.reference_kind")} AS reference_kind,
                       r.line
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE r.container_name IS NOT NULL
                  AND r.reference_kind IN {CallGraphReferenceKindsSql}
                  AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}"
            : @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   r.reference_kind, MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        if (referenceKind != null)
            sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += NonInvocationReferenceKindsExclusion;
        if (exact && _foldReady)
            sql += " AND (r.symbol_name_folded = @query OR (f.lang = 'sql' AND r.symbol_name_folded = @aliasQueryLeafFolded))";
        else if (exact)
            sql += " AND (r.symbol_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        else
            sql += " AND (r.symbol_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
        {
            sql += @"
                GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number
            )
            SELECT path, lang, container_kind, container_name, symbol_name,
                   " + GetGroupedCallerReferenceKindSql("r.reference_kind") + @" AS reference_kind,
                   MIN(line) AS first_line, COUNT(*) AS reference_count
            FROM logical_references r
            GROUP BY path, lang, container_kind, container_name, symbol_name";
        }
        else
        {
            sql += " GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name";
        }
        sql += $" ORDER BY CASE WHEN @preferExactCase = 1 AND r.symbol_name = @rawQuery THEN 0 ELSE 1 END, {(referenceKind == null ? GetPathBucketOrderSql("r.path") : PathBucketOrder)}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, {(referenceKind == null ? "r.path" : "f.path")}, first_line LIMIT @limit";

        cmd.CommandText = sql;
        string callersQueryParam;
        if (!exact)
            callersQueryParam = $"%{EscapeLikeQuery(query)}%";
        else if (_foldReady)
            callersQueryParam = NameFold.Fold(query) ?? query;
        else
            callersQueryParam = query;
        cmd.Parameters.AddWithValue("@query", callersQueryParam);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
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
                ReferenceKind = reader.GetString(5),
                FirstLine = reader.GetInt32(6),
                ReferenceCount = reader.GetInt32(7),
            });
        }
        return results;
    }

    public int CountCallers(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return 0;
        using var cmd = _conn.CreateCommand();
        var groupedSql = @"
            SELECT path, lang, container_kind, container_name, symbol_name
            FROM (
                SELECT f.path AS path, f.lang AS lang, r.container_kind AS container_kind,
                       r.container_name AS container_name, r.symbol_name AS symbol_name
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        groupedSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            groupedSql += " AND r.reference_kind = @referenceKind";
        else
            groupedSql += $" AND r.reference_kind IN {CallGraphReferenceKindsSql}";
        if (exact && _foldReady)
            groupedSql += " AND (r.symbol_name_folded = @query OR (f.lang = 'sql' AND r.symbol_name_folded = @aliasQueryLeafFolded))";
        else if (exact)
            groupedSql += " AND (r.symbol_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        else
            groupedSql += " AND (r.symbol_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        if (lang != null)
            groupedSql += " AND f.lang = @lang";
        AppendPathFilters(ref groupedSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            groupedSql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
        groupedSql += " ) grouped_call_sites GROUP BY path, lang, container_kind, container_name, symbol_name LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({groupedSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    public QueryCountResult CountCallersTotal(string query, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable)
            return new QueryCountResult(0, 0);

        using var cmd = _conn.CreateCommand();
        var groupedSql = @"
            SELECT path
            FROM (
                SELECT f.path AS path, f.lang AS lang, r.container_kind AS container_kind,
                       r.container_name AS container_name, r.symbol_name AS symbol_name
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE r.container_name IS NOT NULL";
        groupedSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            groupedSql += " AND r.reference_kind = @referenceKind";
        else
            groupedSql += $" AND r.reference_kind IN {CallGraphReferenceKindsSql}";
        if (exact && _foldReady)
            groupedSql += " AND (r.symbol_name_folded = @query OR (f.lang = 'sql' AND r.symbol_name_folded = @aliasQueryLeafFolded))";
        else if (exact)
            groupedSql += " AND (r.symbol_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        else
            groupedSql += " AND (r.symbol_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@aliasQuery) COLLATE NOCASE))";
        if (lang != null)
            groupedSql += " AND f.lang = @lang";
        AppendPathFilters(ref groupedSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            groupedSql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
        groupedSql += " ) grouped_call_sites GROUP BY path, lang, container_kind, container_name, symbol_name";

        cmd.CommandText = $"SELECT COUNT(*), COUNT(DISTINCT path) FROM ({groupedSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return ExecuteCountSummary(cmd);
    }

    /// <summary>
    /// Find callees used by a caller/container symbol.
    /// 呼び出し元シンボルが使っている呼び出し先を探す。
    /// </summary>
    public List<CalleeResult> GetCallees(string query, int limit = 20, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable) return new List<CalleeResult>();
        using var cmd = _conn.CreateCommand();

        var sql = referenceKind == null
            ? $@"
            WITH logical_references AS (
                SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                       {GetPreferredReferenceKindSql("r.reference_kind")} AS reference_kind,
                       r.line
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE r.container_name IS NOT NULL
                  AND r.reference_kind IN {CallGraphReferenceKindsSql}
                  AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}"
            : @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   r.reference_kind, MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        if (referenceKind != null)
            sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += NonInvocationReferenceKindsExclusion;
        if (exact && _foldReady)
            sql += " AND (r.container_name_folded = @query OR (f.lang = 'sql' AND sql_leaf_name_folded(r.container_name) = @aliasQueryLeafFolded))";
        else if (exact)
            sql += " AND (r.container_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        else
            sql += " AND (r.container_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
        {
            sql += @"
                GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number
            )
            SELECT path, lang, container_kind, container_name, symbol_name,
                   reference_kind, MIN(line) AS first_line, COUNT(*) AS reference_count
            FROM logical_references r
            GROUP BY path, lang, container_kind, container_name, symbol_name, reference_kind";
        }
        else
        {
            sql += " GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.reference_kind";
        }
        sql += $" ORDER BY CASE WHEN @preferExactCase = 1 AND r.container_name = @rawQuery THEN 0 ELSE 1 END, {(referenceKind == null ? GetPathBucketOrderSql("r.path") : PathBucketOrder)}, CASE WHEN lower(r.container_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, {(referenceKind == null ? "r.path" : "f.path")}, first_line LIMIT @limit";

        cmd.CommandText = sql;
        string calleesQueryParam;
        if (!exact)
            calleesQueryParam = $"%{EscapeLikeQuery(query)}%";
        else if (_foldReady)
            calleesQueryParam = NameFold.Fold(query) ?? query;
        else
            calleesQueryParam = query;
        cmd.Parameters.AddWithValue("@query", calleesQueryParam);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
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
        var groupedSql = @"
            SELECT path, lang, container_kind, container_name, symbol_name, reference_kind
            FROM (
                SELECT f.path AS path, f.lang AS lang, r.container_kind AS container_kind,
                       r.container_name AS container_name, r.symbol_name AS symbol_name,
                       " + (referenceKind == null
                           ? GetPreferredReferenceKindSql("r.reference_kind")
                           : "r.reference_kind") + @" AS reference_kind
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";
        groupedSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            groupedSql += " AND r.reference_kind = @referenceKind";
        else
            groupedSql += $" AND r.reference_kind IN {CallGraphReferenceKindsSql}";
        if (exact && _foldReady)
            groupedSql += " AND (r.container_name_folded = @query OR (f.lang = 'sql' AND sql_leaf_name_folded(r.container_name) = @aliasQueryLeafFolded))";
        else if (exact)
            groupedSql += " AND (r.container_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        else
            groupedSql += " AND (r.container_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        if (lang != null)
            groupedSql += " AND f.lang = @lang";
        AppendPathFilters(ref groupedSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            groupedSql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
        groupedSql += " ) grouped_call_sites GROUP BY path, lang, container_kind, container_name, symbol_name, reference_kind LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({groupedSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    public QueryCountResult CountCalleesTotal(string query, string? lang = null, string? referenceKind = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (!_hasReferencesTable)
            return new QueryCountResult(0, 0);

        using var cmd = _conn.CreateCommand();
        var groupedSql = @"
            SELECT path
            FROM (
                SELECT f.path AS path, f.lang AS lang, r.container_kind AS container_kind,
                       r.container_name AS container_name, r.symbol_name AS symbol_name,
                       " + (referenceKind == null
                           ? GetPreferredReferenceKindSql("r.reference_kind")
                           : "r.reference_kind") + @" AS reference_kind
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE r.container_name IS NOT NULL";
        groupedSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "f", "graphLang")}";

        if (referenceKind != null)
            groupedSql += " AND r.reference_kind = @referenceKind";
        else
            groupedSql += $" AND r.reference_kind IN {CallGraphReferenceKindsSql}";
        if (exact && _foldReady)
            groupedSql += " AND (r.container_name_folded = @query OR (f.lang = 'sql' AND sql_leaf_name_folded(r.container_name) = @aliasQueryLeafFolded))";
        else if (exact)
            groupedSql += " AND (r.container_name = @query COLLATE NOCASE OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        else
            groupedSql += " AND (r.container_name LIKE @query ESCAPE '\\' OR (f.lang = 'sql' AND sql_leaf_name(r.container_name) = @aliasQuery COLLATE NOCASE))";
        if (lang != null)
            groupedSql += " AND f.lang = @lang";
        AppendPathFilters(ref groupedSql, pathPatterns, excludePathPatterns, excludeTests);
        if (referenceKind == null)
            groupedSql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number, {GetLogicalReferenceKindSql("r.reference_kind")}";
        groupedSql += " ) grouped_call_sites GROUP BY path, lang, container_kind, container_name, symbol_name, reference_kind";

        cmd.CommandText = $"SELECT COUNT(*), COUNT(DISTINCT path) FROM ({groupedSql})";
        var value = !exact
            ? $"%{EscapeLikeQuery(query)}%"
            : _foldReady
                ? NameFold.Fold(query) ?? query
                : query;
        cmd.Parameters.AddWithValue("@query", value);
        cmd.Parameters.AddWithValue("@aliasQuery", query);
        cmd.Parameters.AddWithValue("@aliasQueryLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(query)) ?? SqlNameResolver.GetLeafName(query));
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return ExecuteCountSummary(cmd);
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
        var normalizedName = SqlNameResolver.NormalizeQualifiedName(symbolName);
        var leafName = SqlNameResolver.GetLeafName(symbolName);
        var segmentCount = SqlNameResolver.GetSegmentCount(symbolName);
        var allowLeafFallback = !SqlNameResolver.HasQualifier(symbolName);
        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "resolveLang");
        var nameCondition = _foldReady
            ? allowLeafFallback
                ? "(s.name_folded = @nameFolded OR (f.lang = 'sql' AND ((sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName COLLATE NOCASE) OR sql_leaf_name(s.name) = @leafName COLLATE NOCASE)))"
                : "(s.name_folded = @nameFolded OR (f.lang = 'sql' AND sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName COLLATE NOCASE))"
            : allowLeafFallback
                ? "(s.name = @name COLLATE NOCASE OR (f.lang = 'sql' AND ((sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName COLLATE NOCASE) OR sql_leaf_name(s.name) = @leafName COLLATE NOCASE)))"
                : "(s.name = @name COLLATE NOCASE OR (f.lang = 'sql' AND sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName COLLATE NOCASE))";
        cmd.CommandText = @"SELECT s.name FROM symbols s JOIN files f ON s.file_id = f.id
                            WHERE " + nameCondition + @"
                              AND " + supportedLangFilter + @"
                            ORDER BY CASE
                                         WHEN s.name = @name THEN 0
                                         WHEN f.lang = 'sql' AND sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName THEN 1
                                         WHEN f.lang = 'sql' AND sql_segment_count(s.name) = @segmentCount AND sql_normalize_name(s.name) = @normalizedName COLLATE NOCASE THEN 2
                                         WHEN @allowLeafFallback = 1 AND f.lang = 'sql' AND sql_leaf_name(s.name) = @leafName THEN 3
                                         WHEN @allowLeafFallback = 1 AND f.lang = 'sql' AND sql_leaf_name(s.name) = @leafName COLLATE NOCASE THEN 4
                                         ELSE 5
                                     END LIMIT 1";
        cmd.Parameters.AddWithValue("@name", symbolName);
        cmd.Parameters.AddWithValue("@normalizedName", normalizedName);
        cmd.Parameters.AddWithValue("@leafName", leafName);
        cmd.Parameters.AddWithValue("@segmentCount", segmentCount);
        cmd.Parameters.AddWithValue("@allowLeafFallback", allowLeafFallback ? 1 : 0);
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
              AND (r.symbol_name_folded = @symbolNameFolded OR (f.lang = 'sql' AND r.symbol_name_folded = @symbolNameLeafFolded))"
            : @"
              AND (r.symbol_name = @symbolName COLLATE NOCASE OR (f.lang = 'sql' AND r.symbol_name = sql_leaf_name(@symbolName) COLLATE NOCASE))";

        // impact BFS must share the call-graph contract with `callers`/`callees`/`hotspots`,
        // so event subscriptions (`Click += OnClick`) also participate in the transitive
        // caller chain. Metadata edges (`attribute`, `annotation`) stay excluded.
        // impact の BFS は `callers`/`callees`/`hotspots` と同じ call-graph 契約を共有し、
        // `subscribe` エッジ（`Click += OnClick` 等）も推移 caller に含める。`attribute` /
        // `annotation` のような metadata エッジは引き続き除外する。
        var sql = $@"
            WITH logical_references AS (
                SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.line
                FROM symbol_references r
                JOIN files f ON r.file_id = f.id
                WHERE r.container_name IS NOT NULL
                  AND r.reference_kind IN {CallGraphReferenceKindsSql}
                  AND {supportedLangFilter}
                  {nameCondition}";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += @"
                GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.file_id, r.line, r.column_number
            )
            SELECT path, lang, container_kind, container_name, symbol_name,
                   MIN(line) AS first_line, COUNT(*) AS reference_count
            FROM logical_references r
            GROUP BY path, lang, container_kind, container_name, symbol_name";
        sql += $" ORDER BY {GetPathBucketOrderSql("r.path")}, reference_count DESC, r.path, COALESCE(r.container_name, ''), COALESCE(r.container_kind, ''), r.symbol_name, first_line LIMIT @limit OFFSET @offset";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@symbolName", symbolName);
        cmd.Parameters.AddWithValue("@symbolNameLeafFolded", NameFold.Fold(SqlNameResolver.GetLeafName(symbolName)) ?? SqlNameResolver.GetLeafName(symbolName));
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
    /// Analyze impact for a query by combining transitive callers with symbol-resolution
    /// metadata and a class-like file-dependency fallback when symbol-level callers are absent.
    /// impact 用に caller BFS と解決メタデータを束ね、class 系で caller 不在なら
    /// file dependency をフォールバックとして返す。
    /// </summary>
    public ImpactAnalysisResult AnalyzeImpact(string symbolName, int maxDepth = 5, int limit = 50, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        var resolvedName = ResolveSymbolName(symbolName, lang);
        var definitions = ResolveImpactDefinitions(resolvedName, lang, pathPatterns, excludePathPatterns, excludeTests);
        var definitionPaths = definitions
            .Select(d => d.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasMultipleDefinitions = definitions.Count > 1;
        var fallbackDefinitions = definitions
            .Where(d => IsPreciseImpactFallbackKind(d.Kind))
            .ToList();
        var fallbackDefinitionPaths = fallbackDefinitions
            .Select(d => d.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasMultipleFallbackDefinitions = fallbackDefinitions.Count > 1;
        var hasMultipleFallbackDefinitionFiles = fallbackDefinitionPaths.Count > 1;
        var hasClassLikeDefinitions = fallbackDefinitions.Count > 0;
        var (callers, truncated) = GetTransitiveCallers(symbolName, maxDepth, limit, lang, pathPatterns, excludePathPatterns, excludeTests);

        var impactMode = "callers";
        var fileImpacts = new List<FileDependencyResult>();
        string? zeroResultReason = null;
        string? suggestion = null;
        var heuristic = false;

        if (callers.Count == 0)
        {
            impactMode = "none";

            if (_hasReferencesTable)
            {
                if (definitions.Count > 0 && definitions.All(d => IsNonCallableImpactKind(d.Kind)))
                {
                    zeroResultReason = "non_callable_symbol_kind";
                    suggestion = "Try `cdidx definition <symbol>` and then run `impact` on a specific callable member instead.";
                }
                else if (hasMultipleFallbackDefinitions)
                {
                    zeroResultReason = hasMultipleFallbackDefinitionFiles ? "multiple_definition_files" : "multiple_definitions";
                    suggestion = BuildImpactSuggestion(fallbackDefinitionPaths, hasClassLikeDefinitions, hasMultipleDefinitions: true, hasMultipleDefinitionFiles: hasMultipleFallbackDefinitionFiles);
                }
                else if (fallbackDefinitions.Count == 1)
                {
                    var fallbackNames = ResolveImpactFallbackNames(fallbackDefinitions[0]);
                    var (hintResults, hintTruncated) = GetFileDependencyHintsToResolvedType(fallbackDefinitions[0], fallbackNames, limit, lang, pathPatterns, excludePathPatterns, excludeTests);
                    fileImpacts = hintResults;
                    truncated |= hintTruncated;
                    if (fileImpacts.Count > 0)
                    {
                        impactMode = "file_dependency_hints";
                        heuristic = true;
                        suggestion = "These file-level dependents are heuristic only; confirm with `cdidx deps --path <definition-path> --reverse` and a member-level `impact` query.";
                    }
                    else
                    {
                        zeroResultReason = "class_symbol_no_symbol_callers";
                        suggestion = BuildImpactSuggestion(definitionPaths, hasClassLikeDefinitions, hasMultipleDefinitions: false, hasMultipleDefinitionFiles: false);
                    }
                }
                else if (hasMultipleDefinitions)
                {
                    zeroResultReason = definitionPaths.Count > 1 ? "multiple_definition_files" : "multiple_definitions";
                    suggestion = BuildImpactSuggestion(definitionPaths, hasClassLikeDefinitions, hasMultipleDefinitions: true, hasMultipleDefinitionFiles: definitionPaths.Count > 1);
                }
                else if (definitions.Count == 0)
                {
                    zeroResultReason = "no_matching_definition";
                    suggestion = "Try `cdidx definition <symbol>` to confirm the indexed name.";
                }
            }
        }

        return new ImpactAnalysisResult
        {
            Query = symbolName,
            ResolvedName = resolvedName,
            ImpactMode = impactMode,
            Heuristic = heuristic,
            MaxDepth = maxDepth,
            DefinitionCount = definitions.Count,
            DefinitionFileCount = definitionPaths.Count,
            HintCount = fileImpacts.Count,
            HasClassLikeDefinitions = hasClassLikeDefinitions,
            HasMultipleDefinitions = hasMultipleDefinitions,
            HasMultipleDefinitionFiles = definitionPaths.Count > 1,
            Definitions = definitions,
            Callers = callers,
            FileImpacts = fileImpacts,
            Truncated = truncated,
            GraphTableAvailable = _hasReferencesTable,
            ZeroResultReason = zeroResultReason,
            Suggestion = suggestion,
        };
    }

    private List<SymbolResult> ResolveImpactDefinitions(string resolvedName, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        var normalizedName = SqlNameResolver.NormalizeQualifiedName(resolvedName);
        var leafName = SqlNameResolver.GetLeafName(resolvedName);
        var segmentCount = SqlNameResolver.GetSegmentCount(resolvedName);
        var allowLeafFallback = !SqlNameResolver.HasQualifier(resolvedName);
        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "impactDefLang");
        var nameCondition = _foldReady
            ? allowLeafFallback
                ? "(s.name_folded = @resolvedNameFolded OR (f.lang = 'sql' AND ((sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized COLLATE NOCASE) OR sql_leaf_name(s.name) = @resolvedNameLeaf COLLATE NOCASE)))"
                : "(s.name_folded = @resolvedNameFolded OR (f.lang = 'sql' AND sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized COLLATE NOCASE))"
            : allowLeafFallback
                ? "(s.name = @resolvedName COLLATE NOCASE OR (f.lang = 'sql' AND ((sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized COLLATE NOCASE) OR sql_leaf_name(s.name) = @resolvedNameLeaf COLLATE NOCASE)))"
                : "(s.name = @resolvedName COLLATE NOCASE OR (f.lang = 'sql' AND sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized COLLATE NOCASE))";
        var sql = $@"
            SELECT f.path, f.lang, s.kind, s.name, s.line,
                   {GetSymbolColumnSql("start_line", "s.line")} AS start_line,
                   {GetSymbolColumnSql("end_line", "s.line")} AS end_line,
                   {GetSymbolColumnSql("body_start_line")} AS body_start_line,
                   {GetSymbolColumnSql("body_end_line")} AS body_end_line,
                   {GetSymbolColumnSql("signature")} AS signature,
                   {GetSymbolColumnSql("container_kind")} AS container_kind,
                   {GetSymbolColumnSql("container_name")} AS container_name,
                   {GetSymbolColumnSql("visibility")} AS visibility,
                   {GetSymbolColumnSql("return_type")} AS return_type
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE {nameCondition}
              AND {supportedLangFilter}";

        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += @" ORDER BY CASE
                     WHEN s.name = @resolvedName THEN 0
                     WHEN f.lang = 'sql' AND sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized THEN 1
                     WHEN f.lang = 'sql' AND sql_segment_count(s.name) = @resolvedNameSegmentCount AND sql_normalize_name(s.name) = @resolvedNameNormalized COLLATE NOCASE THEN 2
                     WHEN @allowLeafFallback = 1 AND f.lang = 'sql' AND sql_leaf_name(s.name) = @resolvedNameLeaf THEN 3
                     WHEN @allowLeafFallback = 1 AND f.lang = 'sql' AND sql_leaf_name(s.name) = @resolvedNameLeaf COLLATE NOCASE THEN 4
                     ELSE 5
                   END, " + $"{PathBucketOrder}, {VisibilityOrder}, s.name, f.path, s.line LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@resolvedName", resolvedName);
        cmd.Parameters.AddWithValue("@resolvedNameNormalized", normalizedName);
        cmd.Parameters.AddWithValue("@resolvedNameLeaf", leafName);
        cmd.Parameters.AddWithValue("@resolvedNameSegmentCount", segmentCount);
        cmd.Parameters.AddWithValue("@allowLeafFallback", allowLeafFallback ? 1 : 0);
        if (_foldReady)
            cmd.Parameters.AddWithValue("@resolvedNameFolded", NameFold.Fold(resolvedName) ?? resolvedName);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", 50);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = reader.GetString(1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = !reader.IsDBNull(5) ? reader.GetInt32(5) : reader.GetInt32(4),
                EndLine = !reader.IsDBNull(6) ? reader.GetInt32(6) : reader.GetInt32(4),
                BodyStartLine = !reader.IsDBNull(7) ? reader.GetInt32(7) : null,
                BodyEndLine = !reader.IsDBNull(8) ? reader.GetInt32(8) : null,
                Signature = !reader.IsDBNull(9) ? reader.GetString(9) : null,
                ContainerKind = !reader.IsDBNull(10) ? reader.GetString(10) : null,
                ContainerName = !reader.IsDBNull(11) ? reader.GetString(11) : null,
                Visibility = !reader.IsDBNull(12) ? reader.GetString(12) : null,
                ReturnType = !reader.IsDBNull(13) ? reader.GetString(13) : null,
            });
        }

        return results;
    }

    // C# convention: a class `FooAttribute` is used in source as `[Foo]`, so the reference
    // site is stored with `symbol_name = "Foo"`. When a user queries with the class name
    // (`references FooAttribute`, `inspect FooAttribute`, `analyze_symbol("FooAttribute")`),
    // return the suffix-stripped form as an alias so the query still reaches the idiomatic
    // use site. Only applies for C# scope — other languages do not share the convention.
    // C# の規約: クラス `FooAttribute` はソース中で `[Foo]` として使われるため、参照サイトは
    // `symbol_name = "Foo"` で保存される。ユーザーがクラス名で問い合わせたとき
    // (`references FooAttribute` 等) でも慣用的な利用サイトに到達できるよう、
    // suffix を外した別名を返す。C# 以外の言語ではこの規約を持たないので適用しない。
    private static string? ComputeCSharpAttributeSuffixAlias(string? query, string? lang, string? referenceKind)
    {
        if (string.IsNullOrEmpty(query)) return null;
        if (lang != null && !lang.Equals("csharp", StringComparison.OrdinalIgnoreCase)) return null;
        // Only metadata lookups should apply the suffix alias: ordinary call-graph
        // queries (`--kind call` / `instantiate` / `subscribe`) must not match `Foo()`
        // call rows when the user typed `FooAttribute`. When `referenceKind` is null,
        // the SQL side additionally constrains the alias clause to attribute rows only.
        // metadata 参照の問い合わせ時だけ alias を適用する: `--kind call` などの call-graph
        // クエリは `FooAttribute` と入力されたときに `Foo()` の call 行に一致してはならない。
        // referenceKind が null のときは SQL 側でも alias 節を attribute 行に限定する。
        if (referenceKind != null && !referenceKind.Equals("attribute", StringComparison.OrdinalIgnoreCase))
            return null;
        const string suffix = "Attribute";
        // Case-insensitive suffix detection so `references myauditattribute` and
        // `inspect MyAuditATTRIBUTE` still produce the `MyAudit` alias, matching the
        // NOCASE / folded contract of the surrounding exact/substring query paths.
        // 大文字小文字を無視して suffix を検出することで、`myauditattribute` や
        // `MyAuditATTRIBUTE` のような形でも alias を生成できる。
        if (!query!.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        if (query.Length <= suffix.Length) return null;
        return query.Substring(0, query.Length - suffix.Length);
    }

    private List<string> ResolveImpactFallbackNames(SymbolResult definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Path) || string.IsNullOrWhiteSpace(definition.Name))
            return new List<string>();

        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "impactSafeNameLang");
        cmd.CommandText = @"
            SELECT DISTINCT s.name
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE f.path = @targetPath
              AND " + supportedLangFilter + @"
              AND (
                    (s.name = @containerName AND s.kind = @containerKind)
                    OR s.container_name = @containerName
                  )
            ORDER BY s.name";
        cmd.Parameters.AddWithValue("@targetPath", definition.Path);
        cmd.Parameters.AddWithValue("@containerName", definition.Name);
        cmd.Parameters.AddWithValue("@containerKind", definition.Kind);

        var results = new List<string>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            results.Add(reader.GetString(0));

        // C# attribute naming convention: a class `FooAttribute` is used as `[Foo]` in source,
        // so reference sites are stored with symbol_name `Foo`. Add the suffix-stripped alias
        // for the resolved definition itself so impact on `FooAttribute` can find metadata-only
        // usage sites. Only the resolved definition's own name gets the alias — applying the
        // strip to every same-file fallback name (e.g. a nested `BarAttribute` inside the file
        // that defines `FooAttribute`) would let `impact FooAttribute` falsely report `[Bar]`
        // usages as part of `FooAttribute`'s blast radius.
        // C# の属性命名規約: クラス `FooAttribute` はソースで `[Foo]` として使われ、参照サイトは
        // symbol_name `Foo` で保存される。`FooAttribute` への impact でも metadata 参照サイトを
        // 見つけられるよう、*解決済み定義自身* にのみサフィックスを外した別名を追加する。
        // same-file fallback 名全体（例: `FooAttribute` と同一ファイルに nested で存在する
        // `BarAttribute`）にまで strip を適用すると、`impact FooAttribute` が `[Bar]` 利用を
        // 誤って `FooAttribute` の影響範囲として報告してしまうため、定義自身だけに限定する。
        if (string.Equals(definition.Lang, "csharp", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(definition.Name) &&
            definition.Name.Length > "Attribute".Length &&
            definition.Name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            var stripped = definition.Name.Substring(0, definition.Name.Length - "Attribute".Length);
            if (stripped.Length > 0 && !results.Contains(stripped))
                results.Add(stripped);
        }

        return results;
    }

    private (List<FileDependencyResult> Results, bool Truncated) GetFileDependencyHintsToResolvedType(SymbolResult definition, IReadOnlyList<string> fallbackNames, int limit, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        if (!_hasReferencesTable || string.IsNullOrWhiteSpace(definition.Path) || fallbackNames.Count == 0)
            return (new List<FileDependencyResult>(), false);

        using var cmd = _conn.CreateCommand();
        var innerSql = @"
                SELECT src.id AS source_file_id, src.path AS source_path, @impactTargetPath AS target_path,
                       r.symbol_name AS symbol_name,
                       r.line,
                       r.column_number,
                       " + GetLogicalReferenceKindSql("r.reference_kind") + @" AS logical_reference_kind
                FROM symbol_references r
                JOIN files src ON r.file_id = src.id
                WHERE src.path != @impactTargetPath";
        // `impact` heuristic file hints intentionally include metadata-only reference
        // kinds (`attribute` / `annotation`). A rename or removal of `User` breaks
        // `[JsonConverter(typeof(User))]` / `@Inject(User.class)` at compile time just
        // as surely as it breaks `new User()`, so file-level blast-radius analysis
        // must surface those sites as real dependencies. `callers` / `callees` still
        // reject metadata kinds at the CLI / MCP boundary because those commands model
        // the dynamic call graph, not the dependency graph.
        // `impact` の heuristic file hint は metadata-only な参照 (`attribute` /
        // `annotation`) も意図的に含める。`User` を rename / 削除すると
        // `[JsonConverter(typeof(User))]` / `@Inject(User.class)` も compile-time で
        // 壊れるため、ファイル単位の blast-radius 分析ではそれらも本物の依存として
        // 出す必要がある。`callers` / `callees` は call graph を扱うので、metadata 種別
        // の拒否は引き続き CLI / MCP boundary 側で行う。
        innerSql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "src", "impactDepsLang")}";
        if (lang != null)
            innerSql += " AND src.lang = @lang";
        var nameClauses = new List<string>(fallbackNames.Count);
        for (int i = 0; i < fallbackNames.Count; i++)
            nameClauses.Add($"r.symbol_name = @impactFallbackName{i}");
        innerSql += " AND (" + string.Join(" OR ", nameClauses) + ")";

        if (pathPatterns is { Count: > 0 })
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"src.path LIKE @pathPattern{i} ESCAPE '\\'");
            innerSql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                innerSql += $" AND src.path NOT LIKE @excludePath{i} ESCAPE '\\'";
        }
        if (excludeTests)
            innerSql += $" AND NOT {TestPathCondition.Replace("f.path", "src.path")}";
        innerSql = "SELECT DISTINCT * FROM (" + innerSql + ")";

        cmd.CommandText = $@"
            SELECT source_file_id, source_path, target_path,
                   COUNT(*) AS reference_count,
                   GROUP_CONCAT(DISTINCT symbol_name) AS symbols,
                   MAX(CASE WHEN logical_reference_kind IN ('attribute','annotation') THEN 1 ELSE 0 END) AS has_metadata_ref
            FROM ({innerSql}) edges
            GROUP BY source_file_id, source_path, target_path
            ORDER BY reference_count DESC, source_path, target_path";
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        cmd.Parameters.AddWithValue("@impactTargetPath", definition.Path);
        for (int i = 0; i < fallbackNames.Count; i++)
            cmd.Parameters.AddWithValue($"@impactFallbackName{i}", fallbackNames[i]);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var candidates = new List<(long SourceFileId, bool HasMetadataRef, FileDependencyResult Edge)>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            candidates.Add((
                reader.GetInt64(0),
                reader.GetInt32(5) == 1,
                new FileDependencyResult
                {
                    SourcePath = reader.GetString(1),
                    TargetPath = reader.GetString(2),
                    ReferenceCount = reader.GetInt32(3),
                    Symbols = reader.GetString(4),
                }));
        }

        // Metadata references only carry the short use-site name (`Foo` for `[Foo]`,
        // `@Foo`). If multiple class-like definitions share the same unqualified name
        // across namespaces / packages (e.g. `A.MyAuditAttribute` and
        // `B.MyAuditAttribute`), we cannot uniquely attribute a `[MyAudit]` site to
        // either target. Skip the metadata evidence bypass in that ambiguous case so
        // `impact` does not over-report the blast radius of a rename / removal.
        // metadata 参照は use-site 側の短縮名 (`[Foo]` / `@Foo` の `Foo`) しか持た
        // ないため、namespace / package を跨いで同名の class-like 定義が複数存在
        // する場合、`[MyAudit]` 参照をどちらの target にも一意に紐付けられない。
        // そのような曖昧なケースでは metadata の evidence bypass を行わず、
        // `impact` が rename / 削除の影響範囲を過大報告しないようにする。
        var metadataBypassSafe = IsMetadataTargetUnambiguous(definition, lang, pathPatterns, excludePathPatterns, excludeTests);
        var evidenceCache = new Dictionary<long, bool>();
        var filtered = new List<FileDependencyResult>();
        foreach (var candidate in candidates)
        {
            // Metadata-only consumers (attribute / annotation sites like `[MyAudit]` or
            // `@Inject(User.class)`) legitimately lack structured type evidence in the
            // source file. Bypass the evidence guard for those edges only when the
            // class-like target is unambiguous so deps/impact can surface pure-attribute
            // consumers without over-attributing same-named targets.
            // metadata 専用の参照 (`[MyAudit]` や `@Inject(User.class)` のような attribute /
            // annotation 利用) は、source 側のファイルに structured な型利用が無くても
            // 正当な依存となるが、class-like target が一意に決まるときだけ evidence guard
            // をスキップする。曖昧なときは下の evidence 要求へフォールスルーさせ、
            // 同名 target への誤帰属を防ぐ。
            if (candidate.HasMetadataRef && metadataBypassSafe)
            {
                filtered.Add(candidate.Edge);
                continue;
            }
            if (!evidenceCache.TryGetValue(candidate.SourceFileId, out var hasEvidence))
            {
                hasEvidence = SourceFileHasStructuredTypeEvidence(candidate.SourceFileId, definition.Name);
                evidenceCache[candidate.SourceFileId] = hasEvidence;
            }
            if (hasEvidence)
                filtered.Add(candidate.Edge);
        }

        var truncated = filtered.Count > limit;
        if (truncated)
            filtered.RemoveRange(limit, filtered.Count - limit);

        return (filtered, truncated);
    }

    // Returns true when the metadata target name resolves to at most one class-like
    // symbol across the graph-supported languages. Ambiguous names (same unqualified
    // name under different namespaces / packages) must not trigger the metadata
    // evidence bypass because attribute / annotation reference rows only keep the
    // short name and cannot disambiguate between them.
    // graph 対応言語の中で class-like シンボルが高々 1 件しか存在しないときに true。
    // namespace / package を跨いで同名の class-like 定義が複数ある曖昧なケースでは
    // attribute / annotation 参照行が短縮名しか持たず区別できないため、metadata の
    // evidence bypass を許可しない。
    private bool IsMetadataTargetUnambiguous(
        SymbolResult definition,
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            return false;
        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "metadataAmbigLang");
        // Count at symbol-identity level (path + line + name) rather than at path
        // level, so two same-named class-like definitions in the same source file
        // (e.g. `namespace A { class MyAuditAttribute { } } namespace B { class
        // MyAuditAttribute { } }` both in one .cs file) still register as ambiguous.
        // DISTINCT f.path alone would collapse them to 1 and falsely trigger the
        // metadata bypass.
        // 曖昧性は path 単位ではなく symbol identity 単位 (path + line + name) で数える。
        // 同じ .cs ファイル内に別名前空間で同名の class-like が 2 つあるケース
        // (例: `namespace A { class MyAuditAttribute { } } namespace B { class
        // MyAuditAttribute { } }`) でも ambiguity を 2 として検出できる。DISTINCT
        // f.path のままだと 1 に潰れ、metadata bypass が誤って有効化される。
        // For C# specifically, only count class-like definitions that are
        // plausible attribute metadata targets. We don't resolve base types
        // transitively at SQL time, so the best portable approximation is
        // "has an inheritance clause": any class declared with `: ...` is a
        // potential attribute type (direct `: Attribute`, indirect
        // `: BaseAudit` where BaseAudit itself derives from Attribute, or
        // any other `: Base` chain). A plain `class MyAuditAttribute { }`
        // with no `:` clause is not a valid `[MyAudit]` target at compile
        // time, so excluding it prevents the metadata bypass from being
        // falsely suppressed. We deliberately over-accept non-attribute
        // derived classes rather than under-accept indirectly-derived
        // attribute classes, because an invalid `[MyFoo]` against a
        // non-attribute class would fail to compile and therefore not
        // appear as a real reference. Other languages keep the broad
        // class-like candidate set because their metadata-target markers
        // don't match this signature shape.
        // C# は SQL 時点で基底型を遡れないため、「何かを継承している
        // class-like」を attribute 候補の近似として扱う。`: Attribute` の
        // 直接継承も、`: BaseAudit` のような中間基底経由の間接継承も、
        // 何らかの `: Base` があれば候補に含める。継承節の無い plain
        // `class MyAuditAttribute { }` だけを除外することで metadata
        // bypass の誤抑止を防ぐ。非 attribute を過剰に含めるが、無効な
        // `[MyFoo]` はコンパイルできないので実参照にはならず実害が無い。
        // 署名列が無い legacy DB では degrade して class 限定のみ使う。
        var metadataTargetKindExprF = BuildMetadataTargetKindExpr("f");
        var sql = $@"
            SELECT COUNT(*) FROM (
                SELECT DISTINCT f.path, s.line, s.name
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE s.name = @metadataAmbigName COLLATE NOCASE
                  AND {metadataTargetKindExprF}
                  AND {supportedLangFilter}";
        if (lang != null)
        {
            sql += " AND f.lang = @metadataAmbigLangFilter";
            cmd.Parameters.AddWithValue("@metadataAmbigLangFilter", lang);
        }
        // Path / exclude-path parameters must be wrapped with `%...%` and escaped
        // through EscapeLikeQuery so the LIKE semantics match the rest of the
        // reader (search / references / callers / deps etc.). Passing the raw
        // CLI value would require an anchored path like `%src/A/%` to match, so
        // normal `--path src/A/` invocations would see zero in-scope definitions,
        // the ambiguity count would underflow to 1, and the metadata bypass
        // would falsely fire on what are actually ambiguous targets.
        // path / exclude-path のパラメータは他の読み取り経路 (search / references /
        // callers / deps 等) と同じ LIKE セマンティクスに合わせるため、
        // EscapeLikeQuery でエスケープした上で `%...%` で包んでバインドする。生値の
        // まま渡すと、通常の `--path src/A/` のような呼び出しでは LIKE が一致せず、
        // 曖昧性カウントが 1 に過小化され、本来抑止すべき metadata bypass が
        // 誤って発動してしまう。
        if (pathPatterns is { Count: > 0 })
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
            {
                ors.Add($"f.path LIKE @metadataAmbigPath{i} ESCAPE '\\'");
                cmd.Parameters.AddWithValue($"@metadataAmbigPath{i}", $"%{EscapeLikeQuery(pathPatterns[i])}%");
            }
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
            {
                sql += $" AND f.path NOT LIKE @metadataAmbigExcludePath{i} ESCAPE '\\'";
                cmd.Parameters.AddWithValue($"@metadataAmbigExcludePath{i}", $"%{EscapeLikeQuery(excludePathPatterns[i])}%");
            }
        }
        if (excludeTests)
            sql += $" AND NOT {TestPathCondition}";
        sql += ")";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@metadataAmbigName", definition.Name);
        var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return count <= 1;
    }

    private bool SourceFileHasStructuredTypeEvidence(long fileId, string typeName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.name,
                   " + GetSymbolColumnSql("signature") + @" AS signature,
                   " + GetSymbolColumnSql("return_type") + @" AS return_type
            FROM symbols s
            WHERE s.file_id = @fileId";
        cmd.Parameters.AddWithValue("@fileId", fileId);

        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var symbolName = reader.GetString(0);
            var signature = !reader.IsDBNull(1) ? reader.GetString(1) : null;
            var returnType = !reader.IsDBNull(2) ? reader.GetString(2) : null;
            if (SymbolProvidesStructuredTypeEvidence(symbolName, signature, returnType, typeName))
                return true;
        }

        return false;
    }

    private static bool SymbolProvidesStructuredTypeEvidence(string symbolName, string? signature, string? returnType, string typeName)
    {
        if (FoldedImpactNameEquals(returnType, typeName))
            return true;
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        foreach (Match match in ImpactSignatureIdentifierRegex.Matches(signature))
        {
            var token = match.Value;
            if (FoldedImpactNameEquals(token, symbolName))
                continue;
            if (FoldedImpactNameEquals(token, typeName))
                return true;
        }

        return false;
    }

    private static bool FoldedImpactNameEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var leftFolded = NameFold.Fold(left) ?? left;
        var rightFolded = NameFold.Fold(right) ?? right;
        return string.Equals(leftFolded, rightFolded, StringComparison.Ordinal);
    }

    private static bool IsNonCallableImpactKind(string? kind) =>
        kind is "namespace" or "import";

    private static bool IsPreciseImpactFallbackKind(string? kind)
    {
        return kind is "class" or "struct" or "interface";
    }

    private static string BuildImpactSuggestion(IReadOnlyList<string> definitionPaths, bool hasClassLikeDefinitions, bool hasMultipleDefinitions, bool hasMultipleDefinitionFiles)
    {
        if (hasClassLikeDefinitions)
        {
            if (hasMultipleDefinitionFiles)
                return "Try `cdidx deps --path <definition-path> --reverse` for each definition file or query a member symbol instead.";
            if (hasMultipleDefinitions)
                return "Try a fully qualified or member symbol query, or inspect the overlapping definitions with `cdidx definition <symbol> --body`.";
            if (definitionPaths.Count > 0)
                return $"Try `cdidx deps --path {definitionPaths[0]} --reverse` or query a member symbol instead.";
        }

        if (hasMultipleDefinitions)
            return "Try a more specific symbol name or inspect each definition file with `cdidx definition <symbol> --body`.";

        return "Try `cdidx definition <symbol>` to confirm the indexed symbol and then query a more specific callable member.";
    }

    /// <summary>
    /// Find literal substring matches inside path-scoped indexed files.
    /// path で絞ったインデックス済みファイル内でリテラル部分文字列一致を探す。
    /// </summary>
    public List<FileFindResult> FindInFiles(string query, int limit, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, int before = 0, int after = 0, bool exact = false, int maxLineWidth = LineWidthFormatter.DefaultMaxLineWidth)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0 || pathPatterns == null || pathPatterns.Count == 0)
            return [];

        before = Math.Max(0, before);
        after = Math.Max(0, after);
        maxLineWidth = LineWidthFormatter.ClampMaxLineWidth(maxLineWidth);
        var comparison = exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var fileCmd = _conn.CreateCommand();
        var sql = "SELECT f.path, f.lang, f.lines FROM files f WHERE 1=1";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, f.path";
        fileCmd.CommandText = sql;
        if (lang != null)
            fileCmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(fileCmd, pathPatterns, excludePathPatterns);

        var results = new List<FileFindResult>();
        using var fileReader = fileCmd.ExecuteTrackedReader();
        while (fileReader.TrackedRead())
        {
            if (results.Count >= limit)
                break;

            var path = fileReader.GetString(0);
            var fileLang = GetNullableString(fileReader, 1);
            var totalLines = fileReader.GetInt32(2);
            if (!TryLoadIndexedFileLines(path, out _, out _, out var lineMap) || lineMap.Count == 0)
                continue;

            for (int lineNumber = 1; lineNumber <= totalLines && results.Count < limit; lineNumber++)
            {
                if (!lineMap.TryGetValue(lineNumber, out var lineText))
                    continue;

                var snippetStart = Math.Max(1, lineNumber - before);
                var snippetEnd = Math.Min(totalLines, lineNumber + after);
                var snippetLineNumbers = Enumerable.Range(snippetStart, snippetEnd - snippetStart + 1)
                    .Where(lineMap.ContainsKey)
                    .ToList();
                if (snippetLineNumbers.Count == 0)
                    continue;

                for (int searchStart = 0; searchStart < lineText.Length && results.Count < limit;)
                {
                    var matchColumn = lineText.IndexOf(query, searchStart, comparison);
                    if (matchColumn < 0)
                        break;

                    var snippetLines = snippetLineNumbers.Select(line => lineMap[line]).ToList();
                    var clampedSnippet = LineWidthFormatter.ClampLines(
                        snippetLines,
                        maxLineWidth,
                        focusLineIndex: snippetLineNumbers.IndexOf(lineNumber),
                        focusColumn: matchColumn + 1,
                        focusLength: query.Length);

                    results.Add(new FileFindResult
                    {
                        Path = path,
                        Lang = fileLang,
                        Line = lineNumber,
                        Column = matchColumn + 1,
                        StartLine = snippetLineNumbers[0],
                        EndLine = snippetLineNumbers[^1],
                        Snippet = clampedSnippet.Text,
                        SnippetTruncated = clampedSnippet.Truncated,
                    });

                    searchStart = matchColumn + 1;
                }
            }
        }

        return results;
    }

    public QueryCountResult CountFindInFiles(string query, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(query) || pathPatterns == null || pathPatterns.Count == 0)
            return new QueryCountResult(0, 0);

        var comparison = exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        using var fileCmd = _conn.CreateCommand();
        var sql = "SELECT f.path, f.lines FROM files f WHERE 1=1";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, f.path";
        fileCmd.CommandText = sql;
        if (lang != null)
            fileCmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(fileCmd, pathPatterns, excludePathPatterns);

        var count = 0;
        var fileCount = 0;
        using var fileReader = fileCmd.ExecuteTrackedReader();
        while (fileReader.TrackedRead())
        {
            var path = fileReader.GetString(0);
            var totalLines = fileReader.GetInt32(1);
            if (!TryLoadIndexedFileLines(path, out _, out _, out var lineMap) || lineMap.Count == 0)
                continue;

            var fileMatches = 0;
            for (int lineNumber = 1; lineNumber <= totalLines; lineNumber++)
            {
                if (!lineMap.TryGetValue(lineNumber, out var lineText))
                    continue;

                for (int searchStart = 0; searchStart < lineText.Length;)
                {
                    var matchColumn = lineText.IndexOf(query, searchStart, comparison);
                    if (matchColumn < 0)
                        break;

                    fileMatches++;
                    searchStart = matchColumn + 1;
                }
            }

            if (fileMatches > 0)
            {
                count += fileMatches;
                fileCount++;
            }
        }

        return new QueryCountResult(count, fileCount);
    }

    /// <summary>
    /// Reconstruct one indexed file into an ordered line map.
    /// 1つのインデックス済みファイルを順序付き行マップへ再構成する。
    /// </summary>
    private bool TryLoadIndexedFileLines(string path, out string? lang, out int totalLines, out SortedDictionary<int, string> lineMap, int? startLine = null, int? endLine = null)
    {
        lang = null;
        totalLines = 0;
        lineMap = new SortedDictionary<int, string>();
        if (string.IsNullOrWhiteSpace(path))
            return false;

        using var fileCmd = _conn.CreateCommand();
        fileCmd.CommandText = "SELECT lang, lines FROM files WHERE path = @path";
        fileCmd.Parameters.AddWithValue("@path", path);

        using var fileReader = fileCmd.ExecuteTrackedReader();
        if (!fileReader.TrackedRead())
            return false;

        lang = GetNullableString(fileReader, 0);
        totalLines = fileReader.GetInt32(1);

        using var chunkCmd = _conn.CreateCommand();
        var chunkSql = @"
            SELECT c.start_line, c.end_line, c.content
            FROM chunks c
            JOIN files f ON c.file_id = f.id
            WHERE f.path = @path";
        if (startLine.HasValue)
            chunkSql += " AND c.end_line >= @startLine";
        if (endLine.HasValue)
            chunkSql += " AND c.start_line <= @endLine";
        chunkSql += " ORDER BY c.start_line, c.chunk_index";
        chunkCmd.CommandText = chunkSql;
        chunkCmd.Parameters.AddWithValue("@path", path);
        if (startLine.HasValue)
            chunkCmd.Parameters.AddWithValue("@startLine", startLine.Value);
        if (endLine.HasValue)
            chunkCmd.Parameters.AddWithValue("@endLine", endLine.Value);

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
                if (!lineMap.ContainsKey(absoluteLine))
                    lineMap[absoluteLine] = chunkLines[i];
            }
        }

        return lineMap.Count > 0;
    }

    /// <summary>
    /// Reconstruct a file excerpt from indexed chunks.
    /// インデックス済みチャンクからファイル抜粋を再構成する。
    /// </summary>
    public FileExcerptResult? GetExcerpt(
        string path,
        int startLine,
        int endLine,
        int before = 0,
        int after = 0,
        int? maxLineWidth = null,
        int? focusLine = null,
        int? focusColumn = null,
        int focusLength = 1)
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
        var requestedStart = Math.Max(1, startLine - before);
        if (!TryLoadIndexedFileLines(path, out var lang, out var totalLines, out var lineMap, requestedStart, endLine + after))
            return null;
        var requestedEnd = Math.Min(totalLines, endLine + after);

        var selectedLines = Enumerable.Range(requestedStart, requestedEnd - requestedStart + 1)
            .Where(lineMap.ContainsKey)
            .ToList();

        if (selectedLines.Count == 0)
            return null;

        var contentLines = selectedLines.Select(line => lineMap[line]).ToList();
        var focusLineIndex = focusLine.HasValue ? selectedLines.IndexOf(focusLine.Value) : -1;
        if (focusLineIndex >= 0 && focusColumn.HasValue && focusColumn.Value > contentLines[focusLineIndex].Length)
            return null;
        var clampedContent = maxLineWidth.HasValue
            ? LineWidthFormatter.ClampLines(
                contentLines,
                maxLineWidth.Value,
                focusLineIndex >= 0 ? focusLineIndex : null,
                focusLineIndex >= 0 ? focusColumn : null,
                focusLength)
            : new ClampedTextResult(string.Join("\n", contentLines), false);

        return new FileExcerptResult
        {
            Path = path,
            Lang = lang,
            StartLine = selectedLines[0],
            EndLine = selectedLines[^1],
            Content = clampedContent.Text,
            ContentTruncated = clampedContent.Truncated,
        };
    }

    /// <summary>
    /// Return the length of the focused excerpt line when it is part of the reconstructed range.
    /// 抜粋として再構成される範囲内に focus line が含まれる場合、その行長を返す。
    /// </summary>
    public int? GetExcerptFocusLineLength(
        string path,
        int startLine,
        int endLine,
        int before = 0,
        int after = 0,
        int? focusLine = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !focusLine.HasValue)
            return null;

        if (startLine <= 0)
            startLine = 1;
        if (endLine < startLine)
            endLine = startLine;
        if (before < 0)
            before = 0;
        if (after < 0)
            after = 0;

        var requestedStart = Math.Max(1, startLine - before);
        if (!TryLoadIndexedFileLines(path, out _, out var totalLines, out var lineMap, requestedStart, endLine + after))
            return null;
        var requestedEnd = Math.Min(totalLines, endLine + after);

        if (focusLine.Value < requestedStart || focusLine.Value > requestedEnd)
            return null;

        return lineMap.TryGetValue(focusLine.Value, out var line) ? line.Length : null;
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
        var hasCSharpFiles = ScopeMayIncludeCSharpFiles("csharp", pathPatterns: null, excludePathPatterns: null, excludeTests: false, since: null);
        var csharpSymbolNameReady = !hasCSharpFiles || _csharpSymbolNameContractCurrent;
        var hotspotFamilySignal = GetHotspotFamilySignal(lang: null);

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
            HotspotFamilyReady = hotspotFamilySignal.Ready,
            HotspotFamilyDegradedReason = hotspotFamilySignal.DegradedReason,
            CSharpSymbolNameReady = csharpSymbolNameReady,
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

    // Build the language-aware metadata-target eligibility predicate used by
    // `deps` (target_files / target_ambiguity) and `impact`
    // (IsMetadataTargetUnambiguous). Returns a SQL fragment that evaluates to
    // TRUE when a `(symbols s, files <fileAlias>)` row should be counted as a
    // plausible metadata target (`[Attribute]` / `@Annotation` / `@decorator`).
    // Rules by language:
    //   - C# (`csharp`): only `kind = 'class'` with an inheritance clause
    //     (`signature LIKE '%: %'`). Transitive base-type resolution is not
    //     available at SQL time, so "has any inheritance clause" is the
    //     portable approximation for direct `: Attribute` plus indirect
    //     `: BaseAudit` where `BaseAudit` itself derives from Attribute.
    //     Extractor-driven authoritative `is_metadata_target` classification is
    //     tracked as a follow-up (issue #435) and would let `deps` / `impact`
    //     reject non-attribute classes like `class MyAuditAttribute : BaseService`
    //     that this heuristic cannot distinguish.
    //     For legacy-migration DBs whose `signature` column exists but stores
    //     NULL for individual C# class rows, fall back to the canonical C#
    //     attribute-naming convention (`name LIKE '%Attribute'`). This is
    //     strictly narrower than the previous unconditional NULL-signature
    //     pass-through and prevents every NULL-signature class from being
    //     treated as a plausible metadata target. DBs without any `signature`
    //     column at all degrade to the same naming heuristic.
    //   - JS / TS (`javascript` / `typescript`): decorators target runtime
    //     entities — classes and factory `function` definitions
    //     (e.g. `function sealed(target) {}` used as `@sealed class Foo {}`).
    //     TypeScript `interface` is a compile-time type-only construct and
    //     cannot be a decorator target at runtime; including it would let a
    //     same-name `interface` inject false ambiguity against the real
    //     `function` or `class` provider and silently drop the decorator edge.
    //   - Everything else (Java `@interface`, Kotlin `annotation class`,
    //     Scala annotation classes, etc.): the annotation target is a
    //     class-like declaration, so keep the original class-like candidate
    //     set (`class` / `struct` / `interface`).
    // `deps` と `impact` で共有する言語別 metadata-target 適格性判定。
    // C# は `kind = 'class'` かつ継承節を持つ行を対象とする（直接/間接の Attribute 継承を
    // ポータブルに近似するため）。signature 列は存在するが値が NULL の legacy-migration
    // DB では C# の命名規約 `name LIKE '%Attribute'` にフォールバック — 従来の
    // 無条件許容より厳密で、NULL-signature の全 class を metadata target 扱いしない。
    // signature 列自体が無い旧 DB も同じ命名規約ヒューリスティックを使う。
    // extractor 主導の authoritative な `is_metadata_target` 判定は follow-up（issue #435）
    // として追跡しており、schema 化すれば `class MyAuditAttribute : BaseService` のような
    // 非 attribute 継承も厳密に除外できるが、現状のヒューリスティックでは判別できない。
    // JS / TS は decorator が runtime entity (class / factory function) のみ対象。
    // TypeScript の `interface` は型定義で runtime decorator target にならないため除外し、
    // 同名 `interface` が本物の `function` / `class` provider を曖昧化するのを防ぐ。
    // それ以外は従来どおり class-like を候補にする。
    private string BuildMetadataTargetKindExpr(string fileAlias)
    {
        // C# clause — class only (interface/struct cannot be attribute targets).
        // Non-NULL signature: accept any inheritance clause (`: %`) as the portable
        // approximation of direct/indirect Attribute derivation (see issue #435).
        // NULL signature: require the C# attribute naming convention
        // (`name LIKE '%Attribute'`). This is strictly narrower than the previous
        // unconditional NULL pass-through and prevents arbitrary NULL-signature
        // classes on a legacy-migration DB from being treated as metadata targets.
        // DBs missing the `signature` column entirely degrade to the same naming
        // heuristic.
        // C# は class のみ（interface/struct は attribute target にできない）。
        // 非 NULL signature は従来どおり継承節 `: %` で判定（直接/間接 Attribute の近似）。
        // NULL signature は C# 命名規約 `name LIKE '%Attribute'` に縮退 — 従来の
        // 無条件許容より厳密で、legacy-migration DB で任意の NULL-signature class が
        // metadata target 扱いされるのを防ぐ。signature 列欠落 DB も同じ命名規約を使う。
        var csharpClause = _symbolColumns.Contains("signature")
            ? $"({fileAlias}.lang = 'csharp' AND s.kind = 'class' AND ((s.signature IS NOT NULL AND s.signature LIKE '%: %') OR (s.signature IS NULL AND s.name LIKE '%Attribute')))"
            : $"({fileAlias}.lang = 'csharp' AND s.kind = 'class' AND s.name LIKE '%Attribute')";
        // JS / TS clause — decorators target runtime entities (classes and factory
        // functions). TS `interface` is a type-only construct that cannot be a
        // decorator target, so excluding it avoids false ambiguity against a
        // real function/class provider sharing the same name.
        // JS / TS: decorator は runtime entity (class / factory function) のみ対象。
        // TS の `interface` は型定義のため除外しないと同名 interface が偽の曖昧さを
        // 発生させる。
        var jsClause = $"({fileAlias}.lang IN ('javascript','typescript') AND s.kind IN ('class','function'))";
        // All other graph-supported languages keep the original class-like set.
        // その他の graph 対応言語は従来どおり class-like を対象にする。
        var otherClause = $"({fileAlias}.lang NOT IN ('csharp','javascript','typescript') AND s.kind IN ('class','struct','interface'))";
        return $"({csharpClause} OR {jsClause} OR {otherClause})";
    }

    // `deps` keeps persisted SQL symbol names qualified (`dbo.fn_X`) but must
    // still join bare SQL reference rows (`fn_X`) back to that definition.
    // Normalize only the read-time dependency key instead of rewriting storage.
    // `deps` は保存済み SQL symbol 名を qualified (`dbo.fn_X`) のまま維持しつつ、
    // bare な SQL reference 行 (`fn_X`) も同じ定義へ結び付ける必要があるため、
    // 依存関係の read-time key だけ leaf 名へ正規化する。
    private static string BuildLogicalDependencySymbolNameExpr(string fileAlias, string symbolNameExpr)
        => $"CASE WHEN {fileAlias}.lang = 'sql' THEN sql_leaf_name({symbolNameExpr}) ELSE {symbolNameExpr} END";

    /// <summary>
    /// Compute file-level dependency edges: which files reference symbols defined in which other files.
    /// ファイル間の依存関係エッジを算出: どのファイルがどのファイルで定義されたシンボルを参照しているか。
    /// </summary>
    public List<FileDependencyResult> GetFileDependencies(int limit = 50, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool reverse = false)
    {
        if (!_hasReferencesTable) return new List<FileDependencyResult>();
        using var cmd = _conn.CreateCommand();
        // Aggregate logical reference sites per source-file/name first, then join that bounded
        // set to distinct target files. This avoids the per-reference × per-symbol explosion that
        // could exhaust SQLite temp-store on large indexes with many same-named symbols.
        // まず source-file/name 単位に logical reference site 数を集約し、その後で distinct な
        // target file と結合することで、大規模 index で SQLite temp-store を枯渇させる
        // per-reference × per-symbol の膨張を防ぐ。
        var sourceFilterAlias = "src";
        var targetFilterAlias = "dst";
        var targetLogicalSymbolNameExpr = BuildLogicalDependencySymbolNameExpr("dst", "s.name");
        var sql = @"
            WITH logical_references_primary AS (
                SELECT src.id AS source_file_id,
                       src.path AS source_path,
                       src.lang AS source_lang,
                       r.symbol_name,
                       r.line,
                       r.column_number,
                       " + GetLogicalReferenceKindSql("r.reference_kind") + @" AS logical_reference_kind
                FROM symbol_references r
                JOIN files src ON r.file_id = src.id
                WHERE 1 = 1";
        // `deps` intentionally includes metadata-only reference kinds
        // (`attribute` / `annotation`). Same rationale as
        // `GetFileDependencyHintsToResolvedType`: renaming or removing a type that
        // is only referenced via `[JsonConverter(typeof(User))]` or
        // `@Inject(User.class)` still breaks the annotated file at compile time, so
        // file-level dependency analysis must treat those sites as real edges.
        // Call-graph-specific commands (`callers` / `callees`) keep rejecting
        // metadata kinds at the CLI / MCP boundary — that is a separate contract.
        // `deps` は metadata-only 参照 (`attribute` / `annotation`) も意図的に
        // 含める。`GetFileDependencyHintsToResolvedType` と同じ理由で、
        // `[JsonConverter(typeof(User))]` や `@Inject(User.class)` 経由でしか参照
        // されない型でも、rename / 削除すれば annotated ファイルは compile-time
        // で壊れるため、ファイル単位の依存分析では本物の edge として扱う必要が
        // ある。call-graph 専用コマンド (`callers` / `callees`) 側では metadata
        // 種別の拒否を CLI / MCP boundary で引き続き行う — そちらは別契約。
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "src", "depsLang")}";
        if (lang != null)
            sql += " AND src.lang = @lang";
        if (!reverse && pathPatterns is { Count: > 0 })
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"{sourceFilterAlias}.path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (!reverse && excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND {sourceFilterAlias}.path NOT LIKE @excludePath{i} ESCAPE '\\'";
        }
        if (!reverse && excludeTests)
            sql += $" AND NOT {TestPathCondition.Replace("f.path", $"{sourceFilterAlias}.path")}";
        sql += @"
                GROUP BY src.id, src.path, src.lang, r.symbol_name, r.line, r.column_number, logical_reference_kind
            ),
            logical_references AS (
                SELECT source_file_id, source_path, source_lang, symbol_name, line, column_number, logical_reference_kind,
                       0 AS is_attribute_alias,
                       CASE WHEN logical_reference_kind IN ('attribute', 'annotation') THEN 1 ELSE 0 END AS is_metadata
                FROM logical_references_primary
                UNION ALL
                -- C# attribute suffix alias: [Foo] in source is stored with symbol_name='Foo',
                -- but the defining class is named 'FooAttribute'. Emit the canonical 'Foo' + 'Attribute'
                -- form so deps can match the class file as a target. The alias rows are flagged
                -- so the edges CTE can restrict them to class-like targets and avoid spurious
                -- edges to unrelated functions / properties that happen to be named 'FooAttribute'.
                -- C# 属性のサフィックス別名: ソース上の [Foo] は symbol_name='Foo' で保存されるが、
                -- 定義クラスは 'FooAttribute' 命名になるため、正規形 'Foo' + 'Attribute' を補って
                -- deps がクラス側のファイルを target として join できるようにする。alias 行には
                -- フラグを付け、edges CTE 側で class-like target だけに限定する。これにより、
                -- 偶然 'FooAttribute' という名前を持つ関数やプロパティへの誤ったエッジを防ぐ。
                SELECT source_file_id, source_path, source_lang,
                       symbol_name || 'Attribute' AS symbol_name,
                       line, column_number, logical_reference_kind,
                       1 AS is_attribute_alias,
                       1 AS is_metadata
                FROM logical_references_primary
                WHERE source_lang = 'csharp'
                  AND logical_reference_kind = 'attribute'
                  AND symbol_name NOT LIKE '%Attribute'
            ),
            source_name_counts AS (
                -- Grouping includes is_metadata so metadata-only groups ([Foo] / @Foo)
                -- can be restricted to class-like targets independently from non-metadata
                -- call-graph groups that share the same symbol_name in the same file
                -- (e.g. `Foo()` call + `[Foo]` attribute both present in the same source).
                -- is_metadata を GROUP BY に含めることで、同じ source file / symbol_name を
                -- 共有する metadata 行と call-graph 行 (例: 同じファイル内の `Foo()` 呼び出し
                -- と `[Foo]` 属性) を別グループとして扱い、metadata 側だけに class-like
                -- target 制限を掛けられるようにする。
                SELECT source_file_id,
                       source_path,
                       source_lang,
                       symbol_name,
                       is_attribute_alias,
                       is_metadata,
                       COUNT(*) AS ref_count
                FROM logical_references
                GROUP BY source_file_id, source_path, source_lang, symbol_name, is_attribute_alias, is_metadata
            ),
            target_files AS (
                -- Collapse per-symbol rows to one per (target_path, target_lang, symbol_name)
                -- and remember whether any of the same-name symbols is a class-like kind
                -- via MAX. Keeping kind in DISTINCT would split identical (path, lang, name)
                -- rows when one file defines both a class and a same-name function (e.g. a
                -- C# constructor), inflating the deps reference count.
                -- (target_path, target_lang, symbol_name) 単位に集約し、同名のシンボルの
                -- いずれかが class 系であるかを MAX で覚える。kind を DISTINCT に含めると、
                -- 同じ (path, lang, name) でも class と同名 function (C# のコンストラクタ等)
                -- が別行として残り、deps の参照カウントが膨らんでしまう。
                -- has_metadata_target_kind further narrows the class-like set to targets
                -- that can legitimately be referenced as [Attribute] metadata. For C#
                -- we cannot resolve base types transitively at SQL time, so the best
                -- portable approximation is an inheritance-clause check: any class
                -- declared with a base list is a potential attribute type (direct or
                -- indirect Attribute derivation). A plain class FooAttribute with no
                -- base clause is not a valid [Foo] target at compile time.
                -- Other languages keep the original class-like breadth. Legacy DBs
                -- without a signature column degrade to the broad class-like set.
                -- has_metadata_target_kind は [Attribute] metadata target として妥当な
                -- class-like のみに絞る。C# は SQL 時点で基底型を遡れないため、継承節を
                -- 持つクラスを候補とする近似を採る(直接・間接の Attribute 継承を
                -- 取りこぼさない)。他言語は class-like 全体を残す。signature 列が無い
                -- legacy DB では filter を無効化し class-like 全体に戻る。
                SELECT dst.path AS target_path,
                       dst.lang AS target_lang,
                       " + targetLogicalSymbolNameExpr + @" AS symbol_name,
                       MAX(CASE WHEN s.kind IN ('class','struct','interface') THEN 1 ELSE 0 END) AS has_class_like_kind,
                       MAX(CASE WHEN " + BuildMetadataTargetKindExpr("dst") + @"
                                THEN 1 ELSE 0 END) AS has_metadata_target_kind
                FROM symbols s
                JOIN files dst ON s.file_id = dst.id
                WHERE 1 = 1";
        sql += $" AND {BuildGraphSupportedLanguagePredicate(cmd, "dst", "depsTargetLang")}";
        if (lang != null)
            sql += " AND dst.lang = @lang";
        if (reverse && pathPatterns is { Count: > 0 })
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"{targetFilterAlias}.path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (reverse && excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND {targetFilterAlias}.path NOT LIKE @excludePath{i} ESCAPE '\\'";
        }
        if (reverse && excludeTests)
            sql += $" AND NOT {TestPathCondition.Replace("f.path", $"{targetFilterAlias}.path")}";
        sql += @"
                GROUP BY dst.path, dst.lang, " + targetLogicalSymbolNameExpr + @"
            ),
            metadata_raw_suppression AS (
                -- When a raw C# attribute reference '[Foo]' (stored as symbol_name='Foo',
                -- logical_reference_kind='attribute') also has a synthetic suffix alias
                -- row that resolves to a class-like 'FooAttribute' target, drop the raw
                -- row to avoid creating a duplicate edge to any unrelated 'Foo' symbol
                -- (method, property, local class) that merely shares the bare name.
                -- 生の C# 属性参照 '[Foo]' (symbol_name='Foo', kind='attribute') に対して
                -- 同じ source_file 内で 'FooAttribute' の synthetic alias 行が
                -- class 系 target に解決できる場合、この行自体は落として
                -- 同名の関数/プロパティ/ローカルクラス 'Foo' への誤依存を防ぐ。
                SELECT DISTINCT lrp.source_file_id, lrp.symbol_name
                FROM logical_references_primary lrp
                JOIN target_files tf_alias
                  ON tf_alias.target_lang = lrp.source_lang
                 AND tf_alias.symbol_name = lrp.symbol_name || 'Attribute'
                 AND tf_alias.has_metadata_target_kind = 1
                WHERE lrp.source_lang = 'csharp'
                  AND lrp.logical_reference_kind = 'attribute'
                  AND lrp.symbol_name NOT LIKE '%Attribute'
            ),
            target_ambiguity AS (
                -- Count class-like definitions at symbol-identity level rather than
                -- file level. Two same-named class-like definitions in the same file
                -- (e.g. `namespace A { class FooAttribute { } } namespace B { class
                -- FooAttribute { } }` both inside one .cs file) collapse to a single
                -- target_files row because target_files is GROUPed by dst.path, so
                -- COUNT(DISTINCT target_path) alone would see count=1 and falsely
                -- treat the metadata target as unambiguous. Joining target_files back
                -- through files + symbols recovers the per-definition row count while
                -- still inheriting target_files' lang / path / graph-supported scope
                -- (since the join only keeps rows whose (path, lang, name) already
                -- appear in target_files).
                -- class-like 定義は path 単位ではなく symbol identity 単位で数える。
                -- 同じ .cs ファイル内に別名前空間で同名 class-like が 2 つあるケースは
                -- target_files (dst.path で GROUP BY) 上では 1 行に潰れており、
                -- COUNT(DISTINCT target_path) だけでは count=1 となり metadata target
                -- が一意と誤判定される。target_files から files + symbols に JOIN し直す
                -- ことで定義単位の件数を復元する。JOIN が target_files 既存行にしか
                -- 当たらないため、lang / path / graph-supported スコープはそのまま継承。
                SELECT tf.target_lang,
                       tf.symbol_name,
                       COUNT(*) AS class_like_target_count
                FROM target_files tf
                JOIN files dst
                  ON dst.path = tf.target_path
                 AND dst.lang = tf.target_lang
                JOIN symbols s
                  ON s.file_id = dst.id
                 AND " + targetLogicalSymbolNameExpr + @" = tf.symbol_name
                 -- Same language-aware metadata-eligibility filter as
                 -- target_files: C# restricts to `class` with inheritance
                 -- clause (interface/struct cannot be attribute targets);
                 -- JS/TS additionally accepts `function` (decorator
                 -- factory); others keep the class-like candidate set.
                 -- target_files と同じ言語別 metadata 適格性フィルタ。
                 -- C# は class 限定 + 継承節 (interface/struct は除外)。
                 -- JS/TS は decorator factory 用に function も許容。
                 -- それ以外は class-like 全体を候補にする。
                 AND " + BuildMetadataTargetKindExpr("dst") + @"
                WHERE tf.has_metadata_target_kind = 1
                GROUP BY tf.target_lang, tf.symbol_name
            ),
            edges AS (
                SELECT snc.source_path,
                       tf.target_path,
                       snc.symbol_name,
                       snc.ref_count
                FROM source_name_counts snc
                JOIN target_files tf
                  ON tf.symbol_name = snc.symbol_name
                 AND tf.target_lang = snc.source_lang
                LEFT JOIN metadata_raw_suppression mrs
                  ON mrs.source_file_id = snc.source_file_id
                 AND mrs.symbol_name = snc.symbol_name
                LEFT JOIN target_ambiguity ta
                  ON ta.target_lang = snc.source_lang
                 AND ta.symbol_name = snc.symbol_name
                WHERE snc.source_path != tf.target_path
                  -- All metadata references ([Foo] / @Foo) and their synthetic C#
                  -- suffix aliases must only match class-like target kinds; otherwise
                  -- a metadata reference would spuriously depend on any file that
                  -- merely defines a function / property / variable sharing the name.
                  -- Non-metadata call-graph refs keep matching any kind so e.g. a
                  -- constructor call can still tie back to a class definition.
                  -- metadata 参照 ([Foo] / @Foo) と C# の合成 alias 行はいずれも
                  -- class 系の target 種別にのみ一致させる。これを許すと同名の
                  -- 関数/プロパティ/変数を持つだけのファイルまで誤って依存してしまう。
                  -- 非 metadata の call-graph 参照は任意の kind に一致させて構わない
                  -- (コンストラクタ呼び出しがクラス定義に結び付くケースなど)。
                  AND (snc.is_metadata = 0 OR tf.has_metadata_target_kind = 1)
                  -- Drop raw C# '[Foo]' rows when the suffix alias already resolves
                  -- to a class-like 'FooAttribute' target in the same source file.
                  -- 同じ source file で suffix alias が class 系 'FooAttribute' に
                  -- 解決できている C# の raw '[Foo]' 行は落とす。
                  AND NOT (
                        snc.is_metadata = 1
                    AND snc.is_attribute_alias = 0
                    AND snc.source_lang = 'csharp'
                    AND mrs.source_file_id IS NOT NULL
                  )
                  -- Metadata edges only survive when the target symbol resolves to
                  -- a single class-like definition within scope; ambiguous cases
                  -- (multiple same-name attribute / annotation classes) are dropped.
                  -- metadata エッジは同名 class 系 target が 1 つだけのときのみ残す。
                  AND (snc.is_metadata = 0 OR COALESCE(ta.class_like_target_count, 0) <= 1)
            )
            SELECT source_path,
                   target_path,
                   SUM(ref_count) AS reference_count,
                   GROUP_CONCAT(symbol_name) AS symbols
            FROM edges
            GROUP BY source_path, target_path
            ORDER BY reference_count DESC, source_path, target_path
            LIMIT @limit";

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
