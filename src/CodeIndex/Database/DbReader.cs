using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Handles read/query operations against the database for search, symbols, and files.
/// 検索・シンボル・ファイル一覧などのDB読み取り操作を担当する。
/// </summary>
public class DbReader
{
    private readonly SqliteConnection _conn;
    private readonly HashSet<string> _fileColumns;
    private readonly HashSet<string> _symbolColumns;
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
        _fileColumns = LoadColumns("files");
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
    internal static string EscapeLikeQuery(string input)
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
                   COUNT(s.id) as symbol_count,
                   " + GetFileColumnSql("checksum") + @" AS checksum,
                   " + GetFileColumnSql("modified") + @" AS modified,
                   " + GetFileColumnSql("indexed_at") + @" AS indexed_at
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
                Checksum = reader.IsDBNull(5) ? null : reader.GetString(5),
                Modified = GetNullableDateTime(reader, 6),
                IndexedAt = GetNullableDateTime(reader, 7),
            });
        }
        return results;
    }

    /// <summary>
    /// Search indexed references such as call sites.
    /// 呼び出し箇所などのインデックス済み参照を検索する。
    /// </summary>
    public List<ReferenceResult> SearchReferences(string? query = null, int limit = 20, string? lang = null, string? referenceKind = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.symbol_name, r.reference_kind, r.line, r.column_number,
                   r.context, r.container_kind, r.container_name
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE 1=1";

        if (query != null)
            sql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {PathBucketOrder}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, CASE WHEN lower(r.symbol_name) LIKE lower(@rankingQueryPrefix) ESCAPE '\\' THEN 0 ELSE 1 END, f.path, r.line LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
        {
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
            cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
            cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(query.Trim())}%");
        }
        else
        {
            cmd.Parameters.AddWithValue("@rankingQuery", "");
            cmd.Parameters.AddWithValue("@rankingQueryPrefix", "%");
        }
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<ReferenceResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ReferenceResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                SymbolName = reader.GetString(2),
                ReferenceKind = reader.GetString(3),
                Line = reader.GetInt32(4),
                Column = reader.GetInt32(5),
                Context = reader.GetString(6),
                ContainerKind = reader.IsDBNull(7) ? null : reader.GetString(7),
                ContainerName = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }
        return results;
    }

    /// <summary>
    /// Find callers for a referenced symbol.
    /// 指定シンボルを呼び出している呼び出し元を探す。
    /// </summary>
    public List<CallerResult> GetCallers(string query, int limit = 20, string? lang = null, string? referenceKind = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += " AND r.reference_kind IN ('call', 'instantiate')";
        sql += " AND r.symbol_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name ORDER BY {PathBucketOrder}, CASE WHEN lower(r.symbol_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, f.path, first_line LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<CallerResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CallerResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                CallerKind = reader.IsDBNull(2) ? null : reader.GetString(2),
                CallerName = reader.IsDBNull(3) ? null : reader.GetString(3),
                CalleeName = reader.GetString(4),
                FirstLine = reader.GetInt32(5),
                ReferenceCount = reader.GetInt32(6),
            });
        }
        return results;
    }

    /// <summary>
    /// Find callees used by a caller/container symbol.
    /// 呼び出し元シンボルが使っている呼び出し先を探す。
    /// </summary>
    public List<CalleeResult> GetCallees(string query, int limit = 20, string? lang = null, string? referenceKind = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        using var cmd = _conn.CreateCommand();

        var sql = @"
            SELECT f.path, f.lang, r.container_kind, r.container_name, r.symbol_name,
                   r.reference_kind, MIN(r.line) AS first_line, COUNT(*) AS reference_count
            FROM symbol_references r
            JOIN files f ON r.file_id = f.id
            WHERE r.container_name IS NOT NULL";

        if (referenceKind != null)
            sql += " AND r.reference_kind = @referenceKind";
        else
            sql += " AND r.reference_kind IN ('call', 'instantiate')";
        sql += " AND r.container_name LIKE @query ESCAPE '\\'";
        if (lang != null)
            sql += " AND f.lang = @lang";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.path, f.lang, r.container_kind, r.container_name, r.symbol_name, r.reference_kind ORDER BY {PathBucketOrder}, CASE WHEN lower(r.container_name) = lower(@rankingQuery) THEN 0 ELSE 1 END, reference_count DESC, f.path, first_line LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        if (referenceKind != null)
            cmd.Parameters.AddWithValue("@referenceKind", referenceKind);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        AddPathFilterParameters(cmd, pathPattern, excludePathPatterns);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<CalleeResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CalleeResult
            {
                Path = reader.GetString(0),
                Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
                CallerKind = reader.IsDBNull(2) ? null : reader.GetString(2),
                CallerName = reader.IsDBNull(3) ? null : reader.GetString(3),
                CalleeName = reader.GetString(4),
                ReferenceKind = reader.GetString(5),
                FirstLine = reader.GetInt32(6),
                ReferenceCount = reader.GetInt32(7),
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
    /// Get one indexed file by exact path.
    /// 完全一致パスでインデックス済みファイルを1件取得する。
    /// </summary>
    public FileResult? GetFileByPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT f.path, f.lang, f.size, f.lines,
                   COUNT(s.id) AS symbol_count,
                   {GetFileColumnSql("checksum")} AS checksum,
                   {GetFileColumnSql("modified")} AS modified,
                   {GetFileColumnSql("indexed_at")} AS indexed_at
            FROM files f
            LEFT JOIN symbols s ON s.file_id = f.id
            WHERE f.path = @path
            GROUP BY f.id";
        cmd.Parameters.AddWithValue("@path", path);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new FileResult
        {
            Path = reader.GetString(0),
            Lang = reader.IsDBNull(1) ? null : reader.GetString(1),
            Size = reader.GetInt64(2),
            Lines = reader.GetInt32(3),
            SymbolCount = reader.GetInt32(4),
            Checksum = reader.IsDBNull(5) ? null : reader.GetString(5),
            Modified = GetNullableDateTime(reader, 6),
            IndexedAt = GetNullableDateTime(reader, 7),
        };
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
    /// Get database statistics.
    /// データベースの統計情報を取得する。
    /// </summary>
    public StatusResult GetStatus()
    {
        var files = ExecuteScalar("SELECT COUNT(*) FROM files");
        var chunks = ExecuteScalar("SELECT COUNT(*) FROM chunks");
        var symbols = ExecuteScalar("SELECT COUNT(*) FROM symbols");
        var references = ExecuteScalar("SELECT COUNT(*) FROM symbol_references");
        var freshness = GetWorkspaceFreshness();

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
            References = references,
            IndexedAt = freshness.IndexedAt,
            LatestModified = freshness.LatestModified,
            Languages = langs,
        };
    }

    /// <summary>
    /// Delegate to RepoMapBuilder for repo-level overview generation.
    /// RepoMapBuilderに委譲してリポジトリ俯瞰情報を生成する。
    /// </summary>
    public RepoMapResult GetRepoMap(int limit = 10, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false)
    {
        var builder = new RepoMapBuilder(_conn, _fileColumns);
        return builder.Build(limit, lang, pathPattern, excludePathPatterns, excludeTests, GetWorkspaceFreshness);
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
    public (long FileCount, DateTime? IndexedAt) GetFreshnessHint()
    {
        var fileCount = ExecuteScalar("SELECT COUNT(*) FROM files");
        var indexedAt = ExecuteNullableDateTime(
            _fileColumns.Contains("indexed_at") ? "SELECT MAX(indexed_at) FROM files" : null);
        return (fileCount, indexedAt);
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

    internal string GetFileColumnSql(string columnName, string? fallbackSql = null)
    {
        if (_fileColumns.Contains(columnName))
            return $"f.{columnName}";

        return fallbackSql ?? "NULL";
    }

    internal static void AppendPathFilters(ref string sql, string? pathPattern, IReadOnlyList<string>? excludePathPatterns, bool excludeTests)
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

    internal static void AddPathFilterParameters(SqliteCommand cmd, string? pathPattern, IReadOnlyList<string>? excludePathPatterns)
    {
        if (pathPattern != null)
            cmd.Parameters.AddWithValue("@pathPattern", $"%{EscapeLikeQuery(pathPattern)}%");

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

    private static DateTime? ParseDateTimeValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            string text when DateTime.TryParse(text, out var parsed) => parsed.Kind == DateTimeKind.Utc ? parsed : DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
            _ => null,
        };
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
    public string? Checksum { get; set; }
    public DateTime? Modified { get; set; }
    public DateTime? IndexedAt { get; set; }
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

public class ReferenceResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string ReferenceKind { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Context { get; set; } = string.Empty;
    public string? ContainerKind { get; set; }
    public string? ContainerName { get; set; }
}

public class CallerResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string? CallerKind { get; set; }
    public string? CallerName { get; set; }
    public string CalleeName { get; set; } = string.Empty;
    public int FirstLine { get; set; }
    public int ReferenceCount { get; set; }
}

public class CalleeResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string? CallerKind { get; set; }
    public string? CallerName { get; set; }
    public string CalleeName { get; set; } = string.Empty;
    public string ReferenceKind { get; set; } = string.Empty;
    public int FirstLine { get; set; }
    public int ReferenceCount { get; set; }
}

public class StatusResult
{
    public long Files { get; set; }
    public long Chunks { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime? LatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public Dictionary<string, long> Languages { get; set; } = new();
}

public class RepoMapResult
{
    public int FileCount { get; set; }
    public long TotalLines { get; set; }
    public long TotalSymbols { get; set; }
    public long TotalReferences { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime? LatestModified { get; set; }
    public DateTime? WorkspaceIndexedAt { get; set; }
    public DateTime? WorkspaceLatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public List<RepoLanguageResult> Languages { get; set; } = [];
    public List<RepoModuleResult> Modules { get; set; } = [];
    public List<RepoFileSummaryResult> TopFiles { get; set; } = [];
    public List<RepoFileSummaryResult> LargestFiles { get; set; } = [];
    public List<RepoFileSummaryResult> SymbolRichFiles { get; set; } = [];
    public List<RepoFileSummaryResult> ReferenceRichFiles { get; set; } = [];
    public List<RepoEntrypointResult> Entrypoints { get; set; } = [];
}

public class RepoLanguageResult
{
    public string Lang { get; set; } = string.Empty;
    public int Files { get; set; }
    public long Lines { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
}

public class RepoModuleResult
{
    public string Module { get; set; } = string.Empty;
    public int Files { get; set; }
    public long Lines { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
}

public class RepoFileSummaryResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int Lines { get; set; }
    public long Size { get; set; }
    public int SymbolCount { get; set; }
    public int ReferenceCount { get; set; }
    public long? Score { get; set; }
}

public class RepoEntrypointResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Score { get; set; }
}

public class SymbolAnalysisResult
{
    public string Query { get; set; } = string.Empty;
    public FileResult? File { get; set; }
    public DateTime? WorkspaceIndexedAt { get; set; }
    public DateTime? WorkspaceLatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public string? GraphLanguage { get; set; }
    public bool? GraphSupported { get; set; }
    public string? GraphSupportReason { get; set; }
    public List<DefinitionResult> Definitions { get; set; } = [];
    public List<SymbolResult> NearbySymbols { get; set; } = [];
    public List<ReferenceResult> References { get; set; } = [];
    public List<CallerResult> Callers { get; set; } = [];
    public List<CalleeResult> Callees { get; set; } = [];
}

internal sealed class RepoFileStat
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public long Size { get; set; }
    public int Lines { get; set; }
    public int SymbolCount { get; set; }
    public int ReferenceCount { get; set; }
    public string? Checksum { get; set; }
    public DateTime? Modified { get; set; }
    public DateTime? IndexedAt { get; set; }
}
