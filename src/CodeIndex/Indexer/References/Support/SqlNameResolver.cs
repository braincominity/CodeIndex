using System.Text;
using CodeIndex.Database;

namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    private readonly record struct SqlNameParts(string NormalizedName, string LeafName, int SegmentCount, bool HasCaseSensitiveQuote);
    private readonly record struct QualifiedNameMatch(
        string NormalizedName,
        int SegmentCount,
        bool HasCaseSensitiveQuote,
        int StartIndex,
        int EndIndexExclusive,
        int LeafStartIndex,
        int LeafEndIndexExclusive);

    public static string NormalizeQualifiedName(string? qualifiedName)
        => ParseParts(qualifiedName).NormalizedName;

    public static string GetLeafName(string? qualifiedName)
        => ParseParts(qualifiedName).LeafName;

    public static int GetSegmentCount(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount;

    public static bool HasQualifier(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount > 1;

    public static bool ContextContainsQualifiedNameAtColumn(string? context, string? query, int? columnNumber)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;

        return TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
            && match.SegmentCount == queryParts.SegmentCount
            && NamesEqual(match.NormalizedName, queryParts.NormalizedName, match.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote);
    }

    public static bool ContextContainsQualifiedNameLikeAtColumn(string? context, string? query, int? columnNumber)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;

        return TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
            && match.SegmentCount == queryParts.SegmentCount
            && NamesEqual(match.NormalizedName, queryParts.NormalizedName, match.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote);
    }

    public static bool ContextContainsQualifiedName(string? context, string? query)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;

        foreach (var candidate in EnumerateQualifiedNameMatches(context))
        {
            if (candidate.SegmentCount == queryParts.SegmentCount
                && NamesEqual(candidate.NormalizedName, queryParts.NormalizedName, candidate.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote))
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveReferenceName(string? symbolName, string? context, string? containerName)
    {
        var normalizedSymbolName = NormalizeQualifiedName(symbolName);
        if (normalizedSymbolName.Length == 0)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(context))
            return normalizedSymbolName;

        var leafName = GetLeafName(symbolName);
        if (leafName.Length == 0)
            return normalizedSymbolName;

        var candidates = EnumerateQualifiedNames(context)
            .Where(candidate => string.Equals(GetLeafName(candidate), leafName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
            return normalizedSymbolName;
        if (candidates.Count == 1)
            return candidates[0];

        var normalizedContainerName = NormalizeQualifiedName(containerName);
        if (normalizedContainerName.Length > 0)
        {
            var nonContainerCandidates = candidates
                .Where(candidate => !string.Equals(candidate, normalizedContainerName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nonContainerCandidates.Count == 1)
                return nonContainerCandidates[0];
        }

        return normalizedSymbolName;
    }

    public static string ResolveReferenceNameAtColumn(string? symbolName, string? context, string? containerName, int? columnNumber)
    {
        var normalizedSymbolName = NormalizeQualifiedName(symbolName);
        if (normalizedSymbolName.Length == 0)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(context))
            return normalizedSymbolName;

        var leafName = GetLeafName(symbolName);
        if (leafName.Length > 0 && columnNumber.HasValue && columnNumber.Value > 0)
        {
            if (TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
                && NamesEqual(GetLeafName(match.NormalizedName), leafName, match.HasCaseSensitiveQuote || ParseParts(symbolName).HasCaseSensitiveQuote))
            {
                return match.NormalizedName;
            }

            return QualifyLeafNameFromContainerCore(normalizedSymbolName, containerName);
        }

        return ResolveReferenceName(symbolName, context, containerName);
    }

    public static string ResolveReferenceNameFolded(string? symbolName, string? context, string? containerName)
    {
        var resolved = ResolveReferenceName(symbolName, context, containerName);
        return resolved.Length == 0 ? string.Empty : NameFold.Fold(resolved) ?? resolved;
    }

    public static string ResolveReferenceNameFoldedAtColumn(string? symbolName, string? context, string? containerName, int? columnNumber)
    {
        var resolved = ResolveReferenceNameAtColumn(symbolName, context, containerName, columnNumber);
        return resolved.Length == 0 ? string.Empty : NameFold.Fold(resolved) ?? resolved;
    }

    public static int ResolveReferenceSegmentCountAtColumn(string? symbolName, string? context, string? containerName, int? columnNumber)
    {
        var normalizedSymbolName = NormalizeQualifiedName(symbolName);
        if (normalizedSymbolName.Length == 0)
            return 0;
        if (string.IsNullOrWhiteSpace(context))
            return GetSegmentCount(normalizedSymbolName);

        var leafName = GetLeafName(symbolName);
        if (leafName.Length > 0 && columnNumber.HasValue && columnNumber.Value > 0)
        {
            if (TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
                && string.Equals(GetLeafName(match.NormalizedName), leafName, StringComparison.OrdinalIgnoreCase))
            {
                return match.SegmentCount;
            }

            return GetSegmentCount(QualifyLeafNameFromContainerCore(normalizedSymbolName, containerName));
        }

        return GetSegmentCount(ResolveReferenceName(symbolName, context, containerName));
    }

    public static bool ReferenceMatchesTargetAtColumn(
        string? symbolName,
        string? context,
        string? containerName,
        int? columnNumber,
        string? targetName)
    {
        var targetParts = ParseParts(targetName);
        if (targetParts.NormalizedName.Length == 0)
            return false;

        var resolved = ResolveReferenceNameAtColumn(symbolName, context, containerName, columnNumber);
        if (resolved.Length == 0)
            return false;

        var preserveCase = targetParts.HasCaseSensitiveQuote;
        if (TryGetQualifiedNameAtColumn(context, columnNumber, out var match))
            preserveCase |= match.HasCaseSensitiveQuote;

        return NamesEqual(resolved, targetParts.NormalizedName, preserveCase);
    }

    public static bool AllowLeafFallbackAtColumn(string? symbolName, string? context, string? containerName, int? columnNumber)
    {
        var normalizedSymbolName = NormalizeQualifiedName(symbolName);
        if (normalizedSymbolName.Length == 0 || HasQualifier(normalizedSymbolName))
            return false;

        var leafName = GetLeafName(symbolName);
        if (leafName.Length > 0
            && TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
            && NamesEqual(GetLeafName(match.NormalizedName), leafName, match.HasCaseSensitiveQuote || ParseParts(symbolName).HasCaseSensitiveQuote))
        {
            return false;
        }

        if (leafName.Length > 0
            && !string.IsNullOrWhiteSpace(context)
            && EnumerateQualifiedNames(context).Any(candidate =>
                HasQualifier(candidate)
                && string.Equals(GetLeafName(candidate), leafName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !IsQuotedSingleIdentifierWithDots(containerName);
    }

    public static bool ContextContainsQualifiedNameFoldedAtColumn(string? context, string? query, int? columnNumber)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;
        if (!TryGetQualifiedNameAtColumn(context, columnNumber, out var match))
            return false;

        var preserveCase = match.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote;
        var foldedCandidate = preserveCase ? match.NormalizedName : NameFold.Fold(match.NormalizedName) ?? match.NormalizedName;
        var foldedQuery = preserveCase ? queryParts.NormalizedName : NameFold.Fold(queryParts.NormalizedName) ?? queryParts.NormalizedName;
        return match.SegmentCount == queryParts.SegmentCount
            && string.Equals(foldedCandidate, foldedQuery, StringComparison.Ordinal);
    }

    public static bool ContextContainsQualifiedNameLikeFoldedAtColumn(string? context, string? query, int? columnNumber)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;
        if (!TryGetQualifiedNameAtColumn(context, columnNumber, out var match))
            return false;

        var preserveCase = match.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote;
        var foldedCandidate = preserveCase ? match.NormalizedName : NameFold.Fold(match.NormalizedName) ?? match.NormalizedName;
        var foldedQuery = preserveCase ? queryParts.NormalizedName : NameFold.Fold(queryParts.NormalizedName) ?? queryParts.NormalizedName;
        return match.SegmentCount == queryParts.SegmentCount
            && string.Equals(foldedCandidate, foldedQuery, StringComparison.Ordinal);
    }

    public static bool ContextContainsQualifiedNameFolded(string? context, string? query)
    {
        var queryParts = ParseParts(query);
        if (queryParts.NormalizedName.Length == 0 || queryParts.SegmentCount <= 1 || string.IsNullOrWhiteSpace(context))
            return false;

        foreach (var candidate in EnumerateQualifiedNameMatches(context))
        {
            var preserveCase = candidate.HasCaseSensitiveQuote || queryParts.HasCaseSensitiveQuote;
            var foldedCandidate = preserveCase ? candidate.NormalizedName : NameFold.Fold(candidate.NormalizedName) ?? candidate.NormalizedName;
            var foldedQuery = preserveCase ? queryParts.NormalizedName : NameFold.Fold(queryParts.NormalizedName) ?? queryParts.NormalizedName;
            if (candidate.SegmentCount == queryParts.SegmentCount
                && string.Equals(foldedCandidate, foldedQuery, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetQualifiedNameAtColumn(string? context, int? columnNumber, out QualifiedNameMatch match)
    {
        match = default;
        if (string.IsNullOrWhiteSpace(context) || !columnNumber.HasValue || columnNumber.Value <= 0)
            return false;

        var zeroBasedColumn = columnNumber.Value - 1;
        foreach (var candidate in EnumerateQualifiedNameMatches(context))
        {
            if (candidate.SegmentCount <= 1 && !candidate.NormalizedName.Contains('.', StringComparison.Ordinal))
                continue;

            if (zeroBasedColumn >= candidate.LeafStartIndex && zeroBasedColumn < candidate.LeafEndIndexExclusive)
            {
                match = candidate;
                return true;
            }

            if (zeroBasedColumn >= candidate.StartIndex
                && zeroBasedColumn < candidate.EndIndexExclusive
                && TryReadQualifiedNamePrefixAtColumn(context, candidate.StartIndex, zeroBasedColumn, out match))
            {
                return true;
            }
        }

        return false;
    }

    private static SqlNameParts ParseParts(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return new SqlNameParts(string.Empty, string.Empty, 0, false);

        var trimmed = qualifiedName.Trim();
        var segments = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        var hasCaseSensitiveQuote = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (quote != '\0')
            {
                if (quote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < trimmed.Length && trimmed[i + 1] == ']')
                        {
                            current.Append(']');
                            i++;
                        }
                        else
                        {
                            quote = '\0';
                        }
                    }
                    else
                    {
                        current.Append(ch);
                    }

                    continue;
                }

                if (ch == quote)
                {
                    if (i + 1 < trimmed.Length && trimmed[i + 1] == quote)
                    {
                        current.Append(quote);
                        i++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch is '[' or '"' or '`')
            {
                quote = ch;
                hasCaseSensitiveQuote |= ch == '"';
                continue;
            }

            if (ch == '.')
            {
                AppendNormalizedSegment(segments, current);
                continue;
            }

            current.Append(ch);
        }

        AppendNormalizedSegment(segments, current);
        var segmentCount = segments.Count;
        if (segmentCount == 0)
            return new SqlNameParts(string.Empty, string.Empty, 0, hasCaseSensitiveQuote);

        var normalized = string.Join(".", segments);
        return new SqlNameParts(normalized, segments[^1], segmentCount, hasCaseSensitiveQuote);
    }

    private static IEnumerable<string> EnumerateQualifiedNames(string text)
    {
        foreach (var match in EnumerateQualifiedNameMatches(text))
        {
            if (match.SegmentCount > 1)
                yield return match.NormalizedName;
        }
    }

    private static IEnumerable<QualifiedNameMatch> EnumerateQualifiedNameMatches(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsSqlIdentifierStartChar(text[i]))
                continue;

            if (!TryReadQualifiedName(text, i, out var match))
                continue;

            yield return match;

            i = Math.Max(i, match.EndIndexExclusive - 1);
        }
    }

    private static string QualifyLeafNameFromContainerCore(string normalizedSymbolName, string? containerName)
    {
        if (normalizedSymbolName.Length == 0 || HasQualifier(normalizedSymbolName))
            return normalizedSymbolName;

        var containerParts = ParseParts(containerName);
        if (containerParts.NormalizedName.Length == 0 || containerParts.SegmentCount <= 1)
            return normalizedSymbolName;

        var lastDot = containerParts.NormalizedName.LastIndexOf('.');
        if (lastDot <= 0)
            return normalizedSymbolName;

        return containerParts.NormalizedName[..(lastDot + 1)] + normalizedSymbolName;
    }

    private static void AppendNormalizedSegment(List<string> segments, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            segments.Add(value);
        current.Clear();
    }

    private static bool IsQuotedSingleIdentifierWithDots(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        if (trimmed.Length < 2)
            return false;

        var first = trimmed[0];
        var last = trimmed[^1];
        if (!((first == '[' && last == ']') || (first == '"' && last == '"') || (first == '`' && last == '`')))
            return false;

        var parts = ParseParts(trimmed);
        return parts.SegmentCount == 1
            && parts.NormalizedName.Contains('.', StringComparison.Ordinal);
    }

    private static bool TryReadQualifiedName(string text, int startIndex, out QualifiedNameMatch match)
    {
        match = default;

        var segments = new List<string>();
        var index = startIndex;
        var leafStartIndex = startIndex;
        var leafEndIndexExclusive = startIndex;
        var hasCaseSensitiveQuote = false;
        while (true)
        {
            var segmentStartIndex = index;
            if (!TryReadQualifiedNameSegment(text, ref index, out var segment, out var segmentHasCaseSensitiveQuote))
                return false;

            segments.Add(segment);
            hasCaseSensitiveQuote |= segmentHasCaseSensitiveQuote;
            leafStartIndex = segmentStartIndex;
            leafEndIndexExclusive = index;
            var scan = index;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || text[scan] != '.')
            {
                break;
            }

            scan++;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || !IsSqlIdentifierStartChar(text[scan]))
            {
                break;
            }

            index = scan;
        }

        var normalizedName = string.Join(".", segments);
        if (normalizedName.Length == 0)
            return false;

        match = new QualifiedNameMatch(normalizedName, segments.Count, hasCaseSensitiveQuote, startIndex, index, leafStartIndex, leafEndIndexExclusive);
        return true;
    }

    private static bool TryReadQualifiedNameSegment(string text, ref int index, out string segment, out bool hasCaseSensitiveQuote)
    {
        segment = string.Empty;
        hasCaseSensitiveQuote = false;
        if (index >= text.Length)
            return false;

        var current = new StringBuilder();
        var quote = text[index];
        if (quote is '[' or '"' or '`')
        {
            hasCaseSensitiveQuote = quote == '"';
            index++;
            while (index < text.Length)
            {
                var ch = text[index++];
                if (quote == '[')
                {
                    if (ch == ']')
                    {
                        if (index < text.Length && text[index] == ']')
                        {
                            current.Append(']');
                            index++;
                            continue;
                        }

                        segment = current.ToString().Trim();
                        return segment.Length > 0;
                    }

                    current.Append(ch);
                    continue;
                }

                if (ch == quote)
                {
                    if (index < text.Length && text[index] == quote)
                    {
                        current.Append(quote);
                        index++;
                        continue;
                    }

                    segment = current.ToString().Trim();
                    return segment.Length > 0;
                }

                current.Append(ch);
            }

            return false;
        }

        if (!IsSqlIdentifierStartChar(text[index]))
            return false;

        current.Append(text[index++]);
        while (index < text.Length && IsSqlIdentifierChar(text[index]))
            current.Append(text[index++]);

        segment = current.ToString().Trim();
        return segment.Length > 0;
    }

    private static bool TryReadQualifiedNamePrefixAtColumn(
        string text,
        int startIndex,
        int zeroBasedColumn,
        out QualifiedNameMatch match)
    {
        match = default;
        var segments = new List<(string Name, bool HasCaseSensitiveQuote, int StartIndex, int EndIndexExclusive)>();
        var index = startIndex;
        while (true)
        {
            var segmentStartIndex = index;
            if (!TryReadQualifiedNameSegment(text, ref index, out var segment, out var segmentHasCaseSensitiveQuote))
                return false;

            segments.Add((segment, segmentHasCaseSensitiveQuote, segmentStartIndex, index));
            var scan = index;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || text[scan] != '.')
                break;

            scan++;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || !IsSqlIdentifierStartChar(text[scan]))
                break;

            index = scan;
        }

        for (var i = 1; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (zeroBasedColumn < segment.StartIndex || zeroBasedColumn >= segment.EndIndexExclusive)
                continue;

            var matchedSegments = segments.Take(i + 1).ToList();
            var normalizedName = string.Join(".", matchedSegments.Select(part => part.Name));
            match = new QualifiedNameMatch(
                normalizedName,
                i + 1,
                matchedSegments.Any(part => part.HasCaseSensitiveQuote),
                startIndex,
                segment.EndIndexExclusive,
                segment.StartIndex,
                segment.EndIndexExclusive);
            return true;
        }

        return false;
    }

    private static bool IsSqlIdentifierStartChar(char ch)
        => ch is '[' or '"' or '`' or '_' or '$' or '#'
           || char.IsLetter(ch);

    private static bool IsSqlIdentifierChar(char ch)
        => ch is '_' or '$' or '#'
           || char.IsLetterOrDigit(ch);

    private static bool NamesEqual(string left, string right, bool preserveCase)
        => string.Equals(left, right, preserveCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
