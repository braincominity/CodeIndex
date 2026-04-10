using CodeIndex.Database;

namespace CodeIndex.Cli;

/// <summary>
/// Formats human-readable search snippets around the first matching line.
/// 人間向け検索スニペットを最初の一致行の前後で整形する。
/// </summary>
public static class SearchSnippetFormatter
{
    public const int DefaultSnippetLines = 8;
    public const int MaxSnippetLines = 20;

    public static IReadOnlyList<string> Format(string content, string query, int maxLines = DefaultSnippetLines)
    {
        var excerpt = BuildExcerpt(content, query, absoluteStartLine: 1, maxLines);
        if (excerpt.Lines.Count == 0)
            return [];

        var snippet = new List<string>();
        if (excerpt.TruncatedBefore)
            snippet.Add("...");

        snippet.AddRange(excerpt.Lines);

        if (excerpt.TruncatedAfter)
            snippet.Add("...");

        return snippet;
    }

    public static CompactSearchResult ToCompactResult(SearchResult result, string query, int maxLines = DefaultSnippetLines)
    {
        var excerpt = BuildExcerpt(result.Content, query, result.StartLine, maxLines);
        return new CompactSearchResult
        {
            Path = result.Path,
            Lang = result.Lang,
            ChunkStartLine = result.StartLine,
            ChunkEndLine = result.EndLine,
            SnippetStartLine = excerpt.StartLine,
            SnippetEndLine = excerpt.EndLine,
            Snippet = string.Join('\n', excerpt.Lines),
            MatchLines = excerpt.MatchLines,
            Highlights = excerpt.Highlights,
            ContextBefore = excerpt.ContextBefore,
            ContextAfter = excerpt.ContextAfter,
            Score = result.Score,
        };
    }

    public static SearchSnippetExcerpt BuildExcerpt(string content, string query, int absoluteStartLine, int maxLines = DefaultSnippetLines)
    {
        maxLines = ClampSnippetLines(maxLines);

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0)
        {
            return new SearchSnippetExcerpt
            {
                StartLine = absoluteStartLine,
                EndLine = absoluteStartLine,
            };
        }

        var normalizedQuery = query.Trim();
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(t => t.Length > 0)
            .Where(t => t is not "AND" and not "OR" and not "NOT" and not "NEAR")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchIndexes = FindMatchingLineIndexes(lines, normalizedQuery, tokens);
        var focusStart = matchIndexes.Count > 0 ? matchIndexes[0] : 0;
        var focusEnd = focusStart;
        foreach (var matchIndex in matchIndexes.Skip(1))
        {
            if ((matchIndex - focusStart) + 1 > maxLines)
                break;

            focusEnd = matchIndex;
        }

        var focusLength = Math.Max(1, (focusEnd - focusStart) + 1);
        var remaining = Math.Max(0, maxLines - focusLength);
        var before = remaining / 2;
        var after = remaining - before;

        var start = Math.Max(0, focusStart - before);
        var end = Math.Min(lines.Length - 1, focusEnd + after);
        while ((end - start) + 1 < maxLines)
        {
            if (start > 0)
            {
                start--;
                continue;
            }

            if (end < lines.Length - 1)
            {
                end++;
                continue;
            }

            break;
        }

        var matchSet = matchIndexes.ToHashSet();
        var matchLines = new List<int>();
        var highlights = new List<SearchHighlight>();

        for (int i = start; i <= end; i++)
        {
            if (!matchSet.Contains(i))
                continue;

            var absoluteLine = absoluteStartLine + i;
            matchLines.Add(absoluteLine);
            highlights.Add(new SearchHighlight
            {
                Line = absoluteLine,
                Text = lines[i],
                Terms = GetMatchedTerms(lines[i], normalizedQuery, tokens),
            });
        }

        return new SearchSnippetExcerpt
        {
            StartLine = absoluteStartLine + start,
            EndLine = absoluteStartLine + end,
            Lines = lines.Skip(start).Take((end - start) + 1).ToList(),
            MatchLines = matchLines,
            Highlights = highlights,
            ContextBefore = focusStart - start,
            ContextAfter = end - focusEnd,
            TruncatedBefore = start > 0,
            TruncatedAfter = end < lines.Length - 1,
        };
    }

    public static int ClampSnippetLines(int maxLines) =>
        Math.Clamp(maxLines, 1, MaxSnippetLines);

    private static List<int> FindMatchingLineIndexes(string[] lines, string query, string[] tokens)
    {
        var matches = new List<int>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    matches.Add(i);
            }
        }

        if (matches.Count > 0 || tokens.Length == 0)
            return matches;

        for (int i = 0; i < lines.Length; i++)
        {
            if (tokens.Any(token => lines[i].Contains(token, StringComparison.OrdinalIgnoreCase)))
                matches.Add(i);
        }

        return matches;
    }

    private static List<string> GetMatchedTerms(string line, string query, string[] tokens)
    {
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(query) && line.Contains(query, StringComparison.OrdinalIgnoreCase))
            terms.Add(query);

        foreach (var token in tokens)
        {
            if (terms.Contains(token, StringComparer.OrdinalIgnoreCase))
                continue;
            if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                terms.Add(token);
        }

        return terms;
    }

    private static string NormalizeToken(string token)
    {
        return token
            .Trim('"', '\'', '(', ')')
            .TrimEnd('*');
    }
}

public sealed class CompactSearchResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int ChunkStartLine { get; set; }
    public int ChunkEndLine { get; set; }
    public int SnippetStartLine { get; set; }
    public int SnippetEndLine { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public List<int> MatchLines { get; set; } = [];
    public List<SearchHighlight> Highlights { get; set; } = [];
    public int ContextBefore { get; set; }
    public int ContextAfter { get; set; }
    public double Score { get; set; }
}

public sealed class SearchHighlight
{
    public int Line { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<string> Terms { get; set; } = [];
}

public sealed class SearchSnippetExcerpt
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public List<string> Lines { get; set; } = [];
    public List<int> MatchLines { get; set; } = [];
    public List<SearchHighlight> Highlights { get; set; } = [];
    public int ContextBefore { get; set; }
    public int ContextAfter { get; set; }
    public bool TruncatedBefore { get; set; }
    public bool TruncatedAfter { get; set; }
}
