using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class SwiftReferenceExtractor
{
    private static readonly string[] DeclarationKeywords = ["let", "var"];
    private static readonly string[] TypeOperatorKeywords = ["is", "as"];

    public static void EmitTrailingClosureReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
        => TrailingLambdaReferenceExtractor.EmitReferences(preparedLine, addCallLikeReference);

    public static void EmitTypePositionReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        EmitCallableSignatureTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitHeritageTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGenericBoundReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitTypealiasRhsTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonVariableTypeReferences(
            preparedLine,
            DeclarationKeywords,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitKeywordFollowingTypeReferences(
            preparedLine,
            TypeOperatorKeywords,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
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
                "swift",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
        }

        TypedLanguageReferenceExtractor.EmitWhereClauseTypeReferences(
            preparedLine,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
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
        var funcIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "func");
        if (funcIndex < 0)
            return;

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', funcIndex + "func".Length);
        if (openParen <= funcIndex)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen < 0)
            return;

        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
            preparedLine,
            openParen + 1,
            closeParen,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

        EmitTypedThrowsReferences(preparedLine, closeParen + 1, references, seen, fileId, context, lineNumber, resolveContainerForColumn);

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
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
    }

    private static void EmitTypealiasRhsTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var typealiasIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "typealias");
        if (typealiasIndex < 0)
            return;

        var equalsIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '=', typealiasIndex + "typealias".Length);
        if (equalsIndex < 0)
            return;

        var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, equalsIndex + 1);
        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart, stopAtComma: false, stopAtArrow: false);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
    }

    private static void EmitTypedThrowsReferences(
        string preparedLine,
        int searchStart,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var throwsIndex in TypedLanguageReferenceExtractor.EnumerateTopLevelKeywordIndices(preparedLine, "throws", searchStart))
        {
            var openParen = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, throwsIndex + "throws".Length);
            if (openParen >= preparedLine.Length || preparedLine[openParen] != '(')
                continue;

            var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
            if (closeParen < 0)
                continue;

            var typeStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, openParen + 1);
            if (typeStart >= closeParen)
                return;

            TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
                preparedLine.Substring(typeStart, closeParen - typeStart),
                typeStart,
                "swift",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(typeStart));
            return;
        }
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
              || trimmed.StartsWith("struct ", StringComparison.Ordinal)
              || trimmed.StartsWith("protocol ", StringComparison.Ordinal)
              || trimmed.StartsWith("enum ", StringComparison.Ordinal)
              || trimmed.StartsWith("extension ", StringComparison.Ordinal)))
        {
            return;
        }

        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, ':');
        if (colonIndex < 0)
            return;

        var listStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, colonIndex + 1);
        var listEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, listStart, stopAtComma: false);
        if (listEnd <= listStart)
            return;

        TypedLanguageReferenceExtractor.EmitCommaSeparatedTypeListReferences(
            preparedLine,
            listStart,
            listEnd,
            "swift",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
    }
}
