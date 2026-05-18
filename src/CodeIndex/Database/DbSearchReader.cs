using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Full-text search operations (partial class split from DbReader.cs).
/// 全文検索操作（DbReader.csからのpartial class分割）。
/// </summary>
public partial class DbReader
{
    internal const int MaxRawFtsQueryLength = 2000;
    internal const int MaxRawFtsBooleanOperators = 64;
    internal const int MaxRawFtsNearOperators = 16;
    internal const int MaxRawFtsParenthesisDepth = 16;

    /// <summary>
    /// Sanitize user input for FTS5 MATCH by quoting each token as a phrase.
    /// FTS5 MATCH用にユーザー入力をサニタイズ（各トークンをフレーズとして引用）。
    /// Prevents FTS5 syntax errors from special characters (*, ", AND, OR, NOT, NEAR, etc.).
    /// 特殊文字（*, ", AND, OR, NOT, NEAR等）によるFTS5構文エラーを防止する。
    /// When <paramref name="prefix"/> is true, every token is treated as an FTS5 prefix phrase
    /// so callers can opt in to wildcard matching for scripts (notably CJK) that unicode61
    /// tokenizes as a single run.
    /// <paramref name="prefix"/> が true の場合、すべてのトークンを FTS5 prefix phrase として扱い、
    /// unicode61 が単一トークンにまとめる CJK のようなスクリプトでも opt-in で wildcard マッチさせる。
    /// </summary>
    internal static string SanitizeFtsQuery(string query, bool prefix)
    {
        // Escape double quotes inside the query, then wrap each whitespace-separated
        // token in double quotes so FTS5 treats them as literal phrases. A trailing
        // `*` on the user-supplied token is preserved as a prefix-search shorthand so
        // `auth*` can match `authenticate` without requiring raw FTS5 syntax.
        // クエリ内のダブルクォートをエスケープし、各トークンをダブルクォートで囲む。
        // ユーザー入力末尾の `*` は prefix 検索の shorthand として保持し、`auth*` で
        // `authenticate` を raw FTS5 構文なしに検索できるようにする。
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return "\"\"";
        return string.Join(" ", tokens.Select(token => FormatFtsToken(token, prefix)));
    }

    /// <summary>
    /// Build a single FTS5 phrase token. A trailing user-supplied `*` is preserved as a prefix
    /// shorthand; otherwise the token is quoted as a literal phrase. When <paramref name="prefix"/>
    /// is true, the token is additionally upgraded to an FTS5 prefix phrase regardless of the
    /// trailing `*`, so callers (e.g. the `--prefix` CLI flag) can opt in to wildcard matching
    /// without editing every token. The default behavior is strict: searching `計算` matches the
    /// indexed token `計算` only and does NOT widen to `計算する` — users who want the broader
    /// behavior pass `--prefix` (or append `*`) explicitly. This is a deliberate trade-off
    /// (issue #1519): previously every CJK token was unconditionally promoted to a prefix phrase
    /// because FTS5's default unicode61 tokenizer keeps a run of adjacent CJK codepoints as a
    /// single token, but that silently widened exact CJK identifier lookups and broke relevance.
    /// FTS5フレーズトークンを構築する。ユーザー入力末尾の `*` は prefix shorthand として保持し、
    /// それ以外は literal phrase として quote する。<paramref name="prefix"/> が true の場合は
    /// 末尾 `*` の有無に関わらず prefix phrase に昇格させ、`--prefix` のような CLI フラグから
    /// 全トークンを一括で wildcard 化できるようにする。デフォルト挙動は strict — `計算` を引いても
    /// indexed token `計算` のみにマッチし、`計算する` までは広げない。広範囲マッチが必要なら
    /// `--prefix` か末尾 `*` を明示的に付ける。issue #1519 の意図的なトレードオフで、以前は CJK
    /// トークンを無条件に prefix phrase に昇格させていたが（FTS5 既定の unicode61 トークナイザが
    /// 連続する CJK コードポイントを単一トークンとして扱うため）、CJK 識別子の厳密検索が静かに広がり
    /// relevance を壊していた。
    /// </summary>
    private static string FormatFtsToken(string token, bool prefix)
    {
        var hasExplicitPrefix = token.Length > 1 && token.EndsWith('*');
        var literalToken = hasExplicitPrefix ? token[..^1] : token;
        var quoted = "\"" + literalToken.Replace("\"", "\"\"") + "\"";
        // '"phrase"*' (no space) is FTS5 prefix-phrase syntax. '"phrase" *' (with space)
        // means "phrase followed by any token", which is not what we want.
        // '"phrase"*'（スペースなし）がFTS5のprefix phrase構文。スペース有りは別意味なので付けない。
        return (hasExplicitPrefix || prefix) ? quoted + "*" : quoted;
    }

    /// <summary>
    /// Full-text search across indexed chunks using FTS5.
    /// FTS5を使ったチャンク全文検索。
    /// </summary>
    public List<SearchResult> Search(string query, int limit = 20, string? lang = null, bool rawQuery = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool deduplicate = true, DateTime? since = null, bool exact = false, bool prefix = false, bool visibilityRank = true)
    {
        // Guard against empty/whitespace queries that would match everything
        // 空白のみのクエリが全件マッチするのを防止
        if (string.IsNullOrWhiteSpace(query))
            return [];

        lang = NormalizeQueryLanguage(lang);
        var normalizedQuery = rawQuery ? query : NormalizeLiteralSearchQuery(query, lang);
        using var cmd = _conn.CreateCommand();
        string sql;

        if (exact)
        {
            // Exact substring match using instr() — case-sensitive, no FTS5 tokenization
            // instr() による完全部分一致検索 — 大文字小文字区別、FTS5トークナイズなし
            sql = $@"
                SELECT f.path, f.lang, c.start_line, c.end_line, c.content,
                       0.0 AS rank,
                       {GetSearchVisibilitySql()} AS visibility
                FROM chunks c
                JOIN files f ON c.file_id = f.id{SearchSymbolMatchJoinsSql}
                WHERE instr(
                    {GetExactSearchTextSql("c.content", "f.lang")},
                    {GetExactSearchTextSql("@exactQuery", "f.lang")}
                ) > 0";
        }
        else
        {
            var sanitizedQuery = rawQuery ? ValidateRawFtsQuery(query) : SanitizeFtsQuery(normalizedQuery, prefix);
            sql = $@"
                SELECT f.path, f.lang, c.start_line, c.end_line, c.content,
                       rank,
                       {GetSearchVisibilitySql()} AS visibility
                FROM fts_chunks
                JOIN chunks c ON fts_chunks.rowid = c.id
                JOIN files f ON c.file_id = f.id{SearchSymbolMatchJoinsSql}";
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
        cmd.Parameters.AddWithValue("@rankingQuery", normalizedQuery.Trim());
        cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(normalizedQuery.Trim())}%");
        cmd.Parameters.AddWithValue("@visibilityRank", visibilityRank ? 1 : 0);
        cmd.Parameters.AddWithValue("@limit", limit);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var raw = new List<SearchResult>();
        try
        {
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
                    Visibility = GetNullableString(reader, 6),
                });
            }
        }
        catch (SqliteException ex) when (rawQuery && IsFtsQuerySyntaxError(ex))
        {
            throw new FtsQuerySyntaxException(ex.Message, ex);
        }
        return deduplicate ? DeduplicateOverlappingResults(raw) : raw;
    }

    public QueryCountResult CountSearchResults(string query, string? lang = null, bool rawQuery = false, IReadOnlyList<string>? pathPatterns = null, IReadOnlyList<string>? excludePathPatterns = null, bool excludeTests = false, bool deduplicate = true, DateTime? since = null, bool exact = false, bool prefix = false, bool visibilityRank = true)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new QueryCountResult(0, 0);

        lang = NormalizeQueryLanguage(lang);
        var normalizedQuery = rawQuery ? query : NormalizeLiteralSearchQuery(query, lang);
        using var cmd = _conn.CreateCommand();
        string sql;

        if (exact)
        {
            sql = $@"
                SELECT f.path, c.start_line, c.end_line,
                       0.0 AS rank
                FROM chunks c
                JOIN files f ON c.file_id = f.id{SearchSymbolMatchJoinsSql}
                WHERE instr(
                    {GetExactSearchTextSql("c.content", "f.lang")},
                    {GetExactSearchTextSql("@exactQuery", "f.lang")}
                ) > 0";
        }
        else
        {
            var sanitizedQuery = rawQuery ? ValidateRawFtsQuery(query) : SanitizeFtsQuery(normalizedQuery, prefix);
            sql = $@"
                SELECT f.path, c.start_line, c.end_line,
                       rank
                FROM fts_chunks
                JOIN chunks c ON fts_chunks.rowid = c.id
                JOIN files f ON c.file_id = f.id{SearchSymbolMatchJoinsSql}";
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
        cmd.Parameters.AddWithValue("@rankingQuery", normalizedQuery.Trim());
        cmd.Parameters.AddWithValue("@rankingQueryPrefix", $"{EscapeLikeQuery(normalizedQuery.Trim())}%");
        cmd.Parameters.AddWithValue("@visibilityRank", visibilityRank ? 1 : 0);
        if (lang != null)
            cmd.Parameters.AddWithValue("@lang", lang);
        if (since != null && _fileColumns.Contains("modified"))
            cmd.Parameters.AddWithValue("@since", since.Value);
        AddPathFilterParameters(cmd, pathPatterns, excludePathPatterns);

        var keptIntervals = new Dictionary<string, IntervalSet>(StringComparer.Ordinal);
        var count = 0;
        var fileCount = 0;
        try
        {
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
        }
        catch (SqliteException ex) when (rawQuery && IsFtsQuerySyntaxError(ex))
        {
            throw new FtsQuerySyntaxException(ex.Message, ex);
        }

        return new QueryCountResult(count, fileCount);
    }

    private static string NormalizeLiteralSearchQuery(string query, string? lang) =>
        string.Equals(lang, "csharp", StringComparison.OrdinalIgnoreCase)
            ? CSharpVerbatimNameNormalizer.Normalize(query)
            : query;

    internal static string ValidateRawFtsQuery(string query)
    {
        if (query.Length > MaxRawFtsQueryLength)
            throw new FtsQuerySyntaxException($"raw FTS5 query is too long ({query.Length} characters); maximum is {MaxRawFtsQueryLength}. Split the query or drop `--fts` for literal-safe search.");

        var booleanOperators = 0;
        var nearOperators = 0;
        var depth = 0;
        var maxDepth = 0;
        var inQuote = false;

        for (var i = 0; i < query.Length; i++)
        {
            var ch = query[i];
            if (ch == '"')
            {
                if (inQuote && i + 1 < query.Length && query[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (ch == '(')
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (!IsFtsIdentifierStart(ch))
                continue;

            var start = i;
            i++;
            while (i < query.Length && IsFtsIdentifierPart(query[i]))
                i++;
            var length = i - start;
            i--;

            if (TokenEquals(query, start, length, "AND") || TokenEquals(query, start, length, "OR") || TokenEquals(query, start, length, "NOT"))
                booleanOperators++;
            else if (TokenEquals(query, start, length, "NEAR"))
                nearOperators++;
        }

        if (booleanOperators > MaxRawFtsBooleanOperators)
            throw new FtsQuerySyntaxException($"raw FTS5 query is too complex ({booleanOperators} boolean operators); maximum is {MaxRawFtsBooleanOperators}. Split the query or drop `--fts` for literal-safe search.");
        if (nearOperators > MaxRawFtsNearOperators)
            throw new FtsQuerySyntaxException($"raw FTS5 query is too complex ({nearOperators} NEAR operators); maximum is {MaxRawFtsNearOperators}. Split the query or drop `--fts` for literal-safe search.");
        if (maxDepth > MaxRawFtsParenthesisDepth)
            throw new FtsQuerySyntaxException($"raw FTS5 query is too deeply nested (parenthesis depth {maxDepth}); maximum is {MaxRawFtsParenthesisDepth}. Split the query or drop `--fts` for literal-safe search.");

        return query;
    }

    private static bool TokenEquals(string query, int start, int length, string value)
        => length == value.Length && string.Compare(query, start, value, 0, value.Length, StringComparison.Ordinal) == 0;

    private static bool IsFtsIdentifierStart(char ch)
        => char.IsLetter(ch) || ch == '_';

    private static bool IsFtsIdentifierPart(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsFtsQuerySyntaxError(SqliteException ex)
    {
        var message = ex.Message;
        return message.Contains("fts5: syntax error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unterminated string", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no such column", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExactSearchTextSql(string valueSql, string langSql)
        => $"CASE WHEN {langSql} IN ('csharp', 'java', 'kotlin') THEN sql_normalize_exact_source_name({valueSql}, {langSql}) " +
           $"WHEN {langSql} = 'sql' THEN sql_normalize_name({valueSql}) " +
           $"ELSE {valueSql} END";

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
        return $"{PathBucketOrder}, {ExactSymbolMatchOrder}, {PrefixSymbolMatchOrder}, {SearchVisibilityOrder}, {PathTextMatchOrder}, {ChunkTextMatchOrder}, rank, f.modified DESC, f.path";
    }

    private static string SearchVisibilityOrder => @"
        CASE
            WHEN @visibilityRank = 0 THEN 0
            ELSE COALESCE(exact_symbol_match.visibility_order, prefix_symbol_match.visibility_order, 4)
        END";

    private static string GetSearchVisibilitySql() => @"
        CASE
            WHEN exact_symbol_match.visibility IS NOT NULL THEN exact_symbol_match.visibility
            ELSE prefix_symbol_match.visibility
        END";

}
