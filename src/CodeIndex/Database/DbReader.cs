using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Handles read/query operations against the database for search, symbols, and files.
/// 検索・シンボル・ファイル一覧などのDB読み取り操作を担当する。
/// </summary>
public partial class DbReader
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

    internal static string EscapeLikeQuery(string input)
    {
        return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    /// <summary>
    /// List indexed files, optionally filtered by name pattern and language.
    /// インデックス済みファイルを一覧（名前パターン・言語でフィルタ可能）。
    /// </summary>
    public List<FileResult> ListFiles(string? query = null, int limit = 20, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, DateTime? since = null)
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
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";
        AppendPathFilters(ref sql, pathPattern, excludePathPatterns, excludeTests);
        sql += $" GROUP BY f.id ORDER BY {PathBucketOrder}, f.path LIMIT @limit";

        cmd.CommandText = sql;
        if (query != null)
            cmd.Parameters.AddWithValue("@query", $"%{EscapeLikeQuery(query)}%");
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
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

    /// <summary>
    /// Compute file-level dependency edges: which files reference symbols defined in which other files.
    /// ファイル間の依存関係エッジを算出: どのファイルがどのファイルで定義されたシンボルを参照しているか。
    /// </summary>
    public List<FileDependencyResult> GetFileDependencies(int limit = 50, string? lang = null, string? pathPattern = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool reverse = false)
    {
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
        if (lang != null)
            innerSql += " AND src.lang = @lang";
        if (pathPattern != null)
            innerSql += $" AND {filterAlias}.path LIKE @pathPattern ESCAPE '\\'";
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
        if (pathPattern != null)
            cmd.Parameters.AddWithValue("@pathPattern", $"%{EscapeLikeQuery(pathPattern)}%");
        if (excludePathPatterns is { Count: > 0 })
        {
            for (int i = 0; i < excludePathPatterns.Count; i++)
                cmd.Parameters.AddWithValue($"@excludePath{i}", $"%{EscapeLikeQuery(excludePathPatterns[i])}%");
        }
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<FileDependencyResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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

    /// <summary>
    /// Get all file validation issues from the index.
    /// インデックスから全ファイル検証問題を取得する。
    /// </summary>
    public List<Models.FileIssue> GetIssues(string? kind = null, string? pathPattern = null)
    {
        using var cmd = _conn.CreateCommand();
        var sql = @"
            SELECT f.path, i.kind, i.line, i.message
            FROM file_issues i
            JOIN files f ON i.file_id = f.id
            WHERE 1=1";
        if (kind != null)
            sql += " AND i.kind = @kind";
        if (pathPattern != null)
            sql += " AND f.path LIKE @pathPattern ESCAPE '\\'";
        sql += " ORDER BY f.path, i.line";

        cmd.CommandText = sql;
        if (kind != null)
            cmd.Parameters.AddWithValue("@kind", kind);
        if (pathPattern != null)
            cmd.Parameters.AddWithValue("@pathPattern", $"%{EscapeLikeQuery(pathPattern)}%");

        var results = new List<Models.FileIssue>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
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
