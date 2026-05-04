using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class TypedLanguageReferenceExtractor
{
    public static void EmitTypeExpressionReferences(
        string expression,
        int expressionStartInLine,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        IReadOnlySet<string>? ignoredSegments = null)
    {
        var leading = CountLeadingWhitespace(expression, 0, expression.Length);
        var trailing = CountTrailingWhitespace(expression, leading, expression.Length - leading);
        var length = expression.Length - leading - trailing;
        if (length <= 0)
            return;

        ReferenceExtractor.AddTypeExpressionSegments(
            references,
            seen,
            fileId,
            expression.Substring(leading, length),
            expressionStartInLine + leading,
            context,
            lineNumber,
            container,
            language,
            ignoredSegments);
    }

    public static void EmitCommaSeparatedTypeListReferences(
        string line,
        int listStart,
        int listEnd,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn,
        bool trimTopLevelCallArguments = false)
    {
        if (listStart < 0 || listEnd <= listStart || listStart >= line.Length)
            return;

        listEnd = Math.Min(listEnd, line.Length);
        var typeList = line.Substring(listStart, listEnd - listStart);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(typeList))
        {
            var rawSegment = typeList.Substring(segmentStart, segmentLength);
            var leading = CountLeadingWhitespace(rawSegment, 0, rawSegment.Length);
            var trailing = CountTrailingWhitespace(rawSegment, leading, rawSegment.Length - leading);
            var length = rawSegment.Length - leading - trailing;
            if (length <= 0)
                continue;

            var expression = rawSegment.Substring(leading, length);
            if (trimTopLevelCallArguments)
                expression = TrimTopLevelCallArguments(expression);
            if (expression.Length == 0)
                continue;

            var absoluteStart = listStart + segmentStart + leading;
            EmitTypeExpressionReferences(
                expression,
                absoluteStart,
                language,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(absoluteStart));
        }
    }

    public static void EmitColonParameterTypeReferences(
        string line,
        int paramStart,
        int paramEnd,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (paramStart < 0 || paramEnd <= paramStart || paramStart >= line.Length)
            return;

        paramEnd = Math.Min(paramEnd, line.Length);
        var parameterList = line.Substring(paramStart, paramEnd - paramStart);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(parameterList))
        {
            var fragment = parameterList.Substring(segmentStart, segmentLength);
            var colonIndex = FindTopLevelChar(fragment, ':');
            if (colonIndex < 0)
                continue;

            var typeStart = SkipTypePrefixTrivia(fragment, colonIndex + 1);
            if (typeStart >= fragment.Length)
                continue;

            var typeEnd = FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = paramStart + segmentStart + typeStart;
            EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                language,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(absoluteStart));
        }
    }

    public static void EmitColonVariableTypeReferences(
        string line,
        IReadOnlyList<string> declarationKeywords,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var keyword in declarationKeywords)
        {
            foreach (var keywordIndex in EnumerateTopLevelKeywordIndices(line, keyword))
            {
                var declarationStart = keywordIndex + keyword.Length;
                var colonIndex = FindTopLevelChar(line, ':', declarationStart);
                if (colonIndex < 0)
                    continue;

                var assignmentIndex = FindTopLevelChar(line, '=', declarationStart);
                if (assignmentIndex >= 0 && assignmentIndex < colonIndex)
                    continue;

                var typeStart = SkipTypePrefixTrivia(line, colonIndex + 1);
                if (typeStart >= line.Length)
                    continue;

                var typeEnd = FindTypeExpressionEnd(line, typeStart);
                if (typeEnd <= typeStart)
                    continue;

                EmitTypeExpressionReferences(
                    line.Substring(typeStart, typeEnd - typeStart),
                    typeStart,
                    language,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn(typeStart));
            }
        }
    }

    public static void EmitKeywordFollowingTypeReferences(
        string line,
        IReadOnlyList<string> keywords,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var keyword in keywords)
        {
            foreach (var keywordIndex in EnumerateTopLevelKeywordIndices(line, keyword))
            {
                var typeStart = SkipTypePrefixTrivia(line, keywordIndex + keyword.Length);
                if (typeStart >= line.Length)
                    continue;

                var typeEnd = FindTypeExpressionEnd(line, typeStart);
                if (typeEnd <= typeStart)
                    continue;

                EmitTypeExpressionReferences(
                    line.Substring(typeStart, typeEnd - typeStart),
                    typeStart,
                    language,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn(typeStart));
            }
        }
    }

    public static void EmitWhereClauseTypeReferences(
        string line,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (var whereIndex in EnumerateTopLevelKeywordIndices(line, "where"))
        {
            var clauseStart = whereIndex + "where".Length;
            var clauseEnd = FindTypeExpressionEnd(line, clauseStart, stopAtComma: false);
            if (clauseEnd <= clauseStart)
                clauseEnd = line.Length;

            var clause = line.Substring(clauseStart, clauseEnd - clauseStart);
            foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(clause))
            {
                var fragment = clause.Substring(segmentStart, segmentLength);
                var colonIndex = FindTopLevelChar(fragment, ':');
                if (colonIndex < 0)
                    continue;

                var typeStart = SkipTypePrefixTrivia(fragment, colonIndex + 1);
                if (typeStart >= fragment.Length)
                    continue;

                var typeEnd = FindTypeExpressionEnd(fragment, typeStart);
                if (typeEnd <= typeStart)
                    continue;

                var absoluteStart = clauseStart + segmentStart + typeStart;
                EmitTypeExpressionReferences(
                    fragment.Substring(typeStart, typeEnd - typeStart),
                    absoluteStart,
                    language,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    resolveContainerForColumn(absoluteStart));
            }
        }
    }

    public static void EmitGenericColonBoundReferences(
        string line,
        int genericOpenIndex,
        string language,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        if (genericOpenIndex < 0 || genericOpenIndex >= line.Length || line[genericOpenIndex] != '<')
            return;

        var genericCloseIndex = ReferenceExtractor.FindMatchingChar(line, genericOpenIndex, '<', '>');
        if (genericCloseIndex <= genericOpenIndex)
            return;

        var clause = line.Substring(genericOpenIndex + 1, genericCloseIndex - genericOpenIndex - 1);
        foreach (var (segmentStart, segmentLength) in ReferenceExtractor.SplitTopLevelCommaSpans(clause))
        {
            var fragment = clause.Substring(segmentStart, segmentLength);
            var colonIndex = FindTopLevelChar(fragment, ':');
            if (colonIndex < 0)
                continue;

            var typeStart = SkipTypePrefixTrivia(fragment, colonIndex + 1);
            if (typeStart >= fragment.Length)
                continue;

            var typeEnd = FindTypeExpressionEnd(fragment, typeStart);
            if (typeEnd <= typeStart)
                continue;

            var absoluteStart = genericOpenIndex + 1 + segmentStart + typeStart;
            EmitTypeExpressionReferences(
                fragment.Substring(typeStart, typeEnd - typeStart),
                absoluteStart,
                language,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForColumn(absoluteStart));
        }
    }

    public static int FindTopLevelChar(string text, char target, int startIndex = 0)
    {
        foreach (var (index, ch) in EnumerateTopLevelCharacters(text, startIndex))
        {
            if (ch == target)
                return index;
        }

        return -1;
    }

    public static int FindTopLevelSequence(string text, string sequence, int startIndex = 0)
    {
        if (string.IsNullOrEmpty(sequence))
            return -1;

        foreach (var (index, _) in EnumerateTopLevelCharacters(text, startIndex))
        {
            if (index + sequence.Length <= text.Length
                && string.CompareOrdinal(text, index, sequence, 0, sequence.Length) == 0)
            {
                return index;
            }
        }

        return -1;
    }

    public static IEnumerable<int> EnumerateTopLevelKeywordIndices(string text, string keyword, int startIndex = 0)
    {
        foreach (var (index, _) in EnumerateTopLevelCharacters(text, startIndex))
        {
            if (index + keyword.Length > text.Length)
                continue;
            if (string.CompareOrdinal(text, index, keyword, 0, keyword.Length) != 0)
                continue;
            if (index > 0 && IsIdentifierPart(text[index - 1]))
                continue;
            var after = index + keyword.Length;
            if (after < text.Length && IsIdentifierPart(text[after]))
                continue;

            yield return index;
        }
    }

    public static int FindTypeExpressionEnd(string text, int startIndex, bool stopAtComma = true)
    {
        foreach (var (index, ch) in EnumerateTopLevelCharacters(text, startIndex))
        {
            if (ch == ',' && stopAtComma)
                return index;
            if (ch is ';' or '{' or '}')
                return index;
            if (ch == '=')
                return index;
            if (index + 2 <= text.Length && string.CompareOrdinal(text, index, "=>", 0, 2) == 0)
                return index;
            if (IsTopLevelStopKeyword(text, index, "where")
                || IsTopLevelStopKeyword(text, index, "implements")
                || IsTopLevelStopKeyword(text, index, "extends"))
            {
                return index;
            }
        }

        return text.Length;
    }

    public static int SkipTypePrefixTrivia(string text, int index)
    {
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            if (index < text.Length && text[index] is '?' or '!')
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static string TrimTopLevelCallArguments(string expression)
    {
        var openParen = FindTopLevelChar(expression, '(');
        if (openParen <= 0)
            return expression;

        return expression.Substring(0, openParen).TrimEnd();
    }

    private static bool IsTopLevelStopKeyword(string text, int index, string keyword)
    {
        if (index + keyword.Length > text.Length)
            return false;
        if (string.CompareOrdinal(text, index, keyword, 0, keyword.Length) != 0)
            return false;
        if (index > 0 && IsIdentifierPart(text[index - 1]))
            return false;
        var after = index + keyword.Length;
        return after >= text.Length || !IsIdentifierPart(text[after]);
    }

    private static IEnumerable<(int Index, char Character)> EnumerateTopLevelCharacters(string text, int startIndex)
    {
        var angleDepth = 0;
        var parenDepth = 0;
        var squareDepth = 0;
        var braceDepth = 0;
        for (var index = Math.Max(0, startIndex); index < text.Length; index++)
        {
            var ch = text[index];
            if (ch is '\'' or '"')
            {
                index = SkipQuotedString(text, index);
                continue;
            }

            if (ch == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                    yield break;
                if (text[index + 1] == '*')
                {
                    index = SkipBlockComment(text, index + 2);
                    continue;
                }
            }

            if (angleDepth == 0 && parenDepth == 0 && squareDepth == 0 && braceDepth == 0)
                yield return (index, ch);

            switch (ch)
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0)
                        angleDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                        squareDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    break;
            }
        }
    }

    private static int SkipQuotedString(string text, int startIndex)
    {
        var quote = text[startIndex];
        for (var index = startIndex + 1; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index++;
                continue;
            }

            if (text[index] == quote)
                return index;
        }

        return text.Length - 1;
    }

    private static int SkipBlockComment(string text, int startIndex)
    {
        for (var index = startIndex; index + 1 < text.Length; index++)
        {
            if (text[index] == '*' && text[index + 1] == '/')
                return index + 1;
        }

        return text.Length - 1;
    }

    private static int CountLeadingWhitespace(string text, int start, int length)
    {
        var end = Math.Min(text.Length, start + length);
        var count = 0;
        for (var index = start; index < end && char.IsWhiteSpace(text[index]); index++)
            count++;
        return count;
    }

    private static int CountTrailingWhitespace(string text, int start, int length)
    {
        var end = Math.Min(text.Length, start + length);
        var count = 0;
        for (var index = end - 1; index >= start && char.IsWhiteSpace(text[index]); index--)
            count++;
        return count;
    }

    private static bool IsIdentifierPart(char ch) =>
        ch == '_' || ch == '$' || ch == '\'' || char.IsLetterOrDigit(ch);
}
