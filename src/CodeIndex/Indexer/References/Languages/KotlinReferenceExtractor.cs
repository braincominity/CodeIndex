using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class KotlinReferenceExtractor
{
    // Kotlin secondary constructor delegation: `constructor(x: Int) : this(x)` / `: super(x)`.
    // Kotlin セカンダリコンストラクタ委譲。
    private static readonly Regex CtorDelegationRegex = new(@":\s*(?<kind>this|super)\s*\(", RegexOptions.Compiled);

    // Kotlin class literals: `User::class` / `User::class.java`. The final segment must look
    // type-like so expression receivers such as `value::class` do not become type references.
    // Kotlin class literal。末尾セグメントを型名らしい形に絞り、`value::class` のような
    // 式レシーバーを type_reference 化しない。
    private static readonly Regex ClassLiteralRegex = new(
        @"(?<![\w$])(?<type>(?:[_\p{L}][\w$]*\.)*[_\p{Lu}][\w$]*)\s*::\s*class\b",
        RegexOptions.Compiled);

    private static readonly string[] DeclarationKeywords = ["val", "var"];
    private static readonly string[] TypeOperatorKeywords = ["is", "as"];

    public static HashSet<string> BuildConstructorTypeNames(string language, IReadOnlyList<SymbolRecord> symbols)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (language != "kotlin")
            return names;

        var callableNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind == "function" && !string.IsNullOrWhiteSpace(symbol.Name))
                callableNames.Add(symbol.Name);
        }

        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "class" || string.IsNullOrWhiteSpace(symbol.Name))
                continue;
            if (!IsConstructableClassSymbol(symbol))
                continue;
            if (callableNames.Contains(symbol.Name))
                continue;

            names.Add(symbol.Name);
        }

        return names;
    }

    public static bool IsConstructorCallName(string name, IReadOnlySet<string> constructorTypeNames)
        => constructorTypeNames.Contains(name);

    private static bool IsConstructableClassSymbol(SymbolRecord symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol.Signature))
            return false;

        var tokens = symbol.Signature.Split(
            [' ', '\t', '\r', '\n', '('],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 0;
        while (index < tokens.Length && tokens[index] is "public" or "private" or "protected" or "internal" or "expect" or "actual" or "abstract" or "sealed" or "data" or "open" or "final" or "value" or "inner")
            index++;

        if (index >= tokens.Length)
            return true;

        if (tokens[index] == "annotation" && index + 1 < tokens.Length && tokens[index + 1] == "class")
            return false;

        return tokens[index] is not ("object" or "companion");
    }

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
            "kotlin",
            preparedLine,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

    public static void EmitClassLiteralReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in ClassLiteralRegex.Matches(preparedLine))
        {
            var typeGroup = match.Groups["type"];
            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                typeGroup.Value,
                typeGroup.Index,
                context,
                lineNumber,
                container,
                "kotlin");
        }
    }

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

        EmitExtensionFunctionReceiverTypeReferences(
            preparedLine,
            funIndex,
            openParen,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn);

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

    private static void EmitExtensionFunctionReceiverTypeReferences(
        string preparedLine,
        int funIndex,
        int openParen,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        var headStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, funIndex + "fun".Length);
        if (headStart >= openParen)
            return;

        if (preparedLine[headStart] == '<')
        {
            var genericClose = ReferenceExtractor.FindMatchingChar(preparedLine, headStart, '<', '>');
            if (genericClose < 0 || genericClose >= openParen)
                return;
            headStart = TypedLanguageReferenceExtractor.SkipTypePrefixTrivia(preparedLine, genericClose + 1);
            if (headStart >= openParen)
                return;
        }

        var receiverDot = FindLastTopLevelChar(preparedLine, '.', headStart, openParen);
        if (receiverDot <= headStart)
            return;

        var receiverEnd = receiverDot;
        while (receiverEnd > headStart && char.IsWhiteSpace(preparedLine[receiverEnd - 1]))
            receiverEnd--;
        if (receiverEnd <= headStart)
            return;

        TypedLanguageReferenceExtractor.EmitTypeExpressionReferences(
            preparedLine.Substring(headStart, receiverEnd - headStart),
            headStart,
            "kotlin",
            references,
            seen,
            fileId,
            context,
            lineNumber,
            resolveContainerForColumn(headStart));
    }

    private static int FindLastTopLevelChar(string text, char target, int startIndex, int endIndex)
    {
        var angleDepth = 0;
        var parenDepth = 0;
        var squareDepth = 0;
        var braceDepth = 0;
        var last = -1;
        var end = Math.Min(text.Length, endIndex);
        for (var i = Math.Max(0, startIndex); i < end; i++)
        {
            var ch = text[i];
            if (angleDepth == 0 && parenDepth == 0 && squareDepth == 0 && braceDepth == 0 && ch == target)
                last = i;

            switch (ch)
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0) angleDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0) squareDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0) braceDepth--;
                    break;
            }
        }

        return last;
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
