namespace CodeIndex.Cli;

/// <summary>
/// Formats human-readable search snippets around the first matching line.
/// 人間向け検索スニペットを最初の一致行の前後で整形する。
/// </summary>
public static class SearchSnippetFormatter
{
    public static IReadOnlyList<string> Format(string content, string query, int maxLines = 5)
    {
        if (maxLines <= 0)
            return [];

        var lines = content.Split('\n');
        if (lines.Length == 0)
            return [];

        var normalizedQuery = query.Trim();
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('"'))
            .Where(t => t.Length > 0)
            .ToArray();

        var matchIndex = FindFirstMatchingLine(lines, normalizedQuery, tokens);
        if (matchIndex < 0)
            matchIndex = 0;

        var start = Math.Max(0, matchIndex - (maxLines / 2));
        var end = Math.Min(lines.Length - 1, start + maxLines - 1);
        start = Math.Max(0, end - maxLines + 1);

        var snippet = new List<string>();
        if (start > 0)
            snippet.Add("...");

        for (int i = start; i <= end; i++)
            snippet.Add(lines[i]);

        if (end < lines.Length - 1)
            snippet.Add("...");

        return snippet;
    }

    private static int FindFirstMatchingLine(string[] lines, string query, string[] tokens)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        if (tokens.Length == 0)
            return -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (tokens.Any(token => lines[i].Contains(token, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        return -1;
    }
}
