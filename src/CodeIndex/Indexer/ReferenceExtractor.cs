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
        "dart", "scala", "elixir", "lua", "vb", "fsharp", "sql",
        "r", "powershell", "haskell",
        "gradle", "terraform", "protobuf", "dockerfile", "makefile",
        "zig", "css"
    ];

    private static readonly HashSet<string> SharedIgnoredCallNames = new(StringComparer.Ordinal)
    {
        // Control flow / 制御フロー
        "if", "else", "for", "foreach", "while", "switch", "catch", "lock", "do", "try", "when",
        // Keywords that look like calls / 呼び出しに見えるキーワード
        "sizeof", "typeof", "return", "throw", "nameof", "await", "using", "new",
        // Type/member keywords / 型・メンバーキーワード
        "class", "struct", "record", "interface", "enum", "delegate", "event", "namespace",
        "def", "function", "func",
    };
    private static readonly HashSet<string> SharedIgnoredCallNamesCaseInsensitive = new(SharedIgnoredCallNames, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> LanguageSpecificIgnoredCallNames = new(StringComparer.Ordinal)
    {
        // C# contextual keywords and common false positives / C# 文脈キーワードとよくある偽陽性
        ["csharp"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "is", "as", "in", "var", "base", "this", "value", "get", "set", "init", "where",
            "from", "select", "orderby", "group", "into", "join", "let", "on", "equals",
            "async", "yield", "checked", "unchecked", "default", "stackalloc", "fixed",
        },
        // Java contextual keywords / Java 文脈キーワード
        ["java"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "instanceof", "super", "assert", "throws", "extends", "implements", "synchronized",
        },
        // JavaScript / TypeScript contextual keywords / JavaScript / TypeScript 文脈キーワード
        ["javascript"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "import", "super", "yield",
        },
        ["typescript"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "import", "super", "yield",
        },
        // Python contextual keywords / Python の文脈キーワード
        ["python"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "raise", "yield", "from",
        },
        // Ruby contextual keywords / Ruby の文脈キーワード
        ["ruby"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "raise", "yield", "super", "include",
        },
        // F# contextual keywords / F# 文脈キーワード
        ["fsharp"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "match", "with", "member", "override", "abstract", "mutable", "rec", "fun", "open",
            "module", "type", "of", "then", "elif", "done", "begin", "end",
        },
        // PHP include/require constructs / PHP の include/require 構文
        ["php"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "require", "require_once", "include", "include_once",
            "echo", "print", "exit", "die", "eval", "unset", "isset", "empty",
        },
        // SQL keywords (uppercase only to avoid collisions with other languages)
        // SQL キーワード（他言語との衝突を避けるため大文字のみ）
        ["sql"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "JOIN", "INTO",
            "VALUES", "ORDER", "GROUP", "HAVING", "LIMIT", "OFFSET", "UNION",
            "EXISTS", "BETWEEN", "LIKE", "CASE", "WHEN", "THEN", "ELSE",
            "AS", "ON", "AND", "OR", "NOT", "NULL", "IN", "IS",
            "CREATE", "ALTER", "DROP", "TABLE", "INDEX", "VIEW", "IF",
        },
        // R keywords / R キーワード
        ["r"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "library", "cat", "paste", "paste0", "sprintf", "stop", "warning", "message",
            "invisible", "tryCatch", "withCallingHandlers", "next", "break", "repeat",
        },
        // PowerShell keywords / PowerShell キーワード
        ["powershell"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "param", "begin", "process", "Write", "trap", "finally", "elseif",
        },
        // Haskell keywords / Haskell キーワード
        ["haskell"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "data", "newtype", "instance", "deriving", "infixl", "infixr", "infix",
            "qualified", "hiding", "forall", "Just", "Nothing", "Left", "Right", "True", "False",
            "putStrLn", "putStr", "print",
        },
        // Gradle/Groovy keywords / Gradle/Groovy キーワード
        ["gradle"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "apply", "plugins", "dependencies", "repositories", "allprojects", "subprojects",
            "task", "buildscript", "ext", "group", "version", "description",
        },
        // Terraform keywords / Terraform キーワード
        ["terraform"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "resource", "data", "variable", "output", "locals", "module", "provider",
            "terraform", "required_providers", "backend",
        },
        // Makefile keywords / Makefile キーワード
        ["makefile"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "all", "clean", "install", "build", "run", "help",
        },
    };

    private static readonly Regex StringLiteralRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`",
        RegexOptions.Compiled);
    private static readonly Regex InlineBlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled);
    private static readonly Regex CallRegex = new(@"(?<![\w$])(?<name>[A-Za-z_]\w*)(?:<[^>\n]+>)?\s*\(", RegexOptions.Compiled);
    // C# event subscription/unsubscription: Click += OnClick — both LHS and RHS must be PascalCase identifiers
    // C# イベント購読・解除: Click += OnClick — LHS と RHS の両方が PascalCase 識別子のみ
    private static readonly Regex EventSubscriptionRegex = new(@"(?<name>[A-Z]\w*)\s*[+-]=\s*(?:new\s+)?[A-Z]\w*", RegexOptions.Compiled);

    // C# targeted-attribute prefixes (e.g. [return: NotNull], [assembly: InternalsVisibleTo(...)]).
    // C# のターゲット付き属性の prefix。
    private static readonly HashSet<string> CSharpAttributeTargets = new(StringComparer.Ordinal)
    {
        "return", "param", "method", "type", "field", "property", "event", "assembly", "module",
    };

    // Languages whose `@Decorator(args)` / `@Annotation(args)` syntax should produce
    // `annotation` reference rows rather than `call` rows (issue #293).
    // `@Decorator(args)` / `@Annotation(args)` 構文を `call` ではなく `annotation` として
    // 記録すべき言語 (issue #293)。
    private static readonly HashSet<string> AnnotationLanguages = new(StringComparer.Ordinal)
    {
        "java", "kotlin", "scala", "typescript",
    };

    // Kotlin use-site target prefixes for annotations (e.g. `@field:Deprecated("msg")`,
    // `@file:JvmName("Foo")`). Keep aligned with the Kotlin language spec use-site targets.
    // Kotlin の use-site target 付き注釈用の接頭辞。
    private static readonly HashSet<string> KotlinAnnotationTargets = new(StringComparer.Ordinal)
    {
        "field", "get", "set", "param", "setparam", "property", "receiver", "file", "delegate", "all",
    };

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
        var structuralLines = StructuralLineMasker.MaskLines(language, lines);
        var definitionNamesByLine = symbols
            .GroupBy(symbol => symbol.Line)
            .ToDictionary(group => group.Key, group => group.Select(symbol => symbol.Name).ToHashSet(StringComparer.Ordinal));
        // Include 'property' so expression-bodied and block-bodied property accessors
        // attribute their calls to the property rather than falling through to the
        // enclosing class (see issue #233).
        // 式本体・ブロック本体のプロパティアクセサ内の呼び出しを、外側のクラスではなく
        // プロパティ自身に帰属させる (issue #233 参照)。
        var containerCandidates = symbols
            .Where(symbol => symbol.BodyStartLine != null && symbol.BodyEndLine != null &&
                             (symbol.Kind == "function" || symbol.Kind == "class"
                              || symbol.Kind == "namespace" || symbol.Kind == "property"))
            .OrderBy(symbol => (symbol.BodyEndLine ?? symbol.EndLine) - (symbol.BodyStartLine ?? symbol.StartLine))
            .ToList();

        var references = new List<ReferenceRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var originalLine = lines[i];
            var preparedLine = PrepareLine(language, structuralLines[i]);
            if (string.IsNullOrWhiteSpace(preparedLine))
                continue;

            var context = originalLine.Trim();
            if (context.Length == 0)
                continue;

            var definitionNames = definitionNamesByLine.TryGetValue(lineNumber, out var namesOnLine)
                ? namesOnLine
                : null;
            var container = FindInnermostContainer(containerCandidates, lineNumber);

            // Event subscription/unsubscription (C#) / イベント購読・解除 (C#)
            if (language is "csharp")
            {
                foreach (Match match in EventSubscriptionRegex.Matches(preparedLine))
                    AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, container);
            }

            foreach (Match match in CallRegex.Matches(preparedLine))
            {
                var name = match.Groups["name"].Value;
                var nameIndex = match.Groups["name"].Index;
                if (IsConstructorCallName(language, preparedLine, nameIndex))
                {
                    AddReference(references, seen, fileId, match, "instantiate", context, lineNumber, container);
                    continue;
                }
                if (IsIgnoredCallName(language, name))
                    continue;
                if (definitionNames != null && definitionNames.Contains(name))
                    continue;

                // issue #293: reclassify C# attribute / Java/Kotlin/Scala/TypeScript annotation
                // usages with arguments so they do not pollute the call-graph as phantom `call` rows.
                // issue #293: 引数付きの C# attribute と Java/Kotlin/Scala/TypeScript annotation 使用を
                // `call` ではなく専用の種別に分類し、call-graph の phantom エッジを防ぐ。
                var metadataKind = TryClassifyMetadataReference(language, preparedLine, nameIndex);
                AddReference(references, seen, fileId, match, metadataKind ?? "call", context, lineNumber, container);
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

        // Lua, SQL, Haskell use -- for line comments / Lua、SQL、Haskell は -- を行コメントに使う
        if (UsesDashDashComments(lang))
        {
            var dashCommentIndex = result.IndexOf("--", StringComparison.Ordinal);
            if (dashCommentIndex >= 0)
                result = result[..dashCommentIndex];
        }

        // VB.NET uses ' for line comments / VB.NET は ' を行コメントに使う
        if (lang is "vb")
        {
            var vbCommentIndex = result.IndexOf('\'');
            if (vbCommentIndex >= 0)
                result = result[..vbCommentIndex];
        }

        return result;
    }

    private static bool IsIgnoredCallName(string language, string name)
    {
        if (language == "php")
        {
            if (SharedIgnoredCallNamesCaseInsensitive.Contains(name))
                return true;
        }
        else if (SharedIgnoredCallNames.Contains(name))
        {
            return true;
        }

        return LanguageSpecificIgnoredCallNames.TryGetValue(language, out var languageSpecificIgnoredNames)
            && languageSpecificIgnoredNames.Contains(name);
    }

    private static bool IsConstructorCallName(string language, string preparedLine, int nameIndex)
    {
        var probe = nameIndex - 1;
        while (probe >= 0 && char.IsWhiteSpace(preparedLine[probe]))
            probe--;

        if (probe < 0)
            return false;

        while (probe >= 0)
        {
            char? separator = null;
            if (probe >= 1 && preparedLine[probe] == ':' && preparedLine[probe - 1] == ':')
            {
                separator = ':';
                probe -= 2;
            }
            else if (preparedLine[probe] is '.' or '\\')
            {
                separator = preparedLine[probe];
                probe--;
            }

            if (separator == null)
                break;

            var segmentEnd = probe;
            while (probe >= 0 && IsIdentifierChar(preparedLine[probe]))
                probe--;

            var consumedSegment = segmentEnd >= 0 && segmentEnd >= probe + 1;
            if (!consumedSegment && separator != '\\')
                return false;
        }

        while (probe >= 0 && char.IsWhiteSpace(preparedLine[probe]))
            probe--;

        if (probe < 0)
            return false;

        var tokenEnd = probe;
        while (probe >= 0 && IsIdentifierChar(preparedLine[probe]))
            probe--;

        var tokenStart = probe + 1;
        if (tokenStart > tokenEnd)
            return false;

        var token = preparedLine[tokenStart..(tokenEnd + 1)];
        return language == "php"
            ? string.Equals(token, "new", StringComparison.OrdinalIgnoreCase)
            : string.Equals(token, "new", StringComparison.Ordinal);
    }

    private static bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    /// <summary>
    /// Classify a call-looking identifier as an attribute/annotation when it appears inside
    /// a C# `[...]` attribute list or is preceded by a Java-family `@` marker. Returns null
    /// for ordinary method calls so the caller emits the default `call` reference kind.
    /// 呼び出しに見える識別子を、C# の `[...]` 属性リスト内や Java 系 `@` 付き注釈に該当する
    /// 場合に専用の reference kind へ分類する。通常の呼び出しは null を返して既定の `call` を維持する。
    /// </summary>
    private static string? TryClassifyMetadataReference(string language, string preparedLine, int nameIndex)
    {
        var probe = nameIndex - 1;
        while (probe >= 0 && char.IsWhiteSpace(preparedLine[probe]))
            probe--;
        if (probe < 0)
            return null;

        if (language == "csharp")
            return IsCSharpAttributeContext(preparedLine, probe) ? "attribute" : null;

        if (AnnotationLanguages.Contains(language))
            return IsAnnotationContext(preparedLine, probe) ? "annotation" : null;

        return null;
    }

    private static bool IsCSharpAttributeContext(string line, int probe)
    {
        // Direct form: `[Foo(...)`. 直接形 `[Foo(...)`。
        // Must verify that this `[` sits at a declaration position, not inside a C# 12
        // collection expression such as `var xs = [Make(), Make()]`.
        // この `[` が宣言位置（`var xs = [...]` のような C# 12 collection expression ではない）
        // であることを必ず確認する必要がある。
        if (line[probe] == '[')
            return IsCSharpAttributeOpenBracket(line, probe);

        // Targeted form: `[target: Foo(...)]` — peek past the `target:` prefix.
        // ターゲット指定形式 `[target: Foo(...)]` — `target:` 部分をスキップして `[` を探す。
        if (line[probe] == ':')
        {
            var j = probe - 1;
            var idEnd = j;
            while (j >= 0 && IsIdentifierChar(line[j]))
                j--;
            if (j + 1 <= idEnd)
            {
                var target = line[(j + 1)..(idEnd + 1)];
                if (CSharpAttributeTargets.Contains(target))
                {
                    var k = j;
                    while (k >= 0 && char.IsWhiteSpace(line[k]))
                        k--;
                    if (k >= 0 && line[k] == '[' && IsCSharpAttributeOpenBracket(line, k))
                        return true;
                }
            }
            return false;
        }

        // Comma-separated list: `[Foo("a"), Bar("b")]` — walk past prior attribute entries
        // (and any balanced parens they contain) until an unbalanced opening `[` appears.
        // 一行に並んだ複数属性 `[Foo("a"), Bar("b")]` では、先行する属性と対応する括弧を
        // 読み飛ばして未対応の `[` に到達した場合にのみ属性として扱う。
        if (line[probe] == ',')
            return ScanLeftForAttributeOpen(line, probe - 1);

        return false;
    }

    /// <summary>
    /// Return true when the `[` at <paramref name="bracketIndex"/> is at a C# declaration
    /// position rather than an expression position. Attribute `[` must be preceded on the
    /// same line only by whitespace, another attribute list's closing `]`, or a scope/
    /// statement boundary (`{`, `}`, `;`). Any expression-context token (identifier, digit,
    /// `=`, `,`, `(`, `:`, etc.) means this is a collection expression or indexer.
    /// `[` が C# の宣言位置にあるか（collection expression や indexer ではないか）を判定する。
    /// </summary>
    private static bool IsCSharpAttributeOpenBracket(string line, int bracketIndex)
    {
        var i = bracketIndex - 1;
        while (i >= 0 && char.IsWhiteSpace(line[i]))
            i--;
        if (i < 0)
            return true;
        var c = line[i];
        if (c == '{' || c == '}' || c == ';')
            return true;
        // A preceding `]` only indicates attribute-list chaining (`[A][B]`) when that `]`
        // actually closed an attribute section. For `arr[i][Compute()]` the preceding `]`
        // closes an indexer, so we must walk back to the matching `[` and re-check that
        // opening bracket's declaration position.
        // 直前の `]` が attribute list のチェーン (`[A][B]`) を意味するのは、その `]` が
        // 実際に attribute section を閉じていたときだけ。`arr[i][Compute()]` のように
        // indexer を閉じた `]` の場合は、対応する `[` まで戻ってそこが宣言位置かを再判定する。
        if (c == ']')
            return IsCSharpAttributeSectionClose(line, i);
        return false;
    }

    /// <summary>
    /// Walk left from a `]` to its matching `[`, skipping balanced parens, and return true
    /// when that opening bracket itself was at a C# declaration position.
    /// `]` から対応する `[` まで左方向に遡り、その開き bracket 自体が宣言位置だった場合のみ true を返す。
    /// </summary>
    private static bool IsCSharpAttributeSectionClose(string line, int closeBracketIndex)
    {
        var bracketDepth = 1;
        var parenDepth = 0;
        for (var i = closeBracketIndex - 1; i >= 0; i--)
        {
            var c = line[i];
            if (c == ')')
            {
                parenDepth++;
                continue;
            }
            if (c == '(')
            {
                if (parenDepth > 0)
                    parenDepth--;
                continue;
            }
            if (parenDepth > 0)
                continue;
            if (c == ']')
            {
                bracketDepth++;
                continue;
            }
            if (c == '[')
            {
                bracketDepth--;
                if (bracketDepth == 0)
                    return IsCSharpAttributeOpenBracket(line, i);
            }
        }
        return false;
    }

    private static bool ScanLeftForAttributeOpen(string line, int start)
    {
        var parenDepth = 0;
        for (var i = start; i >= 0; i--)
        {
            var c = line[i];
            if (c == ')')
            {
                parenDepth++;
                continue;
            }
            if (c == '(')
            {
                if (parenDepth == 0)
                    return false;
                parenDepth--;
                continue;
            }
            if (parenDepth > 0)
                continue;
            if (c == '[')
                return IsCSharpAttributeOpenBracket(line, i);
            if (c == ']' || c == ';' || c == '{' || c == '}')
                return false;
        }
        return false;
    }

    private static bool IsAnnotationContext(string line, int probe)
    {
        // `@Annotation(args)` — direct marker. 直接 `@Annotation(args)` の場合。
        if (line[probe] == '@')
            return true;

        // `@module.Annotation(args)` — walk past the dotted qualifier chain first so that
        // both `@module.Annotation` and `@field:com.example.Annotation` land the probe on
        // either `@` or the Kotlin use-site target `:`.
        // `@module.Annotation(args)` や `@field:com.example.Annotation(args)` のように修飾子が
        // 付く場合も対応するため、先にドット区切り修飾子チェーンを剥がしてから `@` または
        // Kotlin の use-site target `:` を判定する。
        while (probe >= 0 && line[probe] == '.')
        {
            probe--;
            while (probe >= 0 && IsIdentifierChar(line[probe]))
                probe--;
            while (probe >= 0 && char.IsWhiteSpace(line[probe]))
                probe--;
        }

        if (probe < 0)
            return false;

        if (line[probe] == '@')
            return true;

        // Kotlin use-site target: `@field:Deprecated("msg")` or
        // `@field:com.example.Deprecated("msg")`. After unwinding the dotted qualifier, the
        // probe lands on `:`; walk past the target identifier and confirm `@`.
        // Kotlin の use-site target `@field:Deprecated("msg")` や
        // `@field:com.example.Deprecated("msg")` では、ドット修飾子を剥がしたあと probe が `:`
        // に着地するため、target 識別子を読み飛ばして `@` を確認する。
        if (line[probe] == ':')
        {
            var j = probe - 1;
            var idEnd = j;
            while (j >= 0 && IsIdentifierChar(line[j]))
                j--;
            if (j + 1 <= idEnd)
            {
                var target = line[(j + 1)..(idEnd + 1)];
                if (KotlinAnnotationTargets.Contains(target))
                {
                    var k = j;
                    while (k >= 0 && char.IsWhiteSpace(line[k]))
                        k--;
                    if (k >= 0 && line[k] == '@')
                        return true;
                }
            }
        }

        return false;
    }

    private static bool UsesHashComments(string lang) =>
        lang is "python" or "ruby" or "php" or "elixir" or "r" or "powershell"
            or "makefile" or "terraform" or "dockerfile" or "protobuf";

    private static bool UsesSlashComments(string lang) =>
        lang is not "python" and not "ruby" and not "r" and not "haskell"
            and not "makefile" and not "terraform" and not "dockerfile"
            and not "css";

    private static bool UsesDashDashComments(string lang) =>
        lang is "lua" or "sql" or "haskell";
}
