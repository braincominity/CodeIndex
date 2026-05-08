using System.Text.RegularExpressions;

namespace CodeIndex.Indexer;

internal static class FSharpReferenceExtractor
{
    private const string IdentifierPattern = @"(?:``[^`]+``|[_\p{L}][\w']*)";

    private static readonly Regex PipelineCallRegex = new(
        $@"(?<![\w$])(?:\|{{1,3}}>)\s*(?:(?:{IdentifierPattern})\s*\.\s*)*(?<name>{IdentifierPattern})\b",
        RegexOptions.Compiled);

    private static readonly Regex BackwardPipelineCallRegex = new(
        $@"(?<![\w$])(?:(?:{IdentifierPattern})\s*\.\s*)*(?<name>{IdentifierPattern})\s*<\|{{1,3}}",
        RegexOptions.Compiled);

    private static readonly Regex BackwardPipelineArgumentCallRegex = new(
        $@"<\|{{1,3}}\s*(?:(?:{IdentifierPattern})\s*\.\s*)*(?<name>{IdentifierPattern})\b
            (?=\s+(?:{IdentifierPattern}|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|\(|\[|\{{|\d))",
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex SpaceApplicationCallRegex = new(
        $@"(?:\b(?:then|do!?|else|in|return!?|yield!?)\s+|->\s+|[=(,\[\{{;]\s*|^\s*)
            (?:(?:{IdentifierPattern})\s*\.\s*)*
            (?<name>{IdentifierPattern})\b
            (?=\s+(?:{IdentifierPattern}|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|\(|\[|\{{|\d))",
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex OperatorCallRegex = new(
        @"(?<![\w$])(?<name>[!%&*+\-./:<=>?@^|~]{2,})(?![\w$])",
        RegexOptions.Compiled);

    private static readonly Regex OperatorDefinitionCallRegex = new(
        @"^\s*let\s+(?:(?:rec|mutable|inline|private|internal|public)\s+)*\((?<name>[!%&*+\-./:<=>?@^|~]{2,})\)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredOperatorCallNames = new(StringComparer.Ordinal)
    {
        "->", "<-", "..", "<|", "<||", "<|||", "|>", "||>", "|||>", "|>>", "<<", ">>", "<<<", ">>>",
        "&&", "&&&", "||", "|||", "::", "<>", "<=", ">=", "**", "@@", ":>", ":?", ":=",
    };

    public static void EmitAdditionalCallReferences(
        string preparedLine,
        Action<string, int> addCallLikeReference)
    {
        foreach (Match match in PipelineCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        foreach (Match match in BackwardPipelineCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        foreach (Match match in BackwardPipelineArgumentCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        foreach (Match match in SpaceApplicationCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        foreach (Match match in OperatorCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (IgnoredOperatorCallNames.Contains(name))
                continue;

            var definitionMatch = OperatorDefinitionCallRegex.Match(preparedLine);
            if (definitionMatch.Success
                && string.Equals(definitionMatch.Groups["name"].Value, name, StringComparison.Ordinal)
                && match.Groups["name"].Index == definitionMatch.Groups["name"].Index)
            {
                continue;
            }

            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }
    }

    public static bool IsOperatorCallName(string name)
    {
        if (name.Length < 2)
            return false;

        foreach (var ch in name)
        {
            if ("!%&*+-./:<=>?@^|~".IndexOf(ch) < 0)
                return false;
        }

        return true;
    }
}
