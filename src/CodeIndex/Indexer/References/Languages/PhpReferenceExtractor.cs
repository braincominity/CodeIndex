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

    private static readonly Regex PropertyTypeRegex = new(
        @"^\s*(?:public|private|protected|var)\s+(?:(?:static|readonly)\s+)*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s*[|&]\s*\??(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*\s+\$[A-Za-z_]\w*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InheritanceTypeRegex = new(
        @"\b(?:extends|implements)\s+(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s*,\s*(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UseTypeRegex = new(
        @"^\s*use\s+(?!(?:function|const)\b)(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s+as\s+[A-Za-z_]\w*)?(?:\s*,\s*(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*))*\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex GroupUseTypeRegex = new(
        @"^\s*use\s+(?!(?:function|const)\b)(?<prefix>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)\\\{\s*(?<items>[^{}]+?)\s*\}\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex GroupUseTypeItemRegex = new(
        @"(?<name>\\?[A-Za-z_]\w*(?:\\[A-Za-z_]\w*)*)(?:\s+as\s+[A-Za-z_]\w*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

    public static void EmitPropertyTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in PropertyTypeRegex.Matches(preparedLine))
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

    public static void EmitInheritanceTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        foreach (Match match in InheritanceTypeRegex.Matches(preparedLine))
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

    public static void EmitUseTypeReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var groupMatch = GroupUseTypeRegex.Match(preparedLine);
        if (groupMatch.Success)
        {
            EmitGroupUseTypeReferences(groupMatch, references, seen, fileId, context, lineNumber, container);
            return;
        }

        var match = UseTypeRegex.Match(preparedLine);
        if (!match.Success)
            return;

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

    private static void EmitGroupUseTypeReferences(
        Match groupMatch,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var prefixGroup = groupMatch.Groups["prefix"];
        var prefix = prefixGroup.Value.TrimEnd('\\');
        if (prefix.Length == 0)
            return;

        var itemsGroup = groupMatch.Groups["items"];
        foreach (Match itemMatch in GroupUseTypeItemRegex.Matches(itemsGroup.Value))
        {
            var itemGroup = itemMatch.Groups["name"];
            var rawItemName = itemGroup.Value;
            var trimmedItemName = rawItemName.TrimStart('\\');
            if (trimmedItemName.Length == 0)
                continue;

            var leadingBackslashCount = rawItemName.Length - trimmedItemName.Length;
            var itemShortNameStart = trimmedItemName.LastIndexOf('\\') + 1;
            var shortNameIndex = itemsGroup.Index + itemGroup.Index + leadingBackslashCount + itemShortNameStart;
            AddPhpTypeReferenceFromName(
                prefix + "\\" + trimmedItemName,
                prefixGroup.Index,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                container,
                shortNameIndex);
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
        => AddPhpTypeReferenceFromName(
            nameGroup.Value,
            nameGroup.Index,
            references,
            seen,
            fileId,
            context,
            lineNumber,
            container);

    private static void AddPhpTypeReferenceFromName(
        string rawName,
        int nameIndex,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        int? shortNameIndexOverride = null)
    {
        var trimmedName = rawName.TrimStart('\\');
        if (trimmedName.Length == 0)
            return;

        var leadingBackslashCount = rawName.Length - trimmedName.Length;
        var qualifiedNameIndex = nameIndex + leadingBackslashCount;
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
            shortNameIndexOverride ?? qualifiedNameIndex + shortNameStart,
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
