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
        // `this` is listed so generic CallRegex does not emit a phantom `call this` edge
        // after EmitJavaCtorChainReferences rewrites the chain to the owning class.
        // `this` も含めることで、連鎖書き換え後の generic CallRegex が `call this` を二重に出すのを防ぐ。
        ["java"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "instanceof", "super", "this", "assert", "throws", "extends", "implements", "synchronized",
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
    // C# constructor chain initializer: `public A() : this(0)` / `public B() : base(42)`
    // C# コンストラクタ連鎖イニシャライザ
    private static readonly Regex CSharpCtorChainRegex = new(@":\s*(?<kind>this|base)\s*\(", RegexOptions.Compiled);
    // Java constructor chain statement (first statement of a constructor body): `this(0);` / `super(42);`
    // Java コンストラクタ連鎖文（本体先頭に現れる this(...) / super(...)）
    private static readonly Regex JavaCtorChainRegex = new(@"^\s*(?<kind>this|super)\s*\(", RegexOptions.Compiled);
    // Java `extends` clause used to resolve `super(...)` chain targets
    // Java の extends 節を解析して super(...) の呼び先を決定するために使用
    private static readonly Regex JavaExtendsRegex = new(@"\bextends\s+(?<base>[\w.$]+)", RegexOptions.Compiled);
    // Inline `where` constraint in a C# type header; used to trim base-list parsing
    // C# 型ヘッダーの where 制約句。base-list 解析の終端として使用
    private static readonly Regex CSharpWhereClauseRegex = new(@"\s+where\s+[\w?.]+\s*:", RegexOptions.Compiled);
    // C# record declaration with a primary-constructor parameter list.
    // Used to synthesize a function-kind container for primary-ctor base calls
    // (e.g. `record Child(int x) : Parent(x)`), so `callers` / `callees` / `impact`
    // can attribute the `Parent(x)` edge to the record's synthetic constructor.
    // C# record のプライマリーコンストラクタ宣言を検出し、base primary-ctor 呼び出しの
    // 参照を record の合成コンストラクタに紐付けるために使う。
    private static readonly Regex CSharpRecordPrimaryCtorSignatureRegex = new(
        @"\brecord\s+(?:class\s+|struct\s+)?\w+(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled);

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
        var containerCandidates = symbols
            .Where(symbol => symbol.BodyStartLine != null && symbol.BodyEndLine != null &&
                             (symbol.Kind == "function" || symbol.Kind == "class" || symbol.Kind == "namespace"))
            .OrderBy(symbol => (symbol.BodyEndLine ?? symbol.EndLine) - (symbol.BodyStartLine ?? symbol.StartLine))
            .ToList();
        // Enclosing-type candidates for constructor-chain rewrites (class/struct/record; namespace excluded).
        // Ordered innermost-first via ascending body range.
        // コンストラクタ連鎖の呼び先解決で使う外側の型候補（class/struct/record。namespace は含めない）。
        // 内側優先で昇順にソート。
        var enclosingTypeCandidates = symbols
            .Where(symbol => symbol.BodyStartLine != null && symbol.BodyEndLine != null &&
                             (symbol.Kind == "class" || symbol.Kind == "struct" || symbol.Kind == "interface"))
            .OrderBy(symbol => (symbol.BodyEndLine ?? symbol.EndLine) - (symbol.BodyStartLine ?? symbol.StartLine))
            .ToList();

        // Synthetic function-kind container per-line for C# record primary-ctor declarations
        // that include a base primary-ctor call such as `record Child(int x) : Parent(x)`.
        // Keyed by the record class symbol's StartLine so only the declaration line is overridden.
        // C# record のプライマリーコンストラクタ宣言行に限って、合成 function コンテナを用意する。
        var recordPrimaryCtorContainers = BuildCSharpRecordPrimaryCtorContainers(language, symbols);

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
            if (recordPrimaryCtorContainers.TryGetValue(lineNumber, out var syntheticRecordCtor))
                container = syntheticRecordCtor;

            // Event subscription/unsubscription (C#) / イベント購読・解除 (C#)
            if (language is "csharp")
            {
                foreach (Match match in EventSubscriptionRegex.Matches(preparedLine))
                    AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, container);
            }

            // Constructor chain-call rewrites: C# `: this(...)` / `: base(...)` and Java `this(...)` / `super(...)`
            // コンストラクタ連鎖呼び出しの書き換え
            if (language is "csharp")
            {
                EmitCSharpCtorChainReferences(
                    preparedLine, enclosingTypeCandidates, containerCandidates,
                    references, seen, fileId, context, lineNumber, container);
            }
            else if (language is "java")
            {
                EmitJavaCtorChainReferences(
                    preparedLine, enclosingTypeCandidates, symbols, references, seen, fileId, context, lineNumber, container);
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

    private static SymbolRecord? FindInnermostContainer(IReadOnlyList<SymbolRecord> candidates, int lineNumber)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.BodyStartLine!.Value <= lineNumber && candidate.BodyEndLine!.Value >= lineNumber)
                return candidate;
        }

        return null;
    }

    private static SymbolRecord? FindInnermostClassLike(IReadOnlyList<SymbolRecord> candidates, int lineNumber)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Kind != "class" && candidate.Kind != "struct")
                continue;
            if (candidate.BodyStartLine!.Value <= lineNumber && candidate.BodyEndLine!.Value >= lineNumber)
                return candidate;
        }

        return null;
    }

    private static void EmitCSharpCtorChainReferences(
        string preparedLine,
        IReadOnlyList<SymbolRecord> enclosingTypeCandidates,
        IReadOnlyList<SymbolRecord> containerCandidates,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var chainMatches = CSharpCtorChainRegex.Matches(preparedLine);
        if (chainMatches.Count == 0)
            return;

        var enclosingType = FindInnermostClassLike(enclosingTypeCandidates, lineNumber);
        if (enclosingType == null)
            return;

        // For cross-line initializers such as:
        //   public A(int x, int y)
        //       : this(x, 0)
        //   { }
        // the chain line precedes the body, so the inner-most "body-covering" container lookup
        // returns the class rather than the constructor. Fall back to a declaration-to-body-end
        // lookup so the reference is attributed to the constructor that owns the chain.
        // クロス行イニシャライザでは body よりも前に連鎖行が現れるため、body 範囲のみで
        // 判定すると外側クラスが選ばれる。宣言〜body 終端の範囲で探し直す。
        var chainContainer = container;
        if (chainContainer == null || chainContainer.Kind != "function")
        {
            chainContainer = FindDeclarationRangeFunction(containerCandidates, lineNumber) ?? chainContainer;
        }

        foreach (Match match in chainMatches)
        {
            var kindToken = match.Groups["kind"].Value;
            string? target;
            if (kindToken == "this")
            {
                target = enclosingType.Name;
            }
            else
            {
                // `base(...)` needs the base type from the enclosing class's signature.
                // `base(...)` は外側クラスのシグネチャから基底型を解析する必要がある。
                target = ParseCSharpBaseType(enclosingType.Signature);
                if (string.IsNullOrWhiteSpace(target))
                    continue;
            }

            AddChainReference(
                references, seen, fileId, target!, match.Groups["kind"].Index + 1,
                "call", context, lineNumber, chainContainer);
        }
    }

    private static SymbolRecord? FindDeclarationRangeFunction(
        IReadOnlyList<SymbolRecord> candidates, int lineNumber)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Kind != "function")
                continue;
            if (candidate.StartLine <= lineNumber && candidate.BodyEndLine!.Value >= lineNumber)
                return candidate;
        }

        return null;
    }

    private static void EmitJavaCtorChainReferences(
        string preparedLine,
        IReadOnlyList<SymbolRecord> enclosingTypeCandidates,
        IReadOnlyList<SymbolRecord> symbols,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var match = JavaCtorChainRegex.Match(preparedLine);
        if (!match.Success)
            return;

        var enclosingType = FindInnermostClassLike(enclosingTypeCandidates, lineNumber);
        if (enclosingType == null)
            return;

        // Prefer the innermost container when it is already a constructor of the enclosing class.
        // Otherwise, fall back to scanning all function-kind symbols by name match against the enclosing
        // class. Package-private ctors like `Leaf(int x){super(x);}` can appear without body ranges in
        // SymbolExtractor, so they are excluded from containerCandidates and the innermost container
        // becomes the class itself. The fallback still finds them by declaration StartLine.
        // 外側クラスのコンストラクタが innermost container として既に見つかっていれば使う。
        // そうでなければ、関数シンボル全体を走査して外側クラスと同名のコンストラクタを
        // declaration StartLine ベースで引き直す。body 範囲を持たない package-private ctor でも拾える。
        SymbolRecord? ctorContainer = null;
        if (container != null && container.Kind == "function"
            && string.Equals(container.Name, enclosingType.Name, StringComparison.Ordinal))
        {
            ctorContainer = container;
        }
        else
        {
            ctorContainer = FindEnclosingJavaConstructor(symbols, enclosingType, lineNumber);
        }

        if (ctorContainer == null)
            return;

        var kindToken = match.Groups["kind"].Value;
        string? target;
        if (kindToken == "this")
        {
            target = enclosingType.Name;
        }
        else
        {
            target = ParseJavaBaseType(enclosingType.Signature);
            if (string.IsNullOrWhiteSpace(target))
                return;
        }

        AddChainReference(
            references, seen, fileId, target!, match.Groups["kind"].Index + 1,
            "call", context, lineNumber, ctorContainer);
    }

    /// <summary>
    /// Find the Java constructor symbol that encloses the given line number by name-matching
    /// the enclosing class. Covers package-private ctors that SymbolExtractor records without
    /// body ranges (so they are absent from containerCandidates).
    /// 指定行を内包する Java コンストラクタを、外側クラス名との一致で走査して探す。
    /// body 範囲を持たない package-private ctor でも declaration StartLine 起点で拾える。
    /// </summary>
    private static SymbolRecord? FindEnclosingJavaConstructor(
        IReadOnlyList<SymbolRecord> symbols,
        SymbolRecord enclosingType,
        int lineNumber)
    {
        var classStart = enclosingType.BodyStartLine ?? enclosingType.StartLine;
        var classEnd = enclosingType.BodyEndLine ?? enclosingType.EndLine;

        // Collect all ctor-name function symbols declared inside the class body at or before
        // `lineNumber`. When the symbol carries a body range, we can check the range directly.
        // Otherwise we fall back to the most recent declaration line at or before `lineNumber`,
        // bounded above by the next same-name ctor declaration (so the reference is still
        // attributed to the constructor that actually owns the chain line).
        // body 範囲があるシンボルは範囲判定、body 範囲の無い package-private ctor は
        // 次の同名 ctor 宣言の直前までを占有範囲として扱う。
        List<SymbolRecord>? candidates = null;
        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "function")
                continue;
            if (!string.Equals(symbol.Name, enclosingType.Name, StringComparison.Ordinal))
                continue;
            if (symbol.StartLine < classStart || symbol.StartLine > classEnd)
                continue;
            if (symbol.StartLine > lineNumber)
                continue;
            (candidates ??= new List<SymbolRecord>()).Add(symbol);
        }

        if (candidates == null)
            return null;

        candidates.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));

        SymbolRecord? best = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var symbol = candidates[i];
            var hasBodyRange = symbol.BodyStartLine != null && symbol.BodyEndLine != null;
            int rangeEnd;
            if (hasBodyRange)
            {
                rangeEnd = symbol.BodyEndLine!.Value;
            }
            else
            {
                // Extend to just before the next same-name ctor declaration, else to the end
                // of the enclosing class body. This approximates the ctor's true range when
                // SymbolExtractor could not parse the braces (e.g. package-private Java ctors).
                // 次の同名 ctor 宣言の直前、もしくは外側クラス body の終端まで拡張する。
                var nextStart = (i + 1 < candidates.Count) ? candidates[i + 1].StartLine : classEnd + 1;
                rangeEnd = nextStart - 1;
            }

            if (rangeEnd < lineNumber)
                continue;

            // Innermost / most recent declaration wins.
            // 一番近い宣言行を選ぶ。
            if (best == null || symbol.StartLine > best.StartLine)
                best = symbol;
        }

        return best;
    }

    private static void AddChainReference(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string name,
        int column,
        string referenceKind,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
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
    /// Build a per-line map of synthetic function-kind containers for C# record declarations
    /// that carry a base primary-constructor call (e.g. `record Child(int x) : Parent(x)`).
    /// SymbolExtractor does not synthesize a separate ctor symbol for the implicit primary
    /// constructor, so the `Parent(x)` reference would otherwise be attributed to the class
    /// itself (or to no container at all) and `callers` / `callees` / `impact` would miss the
    /// edge. We override the container on the record's declaration line so the chain edge is
    /// attributed to a synthetic function named after the record.
    /// C# record のプライマリーコンストラクタ宣言（`record Child(int x) : Parent(x)` など）に対して、
    /// 宣言行だけ合成 function コンテナへ差し替えるための辞書を作る。
    /// </summary>
    private static Dictionary<int, SymbolRecord> BuildCSharpRecordPrimaryCtorContainers(
        string language,
        IReadOnlyList<SymbolRecord> symbols)
    {
        var map = new Dictionary<int, SymbolRecord>();
        if (language != "csharp")
            return map;

        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "class")
                continue;
            var signature = symbol.Signature;
            if (string.IsNullOrWhiteSpace(signature))
                continue;
            if (!CSharpRecordPrimaryCtorSignatureRegex.IsMatch(signature))
                continue;
            if (!HasCSharpRecordBasePrimaryCtorCall(signature))
                continue;

            var synthetic = new SymbolRecord
            {
                FileId = symbol.FileId,
                Kind = "function",
                Name = symbol.Name,
                Line = symbol.Line,
                StartLine = symbol.StartLine,
                EndLine = symbol.StartLine,
                BodyStartLine = symbol.StartLine,
                BodyEndLine = symbol.StartLine,
                Signature = signature,
                ContainerKind = symbol.ContainerKind,
                ContainerName = symbol.ContainerName,
                ContainerQualifiedName = symbol.ContainerQualifiedName,
                FamilyKey = symbol.FamilyKey,
                Visibility = symbol.Visibility,
            };

            // Record declaration StartLine is the line the base primary-ctor call sits on.
            // Only that exact line is overridden so method bodies inside `{...}` keep their
            // real containers via FindInnermostContainer.
            // 宣言行のみを上書きし、`{...}` 本体内のメソッドは本来のコンテナを保つ。
            map[symbol.StartLine] = synthetic;
        }

        return map;
    }

    /// <summary>
    /// Returns true when the C# type signature carries a base-list entry that looks like a
    /// primary-constructor call (contains `(`).
    /// C# 型シグネチャの base-list 先頭エントリが primary-ctor 呼び出し（`(` を含む）かを判定する。
    /// </summary>
    private static bool HasCSharpRecordBasePrimaryCtorCall(string signature)
    {
        var text = signature.TrimEnd();
        if (text.EndsWith(";", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - 1).TrimEnd();
        if (text.EndsWith("{", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - 1).TrimEnd();

        var colonIndex = FindSignatureColonIndex(text);
        if (colonIndex < 0)
            return false;

        var baseList = text.Substring(colonIndex + 1);
        var whereMatch = CSharpWhereClauseRegex.Match(baseList);
        if (whereMatch.Success)
            baseList = baseList.Substring(0, whereMatch.Index);

        var firstEntry = TakeFirstBaseEntry(baseList).Trim();
        return firstEntry.Contains('(');
    }

    /// <summary>
    /// Parse the first base-class token from a C# class/struct/record signature such as
    /// `class B : A, IFoo`, `record C(int x) : A(x)`, or `class B<T> : A<T> where T : new()`.
    /// Returns null when no base list is present or when the signature is empty.
    /// C# の class/struct/record シグネチャから最初の基底クラストークンを取り出す。
    /// </summary>
    internal static string? ParseCSharpBaseType(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return null;

        var text = signature.TrimEnd();
        if (text.EndsWith("{", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - 1).TrimEnd();

        var colonIndex = FindSignatureColonIndex(text);
        if (colonIndex < 0)
            return null;

        var baseList = text.Substring(colonIndex + 1);
        var whereMatch = CSharpWhereClauseRegex.Match(baseList);
        if (whereMatch.Success)
            baseList = baseList.Substring(0, whereMatch.Index);

        var firstEntry = TakeFirstBaseEntry(baseList).Trim();
        return ExtractBareTypeName(firstEntry);
    }

    /// <summary>
    /// Parse the first extends-clause type from a Java class/interface/record signature.
    /// 例: `class B extends A implements IFoo` → `A`。
    /// </summary>
    internal static string? ParseJavaBaseType(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return null;

        var match = JavaExtendsRegex.Match(signature);
        if (!match.Success)
            return null;

        return ExtractBareTypeName(match.Groups["base"].Value);
    }

    private static int FindSignatureColonIndex(string text)
    {
        var depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '<':
                case '(':
                case '[':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                    if (depth > 0) depth--;
                    break;
                case ':':
                    if (depth == 0)
                    {
                        // Skip `::` alias qualifier (`global::System.Exception`).
                        // `::` エイリアス修飾子（`global::System.Exception`）はスキップ。
                        if (i + 1 < text.Length && text[i + 1] == ':')
                        {
                            i++;
                            continue;
                        }
                        return i;
                    }
                    break;
            }
        }

        return -1;
    }

    private static string TakeFirstBaseEntry(string baseList)
    {
        var depth = 0;
        for (int i = 0; i < baseList.Length; i++)
        {
            var c = baseList[i];
            switch (c)
            {
                case '<':
                case '(':
                case '[':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                    if (depth > 0) depth--;
                    break;
                case ',':
                    if (depth == 0)
                        return baseList.Substring(0, i);
                    break;
            }
        }

        return baseList;
    }

    private static string? ExtractBareTypeName(string entry)
    {
        var trimmed = entry.Trim();
        if (trimmed.Length == 0)
            return null;

        // Strip generic args: `A<int>` → `A`.
        // ジェネリック引数を剥がす。
        var ltIndex = trimmed.IndexOf('<');
        if (ltIndex >= 0)
            trimmed = trimmed.Substring(0, ltIndex);

        // Strip record primary-ctor args: `A(...)` → `A`.
        // record のプライマリコンストラクタ引数を剥がす。
        var lparenIndex = trimmed.IndexOf('(');
        if (lparenIndex >= 0)
            trimmed = trimmed.Substring(0, lparenIndex);

        trimmed = trimmed.Trim();
        if (trimmed.Length == 0)
            return null;

        // Use the unqualified terminal segment so references align with other call-site names.
        // 他の呼び出し参照と揃えるため、修飾なしの末尾セグメントだけを使う。
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot >= 0)
            trimmed = trimmed.Substring(lastDot + 1);

        var lastColon = trimmed.LastIndexOf(':');
        if (lastColon >= 0)
            trimmed = trimmed.Substring(lastColon + 1);

        trimmed = trimmed.Trim();
        return trimmed.Length > 0 ? trimmed : null;
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
