using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts lightweight symbol references such as call sites.
/// 軽量なシンボル参照（呼び出し箇所など）を抽出する。
/// </summary>
public static class ReferenceExtractor
{
    private static readonly HashSet<string> SupportedLanguages =
    [
        "python", "javascript", "typescript", "csharp", "go", "rust",
        "java", "kotlin", "ruby", "c", "cpp", "php", "swift",
        "dart", "scala", "elixir"
    ];

    private static readonly HashSet<string> IgnoredCallNames = new(StringComparer.Ordinal)
    {
        // Control flow / 制御フロー
        "if", "else", "for", "foreach", "while", "switch", "catch", "lock", "do", "try", "when",
        // Keywords that look like calls / 呼び出しに見えるキーワード
        "sizeof", "typeof", "return", "throw", "nameof", "await", "using", "new",
        // Type/member keywords / 型・メンバーキーワード
        "class", "struct", "record", "interface", "enum", "delegate", "event", "namespace",
        "def", "function", "func",
        // C# contextual keywords and common false positives / C# 文脈キーワードとよくある偽陽性
        "is", "as", "in", "var", "base", "this", "value", "get", "set", "init", "where",
        "from", "select", "orderby", "group", "into", "join", "let", "on", "equals",
        "async", "yield", "checked", "unchecked", "default", "stackalloc", "fixed",
        // Java contextual keywords / Java 文脈キーワード
        "instanceof", "super", "assert", "throws", "extends", "implements", "synchronized",
        // Other languages / 他言語
        "print", "require", "import", "include", "raise", "lambda",
    };

    private static readonly Regex StringLiteralRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`",
        RegexOptions.Compiled);
    private static readonly Regex InlineBlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled);
    private static readonly Regex ConstructorCallRegex = new(@"\bnew\s+(?<name>[A-Za-z_]\w*)(?:<[^>\n]+>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex CallRegex = new(@"(?<![\w$])(?<name>[A-Za-z_]\w*)(?:<[^>\n]+>)?\s*\(", RegexOptions.Compiled);
    // C# event subscription/unsubscription: Click += OnClick — both LHS and RHS must be PascalCase identifiers
    // C# イベント購読・解除: Click += OnClick — LHS と RHS の両方が PascalCase 識別子のみ
    private static readonly Regex EventSubscriptionRegex = new(@"(?<name>[A-Z]\w*)\s*[+-]=\s*(?:new\s+)?[A-Z]\w*", RegexOptions.Compiled);

    public static IReadOnlyCollection<string> GetSupportedLanguages() => SupportedLanguages;

    public static bool SupportsLanguage(string? lang) =>
        lang != null && SupportedLanguages.Contains(lang);

    /// <summary>
    /// Build a human-readable reason explaining graph-support status for the given language.
    /// Returns null when neither language nor support status is known.
    /// 指定言語の graph 対応状況を人間向けに説明する文字列を返す。言語も対応状況も不明なら null。
    /// </summary>
    public static string? BuildGraphSupportReason(string? lang, bool? graphSupported)
    {
        if (lang == null || graphSupported == null)
            return null;

        if (graphSupported.Value)
            return $"Call-graph extraction is indexed for '{lang}'.";

        return $"Call-graph extraction is not indexed for '{lang}'. Use search, definition, excerpt, or files instead.";
    }

    /// <summary>
    /// Extract indexed references for supported languages.
    /// 対応言語向けにインデックス化する参照を抽出する。
    /// </summary>
    public static List<ReferenceRecord> Extract(long fileId, string? lang, string content, IReadOnlyList<SymbolRecord> symbols)
    {
        if (!SupportsLanguage(lang))
            return [];

        var language = lang!;

        var lines = content.Split('\n');
        var definitionNamesByLine = symbols
            .GroupBy(symbol => symbol.Line)
            .ToDictionary(group => group.Key, group => group.Select(symbol => symbol.Name).ToHashSet(StringComparer.Ordinal));
        var containerCandidates = symbols
            .Where(symbol => symbol.BodyStartLine != null && symbol.BodyEndLine != null &&
                             (symbol.Kind == "function" || symbol.Kind == "class" || symbol.Kind == "namespace"))
            .OrderBy(symbol => (symbol.BodyEndLine ?? symbol.EndLine) - (symbol.BodyStartLine ?? symbol.StartLine))
            .ToList();

        var references = new List<ReferenceRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var originalLine = lines[i];
            var preparedLine = PrepareLine(language, originalLine);
            if (string.IsNullOrWhiteSpace(preparedLine))
                continue;

            var context = originalLine.Trim();
            if (context.Length == 0)
                continue;

            var definitionNames = definitionNamesByLine.TryGetValue(lineNumber, out var namesOnLine)
                ? namesOnLine
                : null;
            var container = FindInnermostContainer(containerCandidates, lineNumber);

            foreach (Match match in ConstructorCallRegex.Matches(preparedLine))
            {
                AddReference(references, seen, fileId, match, "instantiate", context, lineNumber, container);
            }

            // Event subscription/unsubscription (C#) / イベント購読・解除 (C#)
            if (language is "csharp")
            {
                foreach (Match match in EventSubscriptionRegex.Matches(preparedLine))
                    AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, container);
            }

            foreach (Match match in CallRegex.Matches(preparedLine))
            {
                var name = match.Groups["name"].Value;
                if (IgnoredCallNames.Contains(name))
                    continue;
                if (definitionNames != null && definitionNames.Contains(name))
                    continue;

                AddReference(references, seen, fileId, match, "call", context, lineNumber, container);
            }
        }

        return references;
    }

    private static void AddReference(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        Match match,
        string referenceKind,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var name = match.Groups["name"].Value;
        var column = match.Groups["name"].Index + 1;
        var dedupeKey = $"{lineNumber}:{column}:{referenceKind}:{name}";
        if (!seen.Add(dedupeKey))
            return;

        references.Add(new ReferenceRecord
        {
            FileId = fileId,
            SymbolName = name,
            ReferenceKind = referenceKind,
            Line = lineNumber,
            Column = column,
            Context = context,
            ContainerKind = container?.Kind,
            ContainerName = container?.Name,
        });
    }

    private static SymbolRecord? FindInnermostContainer(IReadOnlyList<SymbolRecord> candidates, int lineNumber)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.BodyStartLine!.Value <= lineNumber && candidate.BodyEndLine!.Value >= lineNumber)
                return candidate;
        }

        return null;
    }

    private static string PrepareLine(string lang, string line)
    {
        var result = StringLiteralRegex.Replace(line, "\"\"");
        result = InlineBlockCommentRegex.Replace(result, " ");

        if (UsesHashComments(lang))
        {
            var hashIndex = result.IndexOf('#');
            if (hashIndex >= 0)
                result = result[..hashIndex];
        }

        if (UsesSlashComments(lang))
        {
            var slashIndex = result.IndexOf("//", StringComparison.Ordinal);
            if (slashIndex >= 0)
                result = result[..slashIndex];
        }

        return result;
    }

    private static bool UsesHashComments(string lang) =>
        lang is "python" or "ruby" or "php" or "elixir";

    private static bool UsesSlashComments(string lang) =>
        lang is not "python" and not "ruby";
}
