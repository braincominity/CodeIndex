using System.Text.RegularExpressions;

namespace CodeIndex.Indexer;

internal static class ScalaReferenceExtractor
{
    private const string FunctionalIdentifierPattern = @"@?[_\p{L}\$][\w$]*";

    // Scala's `name { ... }` / `name { x => ... }` block-call form does not use
    // trailing `(`, so the shared CallRegex cannot see it.
    private static readonly Regex TrailingBlockCallRegex = new(
        $@"(?<![\w$])(?<name>{FunctionalIdentifierPattern})(?:\[[^\]\n]+\])?\s*\{{",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredBlockCallNames = new(StringComparer.Ordinal)
    {
        "match", "catch", "else", "finally",
    };

    public static void EmitTrailingBlockCallReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
    {
        foreach (Match match in TrailingBlockCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (IgnoredBlockCallNames.Contains(name))
                continue;

            addCallLikeReference(name, match.Groups["name"].Index);
        }
    }
}
