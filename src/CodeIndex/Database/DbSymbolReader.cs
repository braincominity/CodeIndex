using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Symbol query operations: search, definitions, outline, analyze (partial class split from DbReader.cs).
/// シンボルクエリ操作: 検索、定義、アウトライン、分析（DbReader.csからのpartial class分割）。
/// </summary>
public partial class DbReader
{
    private const string UnusedBucketLikelyPrivate = "likely_unused_private";
    private const string UnusedBucketMaybeNonPublic = "maybe_unused_nonpublic";
    private const string UnusedBucketPublicOrExported = "public_or_exported_no_refs";
    private const string UnusedBucketReflectionOrConfig = "reflection_or_config_suspect";
    private static readonly HashSet<string> ReflectionAttributeNames = new(StringComparer.Ordinal)
    {
        "jsonpropertyname",
        "jsonproperty",
        "jsoninclude",
        "datamember",
        "bsonelement",
        "bsonid",
        "xmlelement",
        "xmlattribute",
        "yamlmember",
        "column",
    };
    private static readonly HashSet<string> ReflectionIgnoreAttributeNames = new(StringComparer.Ordinal)
    {
        "jsonignore",
        "ignoredatamember",
    };
    private static readonly HashSet<string> AttributeTargetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "assembly",
        "module",
        "field",
        "event",
        "method",
        "param",
        "property",
        "return",
        "type",
    };
    private const int UnusedAttributeContextWindow = 16;
    private const int UnusedPublicOverfetchMultiplier = 16;
    private const int UnusedPublicOverfetchMinimum = 64;
    private const int UnusedPublicOverfetchMaximum = 1024;
    private const int UnusedPublicCandidateBudget = 2048;

    /// <summary>
    /// Escape LIKE wildcards (%, _) in user input to prevent unintended pattern matching.
    /// ユーザー入力のLIKEワイルドカード（%, _）をエスケープして意図しないパターンマッチを防止。
    /// </summary>
    /// <summary>
    /// Return all distinct symbol kinds present in the index.
    /// インデックス内の全シンボル種別を返す。
    /// </summary>
    /// <summary>
    /// Return symbol kind counts for status display.
    /// ステータス表示用のシンボル種別カウントを返す。
    /// </summary>
    public Dictionary<string, long> GetSymbolKindCounts()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT kind, COUNT(*) FROM symbols GROUP BY kind ORDER BY COUNT(*) DESC";
        var counts = new Dictionary<string, long>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    public List<string> GetDistinctKinds()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT kind FROM symbols ORDER BY kind";
        var kinds = new List<string>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            kinds.Add(reader.GetString(0));
        return kinds;
    }

    /// <summary>
    /// Search symbols by name pattern, optionally filtered by kind and language.
    /// シンボルを名前パターンで検索（種別・言語でフィルタ可能）。
    /// </summary>
    public List<SymbolResult> SearchSymbols(string? query = null, int limit = 20, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        return SearchSymbols(query == null ? null : new[] { query }, limit, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact);
    }

    public int CountSearchSymbols(string? query = null, int limit = 20, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        return CountSearchSymbols(query == null ? null : new[] { query }, limit, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact);
    }

    public bool AnySearchSymbols(IReadOnlyList<string>? queries, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        var validQueries = queries?.Where(q => !string.IsNullOrEmpty(q)).Distinct().ToList();
        if (validQueries == null || validQueries.Count == 0)
            return CountSearchSymbols(validQueries, 1, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact) > 0;

        foreach (var query in validQueries)
        {
            if (CountSearchSymbols([query], 1, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact) > 0)
                return true;
        }

        return false;
    }

    public int CountSearchSymbols(IReadOnlyList<string>? queries, int limit = 20, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        var validQueries = queries?.Where(q => !string.IsNullOrEmpty(q)).Distinct().ToList();
        if (validQueries != null && validQueries.Count > 1)
            return SearchSymbols(validQueries, limit, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact).Count;

        using var cmd = _conn.CreateCommand();

        var innerSql = @"
            SELECT 1
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE 1=1";

        if (validQueries != null && validQueries.Count == 1)
        {
            var exactColumn = exact && _foldReady ? "s.name_folded" : "s.name";
            var exactSuffix = exact && _foldReady ? string.Empty : " COLLATE NOCASE";
            innerSql += exact
                ? $" AND {exactColumn} = @query0{exactSuffix}"
                : " AND s.name LIKE @query0 ESCAPE '\\'";
        }
        if (kind != null)
            innerSql += " AND s.kind = @kind";
        if (lang != null)
            innerSql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            innerSql += " AND f.modified >= @since";
        AppendPathFilters(ref innerSql, pathPatterns, excludePathPatterns, excludeTests);
        innerSql += " LIMIT @limit";

        cmd.CommandText = $"SELECT COUNT(*) FROM ({innerSql})";
        if (validQueries != null && validQueries.Count == 1)
        {
            var value = validQueries[0];
            var paramValue = !exact
                ? $"%{EscapeLikeQuery(value)}%"
                : _foldReady
                    ? NameFold.Fold(value) ?? value
                    : value;
            cmd.Parameters.AddWithValue("@query0", paramValue);
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var raw = cmd.ExecuteScalar();
        return raw is long l ? (int)l : Convert.ToInt32(raw);
    }

    public QueryCountResult CountSearchSymbolsTotal(string? query = null, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        return CountSearchSymbolsTotal(query == null ? null : new[] { query }, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact);
    }

    public QueryCountResult CountSearchSymbolsTotal(IReadOnlyList<string>? queries, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT COUNT(*), COUNT(DISTINCT path)
            FROM (
                SELECT f.path AS path
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE 1=1";

        var effectiveQueries = queries?.Where(q => !string.IsNullOrEmpty(q)).Distinct().ToList();
        if (effectiveQueries != null && effectiveQueries.Count > 0)
        {
            var exactColumn = exact && _foldReady ? "s.name_folded" : "s.name";
            var exactSuffix = exact && _foldReady ? string.Empty : " COLLATE NOCASE";
            var orClauses = exact
                ? string.Join(" OR ", effectiveQueries.Select((_, idx) => $"{exactColumn} = @query{idx}{exactSuffix}"))
                : string.Join(" OR ", effectiveQueries.Select((_, idx) => $"s.name LIKE @query{idx} ESCAPE '\\'"));
            sql += $" AND ({orClauses})";
        }
        if (kind != null)
            sql += " AND s.kind = @kind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += ")";

        cmd.CommandText = sql;
        if (effectiveQueries != null)
        {
            for (int i = 0; i < effectiveQueries.Count; i++)
            {
                var value = effectiveQueries[i];
                var paramValue = !exact
                    ? $"%{EscapeLikeQuery(value)}%"
                    : _foldReady
                        ? NameFold.Fold(value) ?? value
                        : value;
                cmd.Parameters.AddWithValue($"@query{i}", paramValue);
            }
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        using var reader = cmd.ExecuteTrackedReader();
        return reader.TrackedRead()
            ? new QueryCountResult(reader.GetInt32(0), reader.GetInt32(1))
            : new QueryCountResult(0, 0);
    }

    /// <summary>
    /// Search symbols by one or more name patterns (OR-joined). Empty/null list returns all symbols matching other filters.
    /// When <paramref name="exact"/> is true, names are matched case-insensitively for equality instead of substring.
    /// 複数名前パターン（OR結合）でシンボルを検索。空/null なら他フィルタに一致する全シンボルを返す。
    /// <paramref name="exact"/> が true の場合、部分一致ではなく大文字小文字を無視した完全一致になる。
    /// </summary>
    public List<SymbolResult> SearchSymbols(IReadOnlyList<string>? queries, int limit = 20, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        // Multi-name queries: run one search per name to guarantee per-name candidate coverage
        // (a common/earlier-sorting name cannot starve others out of the candidate pool), then
        // round-robin interleave the per-name results under a single global `limit` cap so the
        // public `limit` contract stays "Max total results", not per-name.
        // 複数名指定: 名前ごとに独立検索して候補プールを確保した上で、round-robin で統合し、
        // 最終的に全体で `limit` 件に収める。`limit` は従来どおり「合計の上限」。
        var validQueries = queries?.Where(q => !string.IsNullOrEmpty(q)).Distinct().ToList();
        if (validQueries != null && validQueries.Count > 1)
        {
            var perName = new List<List<SymbolResult>>(validQueries.Count);
            foreach (var q in validQueries)
                perName.Add(SearchSymbols(new[] { q }, limit, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact));

            var seen = new HashSet<(string Path, int Line, string Name, string Kind)>();
            var merged = new List<SymbolResult>();
            var cursors = new int[perName.Count];
            bool advanced;
            do
            {
                advanced = false;
                for (int i = 0; i < perName.Count && merged.Count < limit; i++)
                {
                    while (cursors[i] < perName[i].Count)
                    {
                        var r = perName[i][cursors[i]++];
                        if (seen.Add((r.Path, r.Line, r.Name, r.Kind)))
                        {
                            merged.Add(r);
                            advanced = true;
                            break;
                        }
                    }
                }
            } while (advanced && merged.Count < limit);
            return merged;
        }

        using var cmd = _conn.CreateCommand();

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
            WHERE 1=1";

        var effectiveQueries = queries?.Where(q => !string.IsNullOrEmpty(q)).Distinct().ToList();
        if (effectiveQueries != null && effectiveQueries.Count > 0)
        {
            // --exact: Unicode-aware equality when FoldReady (#86), else ASCII COLLATE NOCASE.
            // Fold path: `s.name_folded = @qFolded` (indexed by idx_symbols_name_folded), query
            // value is pre-folded in .NET with NameFold.Fold so Ä vs ä / 全角 vs 半角 match.
            // Fallback: `s.name = @q COLLATE NOCASE` (indexed by idx_symbols_name_nocase). Both
            // paths stay SARGable. Using `lower(col)` would force a full scan per name.
            // --exact: FoldReady なら Unicode 折り畳み経路、未 ready ならレガシー NOCASE 経路へ fallback。
            var exactColumn = exact && _foldReady ? "s.name_folded" : "s.name";
            var exactSuffix = exact && _foldReady ? string.Empty : " COLLATE NOCASE";
            var orClauses = exact
                ? string.Join(" OR ", effectiveQueries.Select((_, idx) => $"{exactColumn} = @query{idx}{exactSuffix}"))
                : string.Join(" OR ", effectiveQueries.Select((_, idx) => $"s.name LIKE @query{idx} ESCAPE '\\'"));
            sql += $" AND ({orClauses})";
        }
        if (kind != null)
            sql += " AND s.kind = @kind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY CASE " +
            "WHEN @preferLiteralExactMatch = 1 AND s.name = @rawQuery THEN 0 " +
            "WHEN @preferCaseInsensitiveExactMatch = 1 AND s.name = @rawQuery COLLATE NOCASE THEN 1 " +
            "ELSE 2 END, " +
            $"{PathBucketOrder}, {VisibilityOrder}, s.name, f.path, s.line LIMIT @limit";

        cmd.CommandText = sql;
        if (effectiveQueries != null)
        {
            for (int idx = 0; idx < effectiveQueries.Count; idx++)
            {
                string paramValue;
                if (!exact)
                    paramValue = $"%{EscapeLikeQuery(effectiveQueries[idx])}%";
                else if (_foldReady)
                    paramValue = NameFold.Fold(effectiveQueries[idx]) ?? effectiveQueries[idx];
                else
                    paramValue = effectiveQueries[idx];
                cmd.Parameters.AddWithValue($"@query{idx}", paramValue);
            }
        }
        var preferLiteralExactMatch = effectiveQueries != null && effectiveQueries.Count == 1;
        var preferCaseInsensitiveExactMatch = effectiveQueries != null && effectiveQueries.Count == 1;
        cmd.Parameters.AddWithValue("@preferLiteralExactMatch", preferLiteralExactMatch ? 1 : 0);
        cmd.Parameters.AddWithValue("@preferCaseInsensitiveExactMatch", preferCaseInsensitiveExactMatch ? 1 : 0);
        cmd.Parameters.AddWithValue("@rawQuery", preferLiteralExactMatch ? effectiveQueries![0] : string.Empty);
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = GetInt32OrFallback(reader, 5, 4),
                EndLine = GetInt32OrFallback(reader, 6, 4),
                BodyStartLine = GetNullableInt32(reader, 7),
                BodyEndLine = GetNullableInt32(reader, 8),
                Signature = GetNullableString(reader, 9),
                ContainerKind = GetNullableString(reader, 10),
                ContainerName = GetNullableString(reader, 11),
                Visibility = GetNullableString(reader, 12),
                ReturnType = GetNullableString(reader, 13),
            });
        }
        return results;
    }

    /// <summary>
    /// Resolve symbol definitions with reconstructed excerpts.
    /// シンボル定義を抜粋付きで解決する。
    /// </summary>
    public List<DefinitionResult> GetDefinitions(string query, int limit = 20, string? kind = null, string? lang = null, bool includeBody = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        var symbols = SearchSymbols(query, limit, kind, lang, pathPatterns, excludePathPatterns, excludeTests, since, exact);
        var results = new List<DefinitionResult>();

        foreach (var symbol in symbols)
        {
            var definitionExcerpt = GetExcerpt(symbol.Path, symbol.StartLine, symbol.EndLine);
            if (definitionExcerpt == null)
                continue;

            string? bodyContent = null;
            if (includeBody && symbol.BodyStartLine != null && symbol.BodyEndLine != null)
            {
                bodyContent = GetExcerpt(symbol.Path, symbol.BodyStartLine.Value, symbol.BodyEndLine.Value)?.Content;
            }

            results.Add(new DefinitionResult
            {
                Path = symbol.Path,
                Lang = symbol.Lang,
                Kind = symbol.Kind,
                Name = symbol.Name,
                Line = symbol.Line,
                StartLine = symbol.StartLine,
                EndLine = symbol.EndLine,
                BodyStartLine = symbol.BodyStartLine,
                BodyEndLine = symbol.BodyEndLine,
                Signature = symbol.Signature,
                ContainerKind = symbol.ContainerKind,
                ContainerName = symbol.ContainerName,
                Visibility = symbol.Visibility,
                ReturnType = symbol.ReturnType,
                Content = definitionExcerpt.Content,
                BodyContent = bodyContent,
                Complexity = bodyContent != null ? SymbolExtractor.EstimateComplexity(bodyContent) : null,
            });
        }

        return results;
    }

    public QueryCountResult CountDefinitionsTotal(string query, string? kind = null, string? lang = null, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null, bool exact = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = $@"
            SELECT COUNT(*), COUNT(DISTINCT path)
            FROM (
                SELECT f.path AS path
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(query))
        {
            var exactColumn = exact && _foldReady ? "s.name_folded" : "s.name";
            var exactSuffix = exact && _foldReady ? string.Empty : " COLLATE NOCASE";
            sql += exact
                ? $" AND {exactColumn} = @query{exactSuffix}"
                : " AND s.name LIKE @query ESCAPE '\\'";
        }
        if (kind != null)
            sql += " AND s.kind = @kind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $@"
                  AND EXISTS (
                      SELECT 1
                      FROM chunks c
                      WHERE c.file_id = s.file_id
                        AND c.end_line >= {GetSymbolColumnSql("start_line", "s.line")}
                        AND c.start_line <= {GetSymbolColumnSql("end_line", "s.line")}
                  )
            )";

        cmd.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var paramValue = !exact
                ? $"%{EscapeLikeQuery(query)}%"
                : _foldReady
                    ? NameFold.Fold(query) ?? query
                    : query;
            cmd.Parameters.AddWithValue("@query", paramValue);
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        using var reader = cmd.ExecuteTrackedReader();
        return reader.TrackedRead()
            ? new QueryCountResult(reader.GetInt32(0), reader.GetInt32(1))
            : new QueryCountResult(0, 0);
    }

    /// <summary>
    /// Get nearby symbols in the same file ordered by proximity to a focus line.
    /// 同一ファイル内の近傍シンボルを、注目行からの近さ順で取得する。
    /// </summary>
    public List<SymbolResult> GetNearbySymbols(string path, int focusLine, int limit = 10, string? excludeName = null, int? excludeStartLine = null)
    {
        using var cmd = _conn.CreateCommand();

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
            WHERE f.path = @path";

        if (excludeName != null && excludeStartLine != null)
            sql += " AND NOT (s.name = @excludeName AND " + GetSymbolColumnSql("start_line", "s.line") + " = @excludeStartLine)";

        sql += " ORDER BY CASE WHEN @focusLine BETWEEN " + GetSymbolColumnSql("start_line", "s.line") + " AND " + GetSymbolColumnSql("end_line", "s.line") + " THEN 0 ELSE abs(" + GetSymbolColumnSql("start_line", "s.line") + " - @focusLine) END, " + GetSymbolColumnSql("start_line", "s.line") + " LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@focusLine", focusLine);
        cmd.Parameters.AddWithValue("@limit", limit);
        if (excludeName != null && excludeStartLine != null)
        {
            cmd.Parameters.AddWithValue("@excludeName", excludeName);
            cmd.Parameters.AddWithValue("@excludeStartLine", excludeStartLine.Value);
        }

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = GetInt32OrFallback(reader, 5, 4),
                EndLine = GetInt32OrFallback(reader, 6, 4),
                BodyStartLine = GetNullableInt32(reader, 7),
                BodyEndLine = GetNullableInt32(reader, 8),
                Signature = GetNullableString(reader, 9),
                ContainerKind = GetNullableString(reader, 10),
                ContainerName = GetNullableString(reader, 11),
                Visibility = GetNullableString(reader, 12),
                ReturnType = GetNullableString(reader, 13),
            });
        }

        return results;
    }

    /// <summary>
    /// Bundle definition, graph, and local file context for one symbol query.
    /// 単一シンボルクエリ向けに、定義・グラフ・ローカル文脈をまとめて返す。
    /// </summary>
    public SymbolAnalysisResult AnalyzeSymbol(string query, int limit = 10, string? lang = null, bool includeBody = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool exact = false, int maxLineWidth = LineWidthFormatter.DefaultMaxLineWidth)
    {
        // Propagate `exact` to every bundled sub-query so the one-round-trip AI workflow
        // (`inspect` / MCP `analyze_symbol`) keeps the same precision contract as the leaf
        // commands. Without this, `inspect Run --exact` would still pull RunAsync/RunImpact
        // into references / callers / callees. See codex review of #83.
        // `exact` は bundle 内のすべての sub-query に伝播させ、leaf コマンドと precision を揃える。
        var definitionLimit = Math.Min(limit, 5);
        DefinitionResult? primaryDefinition = null;
        var definitions = GetDefinitions(query, definitionLimit, kind: null, lang, includeBody, pathPatterns, excludePathPatterns, excludeTests, since: null, exact);
        if (exact)
        {
            primaryDefinition = definitions
                .FirstOrDefault(definition => ReferenceExtractor.SupportsSymbolGraph(definition.Lang, definition.Kind, definition.ContainerKind) == true)
                ?? definitions.FirstOrDefault();
            definitions = BuildAnalysisDefinitions(primaryDefinition, definitions, definitionLimit);
        }
        primaryDefinition ??= definitions.FirstOrDefault();
        var file = primaryDefinition != null ? GetFileByPath(primaryDefinition.Path) : null;
        var freshness = GetWorkspaceFreshness();
        var hasGraphApplicableFiles = HasGraphApplicableFiles(lang, pathPatterns, excludePathPatterns, excludeTests);
        var graphLanguage = lang ?? file?.Lang;
        var hasUnsupportedEnumMember = exact
            ? HasExactUnsupportedCSharpEnumMember(query, lang, pathPatterns, excludePathPatterns, excludeTests)
            : (primaryDefinition != null && IsCSharpEnumMemberDefinition(primaryDefinition))
                || definitions.Any(IsCSharpEnumMemberDefinition);
        var hasSupportedGraphDefinition = exact
            ? HasExactGraphSupportedDefinition(query, lang, pathPatterns, excludePathPatterns, excludeTests)
            : definitions.Any(definition => ReferenceExtractor.SupportsSymbolGraph(definition.Lang, definition.Kind, definition.ContainerKind) == true);
        var baseGraphSupported = graphLanguage == null
            ? (bool?)null
            : ReferenceExtractor.SupportsLanguage(graphLanguage);
        bool? graphSupported = hasUnsupportedEnumMember && !hasSupportedGraphDefinition
            ? false
            : baseGraphSupported;
        var graphSupportReason = ReferenceExtractor.BuildGraphSupportReasonWithUnsupportedEnumMemberGap(
            graphLanguage,
            graphSupported,
            hasUnsupportedEnumMember,
            hasSupportedGraphDefinition);
        var unsupportedSymbolKind = hasUnsupportedEnumMember ? "enum_member" : null;
        var exactSignal = exact
            ? GetAnalyzeSymbolExactQuerySignal(includeGraphSignal: hasGraphApplicableFiles)
            : (ExactQuerySignal?)null;
        var references = SearchReferences(query, limit, lang, null, pathPatterns, excludePathPatterns, excludeTests, exact, maxLineWidth);
        var callers = GetCallers(query, limit, lang, null, pathPatterns, excludePathPatterns, excludeTests, exact);
        var callees = GetCallees(query, limit, lang, null, pathPatterns, excludePathPatterns, excludeTests, exact);
        var relaxedSymbols = exact && definitions.Count == 0 && references.Count == 0 && callers.Count == 0 && callees.Count == 0
            ? SearchSymbols(query, Math.Max(limit, 5), kind: null, lang, pathPatterns, excludePathPatterns, excludeTests, since: null, exact: false)
            : null;
        var exactZeroHint = exact && definitions.Count == 0 && references.Count == 0 && callers.Count == 0 && callees.Count == 0
            ? ExactZeroHintResult.FromRelaxedMatches(
                relaxedSymbols!.Count,
                relaxedSymbols.Select(result => result.Name))
            : null;
        var nearbySymbols = primaryDefinition != null
            ? GetNearbySymbols(primaryDefinition.Path, primaryDefinition.StartLine, Math.Min(limit, 10), primaryDefinition.Name, primaryDefinition.StartLine)
            : [];

        return new SymbolAnalysisResult
        {
            Query = query,
            File = file,
            WorkspaceIndexedAt = freshness.IndexedAt,
            WorkspaceLatestModified = freshness.LatestModified,
            GraphLanguage = graphLanguage,
            GraphSupported = graphSupported,
            GraphSupportReason = graphSupportReason,
            GraphDegraded = hasUnsupportedEnumMember ? true : null,
            UnsupportedSymbolKind = unsupportedSymbolKind,
            Definitions = definitions,
            NearbySymbols = nearbySymbols,
            References = references,
            Callers = callers,
            Callees = callees,
            GraphTableAvailable = _hasReferencesTable,
            ExactZeroHint = exactZeroHint,
            ExactIndexAvailable = exactSignal?.ExactIndexAvailable,
            ExactHasMissingIndex = exactSignal?.HasMissingIndex,
            ExactHasMissingTable = exactSignal?.HasMissingTable,
            DegradedReason = exactSignal?.DegradedReason,
        };
    }

    public HashSet<string> GetUnsupportedExactGraphSymbolKinds(
        string query,
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        if (HasExactUnsupportedCSharpEnumMember(query, lang, pathPatterns, excludePathPatterns, excludeTests))
            kinds.Add("enum_member");
        return kinds;
    }

    public bool HasExactUnsupportedCSharpEnumMember(
        string query,
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests)
    {
        if (lang != null && !string.Equals(lang, "csharp", StringComparison.Ordinal))
            return false;

        return HasExactDefinitionMatch(
            query,
            lang,
            pathPatterns,
            excludePathPatterns,
            excludeTests,
            $"f.lang = 'csharp' AND s.kind = 'enum' AND {GetSymbolColumnSql("container_kind", "''")} = 'enum'");
    }

    public bool HasExactGraphSupportedDefinition(
        string query,
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests)
    {
        using var cmd = _conn.CreateCommand();
        var supportedLangFilter = BuildGraphSupportedLanguagePredicate(cmd, "f", "supportedGraphLang");
        return HasExactDefinitionMatch(
            query,
            lang,
            pathPatterns,
            excludePathPatterns,
            excludeTests,
            $"{supportedLangFilter} AND NOT (f.lang = 'csharp' AND s.kind = 'enum' AND {GetSymbolColumnSql("container_kind", "''")} = 'enum')",
            cmd);
    }

    private bool HasExactDefinitionMatch(
        string query,
        string? lang,
        IReadOnlyList<string>? pathPatterns,
        IReadOnlyList<string>? excludePathPatterns,
        bool excludeTests,
        string extraConditionSql,
        SqliteCommand? command = null)
    {
        using var ownedCommand = command == null ? _conn.CreateCommand() : null;
        var cmd = command ?? ownedCommand!;
        var nameCondition = _foldReady
            ? "s.name_folded = @query"
            : "s.name = @query COLLATE NOCASE";

        var sql = @"
            SELECT 1
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE " + nameCondition;
        if (lang != null)
            sql += " AND f.lang = @lang";
        sql += " AND " + extraConditionSql;
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " LIMIT 1";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", _foldReady ? NameFold.Fold(query) ?? query : query);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        return cmd.ExecuteScalar() != null;
    }

    public bool HasFilteredCSharpEnumSymbols(string? kind, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (lang != null && !string.Equals(lang, "csharp", StringComparison.Ordinal))
            return false;
        if (kind != null && !string.Equals(kind, "enum", StringComparison.Ordinal))
            return false;

        using var cmd = _conn.CreateCommand();
        var sql = @"
            SELECT 1
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE f.lang = 'csharp'
              AND s.kind = 'enum'";
        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += " LIMIT 1";
        cmd.CommandText = sql;
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value;
    }

    private static bool IsCSharpEnumMemberDefinition(DefinitionResult definition)
    {
        return string.Equals(definition.Lang, "csharp", StringComparison.Ordinal)
            && string.Equals(definition.Kind, "enum", StringComparison.Ordinal)
            && string.Equals(definition.ContainerKind, "enum", StringComparison.Ordinal);
    }

    private static List<DefinitionResult> BuildAnalysisDefinitions(DefinitionResult? primaryDefinition, List<DefinitionResult> definitions, int limit)
    {
        if (primaryDefinition == null || limit <= 0)
            return definitions;

        var ordered = definitions
            .Where(definition => !IsSameDefinition(definition, primaryDefinition))
            .Prepend(primaryDefinition)
            .Take(limit)
            .ToList();
        return ordered;
    }

    private static bool IsSameDefinition(DefinitionResult left, DefinitionResult right)
    {
        return string.Equals(left.Path, right.Path, StringComparison.Ordinal)
            && left.StartLine == right.StartLine
            && left.EndLine == right.EndLine
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Kind, right.Kind, StringComparison.Ordinal);
    }

    /// <summary>
    /// Return a structured outline of symbols in a single file, ordered by line.
    /// 1ファイルのシンボルを行順に構造化アウトラインとして返す。
    /// </summary>
    public OutlineResult? GetOutline(string filePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, lang, lines FROM files WHERE path = @path";
        cmd.Parameters.AddWithValue("@path", filePath);

        string? lang = null;
        int totalLines = 0;
        long fileId = 0;
        using (var reader = cmd.ExecuteTrackedReader())
        {
            if (!reader.TrackedRead())
                return null;
            fileId = reader.GetInt64(0);
            lang = GetNullableString(reader, 2);
            totalLines = reader.GetInt32(3);
        }

        using var symCmd = _conn.CreateCommand();
        symCmd.CommandText = $@"
            SELECT s.kind, s.name, s.line,
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
            WHERE s.file_id = @fileId
            ORDER BY s.line";
        symCmd.Parameters.AddWithValue("@fileId", fileId);

        var symbols = new List<OutlineSymbol>();
        using (var reader = symCmd.ExecuteTrackedReader())
        {
            while (reader.TrackedRead())
            {
                symbols.Add(new OutlineSymbol
                {
                    Kind = reader.GetString(0),
                    Name = reader.GetString(1),
                    Line = reader.GetInt32(2),
                    StartLine = GetInt32OrFallback(reader, 3, 2),
                    EndLine = GetInt32OrFallback(reader, 4, 2),
                    BodyStartLine = GetNullableInt32(reader, 5),
                    BodyEndLine = GetNullableInt32(reader, 6),
                    Signature = GetNullableString(reader, 7),
                    ContainerKind = GetNullableString(reader, 8),
                    ContainerName = GetNullableString(reader, 9),
                    Visibility = GetNullableString(reader, 10),
                    ReturnType = GetNullableString(reader, 11),
                });
            }
        }

        return new OutlineResult
        {
            Path = filePath,
            Lang = lang,
            TotalLines = totalLines,
            SymbolCount = symbols.Count,
            Symbols = symbols,
        };
    }

    /// <summary>
    /// Find symbols with the most references (hotspots — heavily used code).
    /// Counts total reference volume across the codebase for names that stay unambiguous within
    /// the active language/kind candidate set. Path and test filters only decide which logical
    /// target rows are returned, not whether a name is considered globally ambiguous. When
    /// multiple logical targets still share the same name, falls back to conservative in-target
    /// file counts; rows that collapse to one logical target family (same container or top-level
    /// file) are grouped because bare-name references cannot disambiguate the true target symbol.
    /// 最も多く参照されるシンボルを検索する（ホットスポット — 多用されるコード）。
    /// active な言語/種別候補集合の中で名前が曖昧でないシンボルは codebase 全体の参照数を数える。
    /// path/test フィルタは返す logical target 行だけを絞り、名前の曖昧性判定には使わない。
    /// 複数の logical target が同名を共有する場合は bare-name 参照で真の対象を特定できないため
    /// 保守的な in-target file 件数へフォールバックし、1 つの logical target family に収まる行は集約する。
    /// </summary>
    public List<(SymbolResult Symbol, int ReferenceCount)> GetSymbolHotspots(int limit, string? kind, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (!_hasReferencesTable) return new List<(SymbolResult, int)>();
        var containerNameSql = GetSymbolColumnSql("container_name");
        var containerQualifiedNameSql = GetSymbolColumnSql("container_qualified_name");
        var familyKeySql = GetSymbolColumnSql("family_key");
        var hotspotFamilyLangs = _hotspotFamilyReadyLanguages
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var familyLangConditionSql = hotspotFamilyLangs.Count > 0
            ? $"f.lang IN ({string.Join(",", hotspotFamilyLangs.Select((_, i) => $"@hotspotFamilyLang{i}"))})"
            : "0";
        var familyTargetKeySql = hotspotFamilyLangs.Count > 0
            ? $@"CASE
                    WHEN {familyLangConditionSql}
                     AND COALESCE({familyKeySql}, '') <> ''
                        THEN 'family|' || COALESCE(f.lang, '') || '|' || COALESCE(s.kind, '') || '|' || {familyKeySql}
                    ELSE NULL
                END"
            : "NULL";
        var containerTargetKeySql = $@"CASE
                    WHEN COALESCE({containerQualifiedNameSql}, '') <> ''
                        THEN 'container|' || CAST(s.file_id AS TEXT) || '|' || COALESCE(s.kind, '') || '|' || {containerQualifiedNameSql}
                    ELSE NULL
                END";
        // Ambiguity is computed from the unscoped language/kind candidate set so `--path`
        // cannot hide an out-of-scope duplicate and accidentally promote a same-name symbol
        // back to codebase-wide counting. Cross-file grouping is allowed only when the
        // extractor persisted an authoritative family key on a DB that is stamped as fully
        // current for hotspot-family semantics (currently partial-type families). Same-file
        // same-container overloads can still share one conservative target key, but only
        // unique names or authoritative families may promote to codebase-wide counts.
        // 曖昧性は path 非依存の候補集合で判定し、`--path` で隠れた重複定義が一意扱いに
        // 戻ってしまうことを防ぐ。cross-file の集約は current な hotspot-family semantics で
        // fully-ready と判定された DB 上の正式な family key のみに限定し、same-file の
        // same-container overload は保守的な target として扱いつつ、codebase-wide 集計への
        // 昇格は一意名か authoritative family のみに限定する。
        var sql = $@"
            WITH all_candidate_symbols AS (
                SELECT s.id, s.file_id, s.name, s.kind, f.path, f.lang, s.line,
                       {GetSymbolColumnSql("visibility")} AS visibility,
                       {containerNameSql} AS container_name,
                       CASE
                           WHEN {familyTargetKeySql} IS NOT NULL
                               THEN {familyTargetKeySql}
                           WHEN {containerTargetKeySql} IS NOT NULL
                               THEN {containerTargetKeySql}
                           ELSE 'file|' || CAST(s.file_id AS TEXT)
                       END AS logical_target_key,
                       COALESCE({familyTargetKeySql}, {containerTargetKeySql}) AS count_safe_key
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE s.kind NOT IN ('import', 'namespace')";

        // Restrict to graph-supported languages only / グラフ対応言語のみに制限
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();
        if (lang != null)
            sql += " AND f.lang = @lang";
        else
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";
        if (kind != null)
            sql += " AND s.kind = @kind";

        sql += @"
            ),
            name_cardinality AS (
                SELECT lang,
                       name,
                       COUNT(*) AS defs,
                       COUNT(DISTINCT logical_target_key) AS target_groups,
                       COUNT(DISTINCT count_safe_key) AS count_safe_groups,
                       COUNT(count_safe_key) AS count_safe_defs
                FROM all_candidate_symbols
                GROUP BY lang, name
            ),
            filtered_candidates AS (
                SELECT *
                FROM all_candidate_symbols
                WHERE 1 = 1";
        if (pathPatterns != null && pathPatterns.Count > 0)
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND path NOT LIKE @excludePathPattern{i} ESCAPE '\\'";
        }
        if (excludeTests)
            sql += $" AND NOT {TestPathCondition.Replace("f.path", "path")}";
        sql += @"
            ),
            grouped_candidates AS (
                SELECT MIN(id) AS symbol_id,
                       name,
                       kind,
                       logical_target_key
                FROM filtered_candidates
                GROUP BY logical_target_key, name, kind
            ),
            grouped_metadata AS (
                SELECT logical_target_key,
                       name,
                       kind,
                       CASE
                           WHEN COUNT(DISTINCT COALESCE(visibility, '')) = 1 THEN MIN(visibility)
                           ELSE NULL
                       END AS visibility,
                       CASE
                           WHEN COUNT(DISTINCT COALESCE(container_name, '')) = 1 THEN MIN(container_name)
                           ELSE NULL
                       END AS container_name
                FROM filtered_candidates
                GROUP BY logical_target_key, name, kind
            ),
            grouped_rows AS (
                SELECT gc.symbol_id,
                       gc.name,
                       gc.kind,
                       fc.path,
                       fc.lang,
                       fc.line,
                       gm.visibility,
                       gm.container_name,
                       gc.logical_target_key
                FROM grouped_candidates gc
                JOIN filtered_candidates fc ON fc.id = gc.symbol_id
                JOIN grouped_metadata gm
                 ON gm.logical_target_key = gc.logical_target_key
                 AND gm.name = gc.name
                 AND gm.kind = gc.kind
            ),
            logical_references AS (
                SELECT sr.file_id,
                       rf.lang,
                       sr.symbol_name,
                       sr.line,
                       sr.column_number,
                       " + GetLogicalReferenceKindSql("sr.reference_kind") + @" AS logical_reference_kind
                FROM symbol_references sr
                JOIN files rf ON rf.id = sr.file_id
                GROUP BY rf.lang, sr.file_id, sr.symbol_name, sr.line, sr.column_number, logical_reference_kind
            ),
            global_reference_counts AS (
                SELECT lang,
                       symbol_name,
                       COUNT(*) AS ref_count
                FROM logical_references
                GROUP BY lang, symbol_name
            ),
            file_target_cardinality AS (
                SELECT lang,
                       file_id,
                       name,
                       kind,
                       COUNT(DISTINCT logical_target_key) AS target_count
                FROM filtered_candidates
                GROUP BY lang, file_id, name, kind
            ),
            conservative_target_files AS (
                SELECT DISTINCT lang,
                       file_id,
                       name,
                       kind,
                       logical_target_key
                FROM filtered_candidates
            ),
            file_reference_counts AS (
                SELECT lang,
                       file_id,
                       symbol_name,
                       COUNT(*) AS ref_count
                FROM logical_references
                GROUP BY lang, file_id, symbol_name
            ),
            conservative_reference_counts AS (
                SELECT ctf.logical_target_key,
                       ctf.name,
                       ctf.kind,
                       SUM(COALESCE(frc.ref_count, 0)) AS ref_count
                FROM conservative_target_files ctf
                JOIN file_target_cardinality ftc
                  ON ftc.lang = ctf.lang
                 AND ftc.file_id = ctf.file_id
                 AND ftc.name = ctf.name
                 AND ftc.kind = ctf.kind
                 AND ftc.target_count = 1
                LEFT JOIN file_reference_counts frc
                  ON frc.lang = ctf.lang
                 AND frc.file_id = ctf.file_id
                 AND frc.symbol_name = ctf.name
                GROUP BY ctf.logical_target_key, ctf.name, ctf.kind
            ),
            reference_counts AS (
                SELECT gr.symbol_id,
                       CASE
                           WHEN nc.defs = 1
                             OR (nc.count_safe_defs = nc.defs AND nc.count_safe_groups = 1)
                               THEN COALESCE(grc.ref_count, 0)
                           ELSE COALESCE(crc.ref_count, 0)
                       END AS ref_count
                FROM grouped_rows gr
                JOIN name_cardinality nc
                  ON nc.lang = gr.lang
                 AND nc.name = gr.name
                LEFT JOIN global_reference_counts grc
                  ON grc.lang = gr.lang
                 AND grc.symbol_name = gr.name
                LEFT JOIN conservative_reference_counts crc
                  ON crc.logical_target_key = gr.logical_target_key
                 AND crc.name = gr.name
                 AND crc.kind = gr.kind
            )
            SELECT gr.name, rc.ref_count,
                   gr.kind, gr.path, gr.lang, gr.line,
                   gr.visibility, gr.container_name
            FROM grouped_rows gr
            JOIN reference_counts rc ON rc.symbol_id = gr.symbol_id
            WHERE rc.ref_count > 0
            ORDER BY rc.ref_count DESC
            LIMIT @limit";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        else
        {
            var langList = graphLangs.ToList();
            for (int i = 0; i < langList.Count; i++)
                cmd.Parameters.AddWithValue($"@gl{i}", langList[i]);
        }
        if (kind != null) cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        for (int i = 0; i < hotspotFamilyLangs.Count; i++)
            cmd.Parameters.AddWithValue($"@hotspotFamilyLang{i}", hotspotFamilyLangs[i]);

        var results = new List<(SymbolResult, int)>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add((new SymbolResult
            {
                Name = reader.GetString(0),
                Kind = reader.GetString(2),
                Path = reader.GetString(3),
                Lang = GetNullableString(reader, 4),
                Line = reader.GetInt32(5),
                Visibility = GetNullableString(reader, 6),
                ContainerName = GetNullableString(reader, 7),
            }, reader.GetInt32(1)));
        }
        return results;
    }

    /// <summary>
    /// Return grouped hotspot rows collapsed by (name, kind) after the full filtered site set
    /// has been considered, keeping the representative site deterministic.
    /// フィルタ済みの全 definition site を見た上で、(name, kind) 単位に hotspot を集約して返す。
    /// 代表 site は決定的な順序で選ぶ。
    /// </summary>
    public List<GroupedHotspotResult> GetGroupedSymbolHotspots(int limit, string? kind, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (!_hasReferencesTable) return [];

        var containerNameSql = GetSymbolColumnSql("container_name");
        var containerQualifiedNameSql = GetSymbolColumnSql("container_qualified_name");
        var familyKeySql = GetSymbolColumnSql("family_key");
        var hotspotFamilyLangs = _hotspotFamilyReadyLanguages
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var familyLangConditionSql = hotspotFamilyLangs.Count > 0
            ? $"f.lang IN ({string.Join(",", hotspotFamilyLangs.Select((_, i) => $"@hotspotFamilyLang{i}"))})"
            : "0";
        var familyTargetKeySql = hotspotFamilyLangs.Count > 0
            ? $@"CASE
                    WHEN {familyLangConditionSql}
                     AND COALESCE({familyKeySql}, '') <> ''
                        THEN 'family|' || COALESCE(f.lang, '') || '|' || COALESCE(s.kind, '') || '|' || {familyKeySql}
                    ELSE NULL
                END"
            : "NULL";
        var containerTargetKeySql = $@"CASE
                    WHEN COALESCE({containerQualifiedNameSql}, '') <> ''
                        THEN 'container|' || CAST(s.file_id AS TEXT) || '|' || COALESCE(s.kind, '') || '|' || {containerQualifiedNameSql}
                    ELSE NULL
                END";
        var graphLangs = ReferenceExtractor.GetSupportedLanguages().ToList();
        var sql = $@"
            WITH all_candidate_symbols AS (
                SELECT s.id, s.file_id, s.name, s.kind, f.path, f.lang, s.line,
                       {GetSymbolColumnSql("visibility")} AS visibility,
                       {containerNameSql} AS container_name,
                       CASE
                           WHEN {familyTargetKeySql} IS NOT NULL
                               THEN {familyTargetKeySql}
                           WHEN {containerTargetKeySql} IS NOT NULL
                               THEN {containerTargetKeySql}
                           ELSE 'file|' || CAST(s.file_id AS TEXT)
                       END AS logical_target_key,
                       COALESCE({familyTargetKeySql}, {containerTargetKeySql}) AS count_safe_key
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE s.kind NOT IN ('import', 'namespace')";

        if (lang != null)
            sql += " AND f.lang = @lang";
        else
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";
        if (kind != null)
            sql += " AND s.kind = @kind";

        sql += @"
            ),
            name_cardinality AS (
                SELECT lang,
                       name,
                       COUNT(*) AS defs,
                       COUNT(DISTINCT logical_target_key) AS target_groups,
                       COUNT(DISTINCT count_safe_key) AS count_safe_groups,
                       COUNT(count_safe_key) AS count_safe_defs
                FROM all_candidate_symbols
                GROUP BY lang, name
            ),
            filtered_candidates AS (
                SELECT *
                FROM all_candidate_symbols
                WHERE 1 = 1";
        if (pathPatterns is { Count: > 0 })
        {
            var ors = new List<string>(pathPatterns.Count);
            for (int i = 0; i < pathPatterns.Count; i++)
                ors.Add($"path LIKE @pathPattern{i} ESCAPE '\\'");
            sql += " AND (" + string.Join(" OR ", ors) + ")";
        }
        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND path NOT LIKE @excludePathPattern{i} ESCAPE '\\'";
        }
        if (excludeTests)
            sql += $" AND NOT {TestPathCondition.Replace("f.path", "path")}";
        sql += @"
            ),
            logical_references AS (
                SELECT sr.file_id,
                       rf.lang,
                       sr.symbol_name,
                       sr.line,
                       sr.column_number,
                       " + GetLogicalReferenceKindSql("sr.reference_kind") + @" AS logical_reference_kind
                FROM symbol_references sr
                JOIN files rf ON rf.id = sr.file_id
                GROUP BY rf.lang, sr.file_id, sr.symbol_name, sr.line, sr.column_number, logical_reference_kind
            ),
            global_reference_counts AS (
                SELECT lang,
                       symbol_name,
                       COUNT(*) AS ref_count
                FROM logical_references
                GROUP BY lang, symbol_name
            ),
            file_target_cardinality AS (
                SELECT lang,
                       file_id,
                       name,
                       kind,
                       COUNT(DISTINCT logical_target_key) AS target_count
                FROM filtered_candidates
                GROUP BY lang, file_id, name, kind
            ),
            conservative_target_files AS (
                SELECT DISTINCT lang,
                       file_id,
                       name,
                       kind,
                       logical_target_key
                FROM filtered_candidates
            ),
            file_reference_counts AS (
                SELECT lang,
                       file_id,
                       symbol_name,
                       COUNT(*) AS ref_count
                FROM logical_references
                GROUP BY lang, file_id, symbol_name
            ),
            conservative_reference_counts AS (
                SELECT ctf.logical_target_key,
                       ctf.name,
                       ctf.kind,
                       SUM(COALESCE(frc.ref_count, 0)) AS ref_count
                FROM conservative_target_files ctf
                JOIN file_target_cardinality ftc
                  ON ftc.lang = ctf.lang
                 AND ftc.file_id = ctf.file_id
                 AND ftc.name = ctf.name
                 AND ftc.kind = ctf.kind
                 AND ftc.target_count = 1
                LEFT JOIN file_reference_counts frc
                  ON frc.lang = ctf.lang
                 AND frc.file_id = ctf.file_id
                 AND frc.symbol_name = ctf.name
                GROUP BY ctf.logical_target_key, ctf.name, ctf.kind
            ),
            site_reference_counts AS (
                SELECT fc.id AS symbol_id,
                       CASE
                           WHEN nc.defs = 1
                             OR (nc.count_safe_defs = nc.defs AND nc.count_safe_groups = 1)
                               THEN COALESCE(grc.ref_count, 0)
                           ELSE COALESCE(crc.ref_count, 0)
                       END AS ref_count
                FROM filtered_candidates fc
                JOIN name_cardinality nc
                  ON nc.lang = fc.lang
                 AND nc.name = fc.name
                LEFT JOIN global_reference_counts grc
                  ON grc.lang = fc.lang
                 AND grc.symbol_name = fc.name
                LEFT JOIN conservative_reference_counts crc
                  ON crc.logical_target_key = fc.logical_target_key
                 AND crc.name = fc.name
                 AND crc.kind = fc.kind
            ),
            hotspot_sites AS (
                SELECT fc.id AS symbol_id,
                       fc.name,
                       fc.kind,
                       fc.path,
                       fc.lang,
                       fc.line,
                       fc.visibility,
                       fc.container_name,
                       fc.logical_target_key,
                       src.ref_count
                FROM filtered_candidates fc
                JOIN site_reference_counts src ON src.symbol_id = fc.id
                WHERE src.ref_count > 0
            ),
            ranked_sites AS (
                SELECT hs.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY hs.name, hs.kind
                           ORDER BY hs.path, hs.line, COALESCE(hs.container_name, ''), COALESCE(hs.visibility, '')
                       ) AS rep_rank
                FROM hotspot_sites hs
            ),
            grouped_reference_counts AS (
                SELECT hs.name,
                       hs.kind,
                       SUM(hs.ref_count) AS ref_count
                FROM (
                    SELECT DISTINCT name,
                           kind,
                           logical_target_key,
                           ref_count
                    FROM hotspot_sites
                ) hs
                GROUP BY hs.name, hs.kind
            ),
            grouped AS (
                SELECT hs.name,
                       hs.kind,
                       MAX(grc.ref_count) AS ref_count,
                       COUNT(*) AS definition_sites
                FROM ranked_sites hs
                JOIN grouped_reference_counts grc
                  ON grc.name = hs.name
                 AND grc.kind = hs.kind
                GROUP BY hs.name, hs.kind
            )
            SELECT g.name, g.kind, g.ref_count, g.definition_sites,
                   rep.path, rep.lang, rep.line, rep.visibility, rep.container_name,
                   (
                       SELECT GROUP_CONCAT(path, char(10))
                       FROM (
                           SELECT DISTINCT hs2.path AS path
                           FROM ranked_sites hs2
                           WHERE hs2.name = g.name
                             AND hs2.kind = g.kind
                           ORDER BY path
                       )
                   ) AS grouped_paths
            FROM grouped g
            JOIN ranked_sites rep
              ON rep.name = g.name
             AND rep.kind = g.kind
             AND rep.rep_rank = 1
            ORDER BY g.ref_count DESC, g.name, g.kind
            LIMIT @limit";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        else
        {
            for (int i = 0; i < graphLangs.Count; i++)
                cmd.Parameters.AddWithValue($"@gl{i}", graphLangs[i]);
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);
        for (int i = 0; i < hotspotFamilyLangs.Count; i++)
            cmd.Parameters.AddWithValue($"@hotspotFamilyLang{i}", hotspotFamilyLangs[i]);

        var results = new List<GroupedHotspotResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var paths = GetNullableString(reader, 9)?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? [];
            results.Add(new GroupedHotspotResult
            {
                Symbol = new SymbolResult
                {
                    Name = reader.GetString(0),
                    Kind = reader.GetString(1),
                    Path = reader.GetString(4),
                    Lang = GetNullableString(reader, 5),
                    Line = reader.GetInt32(6),
                    Visibility = GetNullableString(reader, 7),
                    ContainerName = GetNullableString(reader, 8),
                },
                ReferenceCount = reader.GetInt32(2),
                DefinitionSites = reader.GetInt32(3),
                Paths = paths,
            });
        }

        return results;
    }

    /// <summary>
    /// Find symbols that have no matching references in the reference table (potential dead code).
    /// Only meaningful for graph-supported languages — unsupported languages are excluded by default.
    /// 参照テーブルに一致する参照がないシンボルを検索する（潜在的なデッドコード）。
    /// グラフ対応言語でのみ意味がある — 未対応言語はデフォルトで除外。
    /// </summary>
    public List<UnusedSymbolResult> GetUnusedSymbols(int limit, string? kind, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        // Without symbol_references (legacy read-only DB), every symbol would appear unused,
        // which is a meaningless signal. Return empty rather than drowning the caller in noise.
        // symbol_references が無いレガシー read-only DB では全シンボルが未使用扱いになってしまうため、
        // ノイズを返すより空を返す。
        if (!_hasReferencesTable) return new List<UnusedSymbolResult>();
        if (lang != null && !ReferenceExtractor.SupportsLanguage(lang))
            return [];
        // Restrict to graph-supported languages to avoid false positives
        // (unsupported languages have no references indexed, so all symbols appear unused)
        // グラフ対応言語に制限して偽陽性を防ぐ
        // （未対応言語は参照がインデックスされないため全シンボルが未使用に見える）
        var targetCount = Math.Max(limit, 1);
        var privateLike = FetchUnusedCandidates(targetCount, 0, 0, kind, lang, pathPatterns, excludePathPatterns, excludeTests);
        var maybeNonPublic = FetchUnusedCandidates(targetCount, 1, 0, kind, lang, pathPatterns, excludePathPatterns, excludeTests);
        var reflectionOrConfig = FetchUnusedCandidates(targetCount, 3, 0, kind, lang, pathPatterns, excludePathPatterns, excludeTests);

        var publicOrExported = new List<UnusedSymbolResult>();
        var publicBucketOffset = 0;
        var publicBatchSize = Math.Min(
            Math.Max(targetCount * UnusedPublicOverfetchMultiplier, UnusedPublicOverfetchMinimum),
            UnusedPublicOverfetchMaximum);
        var publicFetchBudget = Math.Max(targetCount, Math.Max(publicBatchSize, UnusedPublicCandidateBudget));
        var publicCandidatesFetched = 0;
        while ((publicOrExported.Count < targetCount || reflectionOrConfig.Count < targetCount)
            && publicCandidatesFetched < publicFetchBudget)
        {
            var batch = FetchUnusedCandidates(publicBatchSize, 2, publicBucketOffset, kind, lang, pathPatterns, excludePathPatterns, excludeTests);
            if (batch.Count == 0)
                break;

            foreach (var candidate in batch)
            {
                if (candidate.UnusedBucket == UnusedBucketReflectionOrConfig)
                    reflectionOrConfig.Add(candidate);
                else
                    publicOrExported.Add(candidate);
            }

            publicBucketOffset += batch.Count;
            publicCandidatesFetched += batch.Count;
            if (batch.Count < publicBatchSize)
                break;
        }

        var merged = new List<UnusedSymbolResult>(privateLike.Count + maybeNonPublic.Count + publicOrExported.Count + reflectionOrConfig.Count);
        merged.AddRange(privateLike);
        merged.AddRange(maybeNonPublic);
        merged.AddRange(publicOrExported);
        merged.AddRange(reflectionOrConfig);
        return DiversifyUnusedResults(merged, limit);
    }

    private List<UnusedSymbolResult> FetchUnusedCandidates(int fetchLimit, int provisionalBucketOrder, int offset, string? kind, string? lang,
        IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();
        var visibilitySql = $"lower({GetSymbolColumnSql("visibility", "''")})";
        var signatureSql = $"lower({GetSymbolColumnSql("signature", "''")})";
        const string pathSql = "lower(f.path)";
        var isPublicOrExportedSql = $"{visibilitySql} IN ('public', 'open', 'pub', 'export')";
        var isCSharpEnumSql = "(f.lang = 'csharp' AND s.kind = 'enum')";
        var hasConfigContextSql = $@"(
                {pathSql} LIKE 'config/%'
                OR {pathSql} LIKE '%/config/%'
                OR {pathSql} LIKE 'settings/%'
                OR {pathSql} LIKE '%/settings/%'
                OR {pathSql} LIKE 'options/%'
                OR {pathSql} LIKE '%/options/%'
                OR {signatureSql} LIKE '%iconfiguration%'
                OR {signatureSql} LIKE '%configurationsection%'
                OR {signatureSql} LIKE '%ioptions%'
                OR {signatureSql} LIKE '%options<%'
            )";
        var isReflectionOrConfigSuspectSql = $@"(
                {isPublicOrExportedSql}
                AND s.kind = 'property'
                AND {hasConfigContextSql}
            )";
        var provisionalBucketOrderSql = $@"
            CASE
                WHEN {isReflectionOrConfigSuspectSql} THEN 3
                WHEN {isPublicOrExportedSql} THEN 2
                WHEN {visibilitySql} IN ('private', 'fileprivate') THEN 0
                ELSE 1
            END";

        var sql = $@"
            WITH unused_candidates AS (
                SELECT f.path, f.lang, s.kind, s.name, s.line,
                       {GetSymbolColumnSql("start_line", "s.line")} AS start_line,
                       {GetSymbolColumnSql("end_line", "s.line")} AS end_line,
                       {GetSymbolColumnSql("signature")} AS signature,
                       {GetSymbolColumnSql("visibility")} AS visibility,
                       {GetSymbolColumnSql("return_type")} AS return_type,
                       {GetSymbolColumnSql("container_kind")} AS container_kind,
                       {GetSymbolColumnSql("container_name")} AS container_name,
                       CASE WHEN {isPublicOrExportedSql} THEN 1 ELSE 0 END AS is_public_or_exported,
                       CASE WHEN {isReflectionOrConfigSuspectSql} THEN 1 ELSE 0 END AS is_reflection_or_config_suspect,
                       {provisionalBucketOrderSql} AS provisional_bucket_order
                FROM symbols s
                JOIN files f ON s.file_id = f.id
                WHERE s.kind NOT IN ('import', 'namespace')
                  AND NOT {isCSharpEnumSql}
                  AND NOT EXISTS (
                      SELECT 1 FROM symbol_references sr WHERE sr.symbol_name = s.name
                  )";

        if (lang != null)
            sql += " AND f.lang = @lang";
        else
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";

        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += @"
            )
            SELECT path, lang, kind, name, line, start_line, end_line, signature, visibility,
                   return_type, container_kind, container_name, is_public_or_exported,
                   is_reflection_or_config_suspect
            FROM unused_candidates
            WHERE provisional_bucket_order = @bucketOrder
            ORDER BY path, line, name
            LIMIT @limit OFFSET @offset";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@bucketOrder", provisionalBucketOrder);
        cmd.Parameters.AddWithValue("@limit", fetchLimit);
        cmd.Parameters.AddWithValue("@offset", offset);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        else
        {
            var langList = graphLangs.ToList();
            for (int i = 0; i < langList.Count; i++)
                cmd.Parameters.AddWithValue($"@gl{i}", langList[i]);
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var results = new List<UnusedSymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var path = reader.GetString(0);
            var kindValue = reader.GetString(2);
            var startLine = GetInt32OrFallback(reader, 5, 4);
            var isPublicOrExported = reader.GetInt32(12) != 0;
            var isReflectionOrConfigSuspect = reader.GetInt32(13) != 0;
            if (!isReflectionOrConfigSuspect && isPublicOrExported && kindValue == "property")
                isReflectionOrConfigSuspect = HasReflectionAttributeContext(path, startLine);

            var visibility = GetNullableString(reader, 8);
            var classification = ClassifyUnusedSymbol(isPublicOrExported, isReflectionOrConfigSuspect, visibility);
            results.Add(new UnusedSymbolResult
            {
                Path = path,
                Lang = GetNullableString(reader, 1),
                Kind = kindValue,
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = startLine,
                EndLine = GetInt32OrFallback(reader, 6, 4),
                Signature = GetNullableString(reader, 7),
                Visibility = visibility,
                ReturnType = GetNullableString(reader, 9),
                ContainerKind = GetNullableString(reader, 10),
                ContainerName = GetNullableString(reader, 11),
                UnusedBucket = classification.Bucket,
                UnusedConfidence = classification.Confidence,
                UnusedReason = classification.Reason,
            });
        }

        return results;
    }

    private static List<UnusedSymbolResult> DiversifyUnusedResults(List<UnusedSymbolResult> results, int limit)
    {
        if (results.Count == 0 || limit <= 0)
            return results;

        var targetCount = Math.Min(limit, results.Count);
        var buckets = OrderedUnusedBuckets
            .ToDictionary(
                bucket => bucket,
                bucket => new Queue<UnusedSymbolResult>(results.Where(result => result.UnusedBucket == bucket)),
                StringComparer.Ordinal);

        var limited = new List<UnusedSymbolResult>(targetCount);
        bool advanced;
        do
        {
            advanced = false;
            foreach (var bucket in OrderedUnusedBuckets)
            {
                var queue = buckets[bucket];
                if (queue.Count == 0)
                    continue;

                limited.Add(queue.Dequeue());
                advanced = true;
                if (limited.Count >= targetCount)
                    return limited;
            }
        } while (advanced);

        return limited;
    }

    public (int Count, int FileCount) CountUnusedSymbols(string? kind, string? lang, IReadOnlyList<string>? pathPatterns, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (!_hasReferencesTable)
            return (0, 0);
        if (lang != null && !ReferenceExtractor.SupportsLanguage(lang))
            return (0, 0);

        var graphLangs = ReferenceExtractor.GetSupportedLanguages();
        var isCSharpEnumSql = "(f.lang = 'csharp' AND s.kind = 'enum')";
        using var cmd = _conn.CreateCommand();
        var sql = @"
            SELECT COUNT(*), COUNT(DISTINCT f.path)
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE s.kind NOT IN ('import', 'namespace')
              AND NOT " + isCSharpEnumSql + @"
              AND NOT EXISTS (
                  SELECT 1 FROM symbol_references sr WHERE sr.symbol_name = s.name
              )";

        if (lang != null)
            sql += " AND f.lang = @lang";
        else
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";

        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        cmd.CommandText = sql;
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        else
        {
            var langList = graphLangs.ToList();
            for (int i = 0; i < langList.Count; i++)
                cmd.Parameters.AddWithValue($"@gl{i}", langList[i]);
        }
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        using var reader = cmd.ExecuteTrackedReader();
        if (!reader.TrackedRead())
            return (0, 0);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private bool HasReflectionAttributeContext(string path, int startLine)
    {
        if (!_hasChunksTable || startLine <= 1)
            return false;

        var excerptStart = Math.Max(1, startLine - UnusedAttributeContextWindow);
        FileExcerptResult? excerpt;
        try
        {
            excerpt = GetExcerpt(path, excerptStart, startLine + UnusedAttributeContextWindow);
        }
        catch (SqliteException ex) when (IsMissingChunksTableError(ex))
        {
            return false;
        }
        if (excerpt == null)
            return false;

        var lines = excerpt.Content.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var currentIndex = startLine - excerptStart;
        if (currentIndex < 0 || currentIndex >= lines.Length)
            return false;

        var triviaMask = BuildTriviaMask(lines);
        var attributeBlock = GetAdjacentAttributeBlock(lines, triviaMask, currentIndex);
        if (attributeBlock.Count == 0)
            return false;

        var attributeNames = ExtractNormalizedAttributeNames(attributeBlock);
        if (attributeNames.Overlaps(ReflectionIgnoreAttributeNames))
            return false;

        return attributeNames.Overlaps(ReflectionAttributeNames);
    }

    private static bool IsMissingChunksTableError(SqliteException ex) =>
        ex.SqliteErrorCode == 1
        && ex.Message.Contains("no such table: chunks", StringComparison.OrdinalIgnoreCase);

    private static List<string> GetAdjacentAttributeBlock(string[] lines, bool[] triviaMask, int anchorIndex)
    {
        var anchorLine = lines[anchorIndex];
        if (LineContainsInlineAttributeAndDeclaration(anchorLine))
            return [anchorLine.Trim()];

        var declarationIndex = anchorIndex;
        if (LooksLikeAttributeBoundaryLine(anchorLine))
        {
            declarationIndex = FindNextDeclarationLine(lines, triviaMask, anchorIndex + 1);
            if (declarationIndex < 0)
                return [];
        }

        var attributeBottom = FindPreviousNonTriviaLine(lines, triviaMask, declarationIndex - 1);
        if (attributeBottom < 0 || !LooksLikeAttributeBoundaryLine(lines[attributeBottom]))
            return [];

        var block = new List<string>();
        var bracketDepth = 0;
        var sawBracket = false;

        for (int i = attributeBottom; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (triviaMask[i])
            {
                if (sawBracket)
                    block.Add(trimmed);
                continue;
            }

            var hasBracketToken = LooksLikeAttributeBoundaryLine(trimmed);
            if (!sawBracket)
            {
                if (!hasBracketToken)
                    return [];
                sawBracket = true;
            }
            else if (bracketDepth == 0 && !hasBracketToken)
            {
                break;
            }

            block.Add(trimmed);
            bracketDepth += CountChar(trimmed, ']') - CountChar(trimmed, '[');
            if (bracketDepth < 0)
                bracketDepth = 0;
        }

        block.Reverse();
        return block;
    }

    private static bool LineContainsInlineAttributeAndDeclaration(string line)
    {
        if (!LooksLikeAttributeBoundaryLine(line))
            return false;

        return !string.IsNullOrWhiteSpace(StripLeadingCSharpAttributeLists(line));
    }

    private static int FindNextDeclarationLine(string[] lines, bool[] triviaMask, int startIndex)
    {
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (!triviaMask[i])
                return i;
        }

        return -1;
    }

    private static int FindPreviousNonTriviaLine(string[] lines, bool[] triviaMask, int startIndex)
    {
        for (int i = startIndex; i >= 0; i--)
        {
            if (!triviaMask[i])
                return i;
        }

        return -1;
    }

    private static bool[] BuildTriviaMask(string[] lines)
    {
        var triviaMask = new bool[lines.Length];
        var inBlockComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                triviaMask[i] = true;
                continue;
            }

            if (inBlockComment)
            {
                triviaMask[i] = true;
                if (trimmed.Contains("*/", StringComparison.Ordinal))
                    inBlockComment = false;
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                triviaMask[i] = true;
                continue;
            }

            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                triviaMask[i] = true;
                if (!trimmed.Contains("*/", StringComparison.Ordinal))
                    inBlockComment = true;
                continue;
            }

            if (trimmed.StartsWith('*') || trimmed.Contains("*/", StringComparison.Ordinal))
            {
                triviaMask[i] = true;
                continue;
            }
        }

        return triviaMask;
    }

    private static HashSet<string> ExtractNormalizedAttributeNames(IReadOnlyList<string> attributeBlock)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var content = string.Join("\n", attributeBlock.Where(line => !BuildSingleLineTrivia(line.Trim())));
        var parenDepth = 0;

        for (int i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '(')
            {
                parenDepth++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
                continue;
            }

            if (parenDepth != 0 || (ch != '[' && ch != ','))
                continue;

            if (!TryReadAttributeIdentifier(content, ref i, out var identifier))
                continue;

            var normalized = NormalizeAttributeIdentifier(identifier);
            if (normalized != null)
                names.Add(normalized);
        }

        return names;
    }

    private static string StripLeadingCSharpAttributeLists(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length || line[index] != '[')
            return line;

        var cursor = index;
        while (cursor < line.Length && line[cursor] == '[')
        {
            var depth = 0;
            var sawBracket = false;
            while (cursor < line.Length)
            {
                var ch = line[cursor++];
                if (ch == '[')
                {
                    depth++;
                    sawBracket = true;
                }
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0 && sawBracket)
                        break;
                }
            }

            if (depth != 0)
                return string.Empty;

            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                cursor++;
        }

        return cursor < line.Length ? line[cursor..] : string.Empty;
    }

    private static bool TryReadAttributeIdentifier(string content, ref int index, out string? identifier)
    {
        identifier = null;
        var i = index + 1;
        while (i < content.Length && char.IsWhiteSpace(content[i]))
            i++;

        var start = i;
        if (!TryConsumeAttributeName(content, ref i))
            return false;
        if (i == start)
            return false;

        while (i < content.Length && char.IsWhiteSpace(content[i]))
            i++;

        // Skip attribute targets like `[property: JsonPropertyName]`.
        var leadingIdentifier = content[start..i].Trim();
        if (i < content.Length && content[i] == ':' && (i + 1 >= content.Length || content[i + 1] != ':') && AttributeTargetNames.Contains(leadingIdentifier))
        {
            i++;
            while (i < content.Length && char.IsWhiteSpace(content[i]))
                i++;
            start = i;
            if (!TryConsumeAttributeName(content, ref i))
                return false;
            if (i == start)
                return false;
        }

        identifier = content[start..i];
        index = i - 1;
        return true;
    }

    private static bool TryConsumeAttributeName(string content, ref int index)
    {
        var consumed = false;
        while (index < content.Length)
        {
            var segmentStart = index;
            while (index < content.Length && (char.IsLetterOrDigit(content[index]) || content[index] == '_'))
                index++;
            if (index == segmentStart)
                break;

            consumed = true;
            if (index + 1 < content.Length && content[index] == ':' && content[index + 1] == ':')
            {
                index += 2;
                continue;
            }

            if (index < content.Length && content[index] == '.')
            {
                index++;
                continue;
            }

            break;
        }

        return consumed;
    }

    private static string? NormalizeAttributeIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var qualifierIndex = identifier.LastIndexOf("::", StringComparison.Ordinal);
        if (qualifierIndex >= 0)
            identifier = identifier[(qualifierIndex + 2)..];

        var lastDot = identifier.LastIndexOf('.');
        var simpleName = lastDot >= 0 ? identifier[(lastDot + 1)..] : identifier;
        if (simpleName.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            simpleName = simpleName[..^"Attribute".Length];

        return simpleName.Length == 0 ? null : simpleName.ToLowerInvariant();
    }

    private static bool BuildSingleLineTrivia(string trimmed)
    {
        return trimmed.Length == 0
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith('*')
            || trimmed.Contains("*/", StringComparison.Ordinal);
    }

    private static bool LooksLikeAttributeBoundaryLine(string line)
    {
        return line.IndexOf('[') >= 0 || line.IndexOf(']') >= 0;
    }

    private static int CountChar(string text, char value)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == value)
                count++;
        }

        return count;
    }

    private static (string Bucket, string Confidence, string Reason) ClassifyUnusedSymbol(bool isPublicOrExported, bool isReflectionOrConfigSuspect, string? visibility)
    {
        if (isReflectionOrConfigSuspect)
        {
            return (
                UnusedBucketReflectionOrConfig,
                "low",
                "public/exported property with config or attribute-driven reflection surface and no indexed references");
        }

        if (isPublicOrExported)
        {
            return (
                UnusedBucketPublicOrExported,
                "low",
                "public/exported symbol with no indexed references");
        }

        if (IsPrivateLikeVisibility(visibility))
        {
            return (
                UnusedBucketLikelyPrivate,
                "medium",
                "private/file-local symbol with no indexed references; same-file uses may still be missed");
        }

        return (
            UnusedBucketMaybeNonPublic,
            "low",
            "non-public symbol with no indexed references");
    }

    private static bool IsPrivateLikeVisibility(string? visibility)
    {
        return string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase)
            || string.Equals(visibility, "fileprivate", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] OrderedUnusedBuckets =
    [
        UnusedBucketLikelyPrivate,
        UnusedBucketMaybeNonPublic,
        UnusedBucketPublicOrExported,
        UnusedBucketReflectionOrConfig,
    ];

}
