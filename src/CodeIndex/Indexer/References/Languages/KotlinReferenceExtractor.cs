using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class KotlinReferenceExtractor
{
    // Kotlin secondary constructor delegation: `constructor(x: Int) : this(x)` / `: super(x)`.
    // Kotlin セカンダリコンストラクタ委譲。
    private static readonly Regex CtorDelegationRegex = new(@":\s*(?<kind>this|super)\s*\(", RegexOptions.Compiled);

    private static readonly string[] DeclarationKeywords = ["val", "var"];
    private static readonly string[] TypeOperatorKeywords = ["is", "as"];

    public static void EmitTrailingLambdaReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
        => TrailingLambdaReferenceExtractor.EmitReferences(preparedLine, addCallLikeReference);

    public static void EmitMethodReferenceReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
        => JvmMethodReferenceExtractor.EmitMethodReferenceReferences(
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

    public static void EmitCtorDelegationReferences(
        string preparedLine,
        IReadOnlyList<SymbolRecord> enclosingTypeCandidates,
        IReadOnlyList<SymbolRecord> symbols,
        string[] structuralLines,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var matches = CtorDelegationRegex.Matches(preparedLine);
        if (matches.Count == 0)
            return;

        var enclosingType = ReferenceExtractor.FindInnermostClassLike(enclosingTypeCandidates, lineNumber);
        if (enclosingType == null)
            return;

        var ctorContainer = container;
        if (ctorContainer == null
            || ctorContainer.Kind != "function"
            || !string.Equals(ctorContainer.Name, enclosingType.Name, StringComparison.Ordinal))
        {
            ctorContainer = FindEnclosingKotlinConstructor(symbols, enclosingType, lineNumber) ?? ctorContainer;
        }

        foreach (Match match in matches)
        {
            var kindToken = match.Groups["kind"].Value;
            string? target;
            if (kindToken == "this")
            {
                target = enclosingType.Name;
            }
            else
            {
                // Secondary constructors call `super(...)` from the constructor line, while the
                // superclass type lives on the enclosing class header. Reconstruct the header so
                // multi-line signatures can resolve the same way C# and Java constructor chains do.
                // `super(...)` の呼び先は外側クラスヘッダ上の superclass なので、
                // 複数行ヘッダも拾えるよう structuralLines から再構築する。
                var (_, _, headerText) = ReferenceExtractor.CollectCSharpRecordHeader(
                    structuralLines,
                    enclosingType.StartLine);
                target = ParseKotlinBaseType(headerText);
                if (string.IsNullOrWhiteSpace(target))
                    target = ParseKotlinBaseType(enclosingType.Signature);
                if (string.IsNullOrWhiteSpace(target))
                    continue;
            }

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                target!,
                match.Groups["kind"].Index,
                "call",
                context,
                lineNumber,
                ctorContainer);
        }
    }

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
        EmitPrimaryConstructorTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitHeritageTypeReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        EmitGenericBoundReferences(preparedLine, references, seen, fileId, context, lineNumber, resolveContainerForColumn);
        TypedLanguageReferenceExtractor.EmitColonVariableTypeReferences(
            preparedLine,
            DeclarationKeywords,
            "kotlin",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
        if (!preparedLine.TrimStart().StartsWith("import ", StringComparison.Ordinal))
        {
            TypedLanguageReferenceExtractor.EmitKeywordFollowingTypeReferences(
                preparedLine,
                TypeOperatorKeywords,
                "kotlin",
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
        var funIndex = ReferenceExtractor.FindTopLevelKeyword(preparedLine, "fun");
        if (funIndex < 0)
            return;

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(', funIndex + "fun".Length);
        if (openParen <= funIndex)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen < 0)
            return;

        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
            preparedLine,
            openParen + 1,
            closeParen,
            "kotlin",
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
        var typeEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(preparedLine, typeStart);
        if (typeEnd <= typeStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(typeStart, typeEnd - typeStart),
            typeStart,
            "kotlin",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(typeStart));
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
              || trimmed.StartsWith("data class ", StringComparison.Ordinal)
              || trimmed.StartsWith("sealed class ", StringComparison.Ordinal)
              || trimmed.StartsWith("interface ", StringComparison.Ordinal)
              || trimmed.StartsWith("object ", StringComparison.Ordinal)
              || trimmed.StartsWith("enum class ", StringComparison.Ordinal)))
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
            "kotlin",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn,
            trimTopLevelCallArguments: true);
    }

    private static void EmitPrimaryConstructorTypeReferences(
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
              || trimmed.StartsWith("data class ", StringComparison.Ordinal)
              || trimmed.StartsWith("sealed class ", StringComparison.Ordinal)
              || trimmed.StartsWith("enum class ", StringComparison.Ordinal)))
        {
            return;
        }

        var openParen = TypedLanguageReferenceExtractor.FindTopLevelChar(preparedLine, '(');
        if (openParen < 0)
            return;

        var closeParen = ReferenceExtractor.FindMatchingChar(preparedLine, openParen, '(', ')');
        if (closeParen < 0)
            return;

        TypedLanguageReferenceExtractor.EmitColonParameterTypeReferences(
            preparedLine,
            openParen + 1,
            closeParen,
            "kotlin",
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
                "kotlin",
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn);
        }

        TypedLanguageReferenceExtractor.EmitWhereClauseTypeReferences(
            preparedLine,
            "kotlin",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);
    }

    private static SymbolRecord? FindEnclosingKotlinConstructor(
        IReadOnlyList<SymbolRecord> symbols,
        SymbolRecord enclosingType,
        int lineNumber)
    {
        SymbolRecord? best = null;
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "function")
                continue;
            if (!string.Equals(symbol.ContainerName, enclosingType.Name, StringComparison.Ordinal)
                && !IsWithinSymbolRange(enclosingType, symbol.StartLine))
            {
                continue;
            }

            var signature = symbol.Signature?.TrimStart();
            var isSecondaryConstructor = !string.IsNullOrWhiteSpace(signature)
                && (signature.StartsWith("constructor", StringComparison.Ordinal)
                    || signature.StartsWith("public constructor", StringComparison.Ordinal)
                    || signature.StartsWith("private constructor", StringComparison.Ordinal)
                    || signature.StartsWith("protected constructor", StringComparison.Ordinal)
                    || signature.StartsWith("internal constructor", StringComparison.Ordinal));
            if (!isSecondaryConstructor
                && !string.Equals(symbol.Name, enclosingType.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (symbol.StartLine > lineNumber)
                continue;
            var symbolEnd = symbol.BodyEndLine ?? symbol.EndLine;
            if (symbolEnd < lineNumber)
                continue;

            if (best == null || symbol.StartLine >= best.StartLine)
                best = symbol;
        }

        return best;
    }

    private static bool IsWithinSymbolRange(SymbolRecord container, int lineNumber)
    {
        var start = container.BodyStartLine ?? container.StartLine;
        var end = container.BodyEndLine ?? container.EndLine;
        return lineNumber >= start && lineNumber <= end;
    }

    private static string? ParseKotlinBaseType(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return null;

        var colonIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(signature, ':');
        if (colonIndex < 0)
            return null;

        var listStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(signature, colonIndex + 1);
        var listEnd = TypedLanguageReferenceExtractor.FindTypeExpressionEnd(
            signature,
            listStart,
            stopAtComma: false);
        if (listEnd <= listStart)
            return null;

        var typeList = signature.Substring(listStart, listEnd - listStart);
        string? fallback = null;
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typeList))
        {
            var segment = typeList.Substring(segmentStart, segmentLength).Trim();
            if (segment.Length == 0)
                continue;

            var typeName = ExtractKotlinBareTypeName(segment);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            if (TypedLanguageReferenceExtractor.FindTopLevelChar(segment, '(') >= 0)
                return typeName;

            fallback ??= typeName;
        }

        return fallback;
    }

    private static string? ExtractKotlinBareTypeName(string segment)
    {
        var trimmed = TrimTopLevelByClause(segment.Trim());
        if (trimmed.Length == 0)
            return null;

        var callIndex = TypedLanguageReferenceExtractor.FindTopLevelChar(trimmed, '(');
        if (callIndex > 0)
            trimmed = trimmed.Substring(0, callIndex).TrimEnd();

        var lastSegmentStart = 0;
        var endIndex = trimmed.Length;
        var angleDepth = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch == '<')
            {
                if (angleDepth == 0)
                    endIndex = Math.Min(endIndex, i);
                angleDepth++;
            }
            else if (ch == '>')
            {
                if (angleDepth > 0)
                    angleDepth--;
            }
            else if (angleDepth == 0 && ch == '.')
            {
                lastSegmentStart = i + 1;
            }
        }

        if (endIndex < lastSegmentStart)
            endIndex = trimmed.Length;

        var typeName = trimmed.Substring(lastSegmentStart, endIndex - lastSegmentStart).Trim();
        return typeName.Length > 0 ? typeName : null;
    }

    private static string TrimTopLevelByClause(string segment)
    {
        var angleDepth = 0;
        var parenDepth = 0;
        for (var i = 0; i < segment.Length; i++)
        {
            var ch = segment[i];
            if (ch == '<')
            {
                angleDepth++;
            }
            else if (ch == '>')
            {
                if (angleDepth > 0)
                    angleDepth--;
            }
            else if (ch == '(')
            {
                parenDepth++;
            }
            else if (ch == ')')
            {
                if (parenDepth > 0)
                    parenDepth--;
            }
            else if (angleDepth == 0
                     && parenDepth == 0
                     && i + 4 <= segment.Length
                     && string.CompareOrdinal(segment, i, " by ", 0, 4) == 0)
            {
                return segment.Substring(0, i).TrimEnd();
            }
        }

        return segment;
    }
}
