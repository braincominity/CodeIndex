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
    // C# compile-time type/member references: `nameof(X.Y)`, `typeof(T)`, `sizeof(T)`, `default(T)`.
    // Keywords are in SharedIgnoredCallNames so CallRegex skips them, but their arguments have no
    // trailing `(` and therefore slip through. Captured here as a dedicated "type_reference" kind
    // so callers/callees (which exclude type_reference by default) stay unaffected while
    // references and impact see the edge. See issue #253.
    // The regex only locates the keyword and opening `(`; the argument itself is walked by
    // ExtractCSharpTypeKeywordSegments so generic `<...>`, array `[...]`, and `global::` qualifiers
    // are handled without truncating the real type path.
    // C# の nameof/typeof/sizeof/default は、キーワード自体が SharedIgnoredCallNames にあるため
    // CallRegex では読み飛ばされ、引数の識別子も末尾に `(` が無いため通常経路では捕捉できない。
    // ここで type_reference として拾い、callers/callees（既定で type_reference を除外）に影響せず
    // references と impact だけに edge を届ける。issue #253 参照。
    // 正規表現はキーワードと `(` の位置だけを捕捉し、引数本体の走査は ExtractCSharpTypeKeywordSegments
    // に任せる。これにより generic `<...>`、配列 `[...]`、`global::` 等を途中で切らない。
    private static readonly Regex CSharpTypeKeywordIntroRegex = new(
        @"(?<![\w$])(?<keyword>nameof|typeof|sizeof|default)\s*\(",
        RegexOptions.Compiled);
    // Java compile-time type literal: `T.class`, `T[].class`, `outer.Inner.class` etc.
    // `.class` itself is a language keyword, but the type chain in front of it is a genuine
    // reference. Emit each dot-segment as `type_reference`. See issue #253.
    // Java の `T.class` は型リテラル。`.class` 自体はキーワードだが、前置の型チェーンは
    // 正当な参照。各 dot-segment を type_reference として拾う。issue #253 参照。
    private static readonly Regex JavaDotClassArgRegex = new(
        @"(?<![\w$.])(?<arg>[A-Za-z_][\w.]*)\s*(?:\[\s*\])*\s*\.class\b",
        RegexOptions.Compiled);

    // Java primitive type names that can precede `.class` (e.g. `int.class`, `void.class`).
    // Skipped from reference rows because they are language-level keywords, not indexed types.
    // `int.class` 等に現れる Java のプリミティブ型。インデックス対象の型ではないため除外する。
    private static readonly HashSet<string> JavaPrimitiveTypeNames = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "boolean", "byte", "char", "float", "double", "void",
    };

    // C# predefined type aliases / void / dynamic / var. They resolve to BCL primitives that are
    // not indexed as user-defined symbols, so emitting them as `type_reference` just pollutes
    // references/inspect output without ever linking to a real definition.
    // C# の built-in 型 alias / void / dynamic / var。ユーザー定義シンボルに解決しないため
    // type_reference として残すとノイズにしかならない。issue #253 のレビュー指摘により除外。
    private static readonly HashSet<string> CSharpBuiltInTypeNames = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "nint", "nuint", "char", "float", "double", "decimal",
        "string", "object", "void", "dynamic", "var",
    };

    public static IReadOnlyCollection<string> GetSupportedLanguages() => SupportedLanguages;

    public static bool SupportsLanguage(string? lang) =>
        lang != null && SupportedLanguages.Contains(lang);

    public static bool? SupportsSymbolGraph(string? lang, string? kind, string? containerKind)
    {
        if (lang == null)
            return null;

        if (!SupportsLanguage(lang))
            return false;

        return IsUnsupportedCSharpEnumMemberSymbol(lang, kind, containerKind)
            ? false
            : true;
    }

    public static string? GetUnsupportedSymbolKind(string? lang, string? kind, string? containerKind)
    {
        return IsUnsupportedCSharpEnumMemberSymbol(lang, kind, containerKind)
            ? "enum_member"
            : null;
    }

    /// <summary>
    /// Build a human-readable reason explaining graph-support status for the given language.
    /// Returns null when neither language nor support status is known.
    /// 指定言語の graph 対応状況を人間向けに説明する文字列を返す。言語も対応状況も不明なら null。
    /// </summary>
    public static string? BuildGraphSupportReason(string? lang, bool? graphSupported, string? kind = null, string? containerKind = null)
    {
        if (lang == null || graphSupported == null)
            return null;

        if (IsUnsupportedCSharpEnumMemberSymbol(lang, kind, containerKind))
            return "Call-graph extraction is indexed for 'csharp', but enum-member access edges are not indexed yet.";

        if (graphSupported.Value)
            return $"Call-graph extraction is indexed for '{lang}'.";

        return $"Call-graph extraction is not indexed for '{lang}'. Use search, definition, excerpt, or files instead.";
    }

    public static string? BuildGraphSupportReasonWithUnsupportedEnumMemberGap(string? lang, bool? graphSupported, bool hasUnsupportedEnumMember, bool hasSupportedGraphDefinition)
    {
        var baseReason = BuildGraphSupportReason(lang, graphSupported);
        if (!hasUnsupportedEnumMember)
            return baseReason;

        var enumGapReason = hasSupportedGraphDefinition
            ? "Exact results also include C# enum members whose access edges are not indexed yet."
            : BuildGraphSupportReason("csharp", true, "enum", "enum");

        if (!hasSupportedGraphDefinition)
            return enumGapReason;

        if (string.IsNullOrWhiteSpace(baseReason))
            return enumGapReason;
        if (string.IsNullOrWhiteSpace(enumGapReason) || string.Equals(baseReason, enumGapReason, StringComparison.Ordinal))
            return baseReason;

        return $"{baseReason} {enumGapReason}";
    }

    private static bool IsUnsupportedCSharpEnumMemberSymbol(string? lang, string? kind, string? containerKind)
    {
        return string.Equals(lang, "csharp", StringComparison.Ordinal)
            && string.Equals(kind, "enum", StringComparison.Ordinal)
            && string.Equals(containerKind, "enum", StringComparison.Ordinal);
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

            // Compile-time type/member references that CallRegex cannot see because the
            // argument has no trailing `(` of its own. See issue #253.
            // 末尾の `(` を持たず CallRegex では取れないコンパイル時の型/メンバ参照。issue #253 参照。
            if (language is "csharp")
            {
                foreach (Match match in CSharpTypeKeywordIntroRegex.Matches(preparedLine))
                {
                    int parenIndex = match.Index + match.Length - 1; // position of '(' / '(' の位置
                    ExtractCSharpTypeKeywordSegments(
                        references, seen, fileId, preparedLine, parenIndex + 1,
                        context, lineNumber, container, language);
                }
            }
            else if (language is "java")
            {
                foreach (Match match in JavaDotClassArgRegex.Matches(preparedLine))
                {
                    var argGroup = match.Groups["arg"];
                    AddTypeReferenceSegments(references, seen, fileId, argGroup.Value, argGroup.Index, context, lineNumber, container, language);
                }
            }

            foreach (Match match in CallRegex.Matches(preparedLine))
            {
                var name = match.Groups["name"].Value;
                if (IsConstructorCallName(language, preparedLine, match.Groups["name"].Index))
                {
                    AddReference(references, seen, fileId, match, "instantiate", context, lineNumber, container);
                    continue;
                }
                if (IsIgnoredCallName(language, name))
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

    /// <summary>
    /// Emit one `type_reference` row per dot-segment of a captured argument. Columns are
    /// computed relative to the original line so tooling can jump to the exact identifier.
    /// 捕捉した引数の dot-segment ごとに `type_reference` 行を発行する。列位置は元の行基準で計算する。
    /// </summary>
    private static void AddTypeReferenceSegments(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string arg,
        int argStartInLine,
        string context,
        int lineNumber,
        SymbolRecord? container,
        string language)
    {
        int offset = 0;
        foreach (var segment in arg.Split('.'))
        {
            if (segment.Length == 0)
            {
                offset += 1; // '.' separator / ドット区切り分
                continue;
            }

            if (!IsIgnoredTypeReferenceSegment(language, segment))
            {
                int column = argStartInLine + offset + 1; // 1-based / 1始まり
                var dedupeKey = $"{lineNumber}:{column}:type_reference:{segment}";
                if (seen.Add(dedupeKey))
                {
                    references.Add(new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = segment,
                        ReferenceKind = "type_reference",
                        Line = lineNumber,
                        Column = column,
                        Context = context,
                        ContainerKind = container?.Kind,
                        ContainerName = container?.Name,
                    });
                }
            }

            offset += segment.Length + 1; // segment + '.'
        }
    }

    private static bool IsIgnoredTypeReferenceSegment(string language, string segment)
    {
        if (IsIgnoredCallName(language, segment))
            return true;
        if (language == "java" && JavaPrimitiveTypeNames.Contains(segment))
            return true;
        if (language == "csharp" && CSharpBuiltInTypeNames.Contains(segment))
            return true;
        return false;
    }

    /// <summary>
    /// Walk the argument list of a C# nameof/typeof/sizeof/default starting at
    /// <paramref name="startIndex"/> (the char right after `(`). Emits one `type_reference` row
    /// per identifier segment while handling generic `&lt;...&gt;`, array `[...]`, and
    /// `global::` / `Alias::` qualifier skipping so nested paths like `nameof(List&lt;int&gt;.Count)`
    /// or `nameof(global::System.String)` are indexed correctly.
    /// C# の nameof/typeof/sizeof/default の引数を `(` 直後から lexer で走査し、
    /// generic `&lt;...&gt;`・配列 `[...]`・`global::` / `Alias::` 修飾子を跨ぎながら
    /// 識別子セグメントごとに type_reference を発行する。
    /// </summary>
    private static void ExtractCSharpTypeKeywordSegments(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string line,
        int startIndex,
        string context,
        int lineNumber,
        SymbolRecord? container,
        string language)
    {
        int i = startIndex;
        bool expectSegment = true;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == ')' || c == ',')
                return;

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (expectSegment && IsCSharpIdentifierStart(c))
            {
                int segStart = i;
                while (i < line.Length && IsCSharpIdentifierPart(line[i]))
                    i++;
                var segment = line.Substring(segStart, i - segStart);
                // `Alias::Member` — the left-hand side is a namespace alias, not an indexed
                // type. Drop it instead of emitting it, and treat what follows the `::` as a
                // fresh segment head.
                // `Alias::Member` の左辺はエイリアスであり型シンボルではないため発行せず、
                // `::` の右側を新しいセグメント先頭として読み直す。
                if (i + 1 < line.Length && line[i] == ':' && line[i + 1] == ':')
                {
                    i += 2;
                    expectSegment = true;
                    continue;
                }

                AddTypeReferenceSegment(references, seen, fileId, segment, segStart, context, lineNumber, container, language);
                expectSegment = false;
                continue;
            }

            if (c == '.')
            {
                i++;
                expectSegment = true;
                continue;
            }

            if (c == '<')
            {
                i = SkipBalanced(line, i, '<', '>');
                continue;
            }

            if (c == '[')
            {
                i = SkipBalanced(line, i, '[', ']');
                continue;
            }

            if (c == '(')
            {
                // Parenthesized/tuple-like fragments inside an argument such as
                // `typeof((int, int))` — skip the paren body and keep scanning.
                // `typeof((int, int))` のようなタプル形は括弧内をまとめてスキップする。
                i = SkipBalanced(line, i, '(', ')');
                expectSegment = false;
                continue;
            }

            // Unknown token (operator, string start, etc.) — stop scanning this argument.
            // 解釈できないトークンが来たら、このキーワード引数の走査を打ち切る。
            return;
        }
    }

    private static int SkipBalanced(string line, int start, char open, char close)
    {
        int depth = 0;
        int i = start;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth <= 0)
                    return i + 1;
            }
            i++;
        }
        return i;
    }

    private static bool IsCSharpIdentifierStart(char c) =>
        c == '_' || c == '@' || char.IsLetter(c);

    private static bool IsCSharpIdentifierPart(char c) =>
        c == '_' || char.IsLetterOrDigit(c);

    private static void AddTypeReferenceSegment(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string segment,
        int startInLine,
        string context,
        int lineNumber,
        SymbolRecord? container,
        string language)
    {
        if (segment.Length == 0 || IsIgnoredTypeReferenceSegment(language, segment))
            return;

        int column = startInLine + 1; // 1-based / 1始まり
        var dedupeKey = $"{lineNumber}:{column}:type_reference:{segment}";
        if (!seen.Add(dedupeKey))
            return;

        references.Add(new ReferenceRecord
        {
            FileId = fileId,
            SymbolName = segment,
            ReferenceKind = "type_reference",
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
