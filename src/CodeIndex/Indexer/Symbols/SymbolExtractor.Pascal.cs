using System.Text.RegularExpressions;

namespace CodeIndex.Indexer;

public static partial class SymbolExtractor
{
    private static readonly Regex PascalBeginRegex = new(@"\bbegin\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalEndRegex = new(@"\bend\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalRoutineStartRegex = new(@"^\s*(?:(?:class|static)\s+)?(?:procedure|function|constructor|destructor)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalRangeBoundaryRegex = new(@"^\s*(?:interface|implementation|initialization|finalization|end\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindPascalRange(string[] lines, int startIndex)
    {
        var opened = false;
        var depth = 0;
        int? bodyStartLine = null;

        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            var code = StripPascalRangeComments(MaskPascalRangeStrings(lines[i]));
            var trimmed = code.Trim();
            if (trimmed.Length == 0)
                continue;

            if (!opened)
            {
                if (PascalRoutineStartRegex.IsMatch(trimmed) || PascalRangeBoundaryRegex.IsMatch(trimmed))
                    return (startIndex + 1, null, null);

                var beginCount = PascalBeginRegex.Matches(code).Count;
                if (beginCount == 0)
                    continue;

                opened = true;
                bodyStartLine = i + 1;
                depth += beginCount;
                depth -= PascalEndRegex.Matches(code).Count;
                if (depth <= 0)
                    return (i + 1, bodyStartLine, i + 1);
                continue;
            }

            depth += PascalBeginRegex.Matches(code).Count;
            depth -= PascalEndRegex.Matches(code).Count;
            if (depth <= 0)
                return (i + 1, bodyStartLine, i + 1);
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (lines.Length, bodyStartLine, lines.Length);
    }

    private static string MaskPascalRangeStrings(string line)
    {
        var chars = line.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] != '\'')
                continue;

            chars[i] = ' ';
            i++;
            while (i < chars.Length)
            {
                if (chars[i] == '\'' && i + 1 < chars.Length && chars[i + 1] == '\'')
                {
                    chars[i++] = ' ';
                    chars[i] = ' ';
                    i++;
                    continue;
                }

                var closes = chars[i] == '\'';
                chars[i] = ' ';
                if (closes)
                    break;
                i++;
            }
        }

        return new string(chars);
    }

    private static string StripPascalRangeComments(string line)
    {
        var slashComment = line.IndexOf("//", StringComparison.Ordinal);
        if (slashComment >= 0)
            line = line[..slashComment];

        line = StripPascalDelimitedComment(line, "{", "}");
        line = StripPascalDelimitedComment(line, "(*", "*)");
        return line;
    }

    private static string StripPascalDelimitedComment(string line, string open, string close)
    {
        while (true)
        {
            var start = line.IndexOf(open, StringComparison.Ordinal);
            if (start < 0)
                return line;
            var end = line.IndexOf(close, start + open.Length, StringComparison.Ordinal);
            line = end < 0
                ? line[..start]
                : line[..start] + new string(' ', end + close.Length - start) + line[(end + close.Length)..];
        }
    }
}
