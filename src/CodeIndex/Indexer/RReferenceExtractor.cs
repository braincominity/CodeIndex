using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RReferenceExtractor
{
    // R namespace references like `pkg::fun` and `pkg:::fun` should be searchable as references
    // even when they are not invoked as calls.
    // R の namespace 参照 `pkg::fun` / `pkg:::fun` を参照として記録する。
    private static readonly Regex NamespaceReferenceRegex = new(
        @"(?<![\w.])(?<package>[\w.]+)(?<sep>:::?)(?<name>[\w.]+)",
        RegexOptions.Compiled);

    public static void EmitNamespaceReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        HashSet<string>? definitionNames)
    {
        foreach (Match match in NamespaceReferenceRegex.Matches(preparedLine))
        {
            var package = match.Groups["package"].Value;
            var separator = match.Groups["sep"].Value;
            var name = match.Groups["name"].Value;
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                $"{package}{separator}{name}",
                match.Groups["package"].Index,
                "reference",
                context,
                lineNumber,
                container);

            if (definitionNames != null && definitionNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                "reference",
                context,
                lineNumber,
                container);
        }
    }
}
