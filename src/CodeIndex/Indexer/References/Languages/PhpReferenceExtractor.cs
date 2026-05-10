using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class PhpReferenceExtractor
{
    private static readonly Regex StaticAccessRegex = new(
        @"(?<![\w$\\])(?<name>(?:\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))::\$?(?<member>[A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObjectMemberAccessRegex = new(
        @"(?:\?->|->)\s*(?<name>[A-Za-z_]\w*)(?!\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AttributeRegex = new(
        @"(?:#\[\s*|,\s*)(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)\b(?=\s*(?:\(|,|\]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InstanceofRegex = new(
        @"\binstanceof\s+(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CatchTypeRegex = new(
        @"\bcatch\s*\(\s*(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s*\|\s*(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*\s+\$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ReturnTypeRegex = new(
        @"\)\s*:\s*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s*\|\s*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ParameterTypeRegex = new(
        @"(?:^|[(,])\s*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s*[|&]\s*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*\s+\$[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> BuiltinTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "array", "bool", "callable", "false", "float", "int", "iterable", "mixed", "never",
        "null", "object", "self", "static", "string", "true", "void",
    };

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

            var memberGroup = match.Groups["member"];
            if (memberGroup.Success
                && !memberGroup.Value.Equals("class", StringComparison.OrdinalIgnoreCase)
                && !IsPhpCallAfterStaticMember(preparedLine, memberGroup.Index + memberGroup.Length))
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    memberGroup.Value,
                    memberGroup.Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
            }
        }
    }

    public static void EmitInstanceofReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in InstanceofRegex.Matches(preparedLine))
        {
            AddPhpTypeReferenceFromQualifiedName(
                match.Groups["name"],
                references,
                seen,
                fileId,
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitCatchTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in CatchTypeRegex.Matches(preparedLine))
        {
            foreach (Capture capture in match.Groups["name"].Captures)
            {
                AddPhpTypeReferenceFromQualifiedName(
                    capture,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    container);
            }
        }
    }

    public static void EmitReturnTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in ReturnTypeRegex.Matches(preparedLine))
        {
            foreach (Capture capture in match.Groups["name"].Captures)
            {
                if (IsPhpBuiltinTypeName(capture.Value))
                    continue;

                AddPhpTypeReferenceFromQualifiedName(
                    capture,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    container);
            }
        }
    }

    public static void EmitParameterTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in ParameterTypeRegex.Matches(preparedLine))
        {
            foreach (Capture capture in match.Groups["name"].Captures)
            {
                if (IsPhpBuiltinTypeName(capture.Value))
                    continue;

                AddPhpTypeReferenceFromQualifiedName(
                    capture,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    container);
            }
        }
    }

    private static bool IsPhpCallAfterStaticMember(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        return index < line.Length && line[index] == '(';
    }

    private static bool IsPhpBuiltinTypeName(string name)
        => !name.Contains('\\', StringComparison.Ordinal)
           && BuiltinTypeNames.Contains(name.TrimStart('\\'));

    private static void AddPhpTypeReferenceFromQualifiedName(
        Capture nameGroup,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var rawName = nameGroup.Value;
        var trimmedName = rawName.TrimStart('\\');
        if (trimmedName.Length == 0)
            return;

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
            return;

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
