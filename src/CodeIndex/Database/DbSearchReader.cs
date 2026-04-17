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
        return string.Join(" ", tokens.Select(FormatFtsToken));
    }

    /// <summary>
    /// Build a single FTS5 phrase token. Non-ASCII tokens (CJK, emoji, etc.) get an
    /// appended '*' prefix-match operator because FTS5's default unicode61 tokenizer
    /// treats an entire run of non-whitespace-separated codepoints as a single token,
    /// so a bare '計算' query would never match content containing '計算する'.
    /// FTS5フレーズトークンを構築。FTS5既定のunicode61トークナイザはCJK/絵文字の連続を単一トークンとして扱うため、
    /// 非ASCIIトークンには prefix match の '*' を付け、'計算' で '計算する' にマッチさせる。
    /// </summary>
    private static string FormatFtsToken(string token)
    {
        var quoted = "\"" + token.Replace("\"", "\"\"") + "\"";
        // '"phrase"*' (no space) is FTS5 prefix-phrase syntax. '"phrase" *' (with space)
        // means "phrase followed by any token", which is not what we want.
        // '"phrase"*'（スペースなし）がFTS5のprefix phrase構文。スペース有りは別意味なので付けない。
        return ContainsNonAscii(token) ? quoted + "*" : quoted;
    }

    private static bool ContainsNonAscii(string token)
    {
        foreach (var c in token)
        {
            if (c > 0x7F)
                return true;
        }
        return false;
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

    public QueryCountResult CountSearchResults(string query, string? lang = null, bool rawQuery = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool deduplicate = true, DateTime? since = null, bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new QueryCountResult(0, 0);

        using var cmd = _conn.CreateCommand();
        string sql;

        if (exact)
        {
            sql = @"
                SELECT f.path, c.start_line, c.end_line,
                       0.0 AS rank
                FROM chunks c
                JOIN files f ON c.file_id = f.id
                WHERE instr(c.content, @exactQuery) > 0";
        }
        else
        {
            var sanitizedQuery = rawQuery ? query : SanitizeFtsQuery(query);
            sql = @"
                SELECT f.path, c.start_line, c.end_line,
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
        sql += $" ORDER BY {GetSearchOrderSql()}";

        cmd.CommandText = sql;
        if (exact)
            cmd.Parameters.AddWithValue("@exactQuery", query);
        cmd.Parameters.AddWithValue("@rankingQuery", query.Trim());
        cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(query.Trim())}%");
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value.ToString("O"));
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var keptIntervals = new Dictionary<string, IntervalSet>(StringComparer.Ordinal);
        var count = 0;
        var fileCount = 0;
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            var path = reader.GetString(0);
            var startLine = reader.GetInt32(1);
            var endLine = reader.GetInt32(2);
            if (!deduplicate)
            {
                count++;
                if (!keptIntervals.ContainsKey(path))
                {
                    keptIntervals[path] = new IntervalSet();
                    fileCount++;
                }
                continue;
            }

            if (!keptIntervals.TryGetValue(path, out var intervals))
            {
                intervals = new IntervalSet();
                keptIntervals[path] = intervals;
            }

            if (!intervals.AddIfNoOverlap(startLine, endLine))
                continue;

            count++;
            if (intervals.Count == 1)
                fileCount++;
        }

        return new QueryCountResult(count, fileCount);
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

        var keptIntervals = new Dictionary<string, IntervalSet>(StringComparer.Ordinal);
        var deduped = new List<SearchResult>();
        foreach (var r in results)
        {
            if (!keptIntervals.TryGetValue(r.Path, out var intervals))
            {
                intervals = new IntervalSet();
                keptIntervals[r.Path] = intervals;
            }

            if (!intervals.AddIfNoOverlap(r.StartLine, r.EndLine))
                continue;

            deduped.Add(r);
        }
        return deduped;
    }

    private sealed class IntervalSet
    {
        private readonly List<(int Start, int End)> _intervals = [];

        public int Count => _intervals.Count;

        public bool AddIfNoOverlap(int start, int end)
        {
            if (end < start)
                (start, end) = (end, start);

            var insertIndex = FindInsertIndex(start);
            if (insertIndex > 0 && Overlaps(_intervals[insertIndex - 1], start, end))
                return false;
            if (insertIndex < _intervals.Count && Overlaps(_intervals[insertIndex], start, end))
                return false;

            _intervals.Insert(insertIndex, (start, end));
            return true;
        }

        private int FindInsertIndex(int start)
        {
            var low = 0;
            var high = _intervals.Count;
            while (low < high)
            {
                var mid = low + ((high - low) / 2);
                if (_intervals[mid].Start < start)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }

        private static bool Overlaps((int Start, int End) interval, int start, int end)
        {
            return interval.Start <= end && interval.End >= start;
        }
    }

    private static string GetSearchOrderSql()
    {
        return $"{PathBucketOrder}, {ExactSymbolMatchOrder}, {PrefixSymbolMatchOrder}, {PathTextMatchOrder}, {ChunkTextMatchOrder}, rank, f.modified DESC, f.path";
    }

}
