using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Full-text search operations (partial class split from DbReader.cs).
/// 全文検索操作（DbReader.csからのpartial class分割）。
/// </summary>
public partial class DbReader
{
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
    public List<SearchResult> Search(string query, int limit = 20, string? lang = null, bool rawQuery = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool deduplicate = true, DateTime? since = null, bool exact = false)
    {
        // Guard against empty/whitespace queries that would match everything
        // 空白のみのクエリが全件マッチするのを防止
        if (string.IsNullOrWhiteSpace(query))
            return [];

        using var cmd = _conn.CreateCommand();
        string sql;

        if (exact)
        {
            // Exact substring match using instr() — case-sensitive, no FTS5 tokenization
            // instr() による完全部分一致検索 — 大文字小文字区別、FTS5トークナイズなし
            sql = @"
                SELECT f.path, f.lang, c.start_line, c.end_line, c.content,
                       0.0 AS rank
                FROM chunks c
                JOIN files f ON c.file_id = f.id
                WHERE instr(c.content, @exactQuery) > 0";
        }
        else
        {
            var sanitizedQuery = rawQuery ? query : SanitizeFtsQuery(query);
            sql = @"
                SELECT f.path, f.lang, c.start_line, c.end_line, c.content,
                       rank
                FROM fts_chunks
                JOIN chunks c ON fts_chunks.rowid = c.id
                JOIN files f ON c.file_id = f.id";
            sql += " WHERE fts_chunks MATCH @query";
            cmd.Parameters.AddWithValue("@query", sanitizedQuery);
        }
        if (lang != null)
            sql += " AND f.lang = @lang";
        if (since != null && _fileColumns.Contains("modified"))
            sql += " AND f.modified >= @since";

        AppendPathFilters(ref sql, pathPatterns, excludePathPatterns, excludeTests);
        sql += $" ORDER BY {GetSearchOrderSql()} LIMIT @limit";

        cmd.CommandText = sql;
        if (exact)
            cmd.Parameters.AddWithValue("@exactQuery", query);
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(query.Trim())}%");
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var raw = new List<SearchResult>();
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            raw.Add(new SearchResult
            {
                Path = reader.GetString(0),
                Lang = GetNullableString(reader, 1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                Content = reader.GetString(4),
                Score = reader.GetDouble(5),
            });
        }
        return deduplicate ? DeduplicateOverlappingResults(raw) : raw;
    }

    /// <summary>
    /// Remove search results that overlap with a higher-ranked result in the same file.
    /// Chunks use 10-line overlap, so adjacent chunks can produce duplicate matches.
    /// 同じファイル内で上位の結果と行範囲が重なる結果を除去する。
    /// チャンクは10行重複するため、隣接チャンクが重複マッチを生じうる。
    /// </summary>
    private static List<SearchResult> DeduplicateOverlappingResults(List<SearchResult> results)
    {
        if (results.Count <= 1)
            return results;

        var deduped = new List<SearchResult>();
        foreach (var r in results)
        {
            var dominated = false;
            foreach (var kept in deduped)
            {
                if (kept.Path == r.Path && kept.StartLine <= r.EndLine && kept.EndLine >= r.StartLine)
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated)
                deduped.Add(r);
        }
        return deduped;
    }

    private static string GetSearchOrderSql()
    {
        return $"{PathBucketOrder}, {ExactSymbolMatchOrder}, {PrefixSymbolMatchOrder}, {PathTextMatchOrder}, {ChunkTextMatchOrder}, rank, f.modified DESC, f.path";
    }

}
