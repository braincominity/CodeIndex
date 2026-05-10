using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class PhpReferenceExtractor
{
    private static readonly Regex StaticAccessRegex = new(
        @"(?<![\w$\\])(?<name>(?:\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))::(?<member>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObjectMemberAccessRegex = new(
        @"(?:\?->|->)\s*(?<name>[A-Za-z_]\w*)(?!\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AttributeRegex = new(
        @"(?:#\[\s*|,\s*)(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)\b(?=\s*(?:\(|,|\]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void EmitAttributeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        if (!preparedLine.Contains("#[", StringComparison.Ordinal))
            return;

        foreach (Match match in AttributeRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var rawName = nameGroup.Value;
            var trimmedName = rawName.TrimStart('\\');
            if (trimmedName.Length == 0)
                continue;

            var leadingBackslashCount = rawName.Length - trimmedName.Length;
            var qualifiedNameIndex = nameGroup.Index + leadingBackslashCount;
            if (trimmedName.Contains('\\', StringComparison.Ordinal))
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    trimmedName,
                    qualifiedNameIndex,
                    "type_reference",
                    context,
                    lineNumber,
                    container);
            }

            var shortNameStart = trimmedName.LastIndexOf('\\') + 1;
            var shortName = trimmedName[shortNameStart..];
            if (shortName.Length == 0)
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                shortName,
                qualifiedNameIndex + shortNameStart,
                "type_reference",
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitStaticAccessReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in StaticAccessRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var rawName = nameGroup.Value;
            var trimmedName = rawName.TrimStart('\\');
            if (trimmedName.Length == 0)
                continue;

            var leadingBackslashCount = rawName.Length - trimmedName.Length;
            var shortNameStart = trimmedName.LastIndexOf('\\') + 1;
            var shortName = trimmedName[shortNameStart..];
            if (shortName.Length == 0)
                continue;

            var qualifiedNameIndex = nameGroup.Index + leadingBackslashCount;
            if (trimmedName.Length > shortName.Length)
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    trimmedName,
                    qualifiedNameIndex,
                    "type_reference",
                    context,
                    lineNumber,
                    container);
            }

            if (!string.Equals(shortName, "self", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(shortName, "static", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(shortName, "parent", StringComparison.OrdinalIgnoreCase))
            {
                var shortNameIndex = qualifiedNameIndex + shortNameStart;
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    shortName,
                    shortNameIndex,
                    "type_reference",
                    context,
                    lineNumber,
                    container);
            }
        }
    }

    public static void EmitObjectMemberAccessReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in ObjectMemberAccessRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                nameGroup.Value,
                nameGroup.Index,
                "reference",
                context,
                lineNumber,
                container);
        }
    }
}
