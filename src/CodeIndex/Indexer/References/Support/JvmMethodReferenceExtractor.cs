using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class JvmMethodReferenceExtractor
{
    private const string FunctionalIdentifierPattern = @"@?[_\p{L}\$][\w$]*";

    private static readonly Regex MethodReferenceRegex = new(
        $@"(?<![\w$])(?:(?<owner>(?:this|super|{FunctionalIdentifierPattern}(?:\.{FunctionalIdentifierPattern})*))\s*)?::\s*(?<name>{FunctionalIdentifierPattern}|new)\b(?=\s*(?:[;,)\]]|$))",
        RegexOptions.Compiled);

    public static void EmitMethodReferenceReferences(
        string language,
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForColumn)
    {
        foreach (Match match in MethodReferenceRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var container = resolveContainerForColumn(nameGroup.Index);
            var ownerGroup = match.Groups["owner"];

            if (string.Equals(nameGroup.Value, "class", StringComparison.Ordinal))
                continue;

            if (string.Equals(nameGroup.Value, "new", StringComparison.Ordinal))
            {
                if (!ownerGroup.Success || ownerGroup.Value.Length == 0)
                    continue;
                if (ownerGroup.Value is "this" or "super")
                    continue;

                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    ownerGroup.Value,
                    ownerGroup.Index,
                    "instantiate",
                    context,
                    lineNumber,
                    container);
                continue;
            }

            EmitOwnerTypeReference(language, ownerGroup, references, seen, fileId, context, lineNumber, container);
            AddChainReference(references, seen, fileId, nameGroup.Value, nameGroup.Index, context, lineNumber, container);
        }
    }

    private static void EmitOwnerTypeReference(
        string language,
        Group ownerGroup,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        if (language is not ("java" or "kotlin"))
            return;
        if (!ownerGroup.Success || ownerGroup.Value.Length == 0)
            return;
        if (ownerGroup.Value is "this" or "super")
            return;

        var leafStartInOwner = ownerGroup.Value.LastIndexOf('.') + 1;
        if (leafStartInOwner >= ownerGroup.Value.Length)
            return;

        var leaf = ownerGroup.Value[leafStartInOwner..];
        if (!IsLikelyJvmTypeName(leaf))
            return;

        ReferenceExtractor.AddTypeReferenceSegment(
            references,
            seen,
            fileId,
            leaf,
            ownerGroup.Index + leafStartInOwner,
            context,
            lineNumber,
            container,
            language);
    }

    private static bool IsLikelyJvmTypeName(string name)
    {
        if (name.Length == 0)
            return false;

        var index = name[0] == '@' ? 1 : 0;
        return index < name.Length && char.IsUpper(name[index]);
    }

    private static void AddChainReference(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string name,
        int column,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var dedupeKey = $"{lineNumber}:{column}:call:{name}";
        if (!seen.Add(dedupeKey))
            return;

        references.Add(new ReferenceRecord
        {
            FileId = fileId,
            SymbolName = name,
            ReferenceKind = "call",
            Line = lineNumber,
            Column = column,
            Context = context,
            ContainerKind = container?.Kind,
            ContainerName = container?.Name,
        });
    }
}
