using System.Text;
using CodeIndex.Database;

namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    private readonly record struct SqlNameParts(string NormalizedName, string LeafName, int SegmentCount);
    private readonly record struct QualifiedNameMatch(
        string NormalizedName,
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
        var normalizedQuery = NormalizeQualifiedName(query);
        if (normalizedQuery.Length == 0 || !HasQualifier(query) || string.IsNullOrWhiteSpace(context))
            return false;

        return TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
            && string.Equals(match.NormalizedName, normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContextContainsQualifiedName(string? context, string? query)
    {
        var normalizedQuery = NormalizeQualifiedName(query);
        if (normalizedQuery.Length == 0 || !HasQualifier(query) || string.IsNullOrWhiteSpace(context))
            return false;

        foreach (var candidate in EnumerateQualifiedNames(context))
        {
            if (string.Equals(candidate, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return true;
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
        if (leafName.Length > 0
            && TryGetQualifiedNameAtColumn(context, columnNumber, out var match)
            && string.Equals(GetLeafName(match.NormalizedName), leafName, StringComparison.OrdinalIgnoreCase))
        {
            return match.NormalizedName;
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

    public static bool ContextContainsQualifiedNameFoldedAtColumn(string? context, string? query, int? columnNumber)
    {
        var normalizedQuery = NormalizeQualifiedName(query);
        if (normalizedQuery.Length == 0 || !HasQualifier(query) || string.IsNullOrWhiteSpace(context))
            return false;
        if (!TryGetQualifiedNameAtColumn(context, columnNumber, out var match))
            return false;

        var foldedCandidate = NameFold.Fold(match.NormalizedName) ?? match.NormalizedName;
        var foldedQuery = NameFold.Fold(normalizedQuery) ?? normalizedQuery;
        return string.Equals(foldedCandidate, foldedQuery, StringComparison.Ordinal);
    }

    public static bool ContextContainsQualifiedNameFolded(string? context, string? query)
    {
        var normalizedQuery = NormalizeQualifiedName(query);
        if (normalizedQuery.Length == 0 || !HasQualifier(query) || string.IsNullOrWhiteSpace(context))
            return false;

        var foldedQuery = NameFold.Fold(normalizedQuery) ?? normalizedQuery;
        foreach (var candidate in EnumerateQualifiedNames(context))
        {
            var foldedCandidate = NameFold.Fold(candidate) ?? candidate;
            if (string.Equals(foldedCandidate, foldedQuery, StringComparison.Ordinal))
                return true;
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
            if (zeroBasedColumn >= candidate.LeafStartIndex && zeroBasedColumn < candidate.LeafEndIndexExclusive)
            {
                match = candidate;
                return true;
            }
        }

        return false;
    }

    private static SqlNameParts ParseParts(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return new SqlNameParts(string.Empty, string.Empty, 0);

        var trimmed = qualifiedName.Trim();
        var segments = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';

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
            return new SqlNameParts(string.Empty, string.Empty, 0);

        var normalized = string.Join(".", segments);
        return new SqlNameParts(normalized, segments[^1], segmentCount);
    }

    private static IEnumerable<string> EnumerateQualifiedNames(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!IsSqlIdentifierStartChar(text[i]))
                continue;

            if (!TryReadQualifiedName(text, i, out var match))
                continue;

            if (GetSegmentCount(match.NormalizedName) > 1)
                yield return match.NormalizedName;

            i = Math.Max(i, match.EndIndexExclusive - 1);
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

            if (GetSegmentCount(match.NormalizedName) > 1)
                yield return match;

            i = Math.Max(i, match.EndIndexExclusive - 1);
        }
    }

    private static void AppendNormalizedSegment(List<string> segments, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            segments.Add(value);
        current.Clear();
    }

    private static bool TryReadQualifiedName(string text, int startIndex, out QualifiedNameMatch match)
    {
        match = default;

        var segments = new List<string>();
        var index = startIndex;
        var leafStartIndex = startIndex;
        var leafEndIndexExclusive = startIndex;
        while (true)
        {
            var segmentStartIndex = index;
            if (!TryReadQualifiedNameSegment(text, ref index, out var segment))
                return false;

            segments.Add(segment);
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

        match = new QualifiedNameMatch(normalizedName, startIndex, index, leafStartIndex, leafEndIndexExclusive);
        return true;
    }

    private static bool TryReadQualifiedNameSegment(string text, ref int index, out string segment)
    {
        segment = string.Empty;
        if (index >= text.Length)
            return false;

        var current = new StringBuilder();
        var quote = text[index];
        if (quote is '[' or '"' or '`')
        {
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

    private static bool IsSqlIdentifierStartChar(char ch)
        => ch is '[' or '"' or '`' or '_' or '$' or '#'
           || char.IsLetter(ch);

    private static bool IsSqlIdentifierChar(char ch)
        => ch is '_' or '$' or '#'
           || char.IsLetterOrDigit(ch);
}
