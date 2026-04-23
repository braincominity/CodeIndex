using System.Text;

namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    public static string NormalizeQualifiedName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return string.Empty;

        var trimmed = qualifiedName.Trim();
        var segments = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (quote != '\0')
            {
                if (quote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < trimmed.Length && trimmed[i + 1] == ']')
                        {
                            current.Append(']');
                            i++;
                        }
                        else
                        {
                            quote = '\0';
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }

                    continue;
                }

                if (ch == quote)
                {
                    if (i + 1 < trimmed.Length && trimmed[i + 1] == quote)
                    {
                        current.Append(quote);
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch is '[' or '"' or '`')
            {
                quote = ch;
                continue;
            }

            if (ch == '.')
            {
                AppendNormalizedSegment(segments, current);
                continue;
            }

            current.Append(ch);
        }

        AppendNormalizedSegment(segments, current);
        return string.Join(".", segments);
    }

    public static string GetLeafName(string? qualifiedName)
    {
        var normalized = NormalizeQualifiedName(qualifiedName);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        var lastDot = normalized.LastIndexOf('.');
        return lastDot >= 0 ? normalized[(lastDot + 1)..] : normalized;
    }

    public static bool HasQualifier(string? qualifiedName)
        => NormalizeQualifiedName(qualifiedName).Contains('.', StringComparison.Ordinal);

    private static void AppendNormalizedSegment(List<string> segments, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            segments.Add(value);
        current.Clear();
    }
}
