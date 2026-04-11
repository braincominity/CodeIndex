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
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    public List<string> GetDistinctKinds()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT kind FROM symbols ORDER BY kind";
        var kinds = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            kinds.Add(reader.GetString(0));
        return kinds;
    }

    /// <summary>
    /// Search symbols by name pattern, optionally filtered by kind and language.
    /// シンボルを名前パターンで検索（種別・言語でフィルタ可能）。
    /// </summary>
    public List<SymbolResult> SearchSymbols(string? query = null, int limit = 20, string? kind = null, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
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
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, {VisibilityOrder}, s.name, f.path, s.line LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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
    public List<DefinitionResult> GetDefinitions(string query, int limit = 20, string? kind = null, string? lang = null, bool includeBody = false, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        var symbols = SearchSymbols(query, limit, kind, lang, pathPattern, excludePathPatterns, excludeTests);
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
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read())
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
        using (var reader = symCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                symbols.Add(new OutlineSymbol
                {
                    Kind = reader.GetString(0),
                    Name = reader.GetString(1),
                    Line = reader.GetInt32(2),
                    StartLine = reader.GetInt32(3),
                    EndLine = reader.GetInt32(4),
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
    /// 最も多く参照されるシンボルを検索する（ホットスポット — 多用されるコード）。
    /// </summary>
    public List<(SymbolResult Symbol, int ReferenceCount)> GetSymbolHotspots(int limit, string? kind, string? lang, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        var sql = @"
            SELECT sr.symbol_name, COUNT(*) as ref_count,
                   s.kind, f.path, f.lang, s.line,
                   s.visibility, s.container_name
            FROM symbol_references sr
            JOIN symbols s ON sr.symbol_name = s.name
            JOIN files f ON s.file_id = f.id
            WHERE s.kind NOT IN ('import', 'namespace')";

        if (lang != null)
            sql += " AND f.lang = @lang";
        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += " GROUP BY sr.symbol_name, s.kind, f.path ORDER BY ref_count DESC LIMIT @limit";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null) cmd.Parameters.AddWithValue("@lang", lang);
        if (kind != null) cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);

        var results = new List<(SymbolResult, int)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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
    /// 参照テーブルに一致する参照がないシンボルを検索する（潜在的なデッドコード）。
    /// </summary>
    public List<SymbolResult> GetUnusedSymbols(int limit, string? kind, string? lang, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
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
            sql += " AND f.lang = @lang";
        if (kind != null)
            sql += " AND s.kind = @kind";

        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += " ORDER BY f.path, s.line LIMIT @limit";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null) cmd.Parameters.AddWithValue("@lang", lang);
        if (kind != null) cmd.Parameters.AddWithValue("@kind", kind);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);

        var results = new List<SymbolResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SymbolResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = reader.GetString(2),
                Name = reader.GetString(3),
                Line = reader.GetInt32(4),
                StartLine = reader.GetInt32(5),
                EndLine = reader.GetInt32(6),
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
