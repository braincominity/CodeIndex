using System.Text;

namespace CodeIndex.Indexer;

internal static class SqlSymbolNameNormalizer
{
    internal static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var trimmed = name.Trim();
        var normalized = new StringBuilder(trimmed.Length);
        char quote = '\0';
        var pendingWhitespace = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (quote != '\0')
            {
                normalized.Append(ch);
                if (quote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < trimmed.Length && trimmed[i + 1] == ']')
                        {
                            normalized.Append(']');
                            i++;
                        }
                        else
                        {
                            quote = '\0';
                        }
                    }
                }
                else if (ch == quote)
                {
                    if (i + 1 < trimmed.Length && trimmed[i + 1] == quote)
                    {
                        normalized.Append(quote);
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingWhitespace = normalized.Length > 0;
                continue;
            }

            if (ch == '.')
            {
                if (normalized.Length == 0 || normalized[^1] == '.')
                    continue;

                normalized.Append('.');
                pendingWhitespace = false;
                while (i + 1 < trimmed.Length && char.IsWhiteSpace(trimmed[i + 1]))
                    i++;
                continue;
            }

            if (pendingWhitespace)
            {
                normalized.Append(' ');
                pendingWhitespace = false;
            }

            normalized.Append(ch);
            if (ch is '[' or '"' or '`')
                quote = ch;
        }

        return normalized.ToString();
    }
}
