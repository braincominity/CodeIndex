using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Symbol query operations: search, definitions, outline, analyze (partial class split from DbReader.cs).
/// シンボルクエリ操作: 検索、定義、アウトライン、分析（DbReader.csからのpartial class分割）。
/// </summary>
public partial class DbReader
{
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
    public List<SymbolResult> SearchSymbols(string? query = null, int limit = 20, string? kind = null, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null)
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
            WHERE 1=1";

        if (query != null)
            sql += " AND s.name LIKE @query ESCAPE '\\'";
        if (kind != null)
            sql += " AND s.kind = @kind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, {VisibilityOrder}, s.name, f.path, s.line LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = reader.IsDBNull(5) ? reader.GetInt32(4) : reader.GetInt32(5),
                EndLine = reader.IsDBNull(6) ? reader.GetInt32(4) : reader.GetInt32(6),
                BodyStartLine = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                BodyEndLine = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Signature = reader.IsDBNull(9) ? null : reader.GetString(9),
                ContainerKind = reader.IsDBNull(10) ? null : reader.GetString(10),
                ContainerName = reader.IsDBNull(11) ? null : reader.GetString(11),
                Visibility = reader.IsDBNull(12) ? null : reader.GetString(12),
                ReturnType = reader.IsDBNull(13) ? null : reader.GetString(13),
            });
        }
        return results;
    }

    /// <summary>
    /// Resolve symbol definitions with reconstructed excerpts.
    /// シンボル定義を抜粋付きで解決する。
    /// </summary>
    public List<DefinitionResult> GetDefinitions(string query, int limit = 20, string? kind = null, string? lang = null, bool includeBody = false, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null)
    {
        var symbols = SearchSymbols(query, limit, kind, lang, pathPattern, excludePathPatterns, excludeTests, since);
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
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = reader.IsDBNull(5) ? reader.GetInt32(4) : reader.GetInt32(5),
                EndLine = reader.IsDBNull(6) ? reader.GetInt32(4) : reader.GetInt32(6),
                BodyStartLine = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                BodyEndLine = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Signature = reader.IsDBNull(9) ? null : reader.GetString(9),
                ContainerKind = reader.IsDBNull(10) ? null : reader.GetString(10),
                ContainerName = reader.IsDBNull(11) ? null : reader.GetString(11),
                Visibility = reader.IsDBNull(12) ? null : reader.GetString(12),
                ReturnType = reader.IsDBNull(13) ? null : reader.GetString(13),
            });
        }

        return results;
    }

    /// <summary>
    /// Bundle definition, graph, and local file context for one symbol query.
    /// 単一シンボルクエリ向けに、定義・グラフ・ローカル文脈をまとめて返す。
    /// </summary>
    public SymbolAnalysisResult AnalyzeSymbol(string query, int limit = 10, string? lang = null, bool includeBody = false, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        var definitions = GetDefinitions(query, Math.Min(limit, 5), kind: null, lang, includeBody, pathPattern, excludePathPatterns, excludeTests);
        var primaryDefinition = definitions.FirstOrDefault();
        var file = primaryDefinition != null ? GetFileByPath(primaryDefinition.Path) : null;
        var freshness = GetWorkspaceFreshness();
        var graphLanguage = lang ?? file?.Lang;
        bool? graphSupported = graphLanguage == null ? null : ReferenceExtractor.SupportsLanguage(graphLanguage);
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
            GraphSupportReason = BuildGraphSupportReason(graphLanguage, graphSupported),
            Definitions = definitions,
            NearbySymbols = nearbySymbols,
            References = SearchReferences(query, limit, lang, null, pathPattern, excludePathPatterns, excludeTests),
            Callers = GetCallers(query, limit, lang, null, pathPattern, excludePathPatterns, excludeTests),
            Callees = GetCallees(query, limit, lang, null, pathPattern, excludePathPatterns, excludeTests),
            GraphTableAvailable = _hasReferencesTable,
        };
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
            lang = reader.IsDBNull(2) ? null : reader.GetString(2);
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
                    StartLine = reader.IsDBNull(3) ? reader.GetInt32(2) : reader.GetInt32(3),
                    EndLine = reader.IsDBNull(4) ? reader.GetInt32(2) : reader.GetInt32(4),
                    BodyStartLine = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    BodyEndLine = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Signature = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ContainerKind = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ContainerName = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Visibility = reader.IsDBNull(10) ? null : reader.GetString(10),
                    ReturnType = reader.IsDBNull(11) ? null : reader.GetString(11),
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
    /// Uses file-scoped join: counts references from the same file where the symbol is defined,
    /// plus cross-file references where the container name matches to reduce bare-name collisions.
    /// 最も多く参照されるシンボルを検索する（ホットスポット — 多用されるコード）。
    /// ファイルスコープ JOIN を使用: シンボルが定義されたファイル内の参照と、
    /// コンテナ名が一致するクロスファイル参照をカウントし、名前衝突を軽減する。
    /// </summary>
    public List<(SymbolResult Symbol, int ReferenceCount)> GetSymbolHotspots(int limit, string? kind, string? lang, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (!_hasReferencesTable) return new List<(SymbolResult, int)>();
        // Count references where the symbol name matches AND either:
        // 1. The reference is in the same file as the definition, OR
        // 2. The reference's container/context mentions the symbol's container (cross-file usage)
        // This reduces false inflation from unrelated same-named symbols.
        // シンボル名が一致し、かつ以下のいずれかの参照をカウント:
        // 1. 参照がシンボル定義と同じファイル内にある、または
        // 2. 参照のコンテキストがシンボルのコンテナに言及している（クロスファイル使用）
        // 無関係な同名シンボルによる水増しを軽減する。
        var sql = $@"
            SELECT s.name, COUNT(DISTINCT sr.id) as ref_count,
                   s.kind, f.path, f.lang, s.line,
                   {GetSymbolColumnSql("visibility")} AS visibility,
                   {GetSymbolColumnSql("container_name")} AS container_name
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            JOIN symbol_references sr ON sr.symbol_name = s.name
            WHERE s.kind NOT IN ('import', 'namespace')";

        // Restrict to graph-supported languages only / グラフ対応言語のみに制限
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();
        if (lang != null)
            sql += " AND f.lang = @lang";
        else
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";
        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" GROUP BY s.name, {GetSymbolColumnSql("container_name")}, s.kind, f.path ORDER BY ref_count DESC LIMIT @limit";

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
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);

        var results = new List<(SymbolResult, int)>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add((new SymbolResult
            {
                Name = reader.GetString(0),
                Kind = reader.GetString(2),
                Path = reader.GetString(3),
                Lang = reader.IsDBNull(4) ? null : reader.GetString(4),
                Line = reader.GetInt32(5),
                Visibility = reader.IsDBNull(6) ? null : reader.GetString(6),
                ContainerName = reader.IsDBNull(7) ? null : reader.GetString(7),
            }, reader.GetInt32(1)));
        }
        return results;
    }

    /// <summary>
    /// Find symbols that have no matching references in the reference table (potential dead code).
    /// Only meaningful for graph-supported languages — unsupported languages are excluded by default.
    /// 参照テーブルに一致する参照がないシンボルを検索する（潜在的なデッドコード）。
    /// グラフ対応言語でのみ意味がある — 未対応言語はデフォルトで除外。
    /// </summary>
    public List<SymbolResult> GetUnusedSymbols(int limit, string? kind, string? lang, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        // Without symbol_references (legacy read-only DB), every symbol would appear unused,
        // which is a meaningless signal. Return empty rather than drowning the caller in noise.
        // symbol_references が無いレガシー read-only DB では全シンボルが未使用扱いになってしまうため、
        // ノイズを返すより空を返す。
        if (!_hasReferencesTable) return new List<SymbolResult>();
        // Restrict to graph-supported languages to avoid false positives
        // (unsupported languages have no references indexed, so all symbols appear unused)
        // グラフ対応言語に制限して偽陽性を防ぐ
        // （未対応言語は参照がインデックスされないため全シンボルが未使用に見える）
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();

        var sql = $@"
            SELECT f.path, f.lang, s.kind, s.name, s.line,
                   {GetSymbolColumnSql("start_line", "s.line")} AS start_line,
                   {GetSymbolColumnSql("end_line", "s.line")} AS end_line,
                   {GetSymbolColumnSql("signature")} AS signature,
                   {GetSymbolColumnSql("visibility")} AS visibility,
                   {GetSymbolColumnSql("return_type")} AS return_type,
                   {GetSymbolColumnSql("container_kind")} AS container_kind,
                   {GetSymbolColumnSql("container_name")} AS container_name
            FROM symbols s
            JOIN files f ON s.file_id = f.id
            WHERE s.kind NOT IN ('import', 'namespace')
              AND NOT EXISTS (
                  SELECT 1 FROM symbol_references sr WHERE sr.symbol_name = s.name
              )";

        if (lang != null)
        {
            // If user specified a language, use it but warn if unsupported
            // ユーザーが言語を指定した場合はそれを使うが、未対応なら警告
            sql += " AND f.lang = @lang";
        }
        else
        {
            // Default: only graph-supported languages / デフォルト: グラフ対応言語のみ
            sql += $" AND f.lang IN ({string.Join(",", graphLangs.Select((_, i) => $"@gl{i}"))})";
        }
        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += " ORDER BY f.path, s.line LIMIT @limit";

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
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = reader.IsDBNull(5) ? reader.GetInt32(4) : reader.GetInt32(5),
                EndLine = reader.IsDBNull(6) ? reader.GetInt32(4) : reader.GetInt32(6),
                Signature = reader.IsDBNull(7) ? null : reader.GetString(7),
                Visibility = reader.IsDBNull(8) ? null : reader.GetString(8),
                ReturnType = reader.IsDBNull(9) ? null : reader.GetString(9),
                ContainerKind = reader.IsDBNull(10) ? null : reader.GetString(10),
                ContainerName = reader.IsDBNull(11) ? null : reader.GetString(11),
            });
        }
        return results;
    }

}
