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
    private static readonly Regex NamespaceImportDirectiveRegex = new(
        @"^\s*import\s*\(\s*(?<package>[\w.]+)(?:\s*,|\s*\))",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceImportFromDirectiveRegex = new(
        @"^\s*importFrom\s*\(\s*(?<package>[\w.]+)\s*,(?<names>[^)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceExportDirectiveRegex = new(
        @"^\s*export(?:Classes|Methods)?\s*\(\s*(?<names>[^)]*)\)",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceS3MethodDirectiveRegex = new(
        @"^\s*S3method\s*\(\s*(?:`(?<genericBacktick>[^`]+)`|['""](?<genericQuoted>[^'""]+)['""]|(?<generic>[A-Za-z.][\w.]*))\s*,\s*(?:`(?<classBacktick>[^`]+)`|['""](?<classQuoted>[^'""]+)['""]|(?<class>[A-Za-z.][\w.]*))(?:\s*,\s*(?:`(?<methodBacktick>[^`]+)`|['""](?<methodQuoted>[^'""]+)['""]|(?<method>[A-Za-z.][\w.]*)))?\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceUseDynLibDirectiveRegex = new(
        @"^\s*useDynLib\s*\(\s*(?:`(?<packageBacktick>[^`]+)`|['""](?<packageQuoted>[^'""]+)['""]|(?<package>[\w.]+))",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceDirectiveStartRegex = new(
        @"^\s*(?:import\s*\(|importFrom\s*\(|export(?:Classes|Methods)?\s*\(|S3method\s*\(|useDynLib\s*\()",
        RegexOptions.Compiled);
    private static readonly Regex NamespaceDirectiveNameRegex = new(
        @"`(?<backtickName>[^`]+)`|(?<name>[A-Za-z.][\w.]*)",
        RegexOptions.Compiled);
    private static readonly Regex BacktickCallRegex = new(
        @"`(?<name>[^`]+)`\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex InfixOperatorCallRegex = new(
        @"(?<!`)(?<name>%[^%\s]+%)(?!`)",
        RegexOptions.Compiled);
    private static readonly Regex SourceFileReferenceRegex = new(
        @"^\s*(?:(?:[\w.]+)::)?source\s*\(\s*(?:file\s*=\s*)?['""](?<path>[^'""]+)['""]",
        RegexOptions.Compiled);
    private static readonly Regex SourceFileReferenceStartRegex = new(
        @"^\s*(?:(?:[\w.]+)::)?source\s*\(",
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
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var directiveLine = NamespaceDirectiveStartRegex.IsMatch(preparedLine)
            ? StripRNamespaceDirectiveComment(originalLine)
            : preparedLine;

        var importFromMatch = NamespaceImportFromDirectiveRegex.Match(directiveLine);
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

        var importMatch = NamespaceImportDirectiveRegex.Match(directiveLine);
        if (importMatch.Success)
        {
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                importMatch.Groups["package"].Value,
                importMatch.Groups["package"].Index,
                "reference",
                context,
                lineNumber,
                container);
            return;
        }

        var s3MethodMatch = NamespaceS3MethodDirectiveRegex.Match(directiveLine);
        if (s3MethodMatch.Success)
        {
            var generic = GetNamespaceDirectiveToken(
                s3MethodMatch,
                "genericBacktick",
                "genericQuoted",
                "generic");
            var @class = GetNamespaceDirectiveToken(
                s3MethodMatch,
                "classBacktick",
                "classQuoted",
                "class");
            var explicitMethod = GetNamespaceDirectiveToken(
                s3MethodMatch,
                "methodBacktick",
                "methodQuoted",
                "method");
            if (generic != null && @class != null)
            {
                var method = explicitMethod ?? ($"{generic.Value.Name}.{@class.Value.Name}", generic.Value.Index);
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    method.Name,
                    method.Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    generic.Value.Name,
                    generic.Value.Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    @class.Value.Name,
                    @class.Value.Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
            }

            return;
        }

        var useDynLibMatch = NamespaceUseDynLibDirectiveRegex.Match(directiveLine);
        if (useDynLibMatch.Success)
        {
            var package = GetNamespaceDirectiveToken(
                useDynLibMatch,
                "packageBacktick",
                "packageQuoted",
                "package");
            if (package != null)
            {
                ReferenceExtractor.AddReference(
                    references,
                    seen,
                    fileId,
                    package.Value.Name,
                    package.Value.Index,
                    "reference",
                    context,
                    lineNumber,
                    container);
            }

            return;
        }

        var exportMatch = NamespaceExportDirectiveRegex.Match(directiveLine);
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

    public static void EmitBacktickCallReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        HashSet<string>? definitionNames)
    {
        foreach (Match match in BacktickCallRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var name = nameGroup.Value;
            if (definitionNames != null && definitionNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameGroup.Index,
                "call",
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitInfixOperatorCallReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        HashSet<string>? definitionNames)
    {
        foreach (Match match in InfixOperatorCallRegex.Matches(preparedLine))
        {
            var nameGroup = match.Groups["name"];
            var name = nameGroup.Value;
            if (definitionNames != null && definitionNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                nameGroup.Index,
                "call",
                context,
                lineNumber,
                container);
        }
    }

    public static void EmitSourceFileReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        if (!SourceFileReferenceStartRegex.IsMatch(preparedLine))
            return;

        var line = StripRNamespaceDirectiveComment(originalLine);
        var match = SourceFileReferenceRegex.Match(line);
        if (!match.Success)
            return;

        var path = match.Groups["path"];
        ReferenceExtractor.AddReference(
            references,
            seen,
            fileId,
            path.Value,
            path.Index,
            "reference",
            context,
            lineNumber,
            container);
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

    private static (string Name, int Index)? GetNamespaceDirectiveToken(Match match, params string[] groupNames)
    {
        foreach (var groupName in groupNames)
        {
            var group = match.Groups[groupName];
            if (group.Success)
                return (group.Value, group.Index);
        }

        return null;
    }

    private static string StripRNamespaceDirectiveComment(string line)
    {
        var inBacktickIdentifier = false;
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quote != '\0')
            {
                if (ch == '\\' && i + 1 < line.Length)
                {
                    i++;
                    continue;
                }

                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (inBacktickIdentifier)
            {
                if (ch == '\\' && i + 1 < line.Length)
                {
                    i++;
                    continue;
                }

                if (ch == '`')
                    inBacktickIdentifier = false;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '`')
            {
                inBacktickIdentifier = true;
                continue;
            }

            if (ch == '#')
                return line[..i];
        }

        return line;
    }
}
