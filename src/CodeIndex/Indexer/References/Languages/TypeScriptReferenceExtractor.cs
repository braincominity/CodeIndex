using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class TypeScriptReferenceExtractor
{
    internal sealed record NamespaceAliasBinding(string Alias, string ModuleSpecifier, int BindingLine, int? ShadowLine, int? EndLine);

    private static readonly string[] DeclarationKeywords = ["const", "let", "var"];
    private static readonly string[] TypeOperatorKeywords = ["as", "satisfies", "instanceof"];
    private static readonly Regex NamespaceImportExportRegex = new(
        @"^\s*(?:import|export)\s+(?:type\s+)?\*\s*as\s*(?<alias>[A-Za-z_$][\w$]*)\s+from\s*[""'](?<module>[^""']+)[""']",
        RegexOptions.Compiled);
    private static readonly Regex DynamicImportNamespaceRegex = new(
        @"^\s*(?:const|let|var)\s+(?<alias>[A-Za-z_$][\w$]*)\s*=\s*(?:await\s+)?import\s*\(\s*[""'](?<module>[^""']+)[""']\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex NamedImportRegex = new(
        @"^\s*import\s+(?:type\s+)?\{(?<body>[^}]*)\}\s+from\s*[""'](?<module>[^""']+)[""']",
        RegexOptions.Compiled);
    private static readonly Regex LocalDeclarationRegex = new(
        @"^\s*(?:(?:const|let|var)\s+|(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+|(?:export\s+)?(?:abstract\s+)?class\s+|(?:export\s+)?interface\s+|(?:export\s+)?type\s+)(?<name>[A-Za-z_$][\w$]*)\b",
        RegexOptions.Compiled);

    public static IReadOnlyList<NamespaceAliasBinding> BuildNamespaceAliasBindings(
        IReadOnlyList<string> originalLines,
        IReadOnlyList<string> preparedLines)
    {
        var bindings = new List<NamespaceAliasBinding>();
        var braceDepths = BuildBraceDepthsBeforeLine(preparedLines);
        for (var index = 0; index < originalLines.Count; index++)
        {
            var line = originalLines[index];
            var match = NamespaceImportExportRegex.Match(line);
            if (match.Success)
            {
                AddNamespaceAliasBinding(
                    bindings,
                    preparedLines,
                    match.Groups["alias"].Value,
                    match.Groups["module"].Value,
                    index + 1,
                    endLine: null);
                continue;
            }

            match = DynamicImportNamespaceRegex.Match(line);
            if (match.Success)
            {
                var bindingLine = index + 1;
                AddNamespaceAliasBinding(
                    bindings,
                    preparedLines,
                    match.Groups["alias"].Value,
                    match.Groups["module"].Value,
                    bindingLine,
                    FindDynamicImportAliasEndLine(preparedLines, braceDepths, index));
                continue;
            }

            match = NamedImportRegex.Match(line);
            if (!match.Success)
                continue;

            foreach (var alias in ExtractNamedImportExportAliases(match.Groups["body"].Value))
            {
                AddNamespaceAliasBinding(
                    bindings,
                    preparedLines,
                    alias,
                    match.Groups["module"].Value,
                    index + 1,
                    endLine: null);
            }
        }

        return bindings;
    }

    private static void AddNamespaceAliasBinding(
        List<NamespaceAliasBinding> bindings,
        IReadOnlyList<string> preparedLines,
        string alias,
        string module,
        int bindingLine,
        int? endLine)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(module))
            return;

        var shadowLine = FindShadowLine(preparedLines, alias, bindingLine);
        bindings.Add(new NamespaceAliasBinding(alias, module, bindingLine, shadowLine, endLine));
    }

    private static IEnumerable<string> ExtractNamedImportExportAliases(string body)
    {
        foreach (var part in body.Split(','))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;

            var asIndex = item.LastIndexOf(" as ", StringComparison.Ordinal);
            var alias = asIndex >= 0 ? item[(asIndex + 4)..].Trim() : item;
            if (IsTypeScriptIdentifier(alias))
                yield return alias;
        }
    }

    public static void EmitTypePositionReferences(
        IReadOnlyList<string> preparedLines,
        int lineIndex,
        string preparedLine,
        string rawLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlyList<NamespaceAliasBinding> namespaceAliases)
    {
        EmitNamespaceAliasQualifiedReferences(
            preparedLines,
            lineIndex,
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            namespaceAliases);

        ReferenceExtractor.EmitTypeScriptTypePositionReferences(
            preparedLines,
            lineIndex,
            preparedLine,
            rawLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

        EmitHeritageTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitCallableSignatureTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitFunctionPropertyTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitDecoratedMemberTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonVariableTypeReferences(
            preparedLine,
            DeclarationKeywords,
            "typescript",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
        if (!IsImportExportAliasLine(preparedLines, lineIndex, preparedLine))
        {
            TypedLanguageReferenceExtractor.EmitKeywordFollowingTypeReferences(
                preparedLine,
                TypeOperatorKeywords,
                "typescript",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
        }
    }

    public static void EmitDeclarationTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        ReferenceExtractor.EmitTypeScriptDeclarationTypeReferences(
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
    }

    private static void EmitNamespaceAliasQualifiedReferences(
        IReadOnlyList<string> preparedLines,
        int lineIndex,
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        IReadOnlyList<NamespaceAliasBinding> namespaceAliases)
    {
        if (namespaceAliases.Count == 0 || IsImportExportAliasLine(preparedLines, lineIndex, preparedLine))
            return;

        foreach (var binding in namespaceAliases)
        {
            if (lineNumber <= binding.BindingLine
                || (binding.EndLine is int endLine && lineNumber > endLine)
                || (binding.ShadowLine is int shadowLine && lineNumber >= shadowLine))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(
                         preparedLine,
                         $@"(?<![\w$]){Regex.Escape(binding.Alias)}\s*\.\s*[A-Za-z_$][\w$]*"))
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    binding.ModuleSpecifier,
                    match.Index,
                    "reference",
                    context,
                    lineNumber,
                    resolveContainerForColumn(match.Index));
            }
        }
    }

    private static int? FindShadowLine(IReadOnlyList<string> preparedLines, string alias, int bindingLine)
    {
        for (var index = bindingLine; index < preparedLines.Count; index++)
        {
            var line = preparedLines[index];
            if (NamespaceImportExportRegex.IsMatch(line) || DynamicImportNamespaceRegex.IsMatch(line))
                continue;

            var match = LocalDeclarationRegex.Match(line);
            if (match.Success && string.Equals(match.Groups["name"].Value, alias, StringComparison.Ordinal))
                return index + 1;
        }

        return null;
    }

    private static int[] BuildBraceDepthsBeforeLine(IReadOnlyList<string> preparedLines)
    {
        var depths = new int[preparedLines.Count];
        var depth = 0;
        for (var index = 0; index < preparedLines.Count; index++)
        {
            depths[index] = depth;
            foreach (var ch in preparedLines[index])
            {
                if (ch == '{')
                    depth++;
                else if (ch == '}' && depth > 0)
                    depth--;
            }
        }

        return depths;
    }

    private static int? FindDynamicImportAliasEndLine(
        IReadOnlyList<string> preparedLines,
        IReadOnlyList<int> braceDepths,
        int bindingLineIndex)
    {
        var bindingDepth = braceDepths[bindingLineIndex];
        if (bindingDepth <= 0)
            return null;

        for (var index = bindingLineIndex + 1; index < preparedLines.Count; index++)
        {
            if (braceDepths[index] < bindingDepth)
                return index;
        }

        return preparedLines.Count;
    }

    private static void EmitHeritageTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var trimmed = preparedLine.TrimStart();
        if (!(trimmed.StartsWith("class ", StringComparison.Ordinal)
              || trimmed.StartsWith("abstract class ", StringComparison.Ordinal)
              || trimmed.StartsWith("export class ", StringComparison.Ordinal)
              || trimmed.StartsWith("export abstract class ", StringComparison.Ordinal)
              || trimmed.StartsWith("interface ", StringComparison.Ordinal)
              || trimmed.StartsWith("export interface ", StringComparison.Ordinal)))
        {
            return;
        }

        EmitHeritageKeyword(preparedLine, "extends", references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitHeritageKeyword(preparedLine, "implements", references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitHeritageKeyword(
        string preparedLine,
        string keyword,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var keywordIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, keyword))
        {
            var listStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, keywordIndex + keyword.Length);
            var listEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, listStart, stopAtComma: false);
            if (listEnd <= listStart)
                continue;

            TypedLanguageReferenceExtractor.EmitCommaSeparatedTypeListReferences(
                preparedLine,
                listStart,
                listEnd,
                "typescript",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
        }
    }

    private static void EmitCallableSignatureTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(
            preparedLine,
            '(',
            SkipLeadingDecorators(preparedLine));
        if (openParen <= 0)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen < 0)
            return;

        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
            preparedLine,
            openParen + 1,
            closeParen,
            "typescript",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

        var returnColon = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, closeParen + 1);
        if (returnColon >= preparedLine.Length || preparedLine[returnColon] != ':')
            return;

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, returnColon + 1);
        if (typeStart >= preparedLine.Length)
            return;

        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart, stopAtArrow: false);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "typescript",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
    }

    private static void EmitDecoratedMemberTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var memberStart = SkipLeadingDecorators(preparedLine);
        if (memberStart <= 0 || memberStart >= preparedLine.Length)
            return;

        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':', memberStart);
        if (colonIndex < 0)
            return;

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', memberStart);
        if (openParen >= 0 && openParen < colonIndex)
            return;

        var equalsIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', memberStart);
        if (equalsIndex >= 0 && equalsIndex < colonIndex)
            return;

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
        if (typeStart >= preparedLine.Length)
            return;

        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart, stopAtArrow: false);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "typescript",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
    }

    private static void EmitFunctionPropertyTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':');
        if (colonIndex < 0)
            return;

        var equalsIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=');
        if (equalsIndex >= 0 && equalsIndex < colonIndex)
            return;

        var questionIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '?');
        if (questionIndex >= 0 && questionIndex != colonIndex - 1)
            return;

        var prefix = preparedLine.Substring(0, colonIndex).TrimEnd();
        if (prefix.Length == 0 || prefix.EndsWith(")", StringComparison.Ordinal))
            return;

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
        if (typeStart >= preparedLine.Length)
            return;

        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart, stopAtArrow: false);
        if (typeEnd <= typeStart)
            return;

        var container = resolveContainerForColumn(typeStart);
        TypedLanguageReferenceExtractor.TryEmitTypeScriptFunctionTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            container);
    }

    private static bool IsImportExportAliasLine(IReadOnlyList<string> preparedLines, int lineIndex, string preparedLine)
    {
        var trimmed = preparedLine.TrimStart();
        return IsImportDeclarationLine(trimmed)
               || IsNamedExportLine(trimmed)
               || IsExportStarAliasLine(trimmed)
               || IsInsideMultilineImportExportAlias(preparedLines, lineIndex, preparedLine);
    }

    private static bool IsImportDeclarationLine(string text)
    {
        const string importKeyword = "import";
        if (!text.StartsWith(importKeyword, StringComparison.Ordinal))
            return false;

        var index = importKeyword.Length;
        if (index >= text.Length || IsTypeScriptIdentifierPart(text[index]))
            return false;

        return char.IsWhiteSpace(text[index]) || text[index] is '{' or '*';
    }

    private static bool IsNamedExportLine(string text)
    {
        var index = 0;
        if (!TryConsumeKeyword(text, "export", ref index))
            return false;

        SkipWhitespace(text, ref index);
        if (index < text.Length && text[index] == '{')
            return true;

        if (!TryConsumeKeyword(text, "type", ref index))
            return false;

        SkipWhitespace(text, ref index);
        return index < text.Length && text[index] == '{';
    }

    private static bool IsExportStarAliasLine(string text)
    {
        var index = 0;
        if (!TryConsumeKeyword(text, "export", ref index))
            return false;

        SkipWhitespace(text, ref index);
        if (TryConsumeKeyword(text, "type", ref index))
            SkipWhitespace(text, ref index);

        if (index >= text.Length || text[index] != '*')
            return false;

        index++;
        SkipWhitespace(text, ref index);
        return TryConsumeKeyword(text, "as", ref index);
    }

    private static bool IsInsideMultilineImportExportAlias(
        IReadOnlyList<string> preparedLines,
        int lineIndex,
        string preparedLine)
    {
        var asIndex = -1;
        foreach (var keywordIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "as"))
        {
            asIndex = keywordIndex;
            break;
        }

        return asIndex >= 0 && IsInsideImportExportBraceAt(preparedLines, lineIndex, asIndex);
    }

    private static bool IsInsideImportExportBraceAt(IReadOnlyList<string> preparedLines, int lineIndex, int column)
    {
        var unmatchedClosingBraces = 0;
        for (var currentLine = lineIndex; currentLine >= 0; currentLine--)
        {
            var line = preparedLines[currentLine];
            var startColumn = currentLine == lineIndex ? Math.Min(column, line.Length) - 1 : line.Length - 1;
            for (var index = startColumn; index >= 0; index--)
            {
                if (line[index] == '}')
                {
                    unmatchedClosingBraces++;
                    continue;
                }

                if (line[index] != '{')
                    continue;

                if (unmatchedClosingBraces > 0)
                {
                    unmatchedClosingBraces--;
                    continue;
                }

                return IsImportExportOpeningBrace(preparedLines, currentLine, index);
            }
        }

        return false;
    }

    private static int SkipLeadingDecorators(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;

            if (index >= line.Length || line[index] != '@')
                return index;

            index++;
            while (index < line.Length && (IsTypeScriptIdentifierPart(line[index]) || line[index] == '.'))
                index++;

            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;

            if (index < line.Length && line[index] == '(')
            {
                var closeParen = ReferenceExtractor.FindMatchingChar(line, index, '(', ')');
                if (closeParen < 0)
                    return index;

                index = closeParen + 1;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
        }

        return index;
    }

    private static bool IsImportExportOpeningBrace(IReadOnlyList<string> preparedLines, int openLineIndex, int openColumn)
    {
        var sameLinePrefix = preparedLines[openLineIndex].Substring(0, openColumn).Trim();
        if (sameLinePrefix.Length > 0)
            return IsImportBracePrefix(sameLinePrefix) || IsNamedExportBracePrefix(sameLinePrefix);

        for (var lineIndex = openLineIndex - 1; lineIndex >= 0; lineIndex--)
        {
            var previousLine = preparedLines[lineIndex].Trim();
            if (previousLine.Length == 0)
                continue;

            return IsImportBracePrefix(previousLine) || IsNamedExportBracePrefix(previousLine);
        }

        return false;
    }

    private static bool IsImportBracePrefix(string text)
    {
        if (text.IndexOf(';') >= 0 || ContainsTopLevelKeyword(text, "from"))
            return false;

        var index = 0;
        if (!TryConsumeKeyword(text, "import", ref index))
            return false;

        SkipWhitespace(text, ref index);
        if (index >= text.Length)
            return true;

        if (TryConsumeKeyword(text, "type", ref index))
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
                return true;
        }

        return text.TrimEnd().EndsWith(",", StringComparison.Ordinal);
    }

    private static bool ContainsTopLevelKeyword(string text, string keyword)
    {
        foreach (var _ in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(text, keyword))
            return true;

        return false;
    }

    private static bool IsNamedExportBracePrefix(string text)
    {
        var index = 0;
        if (!TryConsumeKeyword(text, "export", ref index))
            return false;

        SkipWhitespace(text, ref index);
        if (index >= text.Length)
            return true;

        if (!TryConsumeKeyword(text, "type", ref index))
            return false;

        SkipWhitespace(text, ref index);
        return index >= text.Length;
    }

    private static bool TryConsumeKeyword(string text, string keyword, ref int index)
    {
        if (index + keyword.Length > text.Length
            || string.CompareOrdinal(text, index, keyword, 0, keyword.Length) != 0)
        {
            return false;
        }

        var after = index + keyword.Length;
        if (after < text.Length && IsTypeScriptIdentifierPart(text[after]))
            return false;

        index = after;
        return true;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private static bool IsTypeScriptIdentifierPart(char ch) =>
        ch == '_' || ch == '$' || char.IsLetterOrDigit(ch);

    private static bool IsTypeScriptIdentifier(string text)
    {
        if (text.Length == 0 || !(text[0] == '_' || text[0] == '$' || char.IsLetter(text[0])))
            return false;

        for (var index = 1; index < text.Length; index++)
        {
            if (!IsTypeScriptIdentifierPart(text[index]))
                return false;
        }

        return true;
    }
}
