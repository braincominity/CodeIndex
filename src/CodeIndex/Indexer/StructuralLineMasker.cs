namespace CodeIndex.Indexer;

/// <summary>
/// Masks non-code regions that would otherwise confuse line-based structural regexes.
/// 行ベースの構造 regex を誤誘導する非コード領域をマスクする。
/// </summary>
internal static class StructuralLineMasker
{
    internal static string[] MaskLines(string? lang, string[] originalLines)
    {
        var maskedLines = (string[])originalLines.Clone();

        if (lang is "csharp")
            MaskCSharpRawStringContents(maskedLines);

        return maskedLines;
    }

    private static void MaskCSharpRawStringContents(string[] lines)
    {
        int? activeDelimiterLength = null;
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
                if (activeDelimiterLength != null)
                {
                    var closeLength = CountQuoteRun(line, searchStart);
                    if (closeLength >= activeDelimiterLength.Value)
                    {
                        ReplaceWithSpaces(masked, searchStart, closeLength);
                        searchStart += closeLength;
                        activeDelimiterLength = null;
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

                var openLength = CountQuoteRun(line, searchStart);
                if (openLength >= 3)
                {
                    ReplaceWithSpaces(masked, searchStart, openLength);
                    searchStart += openLength;
                    activeDelimiterLength = openLength;
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
        if (startIndex >= line.Length || line[startIndex] != '"')
            return 0;

        var length = 1;
        while (startIndex + length < line.Length && line[startIndex + length] == '"')
            length++;

        return length;
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

    private static void ReplaceWithSpaces(char[] buffer, int start, int length)
    {
        for (int i = start; i < start + length; i++)
            buffer[i] = ' ';
    }
}
