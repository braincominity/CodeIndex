using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class PythonReferenceExtractor
{
    // Bare Python decorators like `@staticmethod` or `@pytest.fixture` are reference sites even
    // without trailing parentheses. Keep them distinct from `call` rows so the graph can tell
    // decoration apart from invocation.
    // `@staticmethod` や `@pytest.fixture` のような Python の bare decorator を記録する。
    private static readonly Regex DecoratorRegex = new(
        @"^\s*@(?<name>[_\p{L}]\w*(?:\.[_\p{L}]\w*)*)\s*(?:#.*)?$",
        RegexOptions.Compiled);
    private static readonly Regex DecoratorCallRegex = new(
        @"^\s*@(?<name>[_\p{L}]\w*(?:\.[_\p{L}]\w*)*)\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex BareRaiseTypeRegex = new(
        @"^\s*raise\s+(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)(?:\s+from\s+[_\p{L}]\w*)?\s*(?:#.*)?$",
        RegexOptions.Compiled);
    private static readonly Regex ExceptTypeRegex = new(
        @"^\s*except\s+(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)\s*(?:as\s+\w+)?\s*:",
        RegexOptions.Compiled);
    private static readonly Regex ExceptTupleTypeRegex = new(
        @"^\s*except\s*\((?<types>[^)]*)\)\s*(?:as\s+\w+)?\s*:",
        RegexOptions.Compiled);
    private static readonly Regex TypeNameRegex = new(
        @"(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)",
        RegexOptions.Compiled);
    private static readonly Regex IsInstanceTypeRegex = new(
        @"\bisinstance\s*\(\s*[^,\n]+,\s*(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex IsInstanceTupleTypeRegex = new(
        @"\bisinstance\s*\(\s*[^,\n]+,\s*\((?<types>[^)]*)\)\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex IsSubclassTypeRegex = new(
        @"\bissubclass\s*\(\s*[^,\n]+,\s*(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex IsSubclassTupleTypeRegex = new(
        @"\bissubclass\s*\(\s*[^,\n]+,\s*\((?<types>[^)]*)\)\s*\)",
        RegexOptions.Compiled);
    private static readonly Regex CastTypeRegex = new(
        @"(?<!\.)\bcast\s*\(\s*(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)\s*,",
        RegexOptions.Compiled);
    private static readonly Regex QualifiedCastTypeRegex = new(
        @"\b(?:typing|typing_extensions)\.cast\s*\(\s*(?<name>(?:[_\p{L}]\w*\.)*[_\p{Lu}]\w*)\s*,",
        RegexOptions.Compiled);

    public static void EmitDecoratorReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        HashSet<string>? definitionNames,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in DecoratorCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;
            if (definitionNames != null && definitionNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, match, "decorator", context, lineNumber, container);
        }

        foreach (Match match in DecoratorRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;
            if (definitionNames != null && definitionNames.Contains(name))
                continue;

            ReferenceExtractor.AddReference(references, seen, fileId, match, "decorator", context, lineNumber, container);
        }
    }

    public static void EmitRaiseReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in BareRaiseTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }
    }

    public static void EmitExceptReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in ExceptTupleTypeRegex.Matches(preparedLine))
        {
            var typesGroup = match.Groups["types"];
            foreach (Match typeMatch in TypeNameRegex.Matches(typesGroup.Value))
            {
                var name = typeMatch.Groups["name"].Value;
                if (isIgnoredName(name))
                    continue;

                ReferenceExtractor.AddTypeReferenceSegments(
                    references,
                    seen,
                    fileId,
                    name,
                    typesGroup.Index + typeMatch.Groups["name"].Index,
                    context,
                    lineNumber,
                    container,
                    "python");
            }
        }

        foreach (Match match in ExceptTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }
    }

    public static void EmitIsInstanceReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in IsInstanceTupleTypeRegex.Matches(preparedLine))
        {
            var typesGroup = match.Groups["types"];
            foreach (Match typeMatch in TypeNameRegex.Matches(typesGroup.Value))
            {
                var name = typeMatch.Groups["name"].Value;
                if (isIgnoredName(name))
                    continue;

                ReferenceExtractor.AddTypeReferenceSegments(
                    references,
                    seen,
                    fileId,
                    name,
                    typesGroup.Index + typeMatch.Groups["name"].Index,
                    context,
                    lineNumber,
                    container,
                    "python");
            }
        }

        foreach (Match match in IsInstanceTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }
    }

    public static void EmitIsSubclassReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in IsSubclassTupleTypeRegex.Matches(preparedLine))
        {
            var typesGroup = match.Groups["types"];
            foreach (Match typeMatch in TypeNameRegex.Matches(typesGroup.Value))
            {
                var name = typeMatch.Groups["name"].Value;
                if (isIgnoredName(name))
                    continue;

                ReferenceExtractor.AddTypeReferenceSegments(
                    references,
                    seen,
                    fileId,
                    name,
                    typesGroup.Index + typeMatch.Groups["name"].Index,
                    context,
                    lineNumber,
                    container,
                    "python");
            }
        }

        foreach (Match match in IsSubclassTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }
    }

    public static void EmitCastReferences(
        string preparedLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container,
        Func<string, bool> isIgnoredName)
    {
        foreach (Match match in QualifiedCastTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }

        foreach (Match match in CastTypeRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (isIgnoredName(name))
                continue;

            ReferenceExtractor.AddTypeReferenceSegments(
                references,
                seen,
                fileId,
                name,
                match.Groups["name"].Index,
                context,
                lineNumber,
                container,
                "python");
        }
    }
}
