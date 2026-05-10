using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class DockerfileReferenceExtractor
{
    private static readonly Regex StageReferenceRegex = new(
        @"^\s*FROM\s+(?:--platform=\S+\s+)?(?<name>[A-Za-z0-9_.-]+)\s+AS\s+[A-Za-z0-9_.-]+(?:\s+#.*)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CopyFromReferenceRegex = new(
        @"^\s*(?:ONBUILD\s+)?(?:COPY|ADD)\b.*?--from=[""']?(?<name>[A-Za-z0-9_.-]+)(?![:/@])\b[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RunMountFromReferenceRegex = new(
        @"(?:^|,)from=(?<name>[A-Za-z0-9_.-]+)(?![:/@])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BracedVariableReferenceRegex = new(
        @"(?<![\$\\])\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::?[-+?=][^}]*)?\}",
        RegexOptions.Compiled);

    private static readonly Regex UnbracedVariableReferenceRegex = new(
        @"(?<![\$\\])\$(?<name>[A-Za-z_][A-Za-z0-9_]*)",
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
        string originalLine,
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

        var fromMatch = StageReferenceRegex.Match(originalLine);
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

        foreach (Match match in CopyFromReferenceRegex.Matches(originalLine))
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

        EmitRunMountReferences(
            originalLine,
            context,
            lineNumber,
            references,
            seen,
            fileId,
            stageNames,
            container);
    }

    private static void EmitRunMountReferences(
        string line,
        string context,
        int lineNumber,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        HashSet<string> stageNames,
        SymbolRecord? container)
    {
        if (!TryGetRunOptionsStart(line, out var index))
            return;

        while (index < line.Length)
        {
            index = SkipWhitespace(line, index);
            if (index >= line.Length || !StartsWith(line, index, "--"))
                return;

            var optionStart = index;
            var optionEnd = ScanOptionToken(line, optionStart);
            if (optionEnd <= optionStart)
                return;

            var option = line.Substring(optionStart, optionEnd - optionStart);
            if (option.StartsWith("--mount=", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in RunMountFromReferenceRegex.Matches(option["--mount=".Length..]))
                {
                    var name = match.Groups["name"].Value;
                    if (!stageNames.Contains(name))
                        continue;

                    ReferenceExtractor.AddReference(
                        references,
                        seen,
                        fileId,
                        name,
                        optionStart + "--mount=".Length + match.Groups["name"].Index,
                        "call",
                        context,
                        lineNumber,
                        container);
                }
            }

            index = optionEnd;
        }
    }

    private static bool TryGetRunOptionsStart(string line, out int index)
    {
        index = SkipWhitespace(line, 0);
        if (!StartsWith(line, index, "RUN"))
            return false;

        var afterRun = index + "RUN".Length;
        if (afterRun < line.Length && !char.IsWhiteSpace(line[afterRun]))
            return false;

        index = afterRun;
        return true;
    }

    private static int ScanOptionToken(string line, int index)
    {
        var quote = '\0';
        while (index < line.Length)
        {
            var c = line[index];
            if (quote != '\0')
            {
                if (c == '\\' && index + 1 < line.Length)
                {
                    index += 2;
                    continue;
                }

                if (c == quote)
                    quote = '\0';

                index++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(c))
                break;

            index++;
        }

        return index;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index;
    }

    private static bool StartsWith(string text, int index, string value)
        => index >= 0
           && index + value.Length <= text.Length
           && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

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

        foreach (Match match in UnbracedVariableReferenceRegex.Matches(preparedLine))
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
