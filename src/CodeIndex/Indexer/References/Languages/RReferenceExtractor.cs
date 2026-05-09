using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RReferenceExtractor
{
    // R namespace references like `pkg::fun` and `pkg:::fun` should be searchable as references
    // even when they are not invoked as calls.
    // R の namespace 参照 `pkg::fun` / `pkg:::fun` を参照として記録する。
    private static readonly Regex NamespaceReferenceRegex = new(
        @"(?<![\w.])(?<package>[\w.]+)(?<sep>:::?)(?:(?<backtickName>`[^`]+`)|(?<name>[\w.]+))",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceImportFromDirectiveRegex = new(
        @"^\s*importFrom\s*\(\s*(?<package>[\w.]+)\s*,(?<names>[^)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceExportDirectiveRegex = new(
        @"^\s*export(?:Classes|Methods)?\s*\(\s*(?<names>[^)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceDirectiveNameRegex = new(
        @"`(?<backtickName>[^`]+)`|(?<name>[A-Za-z.][\w.]*)",
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
            var backtickNameGroup = match.Groups["backtickName"];
            var nameGroup = backtickNameGroup.Success ? backtickNameGroup : match.Groups["name"];
            var name = backtickNameGroup.Success
                ? backtickNameGroup.Value[1..^1]
                : nameGroup.Value;
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
                nameGroup.Index + (backtickNameGroup.Success ? 1 : 0),
                "reference",
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitNamespaceDirectiveReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var importFromMatch = NamespaceImportFromDirectiveRegex.Match(preparedLine);
        if (importFromMatch.Success)
        {
            var package = importFromMatch.Groups["package"].Value;
            var namesGroup = importFromMatch.Groups["names"];
            foreach (var (name, nameIndex) in EnumerateNamespaceDirectiveNames(namesGroup.Value, namesGroup.Index))
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    $"{package}::{name}",
                    importFromMatch.Groups["package"].Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    name,
                    nameIndex,
                    "reference",
                    context,
                    lineNumber,
                    container);
            }

            return;
        }

        var exportMatch = NamespaceExportDirectiveRegex.Match(preparedLine);
        if (!exportMatch.Success)
            return;

        var exportNamesGroup = exportMatch.Groups["names"];
        foreach (var (name, nameIndex) in EnumerateNamespaceDirectiveNames(exportNamesGroup.Value, exportNamesGroup.Index))
        {
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameIndex,
                "reference",
                context,
                lineNumber,
                container);
        }
    }

    private static IEnumerable<(string Name, int Index)> EnumerateNamespaceDirectiveNames(string value, int baseIndex)
    {
        foreach (Match match in NamespaceDirectiveNameRegex.Matches(value))
        {
            var backtickNameGroup = match.Groups["backtickName"];
            var nameGroup = backtickNameGroup.Success ? backtickNameGroup : match.Groups["name"];
            yield return (nameGroup.Value, baseIndex + nameGroup.Index + (backtickNameGroup.Success ? 1 : 0));
        }
    }
}
