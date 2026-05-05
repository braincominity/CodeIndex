using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class BroadLanguageReferenceExtractor
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
        @"^\s*(?:import\s+)?(?:[A-Za-z_]\w*\s+)?""(?<name>[^""]+)""",
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
        @"\bAddressOf\s+(?<name>[A-Za-z_]\w*)",
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
        SymbolRecord? container)
    {
        switch (language)
        {
            case "c":
            case "cpp":
                EmitCppTypeReferences(language, preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
                break;
            case "go":
                EmitGoTypeReferences(preparedLine, originalLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
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
        var includeMatch = CppIncludeRegex.Match(originalLine);
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
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var importMatch = GoImportRegex.Match(originalLine);
        if (importMatch.Success)
        {
            var group = importMatch.Groups["name"];
            var packageName = LastPathSegment(group.Value);
            var packageOffset = group.Value.LastIndexOf(packageName, StringComparison.Ordinal);
            ReferenceExtractor.AddReference(references, seen, fileId, packageName, group.Index + Math.Max(0, packageOffset), "type_reference", context, lineNumber, resolveContainerForColumn(group.Index));
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
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "instantiate", context, lineNumber, resolveContainerForColumn(group.Index));
        }
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
            ReferenceExtractor.AddReference(references, seen, fileId, match, "call", context, lineNumber, resolveContainerForColumn(match.Groups["name"].Index));

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
            var group = match.Groups["type"];
            ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, resolveContainerForColumn(group.Index), "pascal");
        }
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
        ReferenceExtractor.AddTypeExpressionSegments(references, seen, fileId, group.Value, group.Index, context, lineNumber, container, "haskell");
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
        foreach (Match match in LuaRequireRegex.Matches(originalLine))
        {
            var group = match.Groups["name"];
            ReferenceExtractor.AddReference(references, seen, fileId, group.Value, group.Index, "type_reference", context, lineNumber, container);
        }
    }

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

        foreach (Match match in SmalltalkMessageSendRegex.Matches(preparedLine))
        {
            var name = match.Groups["selector"].Value;
            if (definitionNames?.Contains(name) == true)
                continue;
            addCallLikeReference(name, match.Groups["selector"].Index);
        }
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

    private static bool IsIdentifierStart(char ch) =>
        ch == '_' || char.IsLetter(ch);
}
