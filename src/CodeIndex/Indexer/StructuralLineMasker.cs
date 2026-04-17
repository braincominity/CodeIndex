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

    // Frames used by the JS/TS template literal scanner only. C# uses the frames above.
    // JS/TS テンプレートリテラル scanner 専用のフレーム。C# は上のフレームを使う。
    private sealed class JsTemplateLiteralFrame : ScannerFrame;

    private sealed class JsTemplateHoleFrame : ScannerFrame
    {
        public int NestedBraceDepth { get; set; }
    }

    internal static string[] MaskLines(string? lang, string[] originalLines)
    {
        var maskedLines = (string[])originalLines.Clone();

        switch (lang)
        {
            case "csharp":
                MaskCSharpRawStringContents(maskedLines);
                break;
            case "python":
                MaskPythonTripleStringContents(maskedLines);
                break;
            case "rust":
                MaskRustRawStringContents(maskedLines);
                break;
            case "javascript":
            case "typescript":
                MaskJsTsTemplateLiteralContents(maskedLines);
                break;
        }

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

    // Python triple-quoted strings: """...""" and '''...''' (with optional r/b/u/f prefixes).
    // f-string interpolation holes `{expr}` preserve expression contents so downstream
    // reference extraction still sees real call edges; `{{` / `}}` are escape sequences.
    // Python の三重引用符文字列: """...""" と '''...'''（r/b/u/f 接頭辞対応）。
    // f-string の補間ホール `{expr}` は内容を残し、real call を参照抽出に見せる。
    // `{{` / `}}` は literal 用のエスケープ。
    private static void MaskPythonTripleStringContents(string[] lines)
    {
        char tripleChar = '\0';
        bool isRaw = false;
        bool isFString = false;
        int holeBraceDepth = -1; // -1 when not inside an f-string hole, >=0 otherwise.
        // Nested triple-quoted string inside an f-string hole. Persists across lines so
        // multi-line nested triples do not leak `}` into the outer hole's brace depth.
        // f-string ホール内にネストした三重引用符文字列の状態。行をまたいで保持し、
        // 複数行にわたるネスト triple 内の `}` が外側のホール brace 数え上げを壊さないようにする。
        char nestedTripleChar = '\0';
        bool nestedTripleRaw = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var masked = line.ToCharArray();
            var pos = 0;

            while (pos < line.Length)
            {
                if (tripleChar != '\0')
                {
                    if (holeBraceDepth >= 0)
                    {
                        // Inside an f-string `{expr}` hole: preserve chars so downstream
                        // regex extraction still sees real calls. Track nested braces so
                        // dict / set / nested f-string literal braces do not terminate early.
                        // Skip over nested string literals and `#` comments so their braces
                        // do not mis-close the hole.
                        // f-string `{expr}` ホール内: 文字を残して real call を抽出に見せる。
                        // dict/set/ネスト f-string のために brace 深度を追う。
                        // ホール内の文字列リテラルや `#` コメントはスキップし、内部の
                        // `{` / `}` が brace 深度に影響しないようにする。
                        if (nestedTripleChar != '\0')
                        {
                            // Scan contents of a nested triple-quoted string until we hit
                            // its closing triple. Mask all content to spaces so that
                            // indentation-sensitive downstream consumers (e.g. Python
                            // symbol-body extraction) still see a blank line here instead
                            // of stray `}` / `'''` at column 0.
                            // ネストした三重引用符文字列の本体を走査し、閉じ三重までの
                            // 全ての文字を空白に置き換える。インデント依存の後段
                            // （Python のシンボル本体抽出など）が 0 桁位置の `}` や
                            // `'''` を見てブロックを早終了しないようにする。
                            if (!nestedTripleRaw && line[pos] == '\\' && pos + 1 < line.Length)
                            {
                                ReplaceWithSpaces(masked, pos, 2);
                                pos += 2;
                                continue;
                            }

                            if (pos + 2 < line.Length
                                && line[pos] == nestedTripleChar
                                && line[pos + 1] == nestedTripleChar
                                && line[pos + 2] == nestedTripleChar)
                            {
                                ReplaceWithSpaces(masked, pos, 3);
                                pos += 3;
                                nestedTripleChar = '\0';
                                nestedTripleRaw = false;
                                continue;
                            }

                            masked[pos] = ' ';
                            pos++;
                            continue;
                        }

                        if (TryOpenPythonTripleString(line, pos, out var nestedPrefixLen, out var nestedQuote, out var nestedRawFlag, out _))
                        {
                            ReplaceWithSpaces(masked, pos, nestedPrefixLen + 3);
                            pos += nestedPrefixLen + 3;
                            nestedTripleChar = nestedQuote;
                            nestedTripleRaw = nestedRawFlag;
                            continue;
                        }

                        if (line[pos] == '\'' || line[pos] == '"')
                        {
                            pos = SkipPythonSingleLineString(line, pos);
                            continue;
                        }

                        if (line[pos] == '#')
                        {
                            // `#` starts a Python comment; skip the rest of the line.
                            // `#` から行末までは Python のコメント。
                            break;
                        }

                        if (line[pos] == '{')
                        {
                            holeBraceDepth++;
                            pos++;
                            continue;
                        }

                        if (line[pos] == '}')
                        {
                            if (holeBraceDepth == 0)
                            {
                                // Mask the closing `}` so it still looks like string
                                // delimiter noise to regex extraction.
                                // 閉じ `}` は文字列境界としてマスクし、regex 抽出に
                                // ホール本体と混在させない。
                                masked[pos] = ' ';
                                holeBraceDepth = -1;
                                pos++;
                                continue;
                            }

                            holeBraceDepth--;
                            pos++;
                            continue;
                        }

                        pos++;
                        continue;
                    }

                    if (!isRaw && line[pos] == '\\' && pos + 1 < line.Length)
                    {
                        ReplaceWithSpaces(masked, pos, 2);
                        pos += 2;
                        continue;
                    }

                    if (pos + 2 < line.Length
                        && line[pos] == tripleChar
                        && line[pos + 1] == tripleChar
                        && line[pos + 2] == tripleChar)
                    {
                        ReplaceWithSpaces(masked, pos, 3);
                        pos += 3;
                        tripleChar = '\0';
                        isRaw = false;
                        isFString = false;
                        continue;
                    }

                    if (isFString)
                    {
                        // `{{` / `}}` are escapes for literal braces — mask both as spaces.
                        // `{{` / `}}` は literal brace のエスケープ。両方マスク。
                        if (pos + 1 < line.Length && line[pos] == '{' && line[pos + 1] == '{')
                        {
                            ReplaceWithSpaces(masked, pos, 2);
                            pos += 2;
                            continue;
                        }

                        if (pos + 1 < line.Length && line[pos] == '}' && line[pos + 1] == '}')
                        {
                            ReplaceWithSpaces(masked, pos, 2);
                            pos += 2;
                            continue;
                        }

                        if (line[pos] == '{')
                        {
                            // Mask the opening `{` so brace balance matches the closing
                            // `}` we also mask; the expression contents are left alone.
                            // 開き `{` をマスクしつつ、式本体は残す。
                            masked[pos] = ' ';
                            holeBraceDepth = 0;
                            pos++;
                            continue;
                        }
                    }

                    masked[pos] = ' ';
                    pos++;
                    continue;
                }

                // Outside any string; `#` starts a line comment (ignore rest of line).
                // 文字列外で `#` は行コメント開始。以降は走査しない。
                if (line[pos] == '#')
                    break;

                if (TryOpenPythonTripleString(line, pos, out var prefixLen, out var openingChar, out var rawFlag, out var fFlag))
                {
                    ReplaceWithSpaces(masked, pos, prefixLen + 3);
                    pos += prefixLen + 3;
                    tripleChar = openingChar;
                    isRaw = rawFlag;
                    isFString = fFlag;
                    continue;
                }

                if (line[pos] == '"' || line[pos] == '\'')
                {
                    pos = SkipPythonSingleLineString(line, pos);
                    continue;
                }

                pos++;
            }

            lines[i] = new string(masked);
        }
    }

    private static bool TryOpenPythonTripleString(string line, int startIndex, out int prefixLength, out char tripleChar, out bool isRaw, out bool isFString)
    {
        prefixLength = 0;
        tripleChar = '\0';
        isRaw = false;
        isFString = false;

        if (startIndex > 0 && IsIdentifierPart(line[startIndex - 1]))
            return false;

        var p = startIndex;
        var seenRaw = false;
        var seenF = false;
        var prefixChars = 0;
        while (p < line.Length && prefixChars < 2 && IsPythonStringPrefixChar(line[p]))
        {
            if (line[p] == 'r' || line[p] == 'R')
                seenRaw = true;
            else if (line[p] == 'f' || line[p] == 'F')
                seenF = true;
            p++;
            prefixChars++;
        }

        if (p + 2 < line.Length && (line[p] == '"' || line[p] == '\'') && line[p] == line[p + 1] && line[p] == line[p + 2])
        {
            prefixLength = p - startIndex;
            tripleChar = line[p];
            isRaw = seenRaw;
            isFString = seenF;
            return true;
        }

        return false;
    }

    private static bool IsPythonStringPrefixChar(char c) =>
        c is 'r' or 'R' or 'b' or 'B' or 'u' or 'U' or 'f' or 'F';

    private static int SkipPythonSingleLineString(string line, int startIndex)
    {
        var quote = line[startIndex];
        var p = startIndex + 1;
        while (p < line.Length && line[p] != quote)
        {
            if (line[p] == '\\' && p + 1 < line.Length)
                p += 2;
            else
                p++;
        }
        if (p < line.Length)
            p++;
        return p;
    }

    // Rust raw string literals: r"...", r#"..."#, r##"..."##, ... (also with b/c byte/C-string prefix).
    // Rust の raw string リテラル: r"..." や r#"..."#、r##"..."## など（b/c 接頭辞も）。
    private static void MaskRustRawStringContents(string[] lines)
    {
        var hashCount = -1;
        // Rust supports nested `/* ... */` block comments; track depth across lines.
        // Rust の `/* ... */` ブロックコメントはネスト可能で、行をまたぐため深度で管理する。
        var blockCommentDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var masked = line.ToCharArray();
            var pos = 0;

            while (pos < line.Length)
            {
                if (blockCommentDepth > 0)
                {
                    if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '*')
                    {
                        blockCommentDepth++;
                        pos += 2;
                        continue;
                    }

                    if (pos + 1 < line.Length && line[pos] == '*' && line[pos + 1] == '/')
                    {
                        blockCommentDepth--;
                        pos += 2;
                        continue;
                    }

                    pos++;
                    continue;
                }

                if (hashCount >= 0)
                {
                    if (line[pos] == '"' && HasHashRun(line, pos + 1, hashCount))
                    {
                        ReplaceWithSpaces(masked, pos, 1 + hashCount);
                        pos += 1 + hashCount;
                        hashCount = -1;
                        continue;
                    }

                    masked[pos] = ' ';
                    pos++;
                    continue;
                }

                if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '/')
                    break;

                if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '*')
                {
                    // Must enter block-comment state before `TryOpenRustRawString`, otherwise
                    // `/* r#" */` would be mis-parsed as a real raw string opener and swallow
                    // subsequent source until the next `"#`.
                    // `TryOpenRustRawString` の前に block comment 状態へ入る。そうしないと
                    // `/* r#" */` が本物の raw string 開始と誤認され、次の `"#` まで
                    // 以降のソースを丸ごとマスクしてしまう。
                    blockCommentDepth = 1;
                    pos += 2;
                    continue;
                }

                if (line[pos] == '\'')
                {
                    // `'X'` / `'\n'` char literals may contain `"`, `r#`, or other sequences
                    // that would otherwise trip the raw-string / string scanners. Lifetimes
                    // (`'a`, `'static`) are advanced one char at a time so their trailing
                    // identifier is scanned normally.
                    // `'X'` / `'\n'` の char literal 内には `"` や `r#` が入りうる。それらを
                    // 丸ごと読み飛ばす。lifetime (`'a`, `'static`) は 1 文字だけ進めて後続の
                    // 識別子を通常走査へ渡す。
                    pos = SkipRustCharLiteralOrLifetime(line, pos);
                    continue;
                }

                if (TryOpenRustRawString(line, pos, out var openingLength, out var hashes))
                {
                    ReplaceWithSpaces(masked, pos, openingLength);
                    pos += openingLength;
                    hashCount = hashes;
                    continue;
                }

                if (line[pos] == '"')
                {
                    // Ordinary non-raw string; Rust permits newlines inside but the common case is single-line.
                    // 通常の非 raw 文字列。Rust は改行を許すが、実運用では単一行がほとんど。
                    pos = SkipRustSingleLineString(line, pos);
                    continue;
                }

                pos++;
            }

            lines[i] = new string(masked);
        }
    }

    private static int SkipRustCharLiteralOrLifetime(string line, int startIndex)
    {
        if (startIndex + 1 >= line.Length)
            return startIndex + 1;

        // Escape-form char literal `'\X'` — scan to the next `'` on the line.
        // エスケープ付き char literal `'\X'` は次の `'` まで読み飛ばす。
        if (line[startIndex + 1] == '\\')
        {
            var p = startIndex + 2;
            while (p < line.Length && line[p] != '\'')
                p++;
            if (p < line.Length)
                p++;
            return p;
        }

        // Simple `'X'`: the character at startIndex+2 must be the closing quote.
        // 単純な `'X'` は startIndex+2 が閉じクォート。
        if (startIndex + 2 < line.Length && line[startIndex + 2] == '\'')
            return startIndex + 3;

        // Lifetime or stray apostrophe: advance one char so the following identifier
        // (or other token) is scanned normally.
        // lifetime やぶら下がり `'` は 1 文字だけ進める。
        return startIndex + 1;
    }

    private static bool HasHashRun(string line, int startIndex, int count)
    {
        if (count == 0)
            return true;
        if (startIndex + count > line.Length)
            return false;
        for (int j = 0; j < count; j++)
        {
            if (line[startIndex + j] != '#')
                return false;
        }
        return true;
    }

    private static bool TryOpenRustRawString(string line, int startIndex, out int openingLength, out int hashCount)
    {
        openingLength = 0;
        hashCount = 0;

        if (startIndex > 0 && IsIdentifierPart(line[startIndex - 1]))
            return false;

        var p = startIndex;
        // Optional byte (b) or C-string (c) prefix: br#"..."#, cr#"..."#
        // 任意の byte (b) / C-string (c) 接頭辞: br#"..."#, cr#"..."#
        if (p < line.Length && (line[p] == 'b' || line[p] == 'c'))
            p++;

        if (p >= line.Length || line[p] != 'r')
            return false;
        p++;

        var hashes = 0;
        while (p < line.Length && line[p] == '#')
        {
            hashes++;
            p++;
        }

        if (p >= line.Length || line[p] != '"')
            return false;

        p++;
        openingLength = p - startIndex;
        hashCount = hashes;
        return true;
    }

    private static int SkipRustSingleLineString(string line, int startIndex)
    {
        var p = startIndex + 1;
        while (p < line.Length && line[p] != '"')
        {
            if (line[p] == '\\' && p + 1 < line.Length)
                p += 2;
            else
                p++;
        }
        if (p < line.Length)
            p++;
        return p;
    }

    // Token-aware state for JS/TS regex-vs-division disambiguation within a line.
    // Carries the last identifier word so keywords like `return` / `throw` / `typeof`
    // flip the following `/` from division to regex literal.
    // 1 行内の JS/TS regex 判定用 state。直前の識別子語も保持し、`return` / `throw` /
    // `typeof` など regex-prefix keyword の後の `/` を division ではなく regex として扱う。
    private enum JsPrevTokenKind { None, Identifier, Numeric, Literal, CloseParen, CloseBracket, CloseBrace, Other }

    private struct JsLexState
    {
        public JsPrevTokenKind PrevTokenKind;
        public string PrevIdentifier;

        public void Reset()
        {
            PrevTokenKind = JsPrevTokenKind.None;
            PrevIdentifier = string.Empty;
        }

        public void SetKind(JsPrevTokenKind kind)
        {
            PrevTokenKind = kind;
            PrevIdentifier = string.Empty;
        }

        public void SetIdentifier(string word)
        {
            PrevTokenKind = JsPrevTokenKind.Identifier;
            PrevIdentifier = word;
        }
    }

    // JavaScript/TypeScript template literals: `...` with ${expr} interpolation holes.
    // Interpolation hole contents are preserved (not masked) so the call-graph keeps real call edges.
    // Regex literals are skipped at the outer and hole scopes so a backtick inside a regex
    // does not start a phantom template and a `}` inside a regex does not close a hole early.
    // JavaScript/TypeScript のテンプレートリテラル `...` と ${expr} 補間ホール。
    // ホール内の本物のコードは参照抽出に見せるためマスクしない。
    // regex literal は外側と hole 内の両方でスキップし、regex 中の backtick が template を
    // 誤って開始したり `}` が hole を早く閉じたりするのを避ける。
    private static void MaskJsTsTemplateLiteralContents(string[] lines)
    {
        var frames = new Stack<ScannerFrame>();
        // `lexState` must persist across lines so that multi-line expressions in
        // template-literal holes keep the preceding token context. For example,
        // `${(() => value\n  / 2 + runTask())()}` continues on a new line: the `/`
        // at the start of line 2 is division (prev token `value`), not a regex
        // opener. Resetting state per line caused `lexState.PrevTokenKind` to be
        // `None`, flipping `/` into regex mode and swallowing the closing `}` and
        // backtick.
        // `lexState` はホール内の複数行式で直前トークンを保持するため、行をまたいで
        // 維持する必要がある。行頭で Reset すると継続行の `/` が常に regex 扱いに
        // なり、hole を閉じる `}` やバッククォートを巻き込んでしまう。
        var lexState = default(JsLexState);
        lexState.Reset();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            var masked = line.ToCharArray();
            var pos = 0;

            while (pos < line.Length)
            {
                if (frames.TryPeek(out var active))
                {
                    if (active is BlockCommentFrame)
                    {
                        if (pos + 1 < line.Length && line[pos] == '*' && line[pos + 1] == '/')
                        {
                            frames.Pop();
                            pos += 2;
                            continue;
                        }

                        pos++;
                        continue;
                    }

                    if (active is JsTemplateLiteralFrame)
                    {
                        if (line[pos] == '\\')
                        {
                            ReplaceWithSpaces(masked, pos, Math.Min(2, line.Length - pos));
                            pos += Math.Min(2, line.Length - pos);
                            continue;
                        }

                        if (pos + 1 < line.Length && line[pos] == '$' && line[pos + 1] == '{')
                        {
                            ReplaceWithSpaces(masked, pos, 2);
                            pos += 2;
                            frames.Push(new JsTemplateHoleFrame());
                            lexState.Reset();
                            continue;
                        }

                        if (line[pos] == '`')
                        {
                            masked[pos] = ' ';
                            pos++;
                            frames.Pop();
                            lexState.SetKind(JsPrevTokenKind.Literal);
                            continue;
                        }

                        masked[pos] = ' ';
                        pos++;
                        continue;
                    }

                    if (active is JsTemplateHoleFrame holeFrame)
                    {
                        if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '/')
                            break;

                        if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '*')
                        {
                            frames.Push(new BlockCommentFrame());
                            pos += 2;
                            continue;
                        }

                        if (line[pos] == '/' && CanStartJsRegexLiteral(lexState))
                        {
                            pos = SkipJsRegexLiteral(line, pos);
                            lexState.SetKind(JsPrevTokenKind.Literal);
                            continue;
                        }

                        if (line[pos] == '`')
                        {
                            pos++;
                            frames.Push(new JsTemplateLiteralFrame());
                            lexState.Reset();
                            continue;
                        }

                        if (line[pos] == '"' || line[pos] == '\'')
                        {
                            pos = SkipJsSingleLineString(line, pos);
                            lexState.SetKind(JsPrevTokenKind.Literal);
                            continue;
                        }

                        if (line[pos] == '{')
                        {
                            holeFrame.NestedBraceDepth++;
                            pos++;
                            lexState.SetKind(JsPrevTokenKind.Other);
                            continue;
                        }

                        if (line[pos] == '}')
                        {
                            if (holeFrame.NestedBraceDepth == 0)
                            {
                                // Mask the hole's closing `}` to keep brace balance intact
                                // for downstream symbol-body brace counting.
                                // ホールを閉じる `}` もマスクし、後段の symbol 本体の
                                // brace 数え上げで brace バランスを崩さないようにする。
                                masked[pos] = ' ';
                                frames.Pop();
                                pos++;
                                lexState.SetKind(JsPrevTokenKind.Other);
                                continue;
                            }

                            holeFrame.NestedBraceDepth--;
                            pos++;
                            // Treat nested-hole `}` as closing an expression-context brace
                            // (object literal / function expression / arrow body); the next
                            // `/` is division, not a regex opener.
                            // ホール内のネストした `}` は式コンテキストの括弧を閉じたもの
                            // （object literal / function expression / arrow body）とみなし、
                            // 次の `/` を regex ではなく division として扱えるようにする。
                            lexState.SetKind(JsPrevTokenKind.CloseBrace);
                            continue;
                        }

                        pos = AdvanceJsToken(line, pos, ref lexState);
                        continue;
                    }
                }

                if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '/')
                    break;

                if (pos + 1 < line.Length && line[pos] == '/' && line[pos + 1] == '*')
                {
                    frames.Push(new BlockCommentFrame());
                    pos += 2;
                    continue;
                }

                if (line[pos] == '/' && CanStartJsRegexLiteral(lexState))
                {
                    pos = SkipJsRegexLiteral(line, pos);
                    lexState.SetKind(JsPrevTokenKind.Literal);
                    continue;
                }

                if (line[pos] == '`')
                {
                    masked[pos] = ' ';
                    pos++;
                    frames.Push(new JsTemplateLiteralFrame());
                    lexState.Reset();
                    continue;
                }

                if (line[pos] == '"' || line[pos] == '\'')
                {
                    pos = SkipJsSingleLineString(line, pos);
                    lexState.SetKind(JsPrevTokenKind.Literal);
                    continue;
                }

                pos = AdvanceJsToken(line, pos, ref lexState);
            }

            lines[i] = new string(masked);
        }
    }

    // Advance past one JS/TS token (identifier run, numeric run, single non-string/regex char)
    // and update lexer state so the next `/` can be classified as regex-start or division.
    // 識別子の連続や数値、単一文字を 1 token として進め、次の `/` を regex / division に
    // 振り分けられるよう lex state を更新する。
    private static int AdvanceJsToken(string line, int pos, ref JsLexState lexState)
    {
        var c = line[pos];
        if (char.IsWhiteSpace(c))
            return pos + 1;

        if (IsJsIdentifierStart(c))
        {
            int start = pos;
            pos++;
            while (pos < line.Length && IsJsIdentifierPart(line[pos]))
                pos++;
            lexState.SetIdentifier(line.Substring(start, pos - start));
            return pos;
        }

        if (char.IsDigit(c))
        {
            while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '.' || line[pos] == '_'))
                pos++;
            lexState.SetKind(JsPrevTokenKind.Numeric);
            return pos;
        }

        switch (c)
        {
            case ')':
                lexState.SetKind(JsPrevTokenKind.CloseParen);
                break;
            case ']':
                lexState.SetKind(JsPrevTokenKind.CloseBracket);
                break;
            case '}':
                // `}` outside a template hole most often closes an object literal or
                // function expression body in expression context within this masker's
                // reach (hole bodies have already been handled separately). Division is
                // the realistic successor; statement-level `} /regex/` would be invalid
                // without a statement separator anyway.
                // テンプレートホール外の `}` は object literal や関数式本体を閉じるケースが
                // 支配的で、直後の `/` は division とみなすのが安全。statement 後の
                // `} /regex/` は文区切りなしでは無効なので無視できる。
                lexState.SetKind(JsPrevTokenKind.CloseBrace);
                break;
            default:
                lexState.SetKind(JsPrevTokenKind.Other);
                break;
        }

        return pos + 1;
    }

    private static bool IsJsIdentifierStart(char c) =>
        c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsJsIdentifierPart(char c) =>
        c == '_' || c == '$' || char.IsLetterOrDigit(c);

    // Decide whether `/` at the current scan position starts a regex literal rather
    // than a division operator. Division follows numeric / string / regex / template literals,
    // `)`, `]`, and non-keyword identifiers. Everything else (operators, `{`, `(`, `[`, `,`,
    // `;`, `=`, `?`, `:`, leading None) puts us in an expression-prefix context where `/`
    // begins a regex. Regex-prefix keywords such as `return`, `throw`, `typeof` re-enable
    // regex mode even though they are identifier-shaped.
    // `/` が division ではなく regex literal の開始かを判定する。数値 / 文字列 / regex /
    // template 等のリテラル、`)`、`]`、および非 keyword な識別子の後は division。
    // それ以外（演算子、`(` / `[` / `=` / `?` / `:` / `,` / `;` や行頭 None）は式の
    // 先頭コンテキストで `/` は regex。`return` / `throw` / `typeof` など regex-prefix
    // keyword は識別子形でも regex を許す。
    private static bool CanStartJsRegexLiteral(JsLexState lexState)
    {
        switch (lexState.PrevTokenKind)
        {
            case JsPrevTokenKind.None:
                return true;
            case JsPrevTokenKind.CloseParen:
            case JsPrevTokenKind.CloseBracket:
            case JsPrevTokenKind.CloseBrace:
            case JsPrevTokenKind.Numeric:
            case JsPrevTokenKind.Literal:
                return false;
            case JsPrevTokenKind.Identifier:
                return IsJsRegexPrefixKeyword(lexState.PrevIdentifier);
            case JsPrevTokenKind.Other:
            default:
                return true;
        }
    }

    private static bool IsJsRegexPrefixKeyword(string word) =>
        word is "return" or "throw" or "case" or "delete" or "typeof" or "void"
            or "new" or "in" or "of" or "instanceof" or "yield" or "await"
            or "else" or "do" or "finally";

    private static int SkipJsRegexLiteral(string line, int startIndex)
    {
        var p = startIndex + 1;
        var inCharClass = false;

        while (p < line.Length)
        {
            var ch = line[p];
            if (ch == '\\')
            {
                if (p + 1 < line.Length)
                {
                    p += 2;
                    continue;
                }

                return line.Length;
            }

            if (ch == '[')
            {
                inCharClass = true;
                p++;
                continue;
            }

            if (ch == ']' && inCharClass)
            {
                inCharClass = false;
                p++;
                continue;
            }

            if (ch == '/' && !inCharClass)
            {
                p++;
                while (p < line.Length && char.IsLetter(line[p]))
                    p++;
                return p;
            }

            p++;
        }

        return line.Length;
    }

    private static int SkipJsSingleLineString(string line, int startIndex)
    {
        var quote = line[startIndex];
        var p = startIndex + 1;
        while (p < line.Length && line[p] != quote)
        {
            if (line[p] == '\\' && p + 1 < line.Length)
                p += 2;
            else
                p++;
        }
        if (p < line.Length)
            p++;
        return p;
    }

    private static bool IsIdentifierPart(char c) =>
        c == '_' || char.IsLetterOrDigit(c);
}
