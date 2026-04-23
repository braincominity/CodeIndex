using System.Text;
using CodeIndex.Database;

namespace CodeIndex.Indexer;

internal static class SqlNameResolver
{
    private readonly record struct SqlNameParts(string NormalizedName, string LeafName, int SegmentCount);

    public static string NormalizeQualifiedName(string? qualifiedName)
        => ParseParts(qualifiedName).NormalizedName;

    public static string GetLeafName(string? qualifiedName)
        => ParseParts(qualifiedName).LeafName;

    public static int GetSegmentCount(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount;

    public static bool HasQualifier(string? qualifiedName)
        => ParseParts(qualifiedName).SegmentCount > 1;

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

    public static string ResolveReferenceNameFolded(string? symbolName, string? context, string? containerName)
    {
        var resolved = ResolveReferenceName(symbolName, context, containerName);
        return resolved.Length == 0 ? string.Empty : NameFold.Fold(resolved) ?? resolved;
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

            if (!TryReadQualifiedName(text, i, out var normalizedName, out var endIndex))
                continue;

            if (GetSegmentCount(normalizedName) > 1)
                yield return normalizedName;

            i = Math.Max(i, endIndex - 1);
        }
    }

    private static void AppendNormalizedSegment(List<string> segments, StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0)
            segments.Add(value);
        current.Clear();
    }

    private static bool TryReadQualifiedName(string text, int startIndex, out string normalizedName, out int endIndexExclusive)
    {
        normalizedName = string.Empty;
        endIndexExclusive = startIndex;

        var segments = new List<string>();
        var index = startIndex;
        while (true)
        {
            if (!TryReadQualifiedNameSegment(text, ref index, out var segment))
                return false;

            segments.Add(segment);
            var scan = index;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || text[scan] != '.')
            {
                endIndexExclusive = index;
                break;
            }

            scan++;
            while (scan < text.Length && char.IsWhiteSpace(text[scan]))
                scan++;

            if (scan >= text.Length || !IsSqlIdentifierStartChar(text[scan]))
            {
                endIndexExclusive = index;
                break;
            }

            index = scan;
        }

        normalizedName = string.Join(".", segments);
        return normalizedName.Length > 0;
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
