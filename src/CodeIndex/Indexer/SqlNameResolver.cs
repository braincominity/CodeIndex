using System.Text;

namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    private readonly record struct SqlNameParts(string NormalizedName, string LeafName, int SegmentCount);

    public static string NormalizeQualifiedName(string? qualifiedName)
        => ParseParts(qualifiedName).NormalizedName;

    public static string GetLeafName(string? qualifiedName)
        => ParseParts(qualifiedName).LeafName;

    public static int GetSegmentCount(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount;

    public static bool HasQualifier(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount > 1;

    private static SqlNameParts ParseParts(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return new SqlNameParts(string.Empty, string.Empty, 0);

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
        var segmentCount = segments.Count;
        if (segmentCount == 0)
            return new SqlNameParts(string.Empty, string.Empty, 0);

        var normalized = string.Join(".", segments);
        return new SqlNameParts(normalized, segments[^1], segmentCount);
    }

    private static void AppendNormalizedSegment(List<string> segments, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            segments.Add(value);
        current.Clear();
    }
}
