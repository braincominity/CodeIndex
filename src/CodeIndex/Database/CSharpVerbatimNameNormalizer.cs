namespace CodeIndex.Database;

/// <summary>
/// Normalizes C# verbatim identifiers by stripping `@` only at identifier boundaries.
/// C# の verbatim 識別子を正規化し、識別子境界の `@` だけを除去する。
/// </summary>
internal static class CSharpVerbatimNameNormalizer
{
    internal static string Normalize(string text)
    {
        if (text.Length == 0 || text.IndexOf('@') < 0)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        bool atBoundary = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (atBoundary && c == '@'
                && i + 1 < text.Length
                && IsCSharpIdentifierStartChar(text[i + 1]))
            {
                atBoundary = false;
                continue;
            }

            sb.Append(c);
            if (c == '.')
            {
                atBoundary = true;
            }
            else if (c == ':' && i + 1 < text.Length && text[i + 1] == ':')
            {
                sb.Append(':');
                i++;
                atBoundary = true;
            }
            else
            {
                atBoundary = false;
            }
        }

        return sb.Length == text.Length ? text : sb.ToString();
    }

    internal static string Normalize(string text, out int[] rawIndexMap)
    {
        if (text.Length == 0)
        {
            rawIndexMap = [];
            return text;
        }

        if (text.IndexOf('@') < 0)
        {
            rawIndexMap = BuildIdentityMap(text.Length);
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        bool atBoundary = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (atBoundary && c == '@'
                && i + 1 < text.Length
                && IsCSharpIdentifierStartChar(text[i + 1]))
            {
                atBoundary = false;
                continue;
            }

            sb.Append(c);
            map.Add(i);
            if (c == '.')
            {
                atBoundary = true;
            }
            else if (c == ':' && i + 1 < text.Length && text[i + 1] == ':')
            {
                sb.Append(':');
                map.Add(++i);
                atBoundary = true;
            }
            else
            {
                atBoundary = false;
            }
        }

        rawIndexMap = sb.Length == text.Length ? BuildIdentityMap(text.Length) : map.ToArray();
        return sb.Length == text.Length ? text : sb.ToString();
    }

    private static int[] BuildIdentityMap(int length)
    {
        var map = new int[length];
        for (int i = 0; i < length; i++)
            map[i] = i;
        return map;
    }

    private static bool IsCSharpIdentifierStartChar(char c) =>
        c == '_' || char.IsLetter(c);
}
