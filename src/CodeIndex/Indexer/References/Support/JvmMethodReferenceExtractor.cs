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

            if (string.Equals(nameGroup.Value, "new", StringComparison.Ordinal))
            {
                var ownerGroup = match.Groups["owner"];
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

            AddChainReference(references, seen, fileId, nameGroup.Value, nameGroup.Index, context, lineNumber, container);
        }
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
