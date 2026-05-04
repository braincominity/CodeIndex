using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

internal static class ShellReferenceExtractor
{
    private static readonly Regex CommandCallRegex = new(
        @"(?:^\s*(?!(?:if|then|do|else|elif|while|until|time|fi)\b)|[|;&{]\s*|&&\s*|\|\|\s*|!\s+|\b(?:if|then|do|else|elif|while|until|time)\s+)(?<name>[A-Za-z_][A-Za-z0-9_-]*)(?=\s|$|[;|&}])",
        RegexOptions.Compiled);

    private static readonly Regex SourceReferenceRegex = new(
        @"(?:^\s*(?!(?:if|then|do|else|elif|while|until|time|fi)\b)|[|;&{]\s*|&&\s*|\|\|\s*|!\s+|\b(?:if|then|do|else|elif|while|until|time)\s+)(?:source|\.)\s+(?<name>(?:'[^']*'|""[^""]*""|[^;\s&#|}]+))",
        RegexOptions.Compiled);

    private static readonly Regex GlobalAliasSignatureRegex = new(
        @"^alias(?:\s+-[^\s=]+)*\s+-g\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static HashSet<string>? BuildCallableNames(string language, IReadOnlyList<SymbolRecord> symbols)
    {
        if (language != "shell")
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind is not ("function" or "alias") || string.IsNullOrWhiteSpace(symbol.Name))
                continue;

            names.Add(symbol.Name);
        }

        return names;
    }

    public static HashSet<string>? BuildGlobalAliasNames(string language, IReadOnlyList<SymbolRecord> symbols)
    {
        if (language != "shell")
            return null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "alias" || string.IsNullOrWhiteSpace(symbol.Name))
                continue;

            var signature = symbol.Signature?.TrimStart();
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            if (!GlobalAliasSignatureRegex.IsMatch(signature))
                continue;

            names.Add(symbol.Name);
        }

        return names;
    }

    public static void EmitReferences(
        string preparedLine,
        string originalLine,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        HashSet<string>? callableNames,
        HashSet<string>? globalAliasNames,
        Func<int, SymbolRecord?> resolveContainerForCall,
        Action<string, int> addCallLikeReference)
    {
        foreach (Match match in CommandCallRegex.Matches(preparedLine))
        {
            var name = match.Groups["name"].Value;
            if (callableNames == null || !callableNames.Contains(name))
                continue;

            var callIndex = match.Groups["name"].Index;
            addCallLikeReference(name, callIndex);
        }

        var shellSourceLine = StripComment(originalLine);
        foreach (Match match in SourceReferenceRegex.Matches(shellSourceLine))
        {
            var name = NormalizeSourceTargetToken(match.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var sourceIndex = match.Groups["name"].Index;
            ReferenceExtractor.AddReference(
                references,
                seen,
                fileId,
                name,
                sourceIndex,
                "reference",
                context,
                lineNumber,
                resolveContainerForCall(sourceIndex));
        }

        if (globalAliasNames == null || globalAliasNames.Count == 0)
            return;

        var trimmedPreparedLine = preparedLine.TrimStart();
        if (trimmedPreparedLine.StartsWith("alias", StringComparison.Ordinal))
            return;

        foreach (var aliasName in globalAliasNames)
        {
            var searchIndex = 0;
            while (searchIndex < preparedLine.Length)
            {
                var aliasIndex = preparedLine.IndexOf(aliasName, searchIndex, StringComparison.Ordinal);
                if (aliasIndex < 0)
                    break;

                if (!IsGlobalAliasReferenceBoundary(preparedLine, aliasIndex, aliasName.Length))
                {
                    searchIndex = aliasIndex + aliasName.Length;
                    continue;
                }

                addCallLikeReference(aliasName, aliasIndex);
                searchIndex = aliasIndex + aliasName.Length;
            }
        }
    }

    private static string NormalizeSourceTargetToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length >= 2)
        {
            var quote = trimmed[0];
            if ((quote == '\'' || quote == '"') && trimmed[^1] == quote)
                return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string StripComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"')
                {
                    inDoubleQuote = false;
                    continue;
                }

                if (ch == '\\')
                    escapeNext = true;
                continue;
            }

            if (ch == '#')
                return line[..i];

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch == '\\')
                escapeNext = true;
        }

        return line;
    }

    private static bool IsGlobalAliasReferenceBoundary(string text, int startIndex, int length)
    {
        if (startIndex < 0 || length <= 0 || startIndex + length > text.Length)
            return false;

        if (startIndex > 0 && !IsGlobalAliasBoundarySeparator(text[startIndex - 1]))
            return false;

        var endIndex = startIndex + length;
        return endIndex >= text.Length || IsGlobalAliasBoundarySeparator(text[endIndex]);
    }

    private static bool IsGlobalAliasBoundarySeparator(char ch) =>
        char.IsWhiteSpace(ch) || ch is '|' or ';' or '&' or '{' or '}' or '(' or ')' or '!';
}
