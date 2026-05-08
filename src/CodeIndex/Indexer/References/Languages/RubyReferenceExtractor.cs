using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class RubyReferenceExtractor
{
    private static readonly Regex CommandCallRegex = new(
        @"(?<![\w$@])(?<name>[A-Za-z_]\w*[?!]?)\s+(?![=<>!~+\-*/%&|^]|do\b|end\b|then\b|\()(?:[:'""\w])",
        RegexOptions.Compiled);

    private static readonly Regex BlockCallRegex = new(
        @"(?<![\w$@])(?<name>[A-Za-z_]\w*[?!]?)(?:\s*\([^\)\r\n]*\))?\s*(?:\{|do\b)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> CommandTargetReferenceNames = new(StringComparer.Ordinal)
    {
        "include", "extend", "prepend", "require", "require_relative", "raise", "attr", "attr_accessor", "attr_reader", "attr_writer",
        "define_method", "before_action", "after_action", "around_action", "helper_method",
        "has_many", "has_one", "belongs_to", "scope", "delegate", "validates",
    };

    private static readonly HashSet<string> CommandTargetSingleTokenNames = new(StringComparer.Ordinal)
    {
        "require", "require_relative", "raise", "define_method",
    };

    private static readonly Regex CommandTargetTokenRegex = new(
        @"(?<![\w$@])(?<token>:(?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|[A-Za-z_]\w*[?!]?)|[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')",
        RegexOptions.Compiled);

    public static void EmitAdditionalCallReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForCall,
        HashSet<int> matchedCallIndices,
        Action<string, int> addCallLikeReference)
    {
        foreach (Match match in CommandCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            matchedCallIndices.Add(callIndex);
            addCallLikeReference(name, callIndex);
            EmitCommandTargetReferences(
                name,
                callIndex,
                originalLine,
                references,
                seen,
                fileId,
                context,
                lineNumber,
                resolveContainerForCall);
        }

        foreach (Match match in BlockCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }
    }

    public static void EmitCommandTargetReferences(
        string name,
        int callIndex,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForCall)
    {
        if (!CommandTargetReferenceNames.Contains(name))
            return;

        var argsStart = callIndex + name.Length;
        while (argsStart < originalLine.Length && char.IsWhiteSpace(originalLine[argsStart]))
            argsStart++;

        if (argsStart < originalLine.Length && originalLine[argsStart] == '(')
            argsStart++;

        while (argsStart < originalLine.Length && char.IsWhiteSpace(originalLine[argsStart]))
            argsStart++;

        if (argsStart >= originalLine.Length)
            return;

        var tail = originalLine[argsStart..];
        var commentIndex = tail.IndexOf('#');
        if (commentIndex >= 0)
            tail = tail[..commentIndex];

        var matchedAny = false;
        foreach (Match match in CommandTargetTokenRegex.Matches(tail))
        {
            var rawToken = match.Groups["token"].Value;
            if (rawToken.Length == 0)
                continue;

            if (string.Equals(rawToken, "do", StringComparison.Ordinal)
                || string.Equals(rawToken, "end", StringComparison.Ordinal)
                || string.Equals(rawToken, "then", StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(name, "raise", StringComparison.Ordinal))
            {
                if (rawToken[0] == ':' || rawToken[0] == '\'' || rawToken[0] == '"')
                    return;
                if (!IsIdentifierStart(rawToken[0]))
                    return;
            }
            else if (CommandTargetSingleTokenNames.Contains(name) && matchedAny)
            {
                break;
            }
            else if (rawToken[0] == '\'' || rawToken[0] == '"')
            {
                if (!string.Equals(name, "require", StringComparison.Ordinal)
                    && !string.Equals(name, "require_relative", StringComparison.Ordinal)
                    && !string.Equals(name, "define_method", StringComparison.Ordinal))
                {
                    continue;
                }
            }

            var token = NormalizeCommandTargetToken(rawToken);
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var tokenIndex = argsStart + match.Groups["token"].Index;
            var targetContainer = resolveContainerForCall(tokenIndex);
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                token,
                tokenIndex,
                "reference",
                context,
                lineNumber,
                targetContainer);
            matchedAny = true;

            if (CommandTargetSingleTokenNames.Contains(name))
                break;
        }
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static string NormalizeCommandTargetToken(string token)
    {
        if (token.Length == 0)
            return token;

        if (token[0] == ':')
        {
            token = token[1..];
            if (token.Length >= 2
                && ((token[0] == '\'' && token[^1] == '\'')
                    || (token[0] == '"' && token[^1] == '"')))
            {
                token = token[1..^1];
            }
        }
        else if (token.Length >= 2
            && ((token[0] == '\'' && token[^1] == '\'')
                || (token[0] == '"' && token[^1] == '"')))
        {
            token = token[1..^1];
        }

        return token;
    }
}
