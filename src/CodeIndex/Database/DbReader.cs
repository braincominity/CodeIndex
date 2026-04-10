using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Handles read/query operations against the database for search, symbols, and files.
/// 検索・シンボル・ファイル一覧などのDB読み取り操作を担当する。
/// </summary>
public class DbReader
{
    private readonly SqliteConnection _conn;
    private readonly HashSet<string> _symbolColumns;
    private const string TestPathCondition = @"
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

    public DbReader(SqliteConnection connection)
    {
        _conn = connection;
        _symbolColumns = LoadColumns("symbols");
    }

    /// <summary>
    /// Sanitize user input for FTS5 MATCH by quoting each token as a phrase.
    /// FTS5 MATCH用にユーザー入力をサニタイズ（各トークンをフレーズとして引用）。
    /// Prevents FTS5 syntax errors from special characters (*, ", AND, OR, NOT, NEAR, etc.).
    /// 特殊文字（*, ", AND, OR, NOT, NEAR等）によるFTS5構文エラーを防止する。
    /// </summary>
    private static string SanitizeFtsQuery(string query)
    {
        // Escape double quotes inside the query, then wrap each whitespace-separated
        // token in double quotes so FTS5 treats them as literal phrases.
        // クエリ内のダブルクォートをエスケープし、各トークンをダブルクォートで囲む。
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return "\"\"";
        return string.Join(" ", tokens.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
    }

    /// <summary>
    /// Full-text search across indexed chunks using FTS5.
    /// FTS5を使ったチャンク全文検索。
    /// </summary>
    public List<SearchResult> Search(string query, int limit = 20, string? lang = null, bool rawQuery = false, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        // Guard against empty/whitespace queries that would match everything
        // 空白のみのクエリが全件マッチするのを防止
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var sanitizedQuery = rawQuery ? query : SanitizeFtsQuery(query);
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, c.start_line, c.end_line, c.content,
                   rank
            FROM fts_chunks
            JOIN chunks c ON fts_chunks.rowid = c.id
            JOIN files f ON c.file_id = f.id";

        sql += " WHERE fts_chunks MATCH @query";
        if (lang != null)
            sql += " AND f.lang = @lang";

        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {GetSearchOrderSql()} LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", sanitizedQuery);
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(query.Trim())}%");
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);

        var results = new List<SearchResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                Content = reader.GetString(4),
                Score = reader.GetDouble(5),
            });
        }
        return results;
    }

    /// <summary>
    /// Escape LIKE wildcards (%, _) in user input to prevent unintended pattern matching.
    /// ユーザー入力のLIKEワイルドカード（%, _）をエスケープして意図しないパターンマッチを防止。
    /// </summary>
    private static string EscapeLikeQuery(string input)
    {
        return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
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
        sql += $" ORDER BY {PathBucketOrder}, s.name, f.path, s.line LIMIT @limit";

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
    /// List indexed files, optionally filtered by name pattern and language.
    /// インデックス済みファイルを一覧（名前パターン・言語でフィルタ可能）。
    /// </summary>
    public List<FileResult> ListFiles(string? query = null, int limit = 20, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, f.size, f.lines,
                   COUNT(s.id) as symbol_count
            FROM files f
            LEFT JOIN symbols s ON s.file_id = f.id
            WHERE 1=1";

        if (query != null)
            sql += " AND f.path LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.id ORDER BY {PathBucketOrder}, f.path LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<FileResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FileResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                Size = reader.GetInt64(2),
                Lines = reader.GetInt32(3),
                SymbolCount = reader.GetInt32(4),
            });
        }
        return results;
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

        using var fileReader = fileCmd.ExecuteReader();
        if (!fileReader.Read())
            return null;

        var lang = fileReader.IsDBNull(0) ? null : fileReader.GetString(0);
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
        using var chunkReader = chunkCmd.ExecuteReader();
        while (chunkReader.Read())
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
    /// Get database statistics.
    /// データベースの統計情報を取得する。
    /// </summary>
    public StatusResult GetStatus()
    {
        var files = ExecuteScalar("SELECT COUNT(*) FROM files");
        var chunks = ExecuteScalar("SELECT COUNT(*) FROM chunks");
        var symbols = ExecuteScalar("SELECT COUNT(*) FROM symbols");

        // Language breakdown / 言語別内訳
        var langs = new Dictionary<string, long>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT lang, COUNT(*) FROM files WHERE lang IS NOT NULL GROUP BY lang ORDER BY COUNT(*) DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            langs[reader.GetString(0)] = reader.GetInt64(1);

        return new StatusResult
        {
            Files = files,
            Chunks = chunks,
            Symbols = symbols,
            Languages = langs,
        };
    }

    private long ExecuteScalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private HashSet<string> LoadColumns(string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        return columns;
    }

    private string GetSymbolColumnSql(string columnName, string? fallbackSql = null)
    {
        if (_symbolColumns.Contains(columnName))
            return $"s.{columnName}";

        return fallbackSql ?? "NULL";
    }

    private static void AppendPathFilters(ref string sql, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
    {
        if (pathPattern != null)
            sql += " AND f.path LIKE @pathPattern ESCAPE '\\'";

        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                sql += $" AND f.path NOT LIKE @excludePathPattern{i} ESCAPE '\\'";
        }

        if (excludeTests)
            sql += $" AND NOT {TestPathCondition}";
    }

    private static string GetSearchOrderSql()
    {
        return $"{PathBucketOrder}, {ExactSymbolMatchOrder}, {PrefixSymbolMatchOrder}, {PathTextMatchOrder}, {ChunkTextMatchOrder}, rank, f.path";
    }

    private static void AddPathFilterParameters(SqliteCommand cmd, string? pathPattern, IReadOnlyList<string>? excludePathPatterns)
    {
        if (pathPattern != null)
            cmd.Parameters.AddWithValue("@pathPattern", $"%{EscapeLikeQuery(pathPattern)}%");

        if (excludePathPatterns != null)
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@excludePathPattern{i}", $"%{EscapeLikeQuery(excludePathPatterns[i])}%");
        }
    }
}

// Result DTOs for query operations / クエリ操作用の結果DTO

public class SearchResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class SymbolResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Line { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int? BodyStartLine { get; set; }
    public int? BodyEndLine { get; set; }
    public string? Signature { get; set; }
    public string? ContainerKind { get; set; }
    public string? ContainerName { get; set; }
    public string? Visibility { get; set; }
    public string? ReturnType { get; set; }
}

public class FileResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public long Size { get; set; }
    public int Lines { get; set; }
    public int SymbolCount { get; set; }
}

public class FileExcerptResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class DefinitionResult : SymbolResult
{
    public string Content { get; set; } = string.Empty;
    public string? BodyContent { get; set; }
}

public class StatusResult
{
    public long Files { get; set; }
    public long Chunks { get; set; }
    public long Symbols { get; set; }
    public Dictionary<string, long> Languages { get; set; } = new();
}
