using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RustReferenceExtractor
{
    private const string RustIdentifierPattern = @"(?:r#)?[_\p{L}][\w$]*";

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

    public static void EmitTypePositionReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        SymbolRecord? container)
    {
        EmitFunctionSignatureTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitLetTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitStructFieldTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, container);
        EmitImplAndTraitTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGenericBoundReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
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

    private static void EmitStructFieldTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        if (container?.Kind != "class")
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

    public static bool IsRawIdentifierPrefix(string line, int callIndex) =>
        callIndex >= 2
        && line[callIndex - 2] == 'r'
        && line[callIndex - 1] == '#';
}
