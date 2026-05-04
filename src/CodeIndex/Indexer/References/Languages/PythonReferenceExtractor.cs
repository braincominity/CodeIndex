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
}
