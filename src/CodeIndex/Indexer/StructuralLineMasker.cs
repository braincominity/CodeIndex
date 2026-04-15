namespace CodeIndex.Indexer;

/// <summary>
/// Masks non-code regions that would otherwise confuse line-based structural regexes.
/// 行ベースの構造 regex を誤誘導する非コード領域をマスクする。
/// </summary>
internal static class StructuralLineMasker
{
    private sealed class RawStringState
    {
        public required int DelimiterLength { get; init; }
        public required int InterpolationBraceCount { get; init; }
        public bool InInterpolation { get; set; }
        public int InterpolationDepth { get; set; }
        public bool HoleInBlockComment { get; set; }
        public bool HoleInRegularString { get; set; }
        public bool HoleInVerbatimString { get; set; }
        public bool HoleInCharLiteral { get; set; }
    }

    internal static string[] MaskLines(string? lang, string[] originalLines)
    {
        var maskedLines = (string[])originalLines.Clone();

        if (lang is "csharp")
            MaskCSharpRawStringContents(maskedLines);

        return maskedLines;
    }

    private static void MaskCSharpRawStringContents(string[] lines)
    {
        RawStringState? activeRawString = null;
        var inBlockComment = false;
        var inRegularString = false;
        var inVerbatimString = false;
        var inCharLiteral = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var masked = line.ToCharArray();
            var searchStart = 0;

            while (searchStart < line.Length)
            {
                if (activeRawString != null)
                {
                    if (activeRawString.InInterpolation)
                    {
                        if (activeRawString.HoleInBlockComment)
                        {
                            if (StartsWith(line, searchStart, "*/"))
                            {
                                activeRawString.HoleInBlockComment = false;
                                searchStart += 2;
                                continue;
                            }

                            searchStart++;
                            continue;
                        }

                        if (activeRawString.HoleInRegularString)
                        {
                            if (line[searchStart] == '\\')
                            {
                                searchStart += Math.Min(2, line.Length - searchStart);
                                continue;
                            }

                            if (line[searchStart] == '"')
                                activeRawString.HoleInRegularString = false;

                            searchStart++;
                            continue;
                        }

                        if (activeRawString.HoleInVerbatimString)
                        {
                            if (line[searchStart] == '"' && searchStart + 1 < line.Length && line[searchStart + 1] == '"')
                            {
                                searchStart += 2;
                                continue;
                            }

                            if (line[searchStart] == '"')
                                activeRawString.HoleInVerbatimString = false;

                            searchStart++;
                            continue;
                        }

                        if (activeRawString.HoleInCharLiteral)
                        {
                            if (line[searchStart] == '\\')
                            {
                                searchStart += Math.Min(2, line.Length - searchStart);
                                continue;
                            }

                            if (line[searchStart] == '\'')
                                activeRawString.HoleInCharLiteral = false;

                            searchStart++;
                            continue;
                        }

                        if (StartsWith(line, searchStart, "//"))
                            break;

                        if (StartsWith(line, searchStart, "/*"))
                        {
                            activeRawString.HoleInBlockComment = true;
                            searchStart += 2;
                            continue;
                        }

                        if (IsInterpolatedVerbatimStringStart(line, searchStart))
                        {
                            activeRawString.HoleInVerbatimString = true;
                            searchStart += 3;
                            continue;
                        }

                        if (StartsWith(line, searchStart, "@\""))
                        {
                            activeRawString.HoleInVerbatimString = true;
                            searchStart += 2;
                            continue;
                        }

                        var closeBraceRun = CountRun(line, searchStart, '}');
                        if (activeRawString.InterpolationDepth == 0 && closeBraceRun >= activeRawString.InterpolationBraceCount)
                        {
                            ReplaceWithSpaces(masked, searchStart, activeRawString.InterpolationBraceCount);
                            searchStart += activeRawString.InterpolationBraceCount;
                            activeRawString.InInterpolation = false;
                            continue;
                        }

                        if (line[searchStart] == '{')
                        {
                            activeRawString.InterpolationDepth++;
                            searchStart++;
                            continue;
                        }

                        if (line[searchStart] == '}' && activeRawString.InterpolationDepth > 0)
                        {
                            activeRawString.InterpolationDepth--;
                            searchStart++;
                            continue;
                        }

                        if (line[searchStart] == '"')
                        {
                            activeRawString.HoleInRegularString = true;
                            searchStart++;
                            continue;
                        }

                        if (line[searchStart] == '\'')
                        {
                            activeRawString.HoleInCharLiteral = true;
                            searchStart++;
                            continue;
                        }

                        searchStart++;
                        continue;
                    }

                    if (activeRawString.InterpolationBraceCount > 0)
                    {
                        var openBraceRun = CountRun(line, searchStart, '{');
                        if (openBraceRun >= activeRawString.InterpolationBraceCount)
                        {
                            ReplaceWithSpaces(masked, searchStart, activeRawString.InterpolationBraceCount);
                            searchStart += activeRawString.InterpolationBraceCount;
                            activeRawString.InInterpolation = true;
                            activeRawString.InterpolationDepth = 0;
                            activeRawString.HoleInBlockComment = false;
                            activeRawString.HoleInRegularString = false;
                            activeRawString.HoleInVerbatimString = false;
                            activeRawString.HoleInCharLiteral = false;
                            continue;
                        }
                    }

                    var closeLength = CountQuoteRun(line, searchStart);
                    if (closeLength >= activeRawString.DelimiterLength)
                    {
                        ReplaceWithSpaces(masked, searchStart, activeRawString.DelimiterLength);
                        searchStart += activeRawString.DelimiterLength;
                        activeRawString = null;
                        continue;
                    }

                    masked[searchStart++] = ' ';
                    continue;
                }

                if (inBlockComment)
                {
                    if (StartsWith(line, searchStart, "*/"))
                    {
                        inBlockComment = false;
                        searchStart += 2;
                        continue;
                    }

                    searchStart++;
                    continue;
                }

                if (inRegularString)
                {
                    if (line[searchStart] == '\\')
                    {
                        searchStart += Math.Min(2, line.Length - searchStart);
                        continue;
                    }

                    if (line[searchStart] == '"')
                        inRegularString = false;

                    searchStart++;
                    continue;
                }

                if (inVerbatimString)
                {
                    if (line[searchStart] == '"' && searchStart + 1 < line.Length && line[searchStart + 1] == '"')
                    {
                        searchStart += 2;
                        continue;
                    }

                    if (line[searchStart] == '"')
                        inVerbatimString = false;

                    searchStart++;
                    continue;
                }

                if (inCharLiteral)
                {
                    if (line[searchStart] == '\\')
                    {
                        searchStart += Math.Min(2, line.Length - searchStart);
                        continue;
                    }

                    if (line[searchStart] == '\'')
                        inCharLiteral = false;

                    searchStart++;
                    continue;
                }

                if (StartsWith(line, searchStart, "//"))
                    break;

                if (StartsWith(line, searchStart, "/*"))
                {
                    inBlockComment = true;
                    searchStart += 2;
                    continue;
                }

                if (IsInterpolatedVerbatimStringStart(line, searchStart))
                {
                    inVerbatimString = true;
                    searchStart += 3;
                    continue;
                }

                if (StartsWith(line, searchStart, "@\""))
                {
                    inVerbatimString = true;
                    searchStart += 2;
                    continue;
                }

                var rawInterpolationDollarCount = CountRawInterpolationDollarRun(line, searchStart);
                if (rawInterpolationDollarCount > 0)
                {
                    var rawQuoteStart = searchStart + rawInterpolationDollarCount;
                    var rawDelimiterLength = CountQuoteRun(line, rawQuoteStart);
                    if (rawDelimiterLength >= 3)
                    {
                        ReplaceWithSpaces(masked, searchStart, rawInterpolationDollarCount + rawDelimiterLength);
                        searchStart += rawInterpolationDollarCount + rawDelimiterLength;
                        activeRawString = new RawStringState
                        {
                            DelimiterLength = rawDelimiterLength,
                            InterpolationBraceCount = rawInterpolationDollarCount,
                        };
                        continue;
                    }
                }

                var openLength = CountQuoteRun(line, searchStart);
                if (openLength >= 3)
                {
                    ReplaceWithSpaces(masked, searchStart, openLength);
                    searchStart += openLength;
                    activeRawString = new RawStringState
                    {
                        DelimiterLength = openLength,
                        InterpolationBraceCount = 0,
                    };
                    continue;
                }

                if (line[searchStart] == '"')
                {
                    inRegularString = true;
                    searchStart++;
                    continue;
                }

                if (line[searchStart] == '\'')
                {
                    inCharLiteral = true;
                    searchStart++;
                    continue;
                }

                searchStart++;
            }

            lines[i] = new string(masked);
        }
    }

    private static int CountQuoteRun(string line, int startIndex)
    {
        return CountRun(line, startIndex, '"');
    }

    private static bool StartsWith(string line, int startIndex, string value)
    {
        if (startIndex + value.Length > line.Length)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            if (line[startIndex + i] != value[i])
                return false;
        }

        return true;
    }

    private static bool IsInterpolatedVerbatimStringStart(string line, int startIndex) =>
        StartsWith(line, startIndex, "$@\"") || StartsWith(line, startIndex, "@$\"");

    private static int CountRawInterpolationDollarRun(string line, int startIndex)
    {
        var dollarCount = CountRun(line, startIndex, '$');
        if (dollarCount == 0)
            return 0;

        return CountQuoteRun(line, startIndex + dollarCount) >= 3 ? dollarCount : 0;
    }

    private static int CountRun(string line, int startIndex, char value)
    {
        if (startIndex >= line.Length || line[startIndex] != value)
            return 0;

        var length = 1;
        while (startIndex + length < line.Length && line[startIndex + length] == value)
            length++;

        return length;
    }

    private static void ReplaceWithSpaces(char[] buffer, int start, int length)
    {
        for (int i = start; i < start + length; i++)
            buffer[i] = ' ';
    }
}
