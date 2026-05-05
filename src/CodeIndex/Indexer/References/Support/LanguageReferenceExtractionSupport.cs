using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class LanguageReferenceExtractionSupport
{
    private static readonly Regex CppIncludeRegex = new(
        @"^\s*#\s*(?:include|import)\s*[<""](?<name>[^>""]+)[>""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppBaseListRegex = new(
        @"^\s*(?:(?:template|requires)\b[^{;]*\s+)*(?:class|struct)\s+[A-Za-z_]\w*(?:\s*final)?\s*:\s*(?<bases>[^{;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppNewTypeRegex = new(
        @"\bnew\s+(?<type>(?:[A-Za-z_]\w*\s*::\s*)*[A-Za-z_]\w*(?:\s*<[^;{}]+>)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CppDeclarationTypeRegex = new(
        @"(?<![\w:])(?<type>(?:(?:const|volatile|static|inline|constexpr|typename|class|struct|enum)\s+)*(?:[A-Z_]\w*|[A-Za-z_]\w*\s*::\s*[A-Za-z_]\w*)(?:\s*<[^;{}]+>)?(?:\s*[*&])*)\s+(?<name>[A-Za-z_]\w*)\s*(?=[,;)=])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GoImportRegex = new(
        @"^\s*import\s+(?:(?<alias>[A-Za-z_]\w*|\.)\s+)?""(?<name>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportBlockStartRegex = new(
        @"^\s*import\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoImportBlockEntryRegex = new(
        @"^\s*(?:(?<alias>[A-Za-z_]\w*|\.)\s+)?""(?<name>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoVarTypeRegex = new(
        @"\b(?:var|const)\s+[A-Za-z_]\w*\s+(?<type>[\*\[\]\w.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFieldTypeRegex = new(
        @"^\s*(?!(?:package|import|func|type|var|const|return|defer|go|if|for|switch|select|case|default|else)\b)[A-Za-z_]\w*\s+(?<type>[\*\[\]\w.]+)(?:\s|`|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoTypeAliasRegex = new(
        @"^\s*type\s+[A-Za-z_]\w*(?:\[[^\]]+\])?\s+=?\s*(?<type>[\*\[\]\w.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoFuncRegex = new(
        @"^\s*func\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GoCompositeLiteralRegex = new(
        @"(?<!\btype\s)(?<name>[A-Z]\w*)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DartCtorRegex = new(
        @"\b(?:new|const)\s+(?<name>[A-Z]\w*(?:\.[A-Za-z_]\w*)?)\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartVariableTypeRegex = new(
        @"^\s*(?:(?:final|late|const)\s+)*(?<type>[A-Z]\w*(?:\s*<[^;=]+>)?)\s+[A-Za-z_]\w*\s*(?:=|;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartFunctionSignatureRegex = new(
        @"^\s*(?:(?:external|static|abstract)\s+)*(?<return>[A-Z]\w*(?:\s*<[^;{}()]+>)?)\s+[A-Za-z_]\w*\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DartParameterTypeRegex = new(
        @"(?:^|,)\s*(?:(?:required|covariant|final)\s+)*(?<type>[A-Z]\w*(?:\s*<[^,)=]+>)?)\s+[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VbTypeKeywordRegex = new(
        @"\b(?:As|New|Inherits|Implements|Of)\s+(?<type>(?:Global\.)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VbAddressOfRegex = new(
        @"\bAddressOf\s+(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VbHandlesRegex = new(
        @"\bHandles\s+(?:[A-Za-z_]\w*\.)?(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FortranUseRegex = new(
        @"^\s*use(?:\s*,\s*(?:intrinsic|non_intrinsic))?(?:\s*::)?\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FortranTypeRegex = new(
        @"\b(?:type|class)\s*\(\s*(?<type>[A-Za-z_]\w*)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FortranCallRegex = new(
        @"^\s*call\s+(?<name>[A-Za-z_]\w*)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PascalUsesRegex = new(
        @"^\s*uses\s+(?<list>.+?)(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalTypeAfterColonRegex = new(
        @":\s*(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PascalClassBaseRegex = new(
        @"=\s*(?:class|interface|object)\s*\((?<bases>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PascalBareCallRegex = new(
        @"^\s*(?<name>[A-Za-z_]\w*)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObjCMessageRegex = new(
        @"\[\s*(?<receiver>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCInterfaceBaseRegex = new(
        @"^\s*@(?:interface|implementation)\s+[A-Za-z_]\w+(?:\s*\([^)]+\))?\s*:\s*(?<type>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCProtocolListRegex = new(
        @"<(?<list>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCDeclTypeRegex = new(
        @"(?<type>[A-Z]\w*)\s*\*+\s*[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ObjCSelectorRegex = new(
        @"@selector\s*\(\s*(?<name>[A-Za-z_]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HaskellSignatureRegex = new(
        @"^\s*[a-z_]\w*\s*::\s*(?<types>.+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HaskellSpaceCallRegex = new(
        @"^\s*(?<name>[a-z_]\w*)\s+(?=(?:[A-Za-z_(]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HaskellDefinitionRegex = new(
        @"^\s*(?<name>[a-z_]\w*)\b.*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ElixirImportRegex = new(
        @"^\s*(?:alias|import|require|use)\s+(?<name>[A-Z]\w*(?:\.[A-Z]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ElixirBehaviourRegex = new(
        @"^\s*@(?:behaviour|impl)\s+(?<name>[A-Z]\w*(?:\.[A-Z]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ElixirParenlessCallRegex = new(
        @"(?<![\w])(?<name>[a-z_]\w*[?!]?)\s+(?=(?:[A-Za-z_:@\[""']))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LuaRequireRegex = new(
        @"\brequire\s*\(?\s*[""'](?<name>[^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LuaCommandCallRegex = new(
        @"^\s*(?<name>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?)\s+(?=[""'{A-Za-z_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SmalltalkClassDeclarationRegex = new(
        @"^\s*(?:(?:[A-Za-z_]\w*)\s+subclass:|Class\s+named:|Object\s+subclass:)\s*#",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SmalltalkMessageSendRegex = new(
        @"(?<![#\w])(?<receiver>[A-Za-z_]\w*)\s+(?<selector>[a-z]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SmalltalkMethodDefinitionRegex = new(
        @">>\s*(?<name>[A-Za-z_]\w*:?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RazorComponentTagRegex = new(
        @"<(?<name>[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z_]\w*)?)(?=[\s>/])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorDirectiveTypeRegex = new(
        @"^\s*@(?:inherits|implements|model)\s+(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorInjectRegex = new(
        @"^\s*@inject\s+(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorEventHandlerRegex = new(
        @"@on[A-Za-z_]\w*\s*=\s*""(?<name>[A-Za-z_]\w*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void EmitTypePositionReferences(
        string language,
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container,
        bool isGoImportBlockLine = false)
    {
        switch (language)
        {
            case "c":
            case "cpp":
                EmitCppTypeReferences(language, preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "go":
                EmitGoTypeReferences(preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, isGoImportBlockLine);
                break;
            case "dart":
                EmitDartTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "vb":
                EmitVbTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "fortran":
                EmitFortranTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "pascal":
                EmitPascalTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "objc":
                EmitObjCTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
                break;
            case "haskell":
                EmitHaskellTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
                break;
            case "elixir":
                EmitElixirTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
                break;
            case "lua":
                EmitLuaTypeReferences(originalLine, references, seen, fileId, context, lineNumber, container);
                break;
        }
    }

    public static void EmitAdditionalCallReferences(
        string language,
        string preparedLine,
        string originalLine,
        Action<string, int> addCallLikeReference,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlySet<string>? definitionNames)
    {
        switch (language)
        {
            case "fortran":
                EmitFortranCallReferences(preparedLine, addCallLikeReference);
                break;
            case "pascal":
                EmitPascalCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "objc":
                EmitObjCMessageReferences(preparedLine, addCallLikeReference, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "haskell":
                EmitHaskellSpaceCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "elixir":
                EmitElixirParenlessCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "lua":
                EmitLuaCommandCallReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
            case "smalltalk":
                EmitSmalltalkMessageReferences(preparedLine, addCallLikeReference, definitionNames);
                break;
        }
    }

    public static void EmitRazorReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlySet<string>? definitionNames)
    {
        foreach (Match match in RazorComponentTagRegex.Matches(originalLine))
        {
            var group = match.Groups["name"];
            var rawName = group.Value;
            var name = LastQualifiedSegment(rawName);
            if (definitionNames?.Contains(name) == true)
                continue;
            var nameOffset = rawName.LastIndexOf(name, StringComparison.Ordinal);
            var nameIndex = group.Index + Math.Max(0, nameOffset);

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameIndex,
                "call",
                context,
                lineNumber,
                resolveContainerForColumn(nameIndex));
        }

        foreach (var match in EnumerateMatches(RazorDirectiveTypeRegex, originalLine).Concat(EnumerateMatches(RazorInjectRegex, originalLine)))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(
                references,
                seen,
                fileId,
                group.Value,
                group.Index,
                context,
                lineNumber,
                resolveContainerForColumn(group.Index),
                "csharp");
        }

        foreach (Match match in RazorEventHandlerRegex.Matches(originalLine))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true)
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                "call",
                context,
                lineNumber,
                resolveContainerForColumn(match.Groups["name"].Index));
        }
    }

    public static bool[] BuildGoImportBlockLineMap(IReadOnlyList<string> originalLines)
    {
        var result = new bool[originalLines.Count];
        var inImportBlock = false;
        var inBlockComment = false;

        for (var i = 0; i < originalLines.Count; i++)
        {
            var line = originalLines[i];
            var codeLine = StripGoComments(line, ref inBlockComment);
            var trimmed = codeLine.Trim();
            if (!inImportBlock)
            {
                if (GoImportBlockStartRegex.IsMatch(codeLine))
                    inImportBlock = !trimmed.Contains(')');
                continue;
            }

            if (trimmed.StartsWith(')'))
            {
                inImportBlock = false;
                continue;
            }

            result[i] = GoImportBlockEntryRegex.IsMatch(codeLine);
            if (trimmed.Contains(')'))
                inImportBlock = false;
        }

        return result;
    }

    private static string StripGoComments(string line, ref bool inBlockComment)
    {
        var chars = line.ToCharArray();
        for (var i = 0; i < line.Length; i++)
        {
            if (inBlockComment)
            {
                chars[i] = ' ';
                if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    chars[++i] = ' ';
                    inBlockComment = false;
                }
                continue;
            }

            if (line[i] is '"' or '`')
            {
                i = SkipGoStringLiteral(line, i);
                continue;
            }

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                for (; i < chars.Length; i++)
                    chars[i] = ' ';
                break;
            }

            if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                chars[i++] = ' ';
                chars[i] = ' ';
                inBlockComment = true;
            }
        }

        return new string(chars);
    }

    private static int SkipGoStringLiteral(string line, int start)
    {
        var quote = line[start];
        var i = start + 1;
        while (i < line.Length)
        {
            if (quote == '"' && line[i] == '\\' && i + 1 < line.Length)
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
                return i;
            i++;
        }

        return line.Length;
    }

    public static string[] MaskLuaLongCommentAndStringLines(IReadOnlyList<string> originalLines)
    {
        var result = new string[originalLines.Count];
        var longTextEqualsCount = -1;

        for (var lineIndex = 0; lineIndex < originalLines.Count; lineIndex++)
        {
            var line = originalLines[lineIndex];
            var chars = line.ToCharArray();
            for (var cursor = 0; cursor < chars.Length; cursor++)
            {
                if (longTextEqualsCount >= 0)
                {
                    if (TryGetLuaLongBracketClose(line, cursor, longTextEqualsCount, out var closeLength))
                    {
                        MaskRange(chars, cursor, cursor + closeLength);
                        cursor += closeLength - 1;
                        longTextEqualsCount = -1;
                        continue;
                    }

                    chars[cursor] = ' ';
                    continue;
                }

                if (chars[cursor] is '"' or '\'')
                {
                    cursor = SkipQuotedLiteral(line, cursor);
                    continue;
                }

                if (chars[cursor] == '-'
                    && cursor + 2 < chars.Length
                    && chars[cursor + 1] == '-'
                    && TryGetLuaLongBracketOpen(line, cursor + 2, out var commentEqualsCount, out var commentOpenLength))
                {
                    MaskRange(chars, cursor, cursor + 2 + commentOpenLength);
                    cursor += 1 + commentOpenLength;
                    longTextEqualsCount = commentEqualsCount;
                    continue;
                }

                if (chars[cursor] == '-' && cursor + 1 < chars.Length && chars[cursor + 1] == '-')
                    break;

                if (TryGetLuaLongBracketOpen(line, cursor, out var stringEqualsCount, out var stringOpenLength))
                {
                    MaskRange(chars, cursor, cursor + stringOpenLength);
                    cursor += stringOpenLength - 1;
                    longTextEqualsCount = stringEqualsCount;
                }
            }

            result[lineIndex] = new string(chars);
        }

        return result;
    }

    private static bool TryGetLuaLongBracketOpen(string line, int start, out int equalsCount, out int length)
    {
        equalsCount = 0;
        length = 0;
        if (start < 0 || start >= line.Length || line[start] != '[')
            return false;

        var cursor = start + 1;
        while (cursor < line.Length && line[cursor] == '=')
        {
            equalsCount++;
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != '[')
            return false;

        length = cursor - start + 1;
        return true;
    }

    private static bool TryGetLuaLongBracketClose(string line, int start, int equalsCount, out int length)
    {
        length = 0;
        if (start < 0 || start >= line.Length || line[start] != ']')
            return false;

        var cursor = start + 1;
        for (var i = 0; i < equalsCount; i++)
        {
            if (cursor >= line.Length || line[cursor] != '=')
                return false;
            cursor++;
        }

        if (cursor >= line.Length || line[cursor] != ']')
            return false;

        length = cursor - start + 1;
        return true;
    }

    public static string[] MaskRazorCommentLines(IReadOnlyList<string> originalLines)
    {
        var result = new string[originalLines.Count];
        var inRazorComment = false;
        var inHtmlComment = false;
        var inCodeBlock = false;
        var codeDepth = 0;
        var inCSharpBlockComment = false;
        var razorControlDepth = 0;
        var inRazorControlBlockComment = false;
        var pendingRazorControlBlock = false;

        for (var lineIndex = 0; lineIndex < originalLines.Count; lineIndex++)
        {
            var line = originalLines[lineIndex];
            var chars = line.ToCharArray();
            var cursor = 0;
            while (cursor < line.Length)
            {
                if (inRazorComment)
                {
                    var close = line.IndexOf("*@", cursor, StringComparison.Ordinal);
                    var end = close < 0 ? line.Length : close + 2;
                    MaskRange(chars, cursor, end);
                    inRazorComment = close < 0;
                    cursor = end;
                    continue;
                }

                if (inHtmlComment)
                {
                    var close = line.IndexOf("-->", cursor, StringComparison.Ordinal);
                    var end = close < 0 ? line.Length : close + 3;
                    MaskRange(chars, cursor, end);
                    inHtmlComment = close < 0;
                    cursor = end;
                    continue;
                }

                if (line.AsSpan(cursor).StartsWith("@*", StringComparison.Ordinal))
                {
                    inRazorComment = true;
                    continue;
                }

                if (line.AsSpan(cursor).StartsWith("<!--", StringComparison.Ordinal))
                {
                    inHtmlComment = true;
                    continue;
                }

                cursor++;
            }

            var codeScanStart = 0;
            if (!inCodeBlock)
            {
                codeScanStart = IndexOfRazorCodeDirective(chars);
                if (codeScanStart >= 0)
                    inCodeBlock = true;
                else
                {
                    codeScanStart = IndexOfRazorExplicitCodeBlock(chars);
                    if (codeScanStart >= 0)
                        inCodeBlock = true;
                }
            }

            if (inCodeBlock)
                MaskCSharpStringsAndCommentsInRazorCode(chars, Math.Max(0, codeScanStart), ref codeDepth, ref inCodeBlock, ref inCSharpBlockComment);
            else
            {
                var controlStart = IndexOfRazorControlDirective(chars);
                if (controlStart >= 0)
                {
                    var delta = MaskRazorControlCodeLine(chars, controlStart, ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    pendingRazorControlBlock = delta <= 0;
                }
                else if (IndexOfRazorBareControlContinuation(chars) is var continuationStart && continuationStart >= 0)
                {
                    var delta = MaskRazorControlCodeLine(chars, continuationStart, ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    pendingRazorControlBlock = delta <= 0;
                }
                else if (pendingRazorControlBlock && IsRazorCodeLineInsideControl(chars))
                {
                    var delta = MaskRazorControlCodeLine(chars, FirstNonWhitespaceIndex(chars), ref inRazorControlBlockComment);
                    razorControlDepth = Math.Max(0, razorControlDepth + delta);
                    if (delta != 0)
                        pendingRazorControlBlock = false;
                }
                else if (razorControlDepth > 0 && IsRazorCodeLineInsideControl(chars))
                {
                    razorControlDepth = Math.Max(
                        0,
                        razorControlDepth + MaskRazorControlCodeLine(chars, FirstNonWhitespaceIndex(chars), ref inRazorControlBlockComment));
                }
            }

            result[lineIndex] = new string(chars);
        }

        return result;
    }

    private static int IndexOfRazorCodeDirective(char[] chars)
    {
        var line = new string(chars);
        foreach (var directive in new[] { "@code", "@functions" })
        {
            var index = line.IndexOf(directive, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var beforeOk = index == 0 || char.IsWhiteSpace(line[index - 1]);
            var afterIndex = index + directive.Length;
            var afterOk = afterIndex == line.Length || char.IsWhiteSpace(line[afterIndex]) || line[afterIndex] == '{';
            if (beforeOk && afterOk)
                return index;
        }

        return -1;
    }

    private static int IndexOfRazorBareControlContinuation(char[] chars)
    {
        var index = FirstNonWhitespaceIndex(chars);
        if (index < 0)
            return -1;

        var line = new string(chars);
        foreach (var keyword in new[] { "else", "catch", "finally" })
        {
            if (line.AsSpan(index).StartsWith(keyword, StringComparison.Ordinal)
                && (index + keyword.Length == line.Length || !IsSimpleIdentifierPart(line[index + keyword.Length])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int IndexOfRazorExplicitCodeBlock(char[] chars)
    {
        var line = new string(chars);
        var index = line.IndexOf("@{", StringComparison.Ordinal);
        return index >= 0 ? index : -1;
    }

    private static int IndexOfRazorControlDirective(char[] chars)
    {
        var line = new string(chars);
        foreach (var directive in new[] { "@if", "@foreach", "@for", "@while", "@switch", "@using", "@lock", "@try", "@catch", "@finally", "@do" })
        {
            for (var index = line.IndexOf(directive, StringComparison.Ordinal);
                 index >= 0;
                 index = line.IndexOf(directive, index + directive.Length, StringComparison.Ordinal))
            {
                var beforeOk = index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] == '}';
                var afterIndex = index + directive.Length;
                var afterOk = afterIndex == line.Length || !IsSimpleIdentifierPart(line[afterIndex]);
                if (beforeOk && afterOk)
                    return index;
            }
        }

        return -1;
    }

    private static bool IsRazorCodeLineInsideControl(char[] chars)
    {
        var index = FirstNonWhitespaceIndex(chars);
        if (index < 0)
            return false;

        return !LooksLikeRazorMarkupStart(new string(chars), index);
    }

    private static int FirstNonWhitespaceIndex(char[] chars)
    {
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsWhiteSpace(chars[i]))
                return i;
        }

        return -1;
    }

    private static bool LooksLikeRazorMarkupStart(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
        if (index >= line.Length)
            return false;
        if (line[index] == '<')
            return true;

        return line[index] == '@'
            && index + 1 < line.Length
            && line[index + 1] is ':' or '<';
    }

    private static int MaskRazorControlCodeLine(char[] chars, int start, ref bool inBlockComment)
    {
        var line = new string(chars);
        var firstOpenBrace = -1;
        var delta = CountCSharpBraceDelta(line, start, ref inBlockComment, ref firstOpenBrace);
        if (firstOpenBrace >= 0 && LooksLikeRazorMarkupStart(line, firstOpenBrace + 1))
        {
            MaskRange(chars, start, firstOpenBrace + 1);
            return delta;
        }

        MaskRange(chars, start, chars.Length);
        return delta;
    }

    private static int CountCSharpBraceDelta(string line, int start, ref bool inBlockComment, ref int firstOpenBrace)
    {
        var delta = 0;
        for (var cursor = Math.Max(0, start); cursor < line.Length; cursor++)
        {
            if (inBlockComment)
            {
                if (line[cursor] == '*' && cursor + 1 < line.Length && line[cursor + 1] == '/')
                {
                    inBlockComment = false;
                    cursor++;
                }

                continue;
            }

            if (line[cursor] == '/' && cursor + 1 < line.Length && line[cursor + 1] == '/')
                break;

            if (line[cursor] == '/' && cursor + 1 < line.Length && line[cursor + 1] == '*')
            {
                inBlockComment = true;
                cursor++;
                continue;
            }

            if (line[cursor] is '"' or '\'')
            {
                var quote = line[cursor++];
                while (cursor < line.Length)
                {
                    if (line[cursor] == '\\' && cursor + 1 < line.Length)
                    {
                        cursor += 2;
                        continue;
                    }

                    if (line[cursor] == quote)
                        break;
                    cursor++;
                }

                continue;
            }

            if (line[cursor] == '{')
            {
                if (firstOpenBrace < 0)
                    firstOpenBrace = cursor;
                delta++;
            }
            else if (line[cursor] == '}')
            {
                delta--;
            }
        }

        return delta;
    }

    private static void MaskCSharpStringsAndCommentsInRazorCode(
        char[] chars,
        int start,
        ref int codeDepth,
        ref bool inCodeBlock,
        ref bool inBlockComment)
    {
        for (var cursor = start; cursor < chars.Length; cursor++)
        {
            if (inBlockComment)
            {
                if (chars[cursor] == '*' && cursor + 1 < chars.Length && chars[cursor + 1] == '/')
                {
                    chars[cursor++] = ' ';
                    chars[cursor] = ' ';
                    inBlockComment = false;
                    continue;
                }

                chars[cursor] = ' ';
                continue;
            }

            if (chars[cursor] == '/' && cursor + 1 < chars.Length && chars[cursor + 1] == '/')
            {
                MaskRange(chars, cursor, chars.Length);
                break;
            }

            if (chars[cursor] == '/' && cursor + 1 < chars.Length && chars[cursor + 1] == '*')
            {
                chars[cursor++] = ' ';
                chars[cursor] = ' ';
                inBlockComment = true;
                continue;
            }

            if (chars[cursor] is '"' or '\'')
            {
                var quote = chars[cursor];
                chars[cursor++] = ' ';
                while (cursor < chars.Length)
                {
                    if (chars[cursor] == '\\' && cursor + 1 < chars.Length)
                    {
                        chars[cursor++] = ' ';
                        chars[cursor] = ' ';
                        cursor++;
                        continue;
                    }

                    var closes = chars[cursor] == quote;
                    chars[cursor++] = ' ';
                    if (closes)
                        break;
                }
                cursor--;
                continue;
            }

            if (chars[cursor] == '{')
            {
                codeDepth++;
                chars[cursor] = ' ';
                continue;
            }

            if (chars[cursor] == '}' && codeDepth > 0)
            {
                codeDepth--;
                chars[cursor] = ' ';
                if (codeDepth == 0)
                    inCodeBlock = false;
                continue;
            }

            chars[cursor] = ' ';
        }
    }

    private static void EmitCppTypeReferences(
        string language,
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var includeMatch = !string.IsNullOrWhiteSpace(preparedLine)
            ? CppIncludeRegex.Match(originalLine)
            : Match.Empty;
        if (includeMatch.Success)
        {
            var group = includeMatch.Groups["name"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "type_reference", context, lineNumber, resolveContainerForColumn(group.Index));
        }

        var baseMatch = CppBaseListRegex.Match(preparedLine);
        if (baseMatch.Success)
        {
            var group = baseMatch.Groups["bases"];
            foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(group.Value))
            {
                var expression = StripCppAccessPrefix(group.Value.Substring(segmentStart, segmentLength));
                if (expression.Length == 0)
                    continue;

                var absoluteStart = group.Index + segmentStart + group.Value.Substring(segmentStart, segmentLength).IndexOf(expression, StringComparison.Ordinal);
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, expression, absoluteStart, context, lineNumber, resolveContainerForColumn(absoluteStart), language);
            }
        }

        foreach (Match match in CppNewTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            var typeName = LastCppQualifiedSegment(group.Value);
            var typeStart = group.Index + group.Value.LastIndexOf(typeName, StringComparison.Ordinal);
            ReferenceExtractor.AddReference(references, seen, fileId, typeName, typeStart, "instantiate", context, lineNumber, resolveContainerForColumn(typeStart));
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), language);
        }

        foreach (Match match in CppDeclarationTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            var expression = StripCppAccessPrefix(group.Value);
            if (expression.Length == 0)
                continue;

            var start = group.Index + group.Value.IndexOf(expression, StringComparison.Ordinal);
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, expression, start, context, lineNumber, resolveContainerForColumn(start), language);
        }
    }

    private static void EmitGoTypeReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        bool isImportBlockLine)
    {
        var importMatch = !string.IsNullOrWhiteSpace(preparedLine)
            ? isImportBlockLine
                ? GoImportBlockEntryRegex.Match(originalLine)
                : GoImportRegex.Match(originalLine)
            : Match.Empty;
        if (importMatch.Success)
        {
            var group = importMatch.Groups["name"];
            var aliasGroup = importMatch.Groups["alias"];
            if (aliasGroup.Success && aliasGroup.Value is not "." and not "_")
            {
                ReferenceExtractor.AddReference(references, seen, fileId, aliasGroup.Value, aliasGroup.Index, "type_reference", context, lineNumber, resolveContainerForColumn(aliasGroup.Index));
            }
            else
            {
                var packageName = LastPathSegment(group.Value);
                var packageOffset = group.Value.LastIndexOf(packageName, StringComparison.Ordinal);
                ReferenceExtractor.AddReference(references, seen, fileId, packageName, group.Index + Math.Max(0, packageOffset), "type_reference", context, lineNumber, resolveContainerForColumn(group.Index));
            }
        }

        foreach (var regex in new[] { GoVarTypeRegex, GoFieldTypeRegex, GoTypeAliasRegex })
        {
            foreach (Match match in regex.Matches(preparedLine))
            {
                var group = match.Groups["type"];
                EmitGoTypeExpression(group.Value, group.Index, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            }
        }

        if (GoFuncRegex.IsMatch(preparedLine))
            EmitGoFunctionSignatureTypes(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        foreach (Match match in GoCompositeLiteralRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            if (!IsGoCompositeLiteralContext(preparedLine, group.Index, group.Value.Length))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "instantiate", context, lineNumber, resolveContainerForColumn(group.Index));
        }
    }

    private static bool IsGoCompositeLiteralContext(string line, int nameIndex, int nameLength)
    {
        var openBraceIndex = line.IndexOf('{', nameIndex + nameLength);
        if (openBraceIndex < 0)
            return false;

        var trimmed = line.TrimStart();
        var firstBraceIndex = line.IndexOf('{');
        if (trimmed.StartsWith("func ", StringComparison.Ordinal) && firstBraceIndex == openBraceIndex)
            return false;

        var previous = nameIndex - 1;
        while (previous >= 0 && char.IsWhiteSpace(line[previous]))
            previous--;
        if (previous < 0)
            return false;

        if (line[previous] is '=' or ':' or '(' or '[' or '{' or ',' or '!' or '&' or '*')
            return true;
        if (line[previous] == '.')
            return previous > 0 && IsSimpleIdentifierPart(line[previous - 1]);
        if (line[previous] == ']')
            return !trimmed.StartsWith("func ", StringComparison.Ordinal);

        var tokenEnd = previous + 1;
        while (previous >= 0 && IsSimpleIdentifierPart(line[previous]))
            previous--;
        var token = line[(previous + 1)..tokenEnd];
        return string.Equals(token, "return", StringComparison.Ordinal);
    }

    private static void EmitDartTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        TypedLanguageReferenceExtractor.EmitKeywordFollowingTypeReferences(
            preparedLine,
            ["extends", "with", "implements", "on", "as", "is"],
            "dart",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(preparedLine, 0, preparedLine.Length, "dart", references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonVariableTypeReferences(preparedLine, ["final", "var", "late", "const"], "dart", references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        foreach (Match match in DartVariableTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "dart");
        }

        var signatureMatch = DartFunctionSignatureRegex.Match(preparedLine);
        if (signatureMatch.Success)
        {
            var returnGroup = signatureMatch.Groups["return"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, returnGroup.Value, returnGroup.Index, context, lineNumber, resolveContainerForColumn(returnGroup.Index), "dart");

            var parametersGroup = signatureMatch.Groups["params"];
            foreach (Match parameterMatch in DartParameterTypeRegex.Matches(parametersGroup.Value))
            {
                var typeGroup = parameterMatch.Groups["type"];
                var absoluteIndex = parametersGroup.Index + typeGroup.Index;
                ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, typeGroup.Value, absoluteIndex, context, lineNumber, resolveContainerForColumn(absoluteIndex), "dart");
            }
        }

        foreach (Match match in DartCtorRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "instantiate", context, lineNumber, resolveContainerForColumn(group.Index));
        }
    }

    private static void EmitVbTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in VbTypeKeywordRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "vb");
        }

        foreach (Match match in VbAddressOfRegex.Matches(preparedLine))
        {
            var group = match.Groups["name"];
            var name = LastQualifiedSegment(group.Value);
            var nameOffset = group.Value.LastIndexOf(name, StringComparison.Ordinal);
            var nameIndex = group.Index + Math.Max(0, nameOffset);
            ReferenceExtractor.AddReference(references, seen, fileId, name, nameIndex, "call", context, lineNumber, resolveContainerForColumn(nameIndex));
        }

        foreach (Match match in VbHandlesRegex.Matches(preparedLine))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, resolveContainerForColumn(match.Groups["name"].Index));
    }

    private static void EmitFortranTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        foreach (Match match in FortranUseRegex.Matches(preparedLine))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "type_reference", context, lineNumber, container);

        foreach (Match match in FortranTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "fortran");
        }
    }

    private static void EmitPascalTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        var usesMatch = PascalUsesRegex.Match(preparedLine);
        if (usesMatch.Success)
            EmitCommaSeparatedNames(usesMatch.Groups["list"].Value, usesMatch.Groups["list"].Index, "pascal", references, seen, fileId, context, lineNumber, container);

        foreach (Match match in PascalClassBaseRegex.Matches(preparedLine))
            EmitCommaSeparatedNames(match.Groups["bases"].Value, match.Groups["bases"].Index, "pascal", references, seen, fileId, context, lineNumber, resolveContainerForColumn(match.Groups["bases"].Index));

        foreach (Match match in PascalTypeAfterColonRegex.Matches(preparedLine))
        {
            if (!IsPascalColonTypeReferenceContext(preparedLine, lineNumber, container))
                continue;

            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "pascal");
        }
    }

    private static bool IsPascalColonTypeReferenceContext(string preparedLine, int lineNumber, SymbolRecord? container)
    {
        var trimmed = preparedLine.TrimStart();
        if (container?.Kind != "function"
            || !container.BodyStartLine.HasValue
            || lineNumber < container.BodyStartLine.Value)
        {
            return true;
        }

        return StartsWithPascalDeclarationKeyword(trimmed);
    }

    private static bool StartsWithPascalDeclarationKeyword(string trimmedLine)
    {
        foreach (var keyword in new[] { "var", "const", "type", "property", "procedure", "function", "constructor", "destructor" })
        {
            if (trimmedLine.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                && (trimmedLine.Length == keyword.Length || !IsSimpleIdentifierPart(trimmedLine[keyword.Length])))
            {
                return true;
            }
        }

        return false;
    }

    private static void EmitObjCTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        foreach (Match match in ObjCInterfaceBaseRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "type_reference", context, lineNumber, container);
        }

        foreach (Match match in ObjCProtocolListRegex.Matches(preparedLine))
            EmitCommaSeparatedNames(match.Groups["list"].Value, match.Groups["list"].Index, "objc", references, seen, fileId, context, lineNumber, container);

        foreach (Match match in ObjCDeclTypeRegex.Matches(preparedLine))
        {
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "objc");
        }
    }

    private static void EmitHaskellTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = HaskellSignatureRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var group = match.Groups["types"];
        ReferenceExtractor.AddTypeExpressionSegments(
            references,
            seen,
            fileId,
            group.Value,
            group.Index,
            context,
            lineNumber,
            container,
            "haskell",
            BuildHaskellIgnoredTypeVariables(group.Value));
    }

    private static IReadOnlySet<string>? BuildHaskellIgnoredTypeVariables(string expression)
    {
        HashSet<string>? ignored = null;
        for (var cursor = 0; cursor < expression.Length; cursor++)
        {
            if (!IsSimpleIdentifierPart(expression[cursor]))
                continue;

            var start = cursor;
            while (cursor < expression.Length && IsSimpleIdentifierPart(expression[cursor]))
                cursor++;

            if (char.IsLower(expression[start]))
            {
                ignored ??= new HashSet<string>(StringComparer.Ordinal);
                ignored.Add(expression[start..cursor]);
            }

            cursor--;
        }

        return ignored;
    }

    private static void EmitElixirTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var match in EnumerateMatches(ElixirImportRegex, preparedLine).Concat(EnumerateMatches(ElixirBehaviourRegex, preparedLine)))
            ReferenceExtractor.AddReference(references, seen, fileId, match, "type_reference", context, lineNumber, container);
    }

    private static void EmitLuaTypeReferences(
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var (name, index) in EnumerateLuaRequireReferences(originalLine))
            ReferenceExtractor.AddReference(references, seen, fileId, name, index, "type_reference", context, lineNumber, container);
    }

    private static IEnumerable<(string Name, int Index)> EnumerateLuaRequireReferences(string line)
    {
        for (var cursor = 0; cursor < line.Length; cursor++)
        {
            if (line[cursor] == '-' && cursor + 1 < line.Length && line[cursor + 1] == '-')
                yield break;

            if (line[cursor] is '"' or '\'')
            {
                cursor = SkipQuotedLiteral(line, cursor);
                continue;
            }

            if (line[cursor] == '[' && cursor + 1 < line.Length && line[cursor + 1] == '[')
            {
                var close = line.IndexOf("]]", cursor + 2, StringComparison.Ordinal);
                cursor = close < 0 ? line.Length : close + 1;
                continue;
            }

            if (!IsIdentifierAt(line, cursor, "require"))
                continue;

            var argStart = cursor + "require".Length;
            while (argStart < line.Length && char.IsWhiteSpace(line[argStart]))
                argStart++;
            if (argStart < line.Length && line[argStart] == '(')
            {
                argStart++;
                while (argStart < line.Length && char.IsWhiteSpace(line[argStart]))
                    argStart++;
            }

            if (argStart >= line.Length || line[argStart] is not ('"' or '\''))
                continue;

            var quote = line[argStart++];
            var nameStart = argStart;
            while (argStart < line.Length)
            {
                if (line[argStart] == '\\' && argStart + 1 < line.Length)
                {
                    argStart += 2;
                    continue;
                }

                if (line[argStart] == quote)
                    break;
                argStart++;
            }

            if (argStart > nameStart)
                yield return (line[nameStart..argStart], nameStart);
            cursor = argStart;
        }
    }

    private static int SkipQuotedLiteral(string line, int start)
    {
        var quote = line[start];
        var cursor = start + 1;
        while (cursor < line.Length)
        {
            if (line[cursor] == '\\' && cursor + 1 < line.Length)
            {
                cursor += 2;
                continue;
            }

            if (line[cursor] == quote)
                return cursor;
            cursor++;
        }

        return line.Length;
    }

    private static bool IsIdentifierAt(string line, int index, string identifier)
    {
        if (index < 0 || index + identifier.Length > line.Length)
            return false;
        if (string.CompareOrdinal(line, index, identifier, 0, identifier.Length) != 0)
            return false;
        if (index > 0 && IsSimpleIdentifierPart(line[index - 1]))
            return false;

        var after = index + identifier.Length;
        return after >= line.Length || !IsSimpleIdentifierPart(line[after]);
    }

    private static bool IsSimpleIdentifierPart(char ch) =>
        ch == '_' || char.IsLetterOrDigit(ch);

    private static void EmitFortranCallReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        foreach (Match match in FortranCallRegex.Matches(preparedLine))
            addCallLikeReference(match.Groups["name"].Value, match.Groups["name"].Index);
    }

    private static void EmitPascalCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var match = PascalBareCallRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var name = match.Groups["name"].Value;
        if (definitionNames?.Contains(name) == true)
            return;

        addCallLikeReference(name, match.Groups["name"].Index);
    }

    private static void EmitObjCMessageReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in ObjCMessageRegex.Matches(preparedLine))
        {
            var receiver = match.Groups["receiver"];
            var selector = match.Groups["name"];
            if (char.IsUpper(receiver.Value[0]) && selector.Value is "alloc" or "new")
            {
                ReferenceExtractor.AddReference(references, seen, fileId, receiver.Value, receiver.Index, "instantiate", context, lineNumber, resolveContainerForColumn(receiver.Index));
            }

            addCallLikeReference(selector.Value, selector.Index);
        }

        foreach (Match match in ObjCSelectorRegex.Matches(preparedLine))
            addCallLikeReference(match.Groups["name"].Value.TrimEnd(':'), match.Groups["name"].Index);
    }

    private static void EmitHaskellSpaceCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var definitionMatch = HaskellDefinitionRegex.Match(preparedLine);
        var definitionName = definitionMatch.Success ? definitionMatch.Groups["name"].Value : null;
        var scanStart = 0;
        var scanText = preparedLine;
        if (definitionMatch.Success)
        {
            var equalsIndex = preparedLine.IndexOf('=');
            if (equalsIndex >= 0)
            {
                scanStart = equalsIndex + 1;
                scanText = preparedLine[scanStart..];
            }
        }

        foreach (Match match in HaskellSpaceCallRegex.Matches(scanText))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true || string.Equals(name, definitionName, StringComparison.Ordinal))
                continue;
            addCallLikeReference(name, scanStart + match.Groups["name"].Index);
        }
    }

    private static void EmitElixirParenlessCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        foreach (Match match in ElixirParenlessCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (definitionNames?.Contains(name) == true)
                continue;
            addCallLikeReference(name, match.Groups["name"].Index);
        }
    }

    private static void EmitLuaCommandCallReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var match = LuaCommandCallRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var name = LastQualifiedSegment(match.Groups["name"].Value);
        if (definitionNames?.Contains(name) == true)
            return;
        addCallLikeReference(name, match.Groups["name"].Index + match.Groups["name"].Value.LastIndexOf(name, StringComparison.Ordinal));
    }

    private static void EmitSmalltalkMessageReferences(string preparedLine, Action<string, int> addCallLikeReference, IReadOnlySet<string>? definitionNames)
    {
        var definitionMatch = SmalltalkMethodDefinitionRegex.Match(preparedLine);
        if (definitionMatch.Success || SmalltalkClassDeclarationRegex.IsMatch(preparedLine))
            return;

        var consumedUntil = 0;
        foreach (Match match in SmalltalkMessageSendRegex.Matches(preparedLine))
        {
            if (match.Index < consumedUntil)
                continue;

            var selectorGroup = match.Groups["selector"];
            var name = ReadSmalltalkSelector(preparedLine, selectorGroup.Index, out var selectorEndIndex);
            consumedUntil = Math.Max(consumedUntil, selectorEndIndex);
            if (definitionNames?.Contains(name) == true)
                continue;
            addCallLikeReference(name, selectorGroup.Index);
        }
    }

    private static string ReadSmalltalkSelector(string line, int selectorIndex, out int endIndex)
    {
        if (!TryReadSmalltalkSelectorPart(line, selectorIndex, out var firstPart, out var cursor))
        {
            endIndex = selectorIndex;
            return string.Empty;
        }

        if (!firstPart.EndsWith(':'))
        {
            endIndex = cursor;
            return firstPart;
        }

        var selector = firstPart;
        while (true)
        {
            var argumentStart = SkipWhitespace(line, cursor);
            if (argumentStart >= line.Length || !IsIdentifierStart(line[argumentStart]))
                break;

            var argumentEnd = argumentStart + 1;
            while (argumentEnd < line.Length && IsSimpleIdentifierPart(line[argumentEnd]))
                argumentEnd++;

            var nextSelectorStart = SkipWhitespace(line, argumentEnd);
            if (!TryReadSmalltalkSelectorPart(line, nextSelectorStart, out var nextPart, out var nextEnd)
                || !nextPart.EndsWith(':'))
            {
                break;
            }

            selector += nextPart;
            cursor = nextEnd;
        }

        endIndex = cursor;
        return selector;
    }

    private static bool TryReadSmalltalkSelectorPart(string line, int start, out string part, out int end)
    {
        part = string.Empty;
        end = start;
        if (start >= line.Length || !IsIdentifierStart(line[start]))
            return false;

        end = start + 1;
        while (end < line.Length && IsSimpleIdentifierPart(line[end]))
            end++;
        if (end < line.Length && line[end] == ':')
            end++;

        part = line[start..end];
        return true;
    }

    private static void EmitGoFunctionSignatureTypes(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var firstParen = preparedLine.IndexOf('(');
        if (firstParen < 0)
            return;

        var parameterOpen = firstParen;
        var receiverClose = ReferenceExtractor.FindMatchingChar(preparedLine, firstParen, '(', ')');
        if (receiverClose >= 0)
        {
            var afterReceiver = receiverClose + 1;
            while (afterReceiver < preparedLine.Length && char.IsWhiteSpace(preparedLine[afterReceiver]))
                afterReceiver++;
            if (afterReceiver < preparedLine.Length && IsIdentifierStart(preparedLine[afterReceiver]))
            {
                var nextParen = preparedLine.IndexOf('(', afterReceiver);
                if (nextParen > afterReceiver)
                    parameterOpen = nextParen;
            }
        }

        var parameterClose = ReferenceExtractor.FindMatchingChar(preparedLine, parameterOpen, '(', ')');
        if (parameterClose < 0)
            return;

        EmitGoParameterListTypes(preparedLine, parameterOpen + 1, parameterClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

        var returnStart = parameterClose + 1;
        while (returnStart < preparedLine.Length && char.IsWhiteSpace(preparedLine[returnStart]))
            returnStart++;
        if (returnStart >= preparedLine.Length || preparedLine[returnStart] == '{')
            return;

        if (preparedLine[returnStart] == '(')
        {
            var returnClose = ReferenceExtractor.FindMatchingChar(preparedLine, returnStart, '(', ')');
            if (returnClose > returnStart)
                EmitGoParameterListTypes(preparedLine, returnStart + 1, returnClose, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
            return;
        }

        var returnEnd = returnStart;
        while (returnEnd < preparedLine.Length && !char.IsWhiteSpace(preparedLine[returnEnd]) && preparedLine[returnEnd] != '{')
            returnEnd++;
        EmitGoTypeExpression(preparedLine[returnStart..returnEnd], returnStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoParameterListTypes(
        string line,
        int start,
        int end,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (end <= start)
            return;

        var list = line[start..end];
        var pendingSingleExpressions = new List<(string Expression, int AbsoluteStart)>();
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(list))
        {
            var rawSegment = list.Substring(segmentStart, segmentLength);
            var fragment = rawSegment.Trim();
            if (fragment.Length == 0)
                continue;

            var fragmentTrimStart = rawSegment.IndexOf(fragment, StringComparison.Ordinal);
            var absoluteFragmentStart = start + segmentStart + Math.Max(0, fragmentTrimStart);
            var typeStartInFragment = LastWhitespaceSeparatedTokenStart(fragment);
            if (typeStartInFragment < 0)
                continue;
            if (typeStartInFragment == 0)
            {
                pendingSingleExpressions.Add((fragment, absoluteFragmentStart));
                continue;
            }

            pendingSingleExpressions.Clear();
            var expression = fragment[typeStartInFragment..];
            var absoluteStart = absoluteFragmentStart + typeStartInFragment;
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        }

        foreach (var (expression, absoluteStart) in pendingSingleExpressions)
            EmitGoTypeExpression(expression, absoluteStart, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitGoTypeExpression(
        string expression,
        int start,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var normalized = expression.Trim();
        var leading = expression.IndexOf(normalized, StringComparison.Ordinal);
        if (normalized.Length == 0)
            return;
        var absoluteStart = start + Math.Max(0, leading);
        ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, normalized, absoluteStart, context, lineNumber, resolveContainerForColumn(absoluteStart), "go");
    }

    private static void EmitCommaSeparatedNames(
        string list,
        int listStart,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(list))
        {
            var raw = list.Substring(segmentStart, segmentLength).Trim();
            if (raw.Length == 0)
                continue;
            var name = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? raw;
            var offset = list.IndexOf(name, segmentStart, StringComparison.Ordinal);
            if (offset < 0)
                offset = segmentStart;
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, name, listStart + offset, context, lineNumber, container, language);
        }
    }

    private static string StripCppAccessPrefix(string value)
    {
        var text = value.Trim();
        bool removed;
        do
        {
            removed = false;
            foreach (var prefix in new[] { "public ", "private ", "protected ", "virtual " })
            {
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    text = text[prefix.Length..].TrimStart();
                    removed = true;
                }
            }
        } while (removed);

        return text;
    }

    private static string LastCppQualifiedSegment(string value)
    {
        var text = value.Trim();
        var genericIndex = text.IndexOf('<');
        if (genericIndex >= 0)
            text = text[..genericIndex].TrimEnd();
        var separator = text.LastIndexOf("::", StringComparison.Ordinal);
        return separator >= 0 ? text[(separator + 2)..].Trim() : text;
    }

    private static string LastQualifiedSegment(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && dot + 1 < value.Length ? value[(dot + 1)..] : value;
    }

    private static string LastPathSegment(string value)
    {
        var slash = value.LastIndexOf('/');
        return slash >= 0 && slash + 1 < value.Length ? value[(slash + 1)..] : value;
    }

    private static int LastWhitespaceSeparatedTokenStart(string value)
    {
        var end = value.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(value[end]))
            end--;
        if (end < 0)
            return -1;

        var start = end;
        while (start >= 0 && !char.IsWhiteSpace(value[start]))
            start--;
        return start + 1;
    }

    private static IEnumerable<Match> EnumerateMatches(Regex regex, string input)
    {
        foreach (Match match in regex.Matches(input))
            yield return match;
    }

    private static void MaskRange(char[] chars, int start, int end)
    {
        for (var i = start; i < end && i < chars.Length; i++)
            chars[i] = ' ';
    }

    private static int SkipWhitespace(string line, int start)
    {
        while (start < line.Length && char.IsWhiteSpace(line[start]))
            start++;
        return start;
    }

    private static bool IsIdentifierStart(char ch) =>
        ch == '_' || char.IsLetter(ch);
}
