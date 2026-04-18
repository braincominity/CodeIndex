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

    // No-arg C# attribute name (`[Serializable]`, `[assembly: CLSCompliant]`, `[System.Obsolete]`,
    // `[global::System.Obsolete]`, `[Alias::MyAttr]`, `[Required, Key]`, and their multi-line
    // variants where `[` / `]` sit on separate lines). CallRegex only matches identifiers followed
    // by `(`, so no-arg attributes would otherwise never be indexed. The pattern refuses to match
    // when the identifier is followed by `(` (handled by CallRegex + TryClassifyMetadataReference)
    // or a qualifier continuation (`.` / `::`). The match is gated downstream by
    // `IsInsideCSharpAttributeRange`, so it is safe to relax the `[` / `,` left-anchor in favor of
    // a word-boundary lookbehind — that lets a bare identifier on a line like `    Serializable`
    // inside a multi-line attribute section still be recognized.
    // 引数なしの C# attribute 名用 regex。`[Serializable]` などは CallRegex では拾えないため専用の
    // 入口で捕捉する。`global::System.Obsolete` や `Alias::MyAttr` のように `::` 修飾子の付く形も
    // 許容する。`[` / `,` / `]` が別行にある複数行形（例: `[\n Serializable\n]`）も取り込むため、
    // 左側は `[` / `,` ではなく単語境界だけでアンカーする。属性以外の位置で誤検出しないよう、
    // マッチ後は `IsInsideCSharpAttributeRange` で属性レンジ内かどうかを確認する。後続が `(`
    // （CallRegex 経路）や `.` / `::`（qualifier 継続）なら名前を確定させず、行末（`$`）・`]`・`,`
    // のいずれかで初めて採用する。
    private static readonly Regex CSharpNoArgAttributeRegex = new(
        @"(?<!\w)(?:[A-Za-z_]\w*\s*:\s*)?(?:[A-Za-z_]\w*\s*(?:\.|::)\s*)*(?<name>[A-Za-z_]\w*)(?:\s*<[^>\n]+>)?\s*(?=[\],]|$)",
        RegexOptions.Compiled);

    // No-arg Java-family annotation (`@Deprecated`, `@Override`, `@org.junit.Test`, `@field:Deprecated`).
    // CallRegex only catches `@Name(` forms; this pattern fills the bare `@Name` gap. The leading
    // lookbehind `(?<![\w)])` prevents Kotlin label references like `return@foo` from matching.
    // 引数なしの Java 系 annotation 名用 regex。`@Deprecated` のような形は CallRegex では拾えないため
    // 専用経路で捕捉する。先頭の lookbehind `(?<![\w)])` で Kotlin の `return@foo` のようなラベル参照を
    // 除外する。
    private static readonly Regex NoArgAnnotationRegex = new(
        @"(?<![\w)])@(?:[A-Za-z_]\w*\s*:\s*)?(?:[A-Za-z_]\w*\s*\.\s*)*(?<name>[A-Za-z_]\w*)\b(?!\s*[.(])",
        RegexOptions.Compiled);

    // Languages whose `@Decorator(args)` / `@Annotation(args)` / `@Attribute(args)` syntax
    // should produce `annotation` reference rows rather than `call` rows (issue #293).
    // Swift uses `@available(...)`, `@objc`, `@MainActor`, etc. as compile-time metadata;
    // Gradle/Groovy uses `@CompileStatic`, `@TaskAction`, etc. the same way. Without this
    // reclassification, `callers` / `callees` / `hotspots` / `impact` on those languages
    // get polluted with metadata edges.
    // `@Decorator(args)` / `@Annotation(args)` / `@Attribute(args)` を `call` ではなく
    // `annotation` として記録すべき言語 (issue #293)。Swift の `@available(...)` / `@objc` /
    // `@MainActor` や、Gradle/Groovy の `@CompileStatic` / `@TaskAction` も compile-time
    // metadata なので同じ扱いにする。再分類しないと `callers` / `callees` / `hotspots` /
    // `impact` に metadata edge が混入する。
    private static readonly HashSet<string> AnnotationLanguages = new(StringComparer.Ordinal)
    {
        "java", "kotlin", "scala", "typescript", "javascript", "swift", "gradle",
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
        var preparedLines = new string[lines.Length];
        for (var pi = 0; pi < lines.Length; pi++)
            preparedLines[pi] = PrepareLine(language, structuralLines[pi]);
        // Pre-pass C# attribute analysis so cross-line `[\n Foo("x")\n]` and parameter
        // attributes `void M([Attr] T x)` are classified consistently with same-line `[Foo]`.
        // 行を跨いだ `[\n Foo("x")\n]` やパラメータ属性 `void M([Attr] T x)` も、同一行の `[Foo]` と
        // 同じ判定で属性として扱えるように、事前パスで C# 属性セクションの範囲を構築する。
        var csharpAttrTables = language == "csharp"
            ? BuildCSharpAttributeRanges(preparedLines)
            : (null, null);
        var csharpAttrRanges = csharpAttrTables.Item1;
        // Top-level (paren-depth 0) zones inside attribute sections. Used by the no-arg
        // attribute regex so that enum / qualified-constant identifiers appearing inside
        // attribute argument lists (e.g. `AllowNumbers` in `[JsonConverter(ConverterStrategy.AllowNumbers)]`)
        // are not misclassified as no-arg attribute references.
        // 属性セクション内で paren 深さ 0 の top-level ゾーンだけを別テーブルで持つ。複数行
        // `[...]` の引数中に現れる enum / 修飾定数（`ConverterStrategy.AllowNumbers` など）が
        // no-arg attribute として誤分類されないよう、no-arg 属性用ゲートに使う。
        var csharpAttrTopLevelRanges = csharpAttrTables.Item2;
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
            var preparedLine = preparedLines[i];
            if (string.IsNullOrWhiteSpace(preparedLine))
                continue;
            var csharpAttrRangesOnLine = csharpAttrRanges?[i];
            var csharpAttrTopLevelOnLine = csharpAttrTopLevelRanges?[i];

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
                var insideCSharpAttributeRange = csharpAttrRangesOnLine != null
                    && IsInsideCSharpAttributeRange(csharpAttrRangesOnLine, nameIndex);
                var metadataKind = TryClassifyMetadataReference(language, preparedLine, nameIndex, insideCSharpAttributeRange);
                AddReference(references, seen, fileId, match, metadataKind ?? "call", context, lineNumber, container);
            }

            // issue #293: bare no-arg attributes / annotations are invisible to CallRegex because
            // it requires `(`. Emit them from dedicated regexes so `[Serializable]` / `@Deprecated`
            // and their siblings still populate the reference table.
            // issue #293: 引数なしの属性・アノテーションは `(` が必須な CallRegex では拾えないため、
            // 専用 regex から `[Serializable]` / `@Deprecated` などの素形を reference テーブルへ反映する。
            if (language == "csharp" && csharpAttrTopLevelOnLine != null && csharpAttrTopLevelOnLine.Count > 0)
            {
                foreach (Match match in CSharpNoArgAttributeRegex.Matches(preparedLine))
                {
                    var name = match.Groups["name"].Value;
                    var nameIndex = match.Groups["name"].Index;
                    // Gate on the attribute-section top-level (paren-depth 0) zones only, so
                    // identifiers that sit inside an attribute's argument list (e.g.
                    // `ConverterStrategy.AllowNumbers` in `[JsonConverter(...)]`) are not
                    // misclassified as no-arg attributes.
                    // 属性セクションの top-level（paren 深さ 0）ゾーンでのみ採用する。属性の
                    // 引数リスト内にある識別子（`[JsonConverter(ConverterStrategy.AllowNumbers)]`
                    // の `AllowNumbers` など）を no-arg 属性として誤分類しないため。
                    if (!IsInsideCSharpAttributeRange(csharpAttrTopLevelOnLine, nameIndex))
                        continue;
                    if (IsIgnoredCallName(language, name))
                        continue;
                    if (definitionNames != null && definitionNames.Contains(name))
                        continue;
                    AddReference(references, seen, fileId, match, "attribute", context, lineNumber, container);
                }
            }
            else if (AnnotationLanguages.Contains(language))
            {
                foreach (Match match in NoArgAnnotationRegex.Matches(preparedLine))
                {
                    var name = match.Groups["name"].Value;
                    if (IsIgnoredCallName(language, name))
                        continue;
                    if (definitionNames != null && definitionNames.Contains(name))
                        continue;
                    AddReference(references, seen, fileId, match, "annotation", context, lineNumber, container);
                }
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
    private static string? TryClassifyMetadataReference(
        string language,
        string preparedLine,
        int nameIndex,
        bool insideCSharpAttributeRange)
    {
        if (language == "csharp")
            return insideCSharpAttributeRange ? "attribute" : null;

        var probe = nameIndex - 1;
        while (probe >= 0 && char.IsWhiteSpace(preparedLine[probe]))
            probe--;
        if (probe < 0)
            return null;

        if (AnnotationLanguages.Contains(language))
            return IsAnnotationContext(preparedLine, probe) ? "annotation" : null;

        return null;
    }

    /// <summary>
    /// Build per-line column ranges that identify C# `[...]` attribute sections. Handles
    /// declaration-position detection (including parameter attributes preceded by `(` / `,`
    /// via forward look-ahead) and multi-line `[\n ... \n]` sections. Each inner list holds
    /// ordered `(startColumn, endColumnExclusive)` ranges that are inside an attribute section
    /// on that line. Call sites whose name column falls inside one of these ranges are
    /// reclassified as `attribute` instead of `call`.
    /// C# の `[...]` 属性セクションを行ごとの列範囲で表すテーブルを構築する。
    /// `(` / `,` の直後に置かれるパラメータ属性を forward lookahead で、複数行にわたる
    /// `[\n ... \n]` 属性を跨行トラッキングで検出する。各行のリストは属性セクションに含まれる
    /// `(開始列, 終端列 (exclusive))` のレンジを保持し、呼び出し名の列がどれかのレンジに含まれる場合に
    /// `call` ではなく `attribute` へ再分類する。
    /// </summary>
    private static (List<List<(int start, int end)>>, List<List<(int start, int end)>>) BuildCSharpAttributeRanges(string[] preparedLines)
    {
        var perLine = new List<List<(int, int)>>(preparedLines.Length);
        var perLineTopLevel = new List<List<(int, int)>>(preparedLines.Length);
        for (var i = 0; i < preparedLines.Length; i++)
        {
            perLine.Add(new List<(int, int)>());
            perLineTopLevel.Add(new List<(int, int)>());
        }

        // Stack entries capture the opening `[` position, whether that bracket was at
        // a C# declaration (attribute) position, and a snapshot of the global paren depth
        // at that moment. The snapshot lets us compute an attribute-section-local paren
        // depth (`parenDepth - parenDepthAtOpen`), which is what the top-level zone tracking
        // uses so that parameter attributes like `void M([Attr] int x)` still have their
        // attribute-list top level at section-local depth 0 even though the global depth
        // is inside the method's parameter list.
        // スタックは `[` の位置、その bracket が属性位置だったか、および開いた瞬間の
        // グローバル paren 深さのスナップショットを保持する。スナップショットを使うと
        // 属性セクション内ローカルの paren 深さ (`parenDepth - parenDepthAtOpen`) が
        // 得られるので、`void M([Attr] int x)` のように外側の method 引数リストの中で
        // 開く属性セクションでも、セクション内では top-level (local depth 0) として扱える。
        var bracketStack = new Stack<(int li, int ci, bool isAttr, int parenDepthAtOpen)>();
        char lastMeaningful = '\0';
        int parenDepth = 0;
        bool lastClosedBracketWasAttribute = false;

        // Top-level zone tracking: while we are inside an attribute section and the paren
        // depth is at the section's open snapshot (section-local depth 0), the current zone
        // span is open. When parens open inside the section we close it; when they fully
        // close again we reopen. When the attribute section itself closes, we emit the span.
        // top-level ゾーン追跡: 属性セクション内かつセクションローカルの paren 深さが 0 の
        // あいだだけゾーンを開いておき、セクション内の `(` で閉じ、`)` で再び開く。
        // セクションが閉じる `]` で確定させる。
        int topZoneStartLi = -1;
        int topZoneStartCi = 0;

        void EmitTopZone(int endLi, int endCi)
        {
            if (topZoneStartLi < 0)
                return;
            for (var l = topZoneStartLi; l <= endLi; l++)
            {
                int s = (l == topZoneStartLi) ? topZoneStartCi : 0;
                int e = (l == endLi) ? endCi : preparedLines[l].Length;
                if (e > s)
                    perLineTopLevel[l].Add((s, e));
            }
            topZoneStartLi = -1;
        }

        for (var li = 0; li < preparedLines.Length; li++)
        {
            var line = preparedLines[li];
            for (var ci = 0; ci < line.Length; ci++)
            {
                var c = line[ci];
                if (char.IsWhiteSpace(c))
                    continue;

                if (c == '(')
                {
                    // If the innermost enclosing bracket is an attribute section and we are
                    // currently at that section's local top level, close the top-level zone
                    // just before the `(`. Use the stack top's `parenDepthAtOpen` snapshot so
                    // parameter attributes inside an outer `(...)` still get their top level
                    // tracked correctly.
                    // 直近の `[` が属性セクションで、かつその section-local 深さで top-level のとき、
                    // `(` 直前でゾーンを閉じる。外側の `(...)` の中で開く属性セクションにも対応するため、
                    // グローバル depth ではなくスタック top の開いたときの snapshot と比較する。
                    if (bracketStack.Count > 0)
                    {
                        var top = bracketStack.Peek();
                        if (top.isAttr && parenDepth == top.parenDepthAtOpen && topZoneStartLi >= 0)
                            EmitTopZone(li, ci);
                    }
                    parenDepth++;
                    lastMeaningful = c;
                    continue;
                }
                if (c == ')')
                {
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                        // If the innermost `[` is an attribute section and we just returned
                        // to that section's local top level, reopen the top-level zone.
                        // 直近の `[` が属性セクションで、section-local top-level に戻ってきたら
                        // top-level ゾーンを再開する。
                        if (bracketStack.Count > 0)
                        {
                            var top = bracketStack.Peek();
                            if (top.isAttr && parenDepth == top.parenDepthAtOpen && topZoneStartLi < 0)
                            {
                                topZoneStartLi = li;
                                topZoneStartCi = ci + 1;
                            }
                        }
                    }
                    lastMeaningful = c;
                    continue;
                }

                if (c == '[')
                {
                    bool isAttr = EvaluateCSharpAttributePosition(
                        lastMeaningful, lastClosedBracketWasAttribute, preparedLines, li, ci);
                    bracketStack.Push((li, ci, isAttr, parenDepth));
                    if (isAttr && topZoneStartLi < 0)
                    {
                        // Start top-level zone just after the `[` so the `[` itself is not
                        // inside the zone. Section-local depth is 0 by construction at the
                        // open bracket.
                        // `[` 直後から top-level ゾーンを開始する。開いた瞬間は section-local 深さ 0。
                        topZoneStartLi = li;
                        topZoneStartCi = ci + 1;
                    }
                    lastMeaningful = c;
                    continue;
                }

                if (c == ']')
                {
                    if (bracketStack.Count > 0)
                    {
                        var opened = bracketStack.Pop();
                        lastClosedBracketWasAttribute = opened.isAttr;
                        if (opened.isAttr)
                        {
                            // Record the attribute section span for every line it covers so
                            // cross-line `[\n Foo("x")\n]` also classifies `Foo` as attribute.
                            // 属性セクションがまたぐ全ての行に対して範囲を記録し、
                            // `[\n Foo("x")\n]` のような跨行ケースでも `Foo` が属性として分類されるようにする。
                            for (var l = opened.li; l <= li; l++)
                            {
                                int s = (l == opened.li) ? opened.ci : 0;
                                int e = (l == li) ? ci + 1 : preparedLines[l].Length;
                                perLine[l].Add((s, e));
                            }
                            // Close the top-level zone at the `]`. Section-local depth should
                            // be 0 here (we are at the closing bracket of this section) — if
                            // it is not, we drop the open zone because paren balancing was
                            // malformed.
                            // `]` で top-level ゾーンを確定する。section-local 深さが 0 のはず。
                            // 不整合入力ならゾーンを捨てる。
                            if (parenDepth == opened.parenDepthAtOpen)
                            {
                                EmitTopZone(li, ci + 1);
                            }
                            else
                            {
                                topZoneStartLi = -1;
                            }
                        }
                    }
                    else
                    {
                        lastClosedBracketWasAttribute = false;
                    }
                    lastMeaningful = c;
                    continue;
                }

                lastMeaningful = c;
            }
        }

        return (perLine, perLineTopLevel);
    }

    /// <summary>
    /// Decide whether a `[` token sits at a C# attribute position based on the immediately
    /// preceding meaningful character. `(` / `,` (parameter attributes) are disambiguated via
    /// forward look-ahead because both attributes and C# 12 collection expressions can follow.
    /// `[` が C# の属性位置にあるかを、直前の非空白文字から判定する。`(` / `,` の直後は
    /// パラメータ属性にも collection expression にもなりうるため、forward lookahead で区別する。
    /// </summary>
    private static bool EvaluateCSharpAttributePosition(
        char lastMeaningful,
        bool lastClosedBracketWasAttribute,
        string[] preparedLines,
        int startLi,
        int startCi)
    {
        // Start of file or after a scope/statement boundary — attribute position.
        // ファイル先頭、あるいはスコープ・文境界の直後は属性位置。
        if (lastMeaningful is '\0' or '{' or '}' or ';')
            return true;

        // Chained attribute list `[A][B]`: the prior `]` must have closed an attribute section.
        // `arr[i][Compute()]` → the prior `]` closed an indexer, so stays `call`.
        // 連続した属性リスト `[A][B]` は、直前の `]` が属性セクションを閉じていたときのみ属性扱い。
        // `arr[i][Compute()]` の `]` は indexer を閉じているため `call` のまま。
        if (lastMeaningful == ']')
            return lastClosedBracketWasAttribute;

        // Parameter / type-parameter / lambda attribute candidates (`(`, `,`, `<`, `=`):
        // `void M([Attr] T x)`, `class C<[Attr] T>`, `var f = [Attr] () => body`, or
        // `Consume([Make()])`. Disambiguate by scanning forward to the matching `]` and
        // checking whether the next meaningful token begins a declaration (identifier /
        // `@` / `(` for tuple types or lambda parameter lists / `[` chained).
        // パラメータ / 型パラメータ / ラムダ属性候補 (`(`, `,`, `<`, `=`) は
        // `void M([Attr] T x)`・`class C<[Attr] T>`・`var f = [Attr] () => body`・
        // `Consume([Make()])` いずれにもなりうる。対応する `]` まで進んで次トークンが
        // 宣言やラムダを開始するか（識別子 / `@` / tuple・ラムダ仮引数の `(` / chained `[`）で区別する。
        if (lastMeaningful is '(' or ',' or '<' or '=')
            return IsCSharpAttributeFollowedByDeclaration(preparedLines, startLi, startCi);

        return false;
    }

    /// <summary>
    /// Keywords that indicate the preceding `[...]` is an expression (collection / pattern /
    /// switch target) rather than an attribute section when they appear after `]`.
    /// `]` の直後に現れると、直前の `[...]` が属性ではなく式（collection / pattern / switch 対象）
    /// であることを示す C# のキーワード集合。
    /// </summary>
    private static readonly HashSet<string> CSharpExpressionContinuationKeywords = new(StringComparer.Ordinal)
    {
        "is", "as", "switch", "with", "when",
    };

    /// <summary>
    /// Scan forward from a `[` to its matching `]` (skipping balanced parens) and return true
    /// when the next meaningful character begins an identifier-like token. Works across lines so
    /// `void M(\n    [Attr]\n    T x\n)` is recognized as a parameter attribute.
    /// `[` から対応する `]` まで進んで、`]` の次の非空白文字が識別子を始める場合に true を返す。
    /// 行を跨ぐ走査にも対応しているため `void M(\n    [Attr]\n    T x\n)` も属性として認識される。
    /// </summary>
    private static bool IsCSharpAttributeFollowedByDeclaration(string[] preparedLines, int startLi, int startCi)
    {
        var bracketDepth = 1;
        var parenDepth = 0;
        var li = startLi;
        var ci = startCi + 1;
        while (li < preparedLines.Length)
        {
            var line = preparedLines[li];
            while (ci < line.Length)
            {
                var c = line[ci];
                if (c == '(')
                {
                    parenDepth++;
                    ci++;
                    continue;
                }
                if (c == ')')
                {
                    if (parenDepth > 0)
                        parenDepth--;
                    ci++;
                    continue;
                }
                if (parenDepth > 0)
                {
                    ci++;
                    continue;
                }
                if (c == '[')
                {
                    bracketDepth++;
                    ci++;
                    continue;
                }
                if (c == ']')
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        ci++;
                        return NextTokenStartsDeclaration(preparedLines, li, ci);
                    }
                    ci++;
                    continue;
                }
                ci++;
            }
            li++;
            ci = 0;
        }
        return false;
    }

    /// <summary>
    /// After the closing `]` of a candidate `[...]`, inspect the next meaningful token to decide
    /// whether it begins a declaration. Accepts identifiers (except expression-continuation
    /// keywords like `is` / `as` / `switch` / `with` / `when`), leading `@` (verbatim identifier),
    /// `(` (tuple-typed parameter), and chained `[` (recurse for `[A][B]`).
    /// 閉じ `]` の直後のトークンで宣言が始まるかを判定する。識別子（式継続の `is` / `as` /
    /// `switch` / `with` / `when` は除外）、`@`（verbatim 識別子）、`(`（tuple パラメータ型）、
    /// `[`（`[A][B]` の連結）を受け入れる。
    /// </summary>
    private static bool NextTokenStartsDeclaration(string[] preparedLines, int li, int ci)
    {
        while (li < preparedLines.Length)
        {
            var line = preparedLines[li];
            while (ci < line.Length && char.IsWhiteSpace(line[ci]))
                ci++;
            if (ci < line.Length)
            {
                var first = line[ci];
                if (first == '@' || first == '(')
                    return true;
                if (first == '[')
                    return IsCSharpAttributeFollowedByDeclaration(preparedLines, li, ci);
                if (!IsIdentifierChar(first))
                    return false;
                var start = ci;
                while (ci < line.Length && IsIdentifierChar(line[ci]))
                    ci++;
                var token = line.Substring(start, ci - start);
                return !CSharpExpressionContinuationKeywords.Contains(token);
            }
            li++;
            ci = 0;
        }
        return false;
    }

    private static bool IsInsideCSharpAttributeRange(List<(int start, int end)> ranges, int index)
    {
        foreach (var (start, end) in ranges)
        {
            if (index >= start && index < end)
                return true;
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
