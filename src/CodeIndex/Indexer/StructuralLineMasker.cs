namespace CodeIndex.Indexer;

/// <summary>
/// Masks non-code regions that would otherwise confuse line-based structural regexes.
/// 行ベースの構造 regex を誤誘導する非コード領域をマスクする。
/// </summary>
internal static class StructuralLineMasker
{
    private enum StringKind
    {
        Regular,
        Verbatim,
        Raw,
    }

    private abstract class ScannerFrame;

    private sealed class BlockCommentFrame : ScannerFrame;

    private sealed class CharLiteralFrame : ScannerFrame;

    private sealed class StringFrame : ScannerFrame
    {
        public required StringKind Kind { get; init; }
        public required int DelimiterLength { get; init; }
        public required int InterpolationBraceCount { get; init; }
    }

    private sealed class InterpolationFrame : ScannerFrame
    {
        public required int CloseBraceCount { get; init; }
        public int NestedBraceDepth { get; set; }
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
        var frames = new Stack<ScannerFrame>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var masked = line.ToCharArray();
            var searchStart = 0;

            while (searchStart < line.Length)
            {
                if (frames.TryPeek(out var activeFrame))
                {
                    if (activeFrame is BlockCommentFrame)
                    {
                        if (StartsWith(line, searchStart, "*/"))
                        {
                            searchStart += 2;
                            frames.Pop();
                            continue;
                        }

                        searchStart++;
                        continue;
                    }

                    if (activeFrame is CharLiteralFrame)
                    {
                        if (line[searchStart] == '\\')
                        {
                            searchStart += Math.Min(2, line.Length - searchStart);
                            continue;
                        }

                        if (line[searchStart] == '\'')
                            frames.Pop();

                        searchStart++;
                        continue;
                    }

                    if (activeFrame is StringFrame stringFrame)
                    {
                        if (stringFrame.InterpolationBraceCount == 1 && stringFrame.Kind != StringKind.Raw)
                        {
                            if (StartsWith(line, searchStart, "{{") || StartsWith(line, searchStart, "}}"))
                            {
                                ReplaceWithSpaces(masked, searchStart, 2);
                                searchStart += 2;
                                continue;
                            }
                        }

                        if (stringFrame.InterpolationBraceCount > 0)
                        {
                            var openBraceRun = CountRun(line, searchStart, '{');
                            if (openBraceRun >= stringFrame.InterpolationBraceCount)
                            {
                                ReplaceWithSpaces(masked, searchStart, stringFrame.InterpolationBraceCount);
                                searchStart += stringFrame.InterpolationBraceCount;
                                frames.Push(new InterpolationFrame { CloseBraceCount = stringFrame.InterpolationBraceCount });
                                continue;
                            }
                        }

                        if (stringFrame.Kind == StringKind.Raw)
                        {
                            var closeLength = CountQuoteRun(line, searchStart);
                            if (closeLength >= stringFrame.DelimiterLength)
                            {
                                ReplaceWithSpaces(masked, searchStart, closeLength);
                                searchStart += closeLength;
                                frames.Pop();
                                continue;
                            }

                            masked[searchStart++] = ' ';
                            continue;
                        }

                        if (stringFrame.Kind == StringKind.Verbatim)
                        {
                            if (line[searchStart] == '"' && searchStart + 1 < line.Length && line[searchStart + 1] == '"')
                            {
                                ReplaceWithSpaces(masked, searchStart, 2);
                                searchStart += 2;
                                continue;
                            }

                            masked[searchStart] = ' ';
                            if (line[searchStart] == '"')
                                frames.Pop();

                            searchStart++;
                            continue;
                        }

                        masked[searchStart] = ' ';
                        if (line[searchStart] == '\\')
                        {
                            if (searchStart + 1 < line.Length)
                                masked[searchStart + 1] = ' ';

                            searchStart += Math.Min(2, line.Length - searchStart);
                            continue;
                        }

                        if (line[searchStart] == '"')
                            frames.Pop();

                        searchStart++;
                        continue;
                    }

                    if (activeFrame is InterpolationFrame interpolationFrame)
                    {
                        if (StartsWith(line, searchStart, "//"))
                            break;

                        if (StartsWith(line, searchStart, "/*"))
                        {
                            frames.Push(new BlockCommentFrame());
                            searchStart += 2;
                            continue;
                        }

                        if (TryStartString(line, searchStart, out var nestedStringLength, out var nestedStringFrame))
                        {
                            ReplaceWithSpaces(masked, searchStart, nestedStringLength);
                            searchStart += nestedStringLength;
                            frames.Push(nestedStringFrame);
                            continue;
                        }

                        if (line[searchStart] == '\'')
                        {
                            frames.Push(new CharLiteralFrame());
                            searchStart++;
                            continue;
                        }

                        var closeBraceRun = CountRun(line, searchStart, '}');
                        if (interpolationFrame.NestedBraceDepth == 0 && closeBraceRun >= interpolationFrame.CloseBraceCount)
                        {
                            ReplaceWithSpaces(masked, searchStart, interpolationFrame.CloseBraceCount);
                            searchStart += interpolationFrame.CloseBraceCount;
                            frames.Pop();
                            continue;
                        }

                        if (line[searchStart] == '{')
                        {
                            interpolationFrame.NestedBraceDepth++;
                            searchStart++;
                            continue;
                        }

                        if (line[searchStart] == '}' && interpolationFrame.NestedBraceDepth > 0)
                        {
                            interpolationFrame.NestedBraceDepth--;
                            searchStart++;
                            continue;
                        }

                        searchStart++;
                        continue;
                    }
                }

                if (StartsWith(line, searchStart, "//"))
                    break;

                if (StartsWith(line, searchStart, "/*"))
                {
                    frames.Push(new BlockCommentFrame());
                    searchStart += 2;
                    continue;
                }

                if (TryStartString(line, searchStart, out var openingLength, out var openingFrame))
                {
                    ReplaceWithSpaces(masked, searchStart, openingLength);
                    searchStart += openingLength;
                    frames.Push(openingFrame);
                    continue;
                }

                if (line[searchStart] == '\'')
                {
                    frames.Push(new CharLiteralFrame());
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

    private static bool TryStartString(string line, int startIndex, out int openingLength, out StringFrame frame)
    {
        if (IsInterpolatedVerbatimStringStart(line, startIndex))
        {
            openingLength = 3;
            frame = new StringFrame
            {
                Kind = StringKind.Verbatim,
                DelimiterLength = 1,
                InterpolationBraceCount = 1,
            };
            return true;
        }

        if (StartsWith(line, startIndex, "@\""))
        {
            openingLength = 2;
            frame = new StringFrame
            {
                Kind = StringKind.Verbatim,
                DelimiterLength = 1,
                InterpolationBraceCount = 0,
            };
            return true;
        }

        var dollarCount = CountRun(line, startIndex, '$');
        var rawDelimiterLength = CountQuoteRun(line, startIndex + dollarCount);
        if (dollarCount > 0 && rawDelimiterLength >= 3)
        {
            openingLength = dollarCount + rawDelimiterLength;
            frame = new StringFrame
            {
                Kind = StringKind.Raw,
                DelimiterLength = rawDelimiterLength,
                InterpolationBraceCount = dollarCount,
            };
            return true;
        }

        var rawOpenLength = CountQuoteRun(line, startIndex);
        if (rawOpenLength >= 3)
        {
            openingLength = rawOpenLength;
            frame = new StringFrame
            {
                Kind = StringKind.Raw,
                DelimiterLength = rawOpenLength,
                InterpolationBraceCount = 0,
            };
            return true;
        }

        if (StartsWith(line, startIndex, "$\""))
        {
            openingLength = 2;
            frame = new StringFrame
            {
                Kind = StringKind.Regular,
                DelimiterLength = 1,
                InterpolationBraceCount = 1,
            };
            return true;
        }

        if (line[startIndex] == '"')
        {
            openingLength = 1;
            frame = new StringFrame
            {
                Kind = StringKind.Regular,
                DelimiterLength = 1,
                InterpolationBraceCount = 0,
            };
            return true;
        }

        openingLength = 0;
        frame = null!;
        return false;
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
