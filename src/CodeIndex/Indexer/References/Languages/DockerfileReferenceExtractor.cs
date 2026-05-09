using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class DockerfileReferenceExtractor
{
    private static readonly Regex StageReferenceRegex = new(
        @"^\s*FROM\s+(?:--platform=\S+\s+)?(?<name>[A-Za-z0-9_.-]+)\s+AS\s+[A-Za-z0-9_.-]+(?:\s+#.*)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CopyFromReferenceRegex = new(
        @"^\s*(?:COPY|ADD)\b.*?--from=(?<name>[A-Za-z0-9_.-]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BracedVariableReferenceRegex = new(
        @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}",
        RegexOptions.Compiled);

    public static HashSet<string>? BuildStageNames(string language, IReadOnlyList<SymbolRecord> symbols)
    {
        if (language != "dockerfile")
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "function" || string.IsNullOrWhiteSpace(symbol.Name))
                continue;

            names.Add(symbol.Name);
        }

        return names;
    }

    public static HashSet<string>? BuildVariableNames(string language, IReadOnlyList<SymbolRecord> symbols)
    {
        if (language != "dockerfile")
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "property" || string.IsNullOrWhiteSpace(symbol.Name))
                continue;

            names.Add(symbol.Name);
        }

        return names;
    }

    public static void EmitStageReferences(
        string preparedLine,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? stageNames,
        SymbolRecord? container)
    {
        if (stageNames == null || stageNames.Count == 0)
            return;

        var fromMatch = StageReferenceRegex.Match(preparedLine);
        if (fromMatch.Success)
        {
            var name = fromMatch.Groups["name"].Value;
            if (stageNames.Contains(name))
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    name,
                    fromMatch.Groups["name"].Index,
                    "call",
                    context,
                    lineNumber,
                    container);
            }
        }

        foreach (Match match in CopyFromReferenceRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (!stageNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                "call",
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitVariableReferences(
        string preparedLine,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string>? variableNames,
        SymbolRecord? container)
    {
        if (variableNames == null || variableNames.Count == 0)
            return;

        foreach (Match match in BracedVariableReferenceRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (!variableNames.Contains(name))
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
