using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RustReferenceExtractor
{
    private const string RustIdentifierPattern = @"(?:r#)?[_\p{L}][\w$]*";
    private static readonly string[] ConstStaticKeywords = ["const", "static"];
    private static readonly Regex DeriveAttributeRegex = new(
        @"#\s*!?\s*\[\s*derive\s*\((?<types>[^\)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex CfgAttrDeriveAttributeRegex = new(
        @"#\s*!?\s*\[\s*cfg_attr\s*\(.*?\bderive\s*\((?<types>[^\)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex AttributeHeadRegex = new(
        $@"#\s*!?\s*\[\s*(?<name>{RustIdentifierPattern}(?:::{RustIdentifierPattern})*)",
        RegexOptions.Compiled);
    private static readonly Regex ExternCrateRegex = new(
        $@"^\s*(?:pub\s+)?extern\s+crate\s+(?<name>{RustIdentifierPattern})(?:\s+as\s+{RustIdentifierPattern})?\s*;",
        RegexOptions.Compiled);
    private static readonly Regex ModuleDeclarationRegex = new(
        $@"^\s*(?:pub(?:\s*\([^\)]*\))?\s+)?mod\s+(?<name>{RustIdentifierPattern})\s*;",
        RegexOptions.Compiled);
    private static readonly Regex UseStatementRegex = new(
        @"^\s*(?:pub(?:\s*\([^\)]*\))?\s+)?use\s+(?<body>.+);",
        RegexOptions.Compiled);
    private static readonly Regex AssociatedCallReceiverRegex = new(
        $@"(?<![\w$])(?<receiver>{RustIdentifierPattern}(?:::{RustIdentifierPattern})*)(?:::\s*<(?<args>[^>\n]+)>)?::\s*{RustIdentifierPattern}(?:<[^>\n]+>)?\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex StructLiteralRegex = new(
        $@"(?<![\w$])(?<name>{RustIdentifierPattern}(?:::{RustIdentifierPattern})*)(?:::\s*<(?<args>[^>\n]+)>)?\s*\{{",
        RegexOptions.Compiled);

    // Rust macro calls use `!` plus one of `()`, `[]`, or `{}` instead of the shared trailing `(`.
    // Capture path-qualified macro names so `std::println!`, `log::info!`, and `my_macro!`
    // surface as references. `macro_rules` declarations are filtered by the Rust ignore list.
    // Rust の macro 呼び出しは共通の末尾 `(` ではなく `!` の後に `()` / `[]` / `{}` を取る。
    private static readonly Regex MacroCallRegex = new(
        $@"(?<![\w$])(?<name>{RustIdentifierPattern}(?:::{RustIdentifierPattern})*)(?:<[^>\n]+>)?!\s*[\(\[\{{]",
        RegexOptions.Compiled);

    // Rust raw identifiers such as `r#type()` are stored without the `r#` prefix, but the shared
    // call regex cannot see them because `#` is not an identifier character.
    // Rust の raw identifier (`r#type()`) は保存時に `r#` を外す。
    private static readonly Regex RawIdentifierCallRegex = new(
        @"(?<![\w$])(?<name>(?:(?:r#)?\w+::)*r#\w+(?:::(?:r#)?\w+)*)(?:<[^>\n]+>)?\s*\(",
        RegexOptions.Compiled);

    public static void EmitAdditionalCallReferences(string preparedLine, Action<string, int> addCallLikeReference)
    {
        foreach (Match match in RawIdentifierCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        foreach (Match match in MacroCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }
    }

    public static void EmitAttributeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in DeriveAttributeRegex.Matches(preparedLine))
        {
            EmitDeriveTypeList(match.Groups["types"], references, seen, fileId, context, lineNumber, container);
        }

        foreach (Match match in CfgAttrDeriveAttributeRegex.Matches(preparedLine))
        {
            EmitDeriveTypeList(match.Groups["types"], references, seen, fileId, context, lineNumber, container);
        }

        foreach (Match match in AttributeHeadRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var name = NormalizeIdentifier(nameGroup.Value);
            if (string.Equals(name, "derive", StringComparison.Ordinal))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, name, nameGroup.Index, "annotation", context, lineNumber, container);
        }
    }

    private static void EmitDeriveTypeList(
        Group typesGroup,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typesGroup.Value))
        {
            var fragment = typesGroup.Value.Substring(segmentStart, segmentLength);
            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(fragment, 0);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                typesGroup.Index + segmentStart + typeStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitTypePositionReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container,
        SymbolRecord? enumContainer)
    {
        EmitUseReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
        EmitExternCrateReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
        EmitModuleDeclarationReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
        EmitFunctionSignatureTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitClosureSignatureTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitLetTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitConstStaticTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitTypeAliasTargetReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitAssociatedTypeBoundReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitTupleStructFieldTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, container);
        EmitStructFieldTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
        EmitEnumVariantTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, enumContainer);
        EmitAsCastTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitAssociatedCallReceiverTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitStructLiteralInstantiationReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn, enumContainer);
        EmitImplAndTraitTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGenericBoundReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
    }

    private static void EmitUseReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = UseStatementRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var bodyGroup = match.Groups["body"];
        EmitUseBodyReferences(bodyGroup.Value, bodyGroup.Index, references, seen, fileId, context, lineNumber, container, prefix: null);
    }

    private static void EmitUseBodyReferences(
        string body,
        int bodyStart,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        string? prefix)
    {
        var text = body.Trim();
        if (text.Length == 0)
            return;

        var textStart = bodyStart + body.IndexOf(text, StringComparison.Ordinal);
        var openBrace = text.IndexOf('{');
        if (openBrace >= 0)
        {
            var closeBrace = text.LastIndexOf('}');
            if (closeBrace > openBrace)
            {
                var groupedPrefix = CombineUsePath(prefix, text[..openBrace].Trim());
                var inner = text.Substring(openBrace + 1, closeBrace - openBrace - 1);
                foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(inner))
                {
                    EmitUseBodyReferences(
                        inner.Substring(segmentStart, segmentLength),
                        textStart + openBrace + 1 + segmentStart,
                        references,
                        seen,
                        fileId,
                        context,
                        lineNumber,
                        container,
                        groupedPrefix);
                }

                return;
            }
        }

        var aliasIndex = FindTopLevelUseAliasIndex(text);
        var target = aliasIndex >= 0 ? text[..aliasIndex].Trim() : text;
        if (target.Length == 0 || target is "crate" or "super" or "*")
            return;

        if (target == "self" && !string.IsNullOrWhiteSpace(prefix))
            target = prefix;
        else if (target == "self")
            return;
        else if (!string.IsNullOrWhiteSpace(prefix))
            target = CombineUsePath(prefix, target);

        var leafStart = target.LastIndexOf("::", StringComparison.Ordinal);
        var leaf = leafStart >= 0 ? target[(leafStart + 2)..].Trim() : target.Trim();
        if (leaf.Length == 0 || leaf is "crate" or "self" or "super" or "*")
            return;

        var leafIndex = text.IndexOf(leaf, StringComparison.Ordinal);
        ReferenceExtractor.AddReference(
            references,
            seen,
            fileId,
            NormalizeIdentifier(leaf),
            textStart + Math.Max(0, leafIndex),
            "reference",
            context,
            lineNumber,
            container);
    }

    private static int FindTopLevelUseAliasIndex(string text)
    {
        foreach (var asIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(text, "as"))
            return asIndex;

        return -1;
    }

    private static string CombineUsePath(string? prefix, string name)
    {
        var cleanedPrefix = prefix?.Trim().TrimEnd(':');
        var cleanedName = name.Trim().TrimEnd(':');
        if (string.IsNullOrWhiteSpace(cleanedPrefix))
            return cleanedName;
        if (string.IsNullOrWhiteSpace(cleanedName))
            return cleanedPrefix;
        return $"{cleanedPrefix}::{cleanedName}";
    }

    private static void EmitExternCrateReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = ExternCrateRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var nameGroup = match.Groups["name"];
        ReferenceExtractor.AddReference(
            references,
            seen,
            fileId,
            NormalizeIdentifier(nameGroup.Value),
            nameGroup.Index,
            "reference",
            context,
            lineNumber,
            container);
    }

    private static void EmitModuleDeclarationReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = ModuleDeclarationRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var nameGroup = match.Groups["name"];
        ReferenceExtractor.AddReference(
            references,
            seen,
            fileId,
            NormalizeIdentifier(nameGroup.Value),
            nameGroup.Index,
            "reference",
            context,
            lineNumber,
            container);
    }

    private static void EmitFunctionSignatureTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var fnIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "fn");
        if (fnIndex < 0)
            return;

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', fnIndex + 2);
        if (openParen <= fnIndex)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen < 0)
            return;

        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
            preparedLine,
            openParen + 1,
            closeParen,
            "rust",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

        var arrowIndex = TypedLanguageReferenceExtractor.FindTopLevelSequence(preparedLine, "->", closeParen + 1);
        if (arrowIndex < 0)
            return;

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, arrowIndex + 2);
        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "rust",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
    }

    private static void EmitClosureSignatureTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var searchIndex = 0;
        while (searchIndex < preparedLine.Length)
        {
            var openPipe = preparedLine.IndexOf('|', searchIndex);
            if (openPipe < 0)
                return;

            var closePipe = preparedLine.IndexOf('|', openPipe + 1);
            if (closePipe < 0)
                return;

            searchIndex = closePipe + 1;
            var parameterList = preparedLine.Substring(openPipe + 1, closePipe - openPipe - 1);
            var hasParameterTypes = TypedLanguageReferenceExtractor.FindTopLevelChar(parameterList, ':') >= 0;
            var arrowIndex = TypedLanguageReferenceExtractor.FindTopLevelSequence(preparedLine, "->", closePipe + 1);
            var hasImmediateReturnType = arrowIndex >= 0 && HasOnlyWhitespace(preparedLine, closePipe + 1, arrowIndex);
            if (!hasParameterTypes && !hasImmediateReturnType)
                continue;

            if (hasParameterTypes)
            {
                TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
                    preparedLine,
                    openPipe + 1,
                    closePipe,
                    "rust",
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn);
            }

            if (!hasImmediateReturnType)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, arrowIndex + 2);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
            if (typeEnd <= typeStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(typeStart, typeEnd - typeStart),
                typeStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(typeStart));
        }
    }

    private static bool HasOnlyWhitespace(string text, int startIndex, int endIndex)
    {
        for (var index = Math.Max(0, startIndex); index < endIndex && index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
                return false;
        }

        return true;
    }

    private static void EmitLetTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var letIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "let"))
        {
            var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':', letIndex + "let".Length);
            if (colonIndex < 0)
                continue;

            var assignmentIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', letIndex + "let".Length);
            if (assignmentIndex >= 0 && assignmentIndex < colonIndex)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
            if (typeEnd <= typeStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(typeStart, typeEnd - typeStart),
                typeStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(typeStart));
        }
    }

    private static void EmitConstStaticTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var keyword in ConstStaticKeywords)
        {
            foreach (var keywordIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, keyword))
            {
                var declarationStart = keywordIndex + keyword.Length;
                if (keyword == "static")
                    declarationStart = SkipOptionalRustMut(preparedLine, declarationStart);

                var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':', declarationStart);
                if (colonIndex < 0)
                    continue;

                var assignmentIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', declarationStart);
                if (assignmentIndex >= 0 && assignmentIndex < colonIndex)
                    continue;

                var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
                var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
                if (typeEnd <= typeStart)
                    continue;

                TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                    preparedLine.Substring(typeStart, typeEnd - typeStart),
                    typeStart,
                    "rust",
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn(typeStart));
            }
        }
    }

    private static int SkipOptionalRustMut(string line, int startIndex)
    {
        var index = startIndex;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index + "mut".Length > line.Length
            || string.CompareOrdinal(line, index, "mut", 0, "mut".Length) != 0)
        {
            return startIndex;
        }

        var afterMut = index + "mut".Length;
        if (afterMut < line.Length && IsRustIdentifierPart(line[afterMut]))
            return startIndex;

        return afterMut;
    }

    private static void EmitTypeAliasTargetReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var typeIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "type"))
        {
            var assignmentIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', typeIndex + "type".Length);
            if (assignmentIndex < 0)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, assignmentIndex + 1);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
            if (typeEnd <= typeStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(typeStart, typeEnd - typeStart),
                typeStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(typeStart));
        }
    }

    private static void EmitAssociatedTypeBoundReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var typeIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "type"))
        {
            var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':', typeIndex + "type".Length);
            if (colonIndex < 0)
                continue;

            var assignmentIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', typeIndex + "type".Length);
            if (assignmentIndex >= 0 && assignmentIndex < colonIndex)
                continue;

            var boundsStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
            var boundsEnd = assignmentIndex > colonIndex
                ? assignmentIndex
                : TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, boundsStart);
            if (boundsEnd <= boundsStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(boundsStart, boundsEnd - boundsStart),
                boundsStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(boundsStart));
        }
    }

    private static void EmitTupleStructFieldTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        var structIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "struct");
        if (structIndex < 0)
            return;

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', structIndex + "struct".Length);
        if (openParen < 0)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen <= openParen)
            return;

        var fieldList = preparedLine.Substring(openParen + 1, closeParen - openParen - 1);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(fieldList))
        {
            var fragment = fieldList.Substring(segmentStart, segmentLength);
            var typeStart = SkipRustTupleFieldPrefix(fragment);
            if (typeStart >= fragment.Length)
                continue;

            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = openParen + 1 + segmentStart + typeStart;
            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                container ?? resolveContainerForColumn(absoluteStart));
        }
    }

    private static void EmitStructFieldTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        if (container?.Kind is not "class" and not "struct")
            return;

        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':');
        if (colonIndex < 0)
            return;

        var trimmed = preparedLine.TrimStart();
        if (trimmed.StartsWith("fn ", StringComparison.Ordinal)
            || trimmed.StartsWith("let ", StringComparison.Ordinal)
            || trimmed.StartsWith("type ", StringComparison.Ordinal)
            || trimmed.StartsWith("impl ", StringComparison.Ordinal))
        {
            return;
        }

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "rust",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            container);
    }

    private static int SkipRustTupleFieldPrefix(string fragment)
    {
        var index = 0;
        while (index < fragment.Length && char.IsWhiteSpace(fragment[index]))
            index++;

        if (index + 3 > fragment.Length
            || string.CompareOrdinal(fragment, index, "pub", 0, "pub".Length) != 0)
        {
            return index;
        }

        var afterPub = index + "pub".Length;
        if (afterPub < fragment.Length && IsRustIdentifierPart(fragment[afterPub]))
            return index;

        index = afterPub;
        while (index < fragment.Length && char.IsWhiteSpace(fragment[index]))
            index++;

        if (index < fragment.Length && fragment[index] == '(')
        {
            var closeParen = ReferenceExtractor.FindMatchingChar(fragment, index, '(', ')');
            if (closeParen < 0)
                return fragment.Length;

            index = closeParen + 1;
            while (index < fragment.Length && char.IsWhiteSpace(fragment[index]))
                index++;
        }

        return index;
    }

    private static bool IsRustIdentifierPart(char ch) =>
        ch == '_' || ch == '$' || char.IsLetterOrDigit(ch);

    private static void EmitEnumVariantTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? enumContainer)
    {
        if (enumContainer?.Kind != "enum")
            return;

        var variantStart = FirstNonWhitespaceIndex(preparedLine);
        if (variantStart >= preparedLine.Length
            || preparedLine[variantStart] is '}' or '#'
            || !IsLikelyRustEnumVariantStart(preparedLine, variantStart))
        {
            return;
        }

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', variantStart);
        var openBrace = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '{', variantStart);
        if (openParen >= 0 && (openBrace < 0 || openParen < openBrace))
        {
            EmitEnumTupleVariantTypeReferences(preparedLine, openParen, references, seen, fileId, context, lineNumber, enumContainer);
        }

        if (openBrace >= 0)
            EmitEnumStructVariantTypeReferences(preparedLine, openBrace, references, seen, fileId, context, lineNumber, enumContainer);
    }

    private static void EmitEnumTupleVariantTypeReferences(
        string preparedLine,
        int openParen,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord enumContainer)
    {
        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen <= openParen)
            return;

        var fieldList = preparedLine.Substring(openParen + 1, closeParen - openParen - 1);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(fieldList))
        {
            var fragment = fieldList.Substring(segmentStart, segmentLength);
            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(fragment, 0);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = openParen + 1 + segmentStart + typeStart;
            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                enumContainer);
        }
    }

    private static void EmitEnumStructVariantTypeReferences(
        string preparedLine,
        int openBrace,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord enumContainer)
    {
        var closeBrace = ReferenceExtractor.FindMatchingChar(preparedLine, openBrace, '{', '}');
        if (closeBrace <= openBrace)
            return;

        var fieldList = preparedLine.Substring(openBrace + 1, closeBrace - openBrace - 1);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(fieldList))
        {
            var fragment = fieldList.Substring(segmentStart, segmentLength);
            var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(fragment, ':');
            if (colonIndex < 0)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(fragment, colonIndex + 1);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = openBrace + 1 + segmentStart + typeStart;
            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                enumContainer);
        }
    }

    private static int FirstNonWhitespaceIndex(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index;
    }

    private static bool IsLikelyRustEnumVariantStart(string line, int startIndex)
    {
        if (startIndex < line.Length && char.IsUpper(line[startIndex]))
            return true;

        return startIndex + 2 < line.Length
            && line[startIndex] == 'r'
            && line[startIndex + 1] == '#'
            && IsRustIdentifierPart(line[startIndex + 2]);
    }

    private static void EmitAsCastTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var trimmed = preparedLine.TrimStart();
        if (trimmed.StartsWith("use ", StringComparison.Ordinal)
            || trimmed.StartsWith("pub use ", StringComparison.Ordinal)
            || trimmed.StartsWith("extern crate ", StringComparison.Ordinal)
            || trimmed.StartsWith("pub extern crate ", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var asIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "as"))
        {
            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, asIndex + "as".Length);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
            if (typeEnd <= typeStart)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(typeStart, typeEnd - typeStart),
                typeStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(typeStart));
        }
    }

    private static void EmitAssociatedCallReceiverTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in AssociatedCallReceiverRegex.Matches(preparedLine))
        {
            var receiverGroup = match.Groups["receiver"];
            var receiver = receiverGroup.Value;
            var leafStart = receiver.LastIndexOf("::", StringComparison.Ordinal);
            var leaf = leafStart >= 0 ? receiver[(leafStart + 2)..] : receiver;
            var leafOffset = leafStart >= 0 ? leafStart + 2 : 0;
            if (!IsLikelyRustTypePathLeaf(leaf))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                NormalizeIdentifier(leaf),
                receiverGroup.Index + leafOffset,
                "type_reference",
                context,
                lineNumber,
                resolveContainerForColumn(receiverGroup.Index));

            var argsGroup = match.Groups["args"];
            if (!argsGroup.Success)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                argsGroup.Value,
                argsGroup.Index,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(argsGroup.Index));
        }
    }

    private static bool IsLikelyRustTypePathLeaf(string leaf)
    {
        if (leaf.StartsWith("r#", StringComparison.Ordinal))
            return leaf.Length > 2 && IsRustIdentifierPart(leaf[2]);

        return leaf.Length > 0 && char.IsUpper(leaf[0]);
    }

    private static void EmitStructLiteralInstantiationReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? enumContainer)
    {
        if (enumContainer != null || IsRustTypeDeclarationLine(preparedLine))
            return;

        foreach (Match match in StructLiteralRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var name = nameGroup.Value;
            var leafStart = name.LastIndexOf("::", StringComparison.Ordinal);
            var leaf = leafStart >= 0 ? name[(leafStart + 2)..] : name;
            var leafOffset = leafStart >= 0 ? leafStart + 2 : 0;
            if (!IsLikelyRustTypePathLeaf(leaf))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                NormalizeIdentifier(leaf),
                nameGroup.Index + leafOffset,
                "instantiate",
                context,
                lineNumber,
                resolveContainerForColumn(nameGroup.Index));

            var argsGroup = match.Groups["args"];
            if (!argsGroup.Success)
                continue;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                argsGroup.Value,
                argsGroup.Index,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(argsGroup.Index));
        }
    }

    private static bool IsRustTypeDeclarationLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("pub", StringComparison.Ordinal))
        {
            var afterPub = "pub".Length;
            if (afterPub < trimmed.Length && trimmed[afterPub] == '(')
            {
                var closeParen = ReferenceExtractor.FindMatchingChar(trimmed, afterPub, '(', ')');
                if (closeParen > afterPub)
                    trimmed = trimmed[(closeParen + 1)..].TrimStart();
            }
            else if (afterPub < trimmed.Length && char.IsWhiteSpace(trimmed[afterPub]))
            {
                trimmed = trimmed[afterPub..].TrimStart();
            }
        }

        return trimmed.StartsWith("struct ", StringComparison.Ordinal)
               || trimmed.StartsWith("enum ", StringComparison.Ordinal)
               || trimmed.StartsWith("union ", StringComparison.Ordinal);
    }

    private static void EmitImplAndTraitTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var implIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "impl");
        if (implIndex >= 0)
        {
            var typeListStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, implIndex + "impl".Length);
            var forIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "for");
            var typeListEnd = forIndex >= 0
                ? forIndex
                : TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeListStart);

            if (typeListEnd > typeListStart)
            {
                TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                    preparedLine.Substring(typeListStart, typeListEnd - typeListStart),
                    typeListStart,
                    "rust",
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn(typeListStart));
            }

            if (forIndex >= 0)
            {
                var targetStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, forIndex + "for".Length);
                var targetEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, targetStart);
                if (targetEnd > targetStart)
                {
                    TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                        preparedLine.Substring(targetStart, targetEnd - targetStart),
                        targetStart,
                        "rust",
                        references,
                        seen,
                        fileId,
                        context,
                        lineNumber,
                        resolveContainerForColumn(targetStart));
                }
            }
        }

        var traitIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "trait");
        if (traitIndex < 0)
            return;

        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':', traitIndex + "trait".Length);
        if (colonIndex < 0)
            return;

        var boundsStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
        var boundsEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, boundsStart);
        if (boundsEnd <= boundsStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(boundsStart, boundsEnd - boundsStart),
            boundsStart,
            "rust",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(boundsStart));
    }

    private static void EmitGenericBoundReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var genericOpenIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '<');
        if (genericOpenIndex >= 0)
        {
            TypedLanguageReferenceExtractor.EmitGenericColonBoundReferences(
                preparedLine,
                genericOpenIndex,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
            EmitGenericDefaultTypeReferences(
                preparedLine,
                genericOpenIndex,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
        }

        TypedLanguageReferenceExtractor.EmitWhereClauseTypeReferences(
            preparedLine,
            "rust",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
    }

    private static void EmitGenericDefaultTypeReferences(
        string preparedLine,
        int genericOpenIndex,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var genericCloseIndex = ReferenceExtractor.FindMatchingChar(preparedLine, genericOpenIndex, '<', '>');
        if (genericCloseIndex <= genericOpenIndex)
            return;

        var clause = preparedLine.Substring(genericOpenIndex + 1, genericCloseIndex - genericOpenIndex - 1);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(clause))
        {
            var fragment = clause.Substring(segmentStart, segmentLength);
            var assignmentIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(fragment, '=');
            if (assignmentIndex < 0)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(fragment, assignmentIndex + 1);
            var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = genericOpenIndex + 1 + segmentStart + typeStart;
            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                "rust",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(absoluteStart));
        }
    }

    public static string NormalizeIdentifier(string identifier)
    {
        if (identifier.Length == 0)
            return identifier;

        var segments = identifier.Split("::");
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.StartsWith("r#", StringComparison.Ordinal))
                segments[i] = segment[2..];
        }

        return string.Join("::", segments);
    }

    public static bool IsFunctionDeclarationCallSite(string line, int callIndex)
    {
        if (callIndex <= 0)
            return false;

        var prefix = line[..callIndex].TrimEnd();
        return prefix.EndsWith("fn", StringComparison.Ordinal);
    }

    public static bool IsDeriveAttributeCallSite(string line, string name, int callIndex)
    {
        if (!string.Equals(name, "derive", StringComparison.Ordinal) || callIndex <= 0)
            return false;

        var index = callIndex - 1;
        while (index >= 0 && char.IsWhiteSpace(line[index]))
            index--;

        if (index < 0 || line[index] != '[')
            return false;

        index--;
        while (index >= 0 && char.IsWhiteSpace(line[index]))
            index--;

        if (index >= 0 && line[index] == '!')
        {
            index--;
            while (index >= 0 && char.IsWhiteSpace(line[index]))
                index--;
        }

        return index >= 0 && line[index] == '#';
    }

    public static bool IsLikelyInstantiationCallName(string originalName, string normalizedName, string line, int callIndex)
    {
        var normalizedLeaf = LastPathSegment(normalizedName);
        var originalLeaf = LastPathSegment(originalName);
        if (!IsLikelyRustTypePathLeaf(originalLeaf) && !IsLikelyRustTypePathLeaf(normalizedLeaf))
            return false;

        var afterName = callIndex + originalName.Length;
        while (afterName < line.Length && char.IsWhiteSpace(line[afterName]))
            afterName++;

        if (afterName >= line.Length)
            return false;

        if (line[afterName] == '!')
            return false;

        return line[afterName] is '(' or '<'
               || (afterName + 1 < line.Length && line[afterName] == ':' && line[afterName + 1] == ':');
    }

    private static string LastPathSegment(string name)
    {
        var leafStart = name.LastIndexOf("::", StringComparison.Ordinal);
        return leafStart >= 0 ? name[(leafStart + 2)..] : name;
    }

    public static bool IsRawIdentifierPrefix(string line, int callIndex) =>
        callIndex >= 2
        && line[callIndex - 2] == 'r'
        && line[callIndex - 1] == '#';
}
