namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    public static string GetLeafName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return string.Empty;

        var trimmed = qualifiedName.AsSpan().Trim();
        var segmentStart = 0;
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
                            i++;
                        }
                        else
                        {
                            quote = '\0';
                        }
                    }

                    continue;
                }

                if (ch == quote)
                {
                    if (i + 1 < trimmed.Length && trimmed[i + 1] == quote)
                    {
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (ch is '[' or '"' or '`')
            {
                quote = ch;
                continue;
            }

            if (ch == '.')
                segmentStart = i + 1;
        }

        return UnquoteIdentifier(trimmed[segmentStart..].Trim());
    }

    private static string UnquoteIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.Length < 2)
            return identifier.ToString();

        if (identifier[0] == '[' && identifier[^1] == ']')
            return identifier[1..^1].ToString().Replace("]]", "]");

        if (identifier[0] == '"' && identifier[^1] == '"')
            return identifier[1..^1].ToString().Replace("\"\"", "\"");

        if (identifier[0] == '`' && identifier[^1] == '`')
            return identifier[1..^1].ToString().Replace("``", "`");

        return identifier.ToString();
    }
}
