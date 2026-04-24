namespace CodeIndex.Indexer;

/// <summary>
/// JavaScript / TypeScript tagged template literal call site captured while masking.
/// Line and Column are 1-based; Column points to the tag identifier's starting column.
/// マスク走査中に検出した JS/TS タグ付きテンプレート呼び出し。Line/Column は 1 始まり。
/// </summary>
internal readonly record struct JsTaggedTemplateHit(int Line, int Column, string Name);

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
    // The frame snapshots the enclosing `JsLexState` on push so the closing backtick can
    // restore the paren stack, class-header hint, and case-label hint that would otherwise
    // be lost by resetting `lexState` for the template body. Without this, patterns like
    // `if (\`${x}\`) /regex/` lose the statement-head `(` context: `)` after the template
    // becomes `CloseParen` instead of `StatementHeadCloseParen`, so the next `/` falls to
    // division and the regex body's backtick is misread as a phantom template opener.
    // JS/TS テンプレートリテラル scanner 専用のフレーム。C# は上のフレームを使う。
    // push 時に外側の `JsLexState` を退避し、閉じ backtick で復元する。これがないと
    // `if (\`${x}\`) /regex/` のように、テンプレート直前に積んだ statement-head `(` の
    // コンテキストがテンプレート本体の Reset で失われ、`)` 後の `/` が division に落ちて
    // regex 本文の backtick を phantom template と誤認してしまう。
    private sealed class JsTemplateLiteralFrame : ScannerFrame
    {
        public JsLexState SavedLexState;
    }

    private sealed class JsTemplateHoleFrame : ScannerFrame
    {
        public int NestedBraceDepth { get; set; }
        // Stack entry `true` = nested `{` opened an expression brace (object literal /
        // arrow-body-with-parens): the matching `}` behaves like `)`/`]` and the next
        // `/` is division. Entry `false` = nested `{` opened a statement block (e.g.
        // `if (x) {}`, `() => { ... }`): the matching `}` keeps regex legal for the
        // next `/`. Using a stack lets us mix both kinds within a single hole.
        // スタック値 `true` は expression brace (object literal / `() => ({})`)で、
        // `}` のあとの `/` を division として扱う。`false` は statement block で、
        // `}` のあとの `/` は regex として扱う。ホール内で両者が混在しても追える。
        public Stack<bool> InnerBraceIsExpression { get; } = new();
    }

    internal static string[] MaskLines(string? lang, string[] originalLines)
        => MaskLines(lang, originalLines, out _);

    internal static string[] MaskLines(string? lang, string[] originalLines, out List<JsTaggedTemplateHit>? jsTaggedTemplateHits)
    {
        jsTaggedTemplateHits = null;
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
                jsTaggedTemplateHits = new List<JsTaggedTemplateHit>();
                MaskJsTsTemplateLiteralContents(maskedLines, jsTaggedTemplateHits, lang);
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
        // Track whether a nested triple-quoted string is itself an f-string so
        // its own `{expr}` holes still surface real call edges.
        // ネストした三重引用符文字列自身が f-string かどうかを追跡し、その内部の
        // `{expr}` ホールに含まれる real call を参照抽出に見せる。
        bool nestedTripleIsFString = false;
        // -1 when outside the nested triple's own hole, >=0 when inside it (tracks `{` depth).
        // ネスト triple 自身のホール外は -1、ホール内は 0 以上（`{` の深さを追跡）。
        int nestedTripleHoleDepth = -1;
        // Triple-quoted string that appears *inside* the nested f-string's own
        // hole. Persists across lines so multi-line triples buried three levels
        // deep (outer f-string hole → nested triple f-string → its inner hole)
        // do not leak `}` into the inner hole's brace depth.
        // ネスト f-string の内側ホールに現れる三重引用符文字列の状態。行を
        // またいで保持し、3 段深いネスト（外側ホール → ネスト三重 f-string →
        // その内側ホール）に置かれた複数行三重が内側ホールの brace 数え上げに
        // `}` を漏らさないようにする。
        char innerHoleTripleChar = '\0';
        bool innerHoleTripleRaw = false;
        // Nested single-line f-string inside the outer hole. Persists across lines so
        // the body, its `{expr}` inner hole, and any triple-quoted string opened
        // inside that inner hole can straddle multiple source lines without losing
        // the inner hole's `}` back into the outer hole's brace depth.
        // 外側ホール内にあるネスト単行 f-string の状態。行をまたいで保持し、本体・
        // 内側 `{expr}` ホール・内側ホールで開かれた三重引用符文字列が複数行に
        // わたっても、内側ホールの `}` が外側ホールの brace 深度に漏れないようにする。
        char nestedSingleFStringQuote = '\0';
        bool nestedSingleFStringRaw = false;
        int nestedSingleFStringInnerHoleDepth = -1;
        char nestedSingleFStringInnerTripleChar = '\0';
        bool nestedSingleFStringInnerTripleRaw = false;

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
                            // its closing triple. For plain nested triples mask all content
                            // to spaces so that indentation-sensitive downstream consumers
                            // (e.g. Python symbol-body extraction) still see a blank line
                            // here instead of stray `}` / `'''` at column 0. For nested
                            // triple f-strings, preserve the `{expr}` hole contents so real
                            // call edges inside the inner hole still survive; only the
                            // non-hole body is blanked.
                            // ネストした三重引用符文字列の本体を走査し、閉じ三重までの
                            // 全ての文字を空白に置き換える。インデント依存の後段
                            // （Python のシンボル本体抽出など）が 0 桁位置の `}` や
                            // `'''` を見てブロックを早終了しないようにする。ネスト三重
                            // f-string の場合は、内部 `{expr}` ホールの内容を保持して
                            // 実呼び出し edge を残し、ホール外の本体のみ空白化する。
                            if (nestedTripleIsFString && nestedTripleHoleDepth >= 0)
                            {
                                // Inside the nested f-string's own hole: preserve chars.
                                // Skip nested single-line / triple-quoted strings and Python
                                // `#` comments so their braces do not mis-close the hole.
                                // ネスト f-string 内部のホール: 文字を残して call edge を
                                // 抽出に見せる。単行文字列・三重引用符文字列・`#` コメントは
                                // スキップし、内部の `{` / `}` がホール深度に影響しないようにする。
                                if (innerHoleTripleChar != '\0')
                                {
                                    // Currently inside a triple-quoted string that opened in
                                    // the inner hole. Blank its body so downstream extraction
                                    // does not see `}` / `'` / `"` as real tokens, and close
                                    // on the matching triple.
                                    // 内側ホール内で開いた三重引用符文字列を走査中。下流が
                                    // `}` / `'` / `"` を実トークンとして読まないよう本体を
                                    // 空白化し、閉じ三重で抜ける。
                                    if (!innerHoleTripleRaw && line[pos] == '\\' && pos + 1 < line.Length)
                                    {
                                        ReplaceWithSpaces(masked, pos, 2);
                                        pos += 2;
                                        continue;
                                    }

                                    if (pos + 2 < line.Length
                                        && line[pos] == innerHoleTripleChar
                                        && line[pos + 1] == innerHoleTripleChar
                                        && line[pos + 2] == innerHoleTripleChar)
                                    {
                                        ReplaceWithSpaces(masked, pos, 3);
                                        pos += 3;
                                        innerHoleTripleChar = '\0';
                                        innerHoleTripleRaw = false;
                                        continue;
                                    }

                                    masked[pos] = ' ';
                                    pos++;
                                    continue;
                                }

                                if (TryOpenPythonTripleString(line, pos, out var innerPrefixLen, out var innerQuote, out var innerRawFlag, out _))
                                {
                                    // A triple-quoted string starts inside the inner hole.
                                    // Its body is opaque; the closing triple restores inner-
                                    // hole scanning. SkipPythonSingleLineString can only
                                    // find a same-line match, so detect triples up front.
                                    // 内側ホール内で三重引用符文字列が開始。本体は opaque と
                                    // 見なし、閉じ三重で内側ホール走査に戻る。
                                    // SkipPythonSingleLineString は同一行の対を探すだけなので、
                                    // 三重を先に検出する必要がある。
                                    ReplaceWithSpaces(masked, pos, innerPrefixLen + 3);
                                    pos += innerPrefixLen + 3;
                                    innerHoleTripleChar = innerQuote;
                                    innerHoleTripleRaw = innerRawFlag;
                                    continue;
                                }

                                if (line[pos] == '\'' || line[pos] == '"')
                                {
                                    pos = SkipPythonSingleLineString(line, pos);
                                    continue;
                                }

                                if (line[pos] == '#')
                                    break;

                                if (line[pos] == '{')
                                {
                                    nestedTripleHoleDepth++;
                                    pos++;
                                    continue;
                                }

                                if (line[pos] == '}')
                                {
                                    if (nestedTripleHoleDepth == 0)
                                    {
                                        masked[pos] = ' ';
                                        nestedTripleHoleDepth = -1;
                                        pos++;
                                        continue;
                                    }

                                    nestedTripleHoleDepth--;
                                    pos++;
                                    continue;
                                }

                                pos++;
                                continue;
                            }

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
                                nestedTripleIsFString = false;
                                nestedTripleHoleDepth = -1;
                                continue;
                            }

                            if (nestedTripleIsFString)
                            {
                                // `{{` / `}}` are escaped literal braces — blank both chars.
                                // `{{` / `}}` は literal brace のエスケープ。両方空白化。
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
                                    masked[pos] = ' ';
                                    nestedTripleHoleDepth = 0;
                                    pos++;
                                    continue;
                                }
                            }

                            masked[pos] = ' ';
                            pos++;
                            continue;
                        }

                        if (nestedSingleFStringQuote != '\0')
                        {
                            // Inside the body of a nested single-line f-string that was
                            // opened earlier in the outer hole. This branch persists
                            // across lines so a multi-line triple-quoted string opened
                            // inside the inner `{expr}` hole does not leak its `}` back
                            // into the outer hole's brace depth and truncate the outer
                            // f-string early.
                            // 外側ホール内で開かれたネスト単行 f-string の本体を走査。
                            // 内側 `{expr}` ホールに複数行の三重引用符文字列が現れても、
                            // その `}` が外側ホールの brace 深度に漏れて外側 f-string を
                            // 早閉じしないよう、状態を行をまたいで保持する。
                            if (nestedSingleFStringInnerHoleDepth >= 0)
                            {
                                if (nestedSingleFStringInnerTripleChar != '\0')
                                {
                                    if (!nestedSingleFStringInnerTripleRaw && line[pos] == '\\' && pos + 1 < line.Length)
                                    {
                                        ReplaceWithSpaces(masked, pos, 2);
                                        pos += 2;
                                        continue;
                                    }

                                    if (pos + 2 < line.Length
                                        && line[pos] == nestedSingleFStringInnerTripleChar
                                        && line[pos + 1] == nestedSingleFStringInnerTripleChar
                                        && line[pos + 2] == nestedSingleFStringInnerTripleChar)
                                    {
                                        ReplaceWithSpaces(masked, pos, 3);
                                        pos += 3;
                                        nestedSingleFStringInnerTripleChar = '\0';
                                        nestedSingleFStringInnerTripleRaw = false;
                                        continue;
                                    }

                                    masked[pos] = ' ';
                                    pos++;
                                    continue;
                                }

                                if (TryOpenPythonTripleString(line, pos, out var nestedSingleInnerPrefixLen, out var nestedSingleInnerQuote, out var nestedSingleInnerRawFlag, out _))
                                {
                                    ReplaceWithSpaces(masked, pos, nestedSingleInnerPrefixLen + 3);
                                    pos += nestedSingleInnerPrefixLen + 3;
                                    nestedSingleFStringInnerTripleChar = nestedSingleInnerQuote;
                                    nestedSingleFStringInnerTripleRaw = nestedSingleInnerRawFlag;
                                    continue;
                                }

                                if (line[pos] == '\'' || line[pos] == '"')
                                {
                                    pos = SkipPythonSingleLineString(line, pos);
                                    continue;
                                }

                                if (line[pos] == '#')
                                    break;

                                if (line[pos] == '{')
                                {
                                    nestedSingleFStringInnerHoleDepth++;
                                    pos++;
                                    continue;
                                }

                                if (line[pos] == '}')
                                {
                                    if (nestedSingleFStringInnerHoleDepth == 0)
                                    {
                                        masked[pos] = ' ';
                                        nestedSingleFStringInnerHoleDepth = -1;
                                        pos++;
                                        continue;
                                    }

                                    nestedSingleFStringInnerHoleDepth--;
                                    pos++;
                                    continue;
                                }

                                pos++;
                                continue;
                            }

                            if (!nestedSingleFStringRaw && line[pos] == '\\' && pos + 1 < line.Length)
                            {
                                ReplaceWithSpaces(masked, pos, 2);
                                pos += 2;
                                continue;
                            }

                            if (line[pos] == nestedSingleFStringQuote)
                            {
                                masked[pos] = ' ';
                                nestedSingleFStringQuote = '\0';
                                nestedSingleFStringRaw = false;
                                pos++;
                                continue;
                            }

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
                                masked[pos] = ' ';
                                nestedSingleFStringInnerHoleDepth = 0;
                                pos++;
                                continue;
                            }

                            masked[pos] = ' ';
                            pos++;
                            continue;
                        }

                        if (TryOpenPythonTripleString(line, pos, out var nestedPrefixLen, out var nestedQuote, out var nestedRawFlag, out var nestedFFlag))
                        {
                            ReplaceWithSpaces(masked, pos, nestedPrefixLen + 3);
                            pos += nestedPrefixLen + 3;
                            nestedTripleChar = nestedQuote;
                            nestedTripleRaw = nestedRawFlag;
                            nestedTripleIsFString = nestedFFlag;
                            nestedTripleHoleDepth = -1;
                            continue;
                        }

                        if (TryOpenPythonSingleLineString(line, pos, out var nestedSinglePrefixLen, out var nestedSingleQuote, out var nestedSingleRaw, out var nestedSingleFString)
                            && nestedSingleFString)
                        {
                            // Nested single-line f-string inside the outer hole. Mask the
                            // quote characters (and prefix) and stash the per-string state
                            // so ReferenceExtractor's StringLiteralRegex does not swallow
                            // the hole expression while still letting the inner `{expr}`
                            // (and any triple-quoted string opened inside it) straddle
                            // multiple source lines.
                            // 外側ホール内のネストした単行 f-string。PrepareLine の
                            // StringLiteralRegex に式本体ごと消されないよう quote と prefix を
                            // マスクし、内側 `{expr}`（および内側ホールで開いた三重引用符
                            // 文字列）が複数行にまたがっても追跡できるよう状態を保持する。
                            ReplaceWithSpaces(masked, pos, nestedSinglePrefixLen + 1);
                            pos += nestedSinglePrefixLen + 1;
                            nestedSingleFStringQuote = nestedSingleQuote;
                            nestedSingleFStringRaw = nestedSingleRaw;
                            nestedSingleFStringInnerHoleDepth = -1;
                            nestedSingleFStringInnerTripleChar = '\0';
                            nestedSingleFStringInnerTripleRaw = false;
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

    private static bool TryOpenPythonSingleLineString(string line, int startIndex, out int prefixLength, out char quoteChar, out bool isRaw, out bool isFString)
    {
        prefixLength = 0;
        quoteChar = '\0';
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

        if (p >= line.Length)
            return false;

        if (line[p] != '"' && line[p] != '\'')
            return false;

        // Triple-quoted strings are handled by the dedicated triple scanner; skip here.
        // 三重引用符は別の scanner が扱うのでここでは対象外。
        if (p + 2 < line.Length && line[p] == line[p + 1] && line[p] == line[p + 2])
            return false;

        prefixLength = p - startIndex;
        quoteChar = line[p];
        isRaw = seenRaw;
        isFString = seenF;
        return true;
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
                        // Blank the nested `/*` opener so downstream reference
                        // extraction cannot mistake it for real tokens.
                        // ネストされた `/*` 自体も空白化し、後段の参照抽出が
                        // 実トークンと誤認しないようにする。
                        ReplaceWithSpaces(masked, pos, 2);
                        blockCommentDepth++;
                        pos += 2;
                        continue;
                    }

                    if (pos + 1 < line.Length && line[pos] == '*' && line[pos + 1] == '/')
                    {
                        // Blank the `*/` closer alongside the body so pseudo
                        // references inside nested comments never reach the
                        // downstream simple comment stripper.
                        // `*/` の閉じも本文と同様に空白化し、ネストされた
                        // コメント内の疑似参照が下流の単純な comment stripper
                        // をすり抜けないようにする。
                        ReplaceWithSpaces(masked, pos, 2);
                        blockCommentDepth--;
                        pos += 2;
                        continue;
                    }

                    // Rust block comments nest, but downstream comment stripping
                    // is non-nesting. Blank the body so identifiers inside an
                    // outer-closed comment do not leak as phantom references.
                    // Rust の block comment はネスト可能だが下流の comment
                    // 除去はネスト非対応。本文を空白化し、外側閉じに
                    // 巻き込まれた識別子が疑似参照として残らないようにする。
                    masked[pos] = ' ';
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
                    // subsequent source until the next `"#`. Blank the opener so nested-comment
                    // body blanking stays contiguous.
                    // `TryOpenRustRawString` の前に block comment 状態へ入る。そうしないと
                    // `/* r#" */` が本物の raw string 開始と誤認され、次の `"#` まで
                    // 以降のソースを丸ごとマスクしてしまう。ネストコメント本文の空白化と
                    // 連続させるため `/*` 自体も空白化する。
                    ReplaceWithSpaces(masked, pos, 2);
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
    private enum JsPrevTokenKind { None, Identifier, Numeric, Literal, CloseParen, StatementHeadCloseParen, CloseBracket, CloseBrace, Arrow, Other }

    private struct JsLexState
    {
        public JsPrevTokenKind PrevTokenKind;
        public string PrevIdentifier;
        // Tracks whether each open `(` was preceded by a statement-head keyword
        // (`if`/`while`/`for`/`switch`/`catch`/`with`). After the matching `)`,
        // a following `/` begins a regex literal, not division.
        // 各 `(` の直前が statement-head キーワード（`if` / `while` / `for` /
        // `switch` / `catch` / `with`）だったかを追跡し、対応する `)` の直後に
        // 続く `/` を division ではなく regex literal として扱えるようにする。
        public Stack<bool> ParenStatementHead;
        // True after a declaration keyword (`class`, TypeScript `enum` /
        // `interface` / `namespace` / `module`) until the next `{` opens its
        // body. Forces `class Foo {}`, `enum Local {}`, `interface Local {}`,
        // `namespace Local {}`, and `module Local {}` to be classified as a
        // statement block instead of an object-literal expression brace, so
        // the matching `}` stays regex-legal and a following `/regex/` does
        // not flip to division and swallow backticks as a phantom template
        // opener.
        // `class` や TypeScript の `enum` / `interface` / `namespace` /
        // `module` キーワードの後から次の `{` で body が開くまで true。
        // `class Foo {}` / `enum Local {}` / `interface Local {}` /
        // `namespace Local {}` / `module Local {}` の `{` を object literal
        // ではなく statement block として扱わせ、対応する `}` を regex-legal
        // に保つことで、続く `/regex/` が division に倒れて regex 本文の
        // backtick を phantom template 開始として読んでしまうのを防ぐ。
        public bool ClassHeaderPending;
        // True after `case` / `default` keyword and cleared at the first `:` at
        // paren depth 0. Used to recognize the following `:` as a case-label
        // colon (not object-key / ternary / type-annotation colon).
        // `case` / `default` キーワード直後に true、paren 深さ 0 の `:` で解除。
        // 以降の `:` が case ラベル終端の `:` か（object key / ternary / type
        // annotation の `:` でないか）を区別するために使う。
        public bool CaseLabelPending;
        // Paren depth captured when `case` / `default` was seen. The matching
        // case-label `:` appears at the same depth; a `:` at a deeper paren
        // level belongs to an object-literal key, ternary, or type annotation
        // inside the case expression and must not flip CaseColonBlockPending.
        // Capturing the base depth (instead of requiring depth 0) is required
        // because the enclosing template-hole expression may itself be wrapped
        // in one or more `(` — e.g. `${(() => { switch (x) { case 1: ... }})()}`.
        // `case` / `default` を読んだ時点の paren 深さ。case ラベル終端の `:`
        // は同じ深さに現れ、それより深い `:` は case 式内の object key /
        // ternary / type annotation の `:` で、case label 扱いにしてはならない。
        // テンプレートホール全体が `(() => { ... })()` 等でラップされている
        // 場合に count==0 を要求すると case 内の `:` をスキップできないため、
        // `case` 時点の深さを基準値として保存する。
        public int CaseLabelBaseParenDepth;
        // True after a case-label `:` is consumed; the next `{` opens a
        // statement block (`case 1: {}`, `default: {}`), so the matching `}`
        // must keep `/regex/` regex-legal. Consumed by the next `{`.
        // case ラベル終端の `:` 消費直後に true。次の `{` は statement block
        // （`case 1: {}` / `default: {}`）として扱い、対応する `}` 後の
        // `/regex/` を regex-legal に保つ。次の `{` で消費。
        public bool CaseColonBlockPending;

        public void Reset()
        {
            PrevTokenKind = JsPrevTokenKind.None;
            PrevIdentifier = string.Empty;
            ClassHeaderPending = false;
            CaseLabelPending = false;
            CaseLabelBaseParenDepth = 0;
            CaseColonBlockPending = false;
            if (ParenStatementHead is null)
                ParenStatementHead = new Stack<bool>();
            else
                ParenStatementHead.Clear();
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
            if (IsJsDeclarationBodyKeyword(word))
                ClassHeaderPending = true;
            if (word == "case" || word == "default")
            {
                CaseLabelPending = true;
                CaseLabelBaseParenDepth = ParenStatementHead?.Count ?? 0;
            }
        }
    }

    private static bool IsJsStatementHeadKeyword(string word) =>
        word is "if" or "while" or "for" or "switch" or "catch" or "with";

    // Keywords whose body is a statement block, not an object-literal expression brace.
    // `class` is JS/TS; `enum`, `interface`, `namespace`, and `module` are TypeScript
    // declarations whose `{...}` body must also keep a following `/regex/` regex-legal.
    // body が statement block になる宣言キーワード。`class` は JS/TS、
    // `enum` / `interface` / `namespace` / `module` は TypeScript の宣言で、
    // 対応する `}` の直後の `/regex/` を regex-legal に保つ必要がある。
    private static bool IsJsDeclarationBodyKeyword(string word) =>
        word is "class" or "enum" or "interface" or "namespace" or "module";

    // JavaScript/TypeScript template literals: `...` with ${expr} interpolation holes.
    // Interpolation hole contents are preserved (not masked) so the call-graph keeps real call edges.
    // Regex literals are skipped at the outer and hole scopes so a backtick inside a regex
    // does not start a phantom template and a `}` inside a regex does not close a hole early.
    // JavaScript/TypeScript のテンプレートリテラル `...` と ${expr} 補間ホール。
    // ホール内の本物のコードは参照抽出に見せるためマスクしない。
    // regex literal は外側と hole 内の両方でスキップし、regex 中の backtick が template を
    // 誤って開始したり `}` が hole を早く閉じたりするのを避ける。
    private static void MaskJsTsTemplateLiteralContents(string[] lines, List<JsTaggedTemplateHit>? taggedTemplateHits = null, string? lang = null)
    {
        // `<...>` before a backtick is a TypeScript-only generic type-argument form. In plain
        // JavaScript the same character sequence is always a comparison chain (`foo<bar>\`x\``
        // is `(foo<bar)>\`x\``), so never strip the bracketed range when indexing JS.
        // `<...>` 付きのタグ付きテンプレートは TypeScript 限定のジェネリクス構文。プレーン
        // な JavaScript では同じ並びが常に比較式になるため、JS を索引するときは剥がさない。
        var allowGenericTag = string.Equals(lang, "typescript", StringComparison.Ordinal);
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
                            // Blank the `*/` closer together with the body so
                            // template-hole block comments like `${/* f(); */ g()}`
                            // never leak `f` as a phantom reference.
                            // テンプレートホール内の `${/* f(); */ g()}` のような
                            // block comment で `f` が疑似参照として残らないよう、
                            // `*/` 自体も本文と同様に空白化する。
                            ReplaceWithSpaces(masked, pos, 2);
                            frames.Pop();
                            pos += 2;
                            continue;
                        }

                        // Blank the body so identifiers inside a template-hole
                        // block comment do not survive into reference extraction.
                        // ホール内 block comment 本文は空白化し、内部の識別子が
                        // 参照抽出まで残らないようにする。
                        masked[pos] = ' ';
                        pos++;
                        continue;
                    }

                    if (active is JsTemplateLiteralFrame tplFrame)
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
                            lexState = default;
                            lexState.Reset();
                            continue;
                        }

                        if (line[pos] == '`')
                        {
                            // Restore the lex state captured when this template opened so the
                            // paren stack, class-header hint, case-label hint, etc. carry
                            // through to the token after the closing backtick.
                            // テンプレート開始時に退避した lex state を復元し、閉じ backtick
                            // の後ろに paren stack や class header hint、case label hint を
                            // 引き継ぐ。
                            masked[pos] = ' ';
                            pos++;
                            lexState = tplFrame.SavedLexState;
                            lexState.SetKind(JsPrevTokenKind.Literal);
                            frames.Pop();
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
                            // Blank the `/*` opener so the hole's block comment
                            // span is fully whitespace for downstream extraction.
                            // ホールの block comment 開始 `/*` を空白化し、
                            // 下流抽出から見えるスパン全体を空白化する。
                            ReplaceWithSpaces(masked, pos, 2);
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
                            // Save the hole's lex state so the closing backtick can
                            // restore paren/state context for the token that follows.
                            // hole 側の lex state を退避し、閉じ backtick 後に paren
                            // などの context を元に戻せるようにする。
                            if (taggedTemplateHits != null)
                                TryRecordJsTaggedTemplateHit(masked, i, pos, taggedTemplateHits, allowGenericTag);
                            pos++;
                            frames.Push(new JsTemplateLiteralFrame { SavedLexState = lexState });
                            lexState = default;
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
                            // Classify the nested `{` as expression brace (object literal
                            // or `() => ({})` body) vs. statement block (arrow body
                            // `=> {...}`, `if (x) {...}`, or `class Foo {...}`). Block
                            // braces preserve `}`→regex behavior; expression braces set
                            // `}`→division so `${({a:1} / 2)}` stays parseable. A pending
                            // class header always opens a class body, regardless of what
                            // identifier or `extends` clause token came last.
                            // ネスト `{` を expression brace（object literal / `() => ({})`）
                            // と statement block（arrow body / `if (x) {}` / `class Foo {}`）
                            // に分類する。block は `}` の次の `/` を regex にし、expression
                            // は division にする。これで `${({a:1} / 2)}` が壊れない。
                            // class header pending 中は直前トークンが何であっても class
                            // body とみなす。
                            var isExpressionBrace = !lexState.ClassHeaderPending
                                && !lexState.CaseColonBlockPending
                                && IsJsExpressionBraceContext(lexState);
                            lexState.ClassHeaderPending = false;
                            lexState.CaseColonBlockPending = false;
                            // A new block scope starts; clear any half-complete case
                            // label tracking so a stray `case` keyword seen earlier
                            // does not tag the wrong `:` in the inner scope.
                            // 新しい block scope の開始。半端な case ラベル追跡を
                            // クリアし、外側の `case` が内側の無関係な `:` を
                            // case-label colon と誤判定しないようにする。
                            lexState.CaseLabelPending = false;
                            holeFrame.InnerBraceIsExpression.Push(isExpressionBrace);
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
                            var wasExpression = holeFrame.InnerBraceIsExpression.Count > 0
                                && holeFrame.InnerBraceIsExpression.Pop();
                            // Expression brace close → division context (CloseBrace).
                            // Block brace close → preserve regex-legal state (Other) so
                            // `if (x) {} /regex/` inside an arrow body is still skipped
                            // correctly and does not consume backticks as division noise.
                            // expression brace の閉じは CloseBrace で division 優先。
                            // block brace の閉じは Other に戻し、arrow body 内の
                            // `if (x) {} /regex/` でも regex を正しく取り込めるようにする。
                            lexState.SetKind(wasExpression ? JsPrevTokenKind.CloseBrace : JsPrevTokenKind.Other);
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
                    // Blank the top-level `/*` opener to match the hole-side
                    // behavior and keep downstream extraction consistent.
                    // 先頭レベルでも `/*` 開始を空白化し、ホール側と挙動を揃える。
                    ReplaceWithSpaces(masked, pos, 2);
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
                    // Save the top-level lex state so the closing backtick can restore
                    // the paren stack / statement-head hints that preceded the template.
                    // テンプレート直前の lex state を退避し、閉じ backtick で paren
                    // stack や statement-head hint を復元できるようにする。
                    if (taggedTemplateHits != null)
                        TryRecordJsTaggedTemplateHit(masked, i, pos, taggedTemplateHits, allowGenericTag);
                    masked[pos] = ' ';
                    pos++;
                    frames.Push(new JsTemplateLiteralFrame { SavedLexState = lexState });
                    lexState = default;
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

        // Postfix / prefix `++` and `--` both produce a numeric-typed expression,
        // so the following `/` must be division, not a regex start. Consume as one
        // 2-char token to stop the second `+` / `-` from being classified as `Other`.
        // postfix / prefix の `++` と `--` は数値を生むため、続く `/` は division と
        // 扱う必要がある。2 文字 token として消費し、2 文字目が `Other` に落ちて
        // 直後の `/` を regex と誤判定するのを防ぐ。
        if ((c == '+' || c == '-') && pos + 1 < line.Length && line[pos + 1] == c)
        {
            lexState.SetKind(JsPrevTokenKind.Numeric);
            return pos + 2;
        }

        switch (c)
        {
            case '(':
                // Remember whether this `(` opens a statement-head control-flow
                // clause. Its matching `)` will need to keep the following `/`
                // regex-legal rather than flipping to division.
                // この `(` が statement-head control-flow（`if (x)` など）を
                // 開いているかを stack に記録し、対応する `)` の直後の `/` を
                // division ではなく regex literal として扱えるようにする。
                var openIsStmtHead = lexState.PrevTokenKind == JsPrevTokenKind.Identifier
                    && IsJsStatementHeadKeyword(lexState.PrevIdentifier);
                lexState.ParenStatementHead?.Push(openIsStmtHead);
                lexState.SetKind(JsPrevTokenKind.Other);
                break;
            case ')':
                var closeIsStmtHead = lexState.ParenStatementHead is { Count: > 0 }
                    && lexState.ParenStatementHead.Pop();
                // Statement-head `)` tags the following `/` as regex-legal and the
                // following `{` as a statement block; other `)` flips `/` to division
                // and `{` to an object-literal-style expression brace.
                // statement-head の `)` は続く `/` を regex、続く `{` を block と扱う。
                // それ以外の `)` は `/` を division、`{` を object literal 的な
                // expression brace と扱う。
                lexState.SetKind(closeIsStmtHead ? JsPrevTokenKind.StatementHeadCloseParen : JsPrevTokenKind.CloseParen);
                break;
            case ']':
                lexState.SetKind(JsPrevTokenKind.CloseBracket);
                break;
            case ':':
                // `case expr :` / `default :` — the case-label colon. Treat it
                // as such only when the paren depth is back to what it was at
                // the `case` / `default` keyword, so object-key, ternary, and
                // type-annotation colons inside the case expression do not
                // consume the hint.
                // `case expr :` / `default :` の case ラベル終端 `:`。paren
                // 深さが `case` / `default` 時点と同じに戻ったときだけ使い、
                // case 式内の object-key / ternary / type annotation の `:`
                // でヒントを消費しないようにする。
                if (lexState.CaseLabelPending
                    && (lexState.ParenStatementHead?.Count ?? 0) == lexState.CaseLabelBaseParenDepth)
                {
                    lexState.CaseLabelPending = false;
                    lexState.CaseColonBlockPending = true;
                }
                lexState.SetKind(JsPrevTokenKind.Other);
                break;
            case ';':
                // `;` terminates any in-progress case-label tracking.
                // `;` で case ラベル追跡を打ち切る。
                lexState.CaseLabelPending = false;
                lexState.CaseColonBlockPending = false;
                lexState.SetKind(JsPrevTokenKind.Other);
                break;
            case '>':
                // `=>` is the only 2-char JS token we need to distinguish here: `{`
                // following `=>` opens an arrow-function body (a statement block, so
                // the next `/` inside is regex), while `{` following most other tokens
                // opens an object literal / expression brace.
                // `=>` は 2 文字 token のうち本マスカーで必要な唯一のケース。続く `{`
                // が arrow body（statement block）か object literal / expression
                // brace かを分けるフラグとして使う。
                if (pos > 0 && line[pos - 1] == '=')
                    lexState.SetKind(JsPrevTokenKind.Arrow);
                else
                    lexState.SetKind(JsPrevTokenKind.Other);
                break;
            // `}` in normal JS / TS code is context-dependent: after a statement block
            // (`if (x) {}`) a `/` legitimately starts a regex; after an object literal
            // in expression position a `/` is division. We classify as `Other` so the
            // regex scanner still runs — that lets us correctly skip `/regex/` literals
            // that may contain backticks or braces which would otherwise open a phantom
            // template literal. Inside template-literal holes the closing brace is
            // handled separately (see `JsPrevTokenKind.CloseBrace` path below).
            // 通常コードの `}` は文脈依存で、`if (x) {}` のあとは regex、object literal
            // のあとは division。ここでは `Other` として regex scanner に任せ、中に
            // backtick や brace を含む `/regex/` を取りこぼして phantom template を
            // 開かないようにする。テンプレート hole 内のブレース close は別扱い。
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

    // Backward-scan the masked buffer at a template-literal opener backtick for a tag
    // identifier such as `gql`, `styled.div` (last segment), or `html<T>` (generics are
    // skipped). Whitespace between the identifier and the backtick is tolerated so
    // `html \`...\`` still matches. `IsIgnoredCallName` downstream filters out keywords
    // like `return` / `throw` / `await` / `typeof` that can legally precede a plain
    // template literal.
    // マスク済みバッファを opener バッククォート位置から後方スキャンし、`gql` や
    // `styled.div`（最後のセグメント）、`html<T>`（ジェネリクスを読み飛ばす）の
    // タグ識別子を取り出す。識別子とバッククォートの間の空白は許容し、
    // `return` / `throw` / `await` / `typeof` のようなプレーンテンプレートの前に
    // 立ちうるキーワードは呼び出し側の `IsIgnoredCallName` で除外する。
    private static void TryRecordJsTaggedTemplateHit(
        char[] masked, int lineIndex, int backtickPos, List<JsTaggedTemplateHit> hits, bool allowGenericTag)
    {
        int k = backtickPos - 1;
        while (k >= 0 && (masked[k] == ' ' || masked[k] == '\t'))
            k--;
        if (k < 0)
            return;

        // Skip a balanced `<...>` (TypeScript generics) so `html<T>\`...\`` still sees `html`.
        // The generic-strip is TypeScript-only (`allowGenericTag`) because plain JavaScript has
        // no generics: `foo<bar>\`x\`` is always the chained comparison `(foo<bar)>\`x\``. Even
        // inside TypeScript we still require the `<` to directly abut an identifier so
        // whitespace-bearing comparison expressions like `foo < bar > \`plain\`` are rejected,
        // and we ignore `>` from `=>` (arrow-function type inside the generic range).
        // `html<T>\`...\`` のジェネリクスを読み飛ばすため、同一行内で `<...>` が釣り合っている
        // 場合のみ括弧を剥がす。ジェネリクスは TypeScript 限定（`allowGenericTag`）。JavaScript
        // では `foo<bar>\`x\`` は常に連鎖比較式なので generic とは扱わない。TypeScript 側でも
        // `foo < bar > \`plain\`` のような比較式と区別するため `<` が識別子に隣接していることを
        // 要求し、`=>` 由来の `>` は関数型なので閉じ記号として数えない。
        if (masked[k] == '>' && allowGenericTag)
        {
            int probe = k - 1;
            int depth = 1;
            while (probe >= 0 && depth > 0)
            {
                var ch = masked[probe];
                if (ch == '>' && probe > 0 && masked[probe - 1] == '=')
                {
                    probe -= 2;
                    continue;
                }
                if (ch == '>') depth++;
                else if (ch == '<') depth--;
                probe--;
            }
            if (depth != 0)
                return;
            if (probe < 0 || !IsJsIdentifierPart(masked[probe]))
                return;
            k = probe;
        }

        if (!IsJsIdentifierPart(masked[k]))
            return;

        int end = k + 1;
        while (k >= 0 && IsJsIdentifierPart(masked[k]))
            k--;
        int start = k + 1;

        if (!IsJsIdentifierStart(masked[start]))
            return;

        var name = new string(masked, start, end - start);
        if (name == "of" && IsInsideJsForHeader(masked, start))
            return;
        hits.Add(new JsTaggedTemplateHit(lineIndex + 1, start + 1, name));
    }

    // `of` is an unreserved identifier in ECMAScript and may legitimately be used as a tag
    // (`const of = ...; of\`x\``). We only want to suppress the `for (<decl> of \`...\`)` loop-
    // header form. Walk backward from `fromIndex` to the nearest unmatched `(` and verify the
    // token immediately before that `(` is the `for` keyword (optionally followed by `await`).
    // This limits the suppression to the loop-header context and keeps real `of` tags visible
    // in references / callers / callees / impact. Only the current line is inspected — the
    // far rarer multi-line `for (\n  const x of \`...\`\n)` form is out of scope.
    // `of` は ECMAScript の予約語ではなく `const of = ...; of\`x\`` のようにタグとして正当に
    // 使えるため、`for (<decl> of \`...\`)` のループヘッダ形だけを局所的に落とす。`fromIndex`
    // から釣り合いの取れていない `(` を後方に探し、その直前トークンが `for`（必要なら後段の
    // `await` も許容）の場合だけ抑制する。現行実装は同一行内のみを走査するため、極めて稀な
    // 複数行に跨る `for (\n  const x of \`...\`\n)` 形は対象外。
    private static bool IsInsideJsForHeader(char[] masked, int fromIndex)
    {
        int depth = 0;
        for (int i = fromIndex - 1; i >= 0; i--)
        {
            char c = masked[i];
            if (c == ')')
            {
                depth++;
                continue;
            }
            if (c == '(')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }
                int j = i - 1;
                while (j >= 0 && (masked[j] == ' ' || masked[j] == '\t'))
                    j--;
                // `for await (<decl> of ...)` — skip an optional `await` token that sits
                // between `for` and `(`.
                // `for await (<decl> of ...)` — `for` と `(` の間の `await` を読み飛ばす。
                if (j >= 4
                    && masked[j] == 't' && masked[j - 1] == 'i'
                    && masked[j - 2] == 'a' && masked[j - 3] == 'w' && masked[j - 4] == 'a'
                    && (j - 5 < 0 || !IsJsIdentifierPart(masked[j - 5])))
                {
                    j -= 5;
                    while (j >= 0 && (masked[j] == ' ' || masked[j] == '\t'))
                        j--;
                }
                if (j >= 2
                    && masked[j] == 'r' && masked[j - 1] == 'o' && masked[j - 2] == 'f'
                    && (j - 3 < 0 || !IsJsIdentifierPart(masked[j - 3])))
                {
                    return true;
                }
                return false;
            }
        }
        return false;
    }

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
            case JsPrevTokenKind.StatementHeadCloseParen:
            case JsPrevTokenKind.Arrow:
            case JsPrevTokenKind.Other:
            default:
                return true;
        }
    }

    // Classify a nested `{` opened inside a template-literal hole as an expression
    // brace (object literal or `() => ({})` body, follows `=`, `(`, `[`, `,`, `:`,
    // `?`, operator, regex-prefix keyword) vs. a statement block (arrow-function
    // body, `if`/`while`/`for`/`function` block body — typically follows `)` or
    // `=>`). Expression braces classify the matching `}` as division-context; block
    // braces keep regex-legal classification so `{} /regex/` still parses.
    // テンプレートホール内でネストした `{` が expression brace（object literal /
    // `() => ({})` 本体）か statement block（arrow body / `if/while/for/function`
    // ブロック）かを判定する。expression は `=`、`(`、`[`、`,`、`:`、`?`、演算子、
    // regex-prefix keyword の直後。block は `)` や `=>` の直後。
    private static bool IsJsExpressionBraceContext(JsLexState lexState)
    {
        switch (lexState.PrevTokenKind)
        {
            case JsPrevTokenKind.CloseParen:
            case JsPrevTokenKind.StatementHeadCloseParen:
            case JsPrevTokenKind.Arrow:
                return false;
            case JsPrevTokenKind.Identifier:
                // Keywords that open a statement block follow the same rule as `)`.
                // `else { ... }`, `do { ... }`, `try { ... }`, `finally { ... }`,
                // and the optional-binding `catch { ... }` (ES2019).
                // block を開く keyword は `)` と同じ扱い。ES2019 の optional
                // binding 付き `catch { ... }` も block として扱う。
                return lexState.PrevIdentifier is not ("else" or "do" or "try" or "finally" or "catch");
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
