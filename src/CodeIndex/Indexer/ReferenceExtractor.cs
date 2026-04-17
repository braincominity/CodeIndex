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
    // Java constructor chain statement (first statement of a constructor body): `this(0);` / `super(42);`.
    // Also matches single-line ctor bodies like `Leaf(int x){super(x);}` where `{` precedes the chain call.
    // Java コンストラクタ連鎖文。`Leaf(int x){super(x);}` のように `{` 直後に連鎖文が続く
    // single-line body 形式にも対応する。
    private static readonly Regex JavaCtorChainRegex = new(@"(?:^\s*|\{\s*)(?<kind>this|super)\s*\(", RegexOptions.Compiled);
    // Java `extends` keyword anchor used to locate the start of the base-type expression.
    // The base type itself is scanned manually in ParseJavaBaseType so that nested generic
    // arguments like `Outer<Integer>.Base` are captured instead of being clipped at the `<`.
    // `extends` キーワードの位置だけ正規表現で拾い、基底型本体は generic のネストを壊さない
    // よう ParseJavaBaseType 側で手動スキャンする。
    private static readonly Regex JavaExtendsRegex = new(@"\bextends\s+", RegexOptions.Compiled);
    // Java access/method modifier set used by the same-line ctor scanner.
    // same-line ctor 本体のスキャナで使うアクセス / メソッド修飾子一覧。
    private static readonly HashSet<string> JavaCtorModifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "static", "final", "synchronized",
        "strictfp", "abstract", "native", "default"
    };
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
        // Enclosing-type candidates for constructor-chain rewrites (class/struct/record; namespace excluded).
        // Ordered innermost-first via ascending body range. Java enums can declare constructors and
        // chain via `this(...)` so `enum` is included; C# enums cannot declare constructors, and
        // `CSharpCtorChainRegex` will not match inside them, so including `enum` is a no-op there.
        // コンストラクタ連鎖の呼び先解決で使う外側の型候補（class/struct/record/enum。namespace は含めない）。
        // 内側優先で昇順にソート。Java の enum は `this(...)` 連鎖を持てるため `enum` も含める。
        // C# の enum はコンストラクタ自体を持てず `CSharpCtorChainRegex` が一致しないので副作用は無い。
        var enclosingTypeCandidates = symbols
            .Where(symbol => symbol.BodyStartLine != null && symbol.BodyEndLine != null &&
                             (symbol.Kind == "class" || symbol.Kind == "struct" || symbol.Kind == "interface" || symbol.Kind == "enum"))
            .OrderBy(symbol => (symbol.BodyEndLine ?? symbol.EndLine) - (symbol.BodyStartLine ?? symbol.StartLine))
            .ToList();

        // Synthetic function-kind container for C# record primary-ctor declarations with a base
        // primary-ctor call such as `record Child(int x) : Parent(x)`. The range spans the entire
        // declaration header so multi-line forms where `: Parent(x)` sits on a later line are
        // covered. Later lines inside the record body keep their real innermost containers.
        // C# record のプライマリコンストラクタ宣言で base primary-ctor を呼んでいる場合、
        // 宣言ヘッダー全体を合成コンテナで上書きする。`{` / `;` 以降の本体行は通常の container に戻す。
        var recordPrimaryCtorRanges = BuildCSharpRecordPrimaryCtorContainers(language, symbols, structuralLines);

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
            foreach (var (rangeStart, rangeEnd, syntheticRecordCtor) in recordPrimaryCtorRanges)
            {
                if (lineNumber >= rangeStart && lineNumber <= rangeEnd)
                {
                    container = syntheticRecordCtor;
                    break;
                }
            }

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
                    structuralLines, references, seen, fileId, context, lineNumber, container);
            }
            else if (language is "java")
            {
                EmitJavaCtorChainReferences(
                    preparedLine, enclosingTypeCandidates, symbols, structuralLines,
                    references, seen, fileId, context, lineNumber, container);
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
            // class/struct/enum are all ctor-owner kinds across supported languages. Java enum bodies
            // can declare constructors and chain via `this(...)`; C# enum cannot declare constructors
            // at all, so the chain regex will not match inside one even if we pick it up here.
            // class/struct/enum はいずれもコンストラクタを持ちうる宿主種別。Java enum は `this(...)`
            // 連鎖を書けるため含める。C# enum はコンストラクタ自体を持てないので副作用は出ない。
            if (candidate.Kind != "class" && candidate.Kind != "struct" && candidate.Kind != "enum")
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
        string[] structuralLines,
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
                // SymbolRecord.Signature only captures the first declaration line, so multi-line
                // base-lists (e.g. `class Child\n    : Parent`) lose the `: Parent` continuation.
                // Reconstruct the joined header up to the first `;` or `{` from structuralLines.
                // `base(...)` は外側クラスのシグネチャから基底型を解析する必要がある。
                // SymbolRecord.Signature は宣言 1 行目しか持たないので複数行 base-list が欠落する。
                // structuralLines から最初の `;` / `{` までを連結し直して渡す。
                var (_, headerText) = CollectCSharpRecordHeader(structuralLines, enclosingType.StartLine);
                target = ParseCSharpBaseType(headerText);
                if (string.IsNullOrWhiteSpace(target))
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
        string[] structuralLines,
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
        // becomes the class itself. Same-line ctor bodies are not emitted as function symbols at all
        // (the enum-member regex requires the line to end with `,`/`{`/`;`), so the final fallback
        // synthesizes a container from the current line when it matches a ctor declaration shape.
        // 外側クラスのコンストラクタが innermost container として既に見つかっていれば使う。
        // そうでなければ、関数シンボル全体を走査して外側クラスと同名のコンストラクタを
        // declaration StartLine ベースで引き直す。same-line body ctor は関数シンボル自体が
        // 存在しないので、行の shape から合成 container を作る。
        SymbolRecord? ctorContainer = null;
        if (container != null && container.Kind == "function"
            && string.Equals(container.Name, enclosingType.Name, StringComparison.Ordinal))
        {
            ctorContainer = container;
        }
        else
        {
            ctorContainer = FindEnclosingJavaConstructor(symbols, enclosingType, lineNumber)
                ?? TrySynthesizeSameLineJavaCtor(preparedLine, enclosingType, lineNumber);
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
            // Java base-list can span multiple lines (`class Leaf\n    extends Base`).
            // SymbolRecord.Signature only captures the first declaration line, so reconstruct
            // the enclosing type header from structural lines (reusing the C# helper — the
            // scanner stops at `;` / `{`, which also bounds Java class / record / enum headers).
            // Java の base-list も複数行にまたがる (`class Leaf\n    extends Base`)。
            // SymbolRecord.Signature は 1 行目しか持たないため、structural lines からヘッダを
            // 再構築する (C# 用ヘルパーを流用。`;` / `{` 終端は Java にも適用可)。
            var (_, headerText) = CollectCSharpRecordHeader(structuralLines, enclosingType.StartLine);
            target = ParseJavaBaseType(headerText);
            if (string.IsNullOrWhiteSpace(target))
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

    /// <summary>
    /// When the current line itself carries a same-line Java constructor body (for example
    /// `Leaf(int x) { super(x); }`), synthesize a function-kind container so the chain rewrite
    /// still attaches the edge to the owning constructor. SymbolExtractor does not emit a
    /// function symbol for this shape because the enum-member regex requires the line to end
    /// with `,`, `{`, or `;`.
    /// same-line body の Java コンストラクタ（`Leaf(int x){super(x);}` など）では SymbolExtractor
    /// が function 化しないため、行自体を直接スキャンして合成 container を作る。
    /// </summary>
    private static SymbolRecord? TrySynthesizeSameLineJavaCtor(
        string preparedLine,
        SymbolRecord enclosingType,
        int lineNumber)
    {
        var name = TryExtractJavaCtorNameFromLine(preparedLine);
        if (name is null)
            return null;
        if (!string.Equals(name, enclosingType.Name, StringComparison.Ordinal))
            return null;

        return new SymbolRecord
        {
            Kind = "function",
            Name = enclosingType.Name,
            Line = lineNumber,
            StartLine = lineNumber,
            EndLine = lineNumber,
            BodyStartLine = lineNumber,
            BodyEndLine = lineNumber,
            ContainerKind = enclosingType.Kind,
            ContainerName = enclosingType.Name,
            ContainerQualifiedName = enclosingType.ContainerQualifiedName,
            Visibility = enclosingType.Visibility,
        };
    }

    /// <summary>
    /// Depth-aware scanner for `@Annot ... <T extends Comparable<Integer>> Ctor(...) { ... }`
    /// style declarations. Returns the constructor name when the line opens a ctor body, or
    /// null otherwise. Handles qualified annotations (`@demo.Ann`), annotation argument lists
    /// with nested parens, and nested generic bounds that a flat regex cannot balance.
    /// 修飾付きアノテーション・引数付きアノテーション・入れ子の generic 境界を含む
    /// same-line ctor 宣言を depth-aware にスキャンして ctor 名を返すヘルパー。
    /// </summary>
    internal static string? TryExtractJavaCtorNameFromLine(string line)
    {
        int i = 0;
        int n = line.Length;

        SkipWhitespace(line, ref i);

        // Consume annotations and access / misc modifiers in any order so that forms such as
        // `public @Deprecated Leaf(...)` and `@demo.Ann private Leaf(...)` are both accepted
        // instead of bailing when an annotation appears after a modifier keyword.
        // アノテーションと access modifier は順不同で交互に現れ得るため、両方を 1 つのループで
        // 反復消費する。途中でどちらでもないトークンが来たら ctor 名（または `<...>`）へ遷移する。
        while (true)
        {
            SkipWhitespace(line, ref i);
            if (i < n && line[i] == '@')
            {
                i++;
                if (!ConsumeQualifiedIdentifier(line, ref i))
                    return null;
                SkipWhitespace(line, ref i);
                if (i < n && line[i] == '(')
                {
                    if (!SkipBalancedParens(line, ref i))
                        return null;
                }
                continue;
            }

            int wordStart = i;
            while (i < n && char.IsLetter(line[i]))
                i++;
            if (i == wordStart)
                break;
            var word = line.Substring(wordStart, i - wordStart);
            if (!JavaCtorModifiers.Contains(word))
            {
                i = wordStart;
                break;
            }
        }

        SkipWhitespace(line, ref i);

        if (i < n && line[i] == '<')
        {
            if (!SkipBalancedAngles(line, ref i))
                return null;
            SkipWhitespace(line, ref i);
        }

        int nameStart = i;
        if (!ConsumeIdentifier(line, ref i))
            return null;
        var name = line.Substring(nameStart, i - nameStart);

        SkipWhitespace(line, ref i);
        if (i >= n || line[i] != '(')
            return null;
        if (!SkipBalancedParens(line, ref i))
            return null;
        SkipWhitespace(line, ref i);

        if (i + 6 <= n && string.CompareOrdinal(line, i, "throws", 0, 6) == 0 &&
            (i + 6 == n || char.IsWhiteSpace(line[i + 6])))
        {
            i += 6;
            while (i < n && line[i] != '{')
                i++;
        }

        SkipWhitespace(line, ref i);
        return i < n && line[i] == '{' ? name : null;
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
    }

    private static bool ConsumeIdentifier(string text, ref int i)
    {
        if (i >= text.Length)
            return false;
        var c = text[i];
        if (!(char.IsLetter(c) || c == '_' || c == '$'))
            return false;
        i++;
        while (i < text.Length)
        {
            c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
                i++;
            else
                break;
        }
        return true;
    }

    private static bool ConsumeQualifiedIdentifier(string text, ref int i)
    {
        if (!ConsumeIdentifier(text, ref i))
            return false;
        while (i < text.Length && text[i] == '.')
        {
            int save = i;
            i++;
            if (!ConsumeIdentifier(text, ref i))
            {
                i = save;
                break;
            }
        }
        return true;
    }

    private static bool SkipBalancedParens(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '(')
            return false;
        int depth = 0;
        for (; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    i++;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool SkipBalancedAngles(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '<')
            return false;
        int depth = 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '<') depth++;
            else if (c == '>')
            {
                depth--;
                if (depth == 0)
                {
                    i++;
                    return true;
                }
            }
            else if (c == '{' || c == ';')
            {
                return false;
            }
        }
        return false;
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
    /// Build a list of line ranges paired with synthetic function-kind containers for C# record
    /// declarations that carry a base primary-constructor call (e.g. `record Child(int x) : Parent(x)`
    /// or the multi-line form where `: Parent(x)` sits on a continuation line). SymbolExtractor does
    /// not synthesize a separate ctor symbol for the implicit primary constructor, so the `Parent(x)`
    /// reference would otherwise land on `container = null` (when the declaration line has no body
    /// range) or on the record class itself. Overriding the container over the whole header range
    /// keeps the chain edge attached to a synthetic function named after the record. Body lines past
    /// `;` / `{` are not included, so methods inside a braced record still resolve to their real
    /// containers via FindInnermostContainer.
    /// C# record のプライマリコンストラクタ宣言に対して、宣言ヘッダー全体を合成 function コンテナで
    /// 上書きするための (start, end, container) リストを作る。multi-line 宣言でも `Parent(...)` が
    /// header 末尾にあれば拾える。
    /// </summary>
    private static List<(int StartLine, int EndLine, SymbolRecord Container)> BuildCSharpRecordPrimaryCtorContainers(
        string language,
        IReadOnlyList<SymbolRecord> symbols,
        string[] structuralLines)
    {
        var ranges = new List<(int, int, SymbolRecord)>();
        if (language != "csharp")
            return ranges;

        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "class")
                continue;
            var signature = symbol.Signature;
            if (string.IsNullOrWhiteSpace(signature))
                continue;
            // Quick filter: only bother for declarations that look like a record primary-ctor opener.
            // 前段フィルタ: record のプライマリコンストラクタ宣言に見える場合だけ続行する。
            if (!CSharpRecordPrimaryCtorSignatureRegex.IsMatch(signature))
                continue;

            // Signature stored on the SymbolRecord is only the first declaration line, so for
            // multi-line forms it may not contain `: Parent(...)`. Walk the structural-masked lines
            // from StartLine until we hit `;` or `{` and examine the joined header text.
            // 宣言の signature は 1 行目だけなので、複数行宣言では `: Parent(...)` が含まれていない
            // ことがある。structuralLines を使って `;` / `{` までヘッダーを連結してから判定する。
            var (headerEndLine, headerText) = CollectCSharpRecordHeader(structuralLines, symbol.StartLine);
            if (!HasCSharpRecordBasePrimaryCtorCall(headerText))
                continue;

            var synthetic = new SymbolRecord
            {
                FileId = symbol.FileId,
                Kind = "function",
                Name = symbol.Name,
                Line = symbol.Line,
                StartLine = symbol.StartLine,
                EndLine = headerEndLine,
                BodyStartLine = symbol.StartLine,
                BodyEndLine = headerEndLine,
                Signature = signature,
                ContainerKind = symbol.ContainerKind,
                ContainerName = symbol.ContainerName,
                ContainerQualifiedName = symbol.ContainerQualifiedName,
                FamilyKey = symbol.FamilyKey,
                Visibility = symbol.Visibility,
            };

            ranges.Add((symbol.StartLine, headerEndLine, synthetic));
        }

        return ranges;
    }

    /// <summary>
    /// Walk structural-masked lines starting at the 1-based <paramref name="startLine"/> and collect
    /// the declaration header up to (but not including) the first `;` or `{` that sits outside a
    /// string or comment. Returns the 1-based line number where the terminator was found (or the
    /// final line index when none was found) and the joined header text for further parsing.
    /// Reused for record primary-ctor container synthesis and multi-line `: base(...)` resolution.
    /// structuralLines を使って、class / struct / record 宣言ヘッダーを最初の `;` / `{` まで連結する。
    /// record primary-ctor のコンテナ合成と、複数行 `: base(...)` 解決の両方で使う。
    /// </summary>
    internal static (int EndLine, string Text) CollectCSharpRecordHeader(string[] structuralLines, int startLine)
    {
        var startIdx = Math.Max(0, startLine - 1);
        if (structuralLines.Length == 0)
            return (startLine, string.Empty);

        // Depth-aware termination so that `{` / `;` inside annotation arg lists (e.g. the `{` in
        // `@Ann({A.class, B.class})`), parentheses, or angle brackets does not cut the header
        // off before the real base-list terminator, which would silently drop the base type.
        // アノテーション引数の `{` などを本当のヘッダ終端と誤認しないよう、`()` / `[]` / `<>`
        // の深さを追いながら最初の top-level `;` / `{` でのみ終了する。
        var sb = new System.Text.StringBuilder();
        int parenDepth = 0;
        int bracketDepth = 0;
        int angleDepth = 0;
        for (int i = startIdx; i < structuralLines.Length; i++)
        {
            var line = structuralLines[i];
            var terminatorIdx = -1;
            for (int j = 0; j < line.Length; j++)
            {
                var c = line[j];
                if (c == '(') parenDepth++;
                else if (c == ')') { if (parenDepth > 0) parenDepth--; }
                else if (c == '[') bracketDepth++;
                else if (c == ']') { if (bracketDepth > 0) bracketDepth--; }
                else if (c == '<') angleDepth++;
                else if (c == '>') { if (angleDepth > 0) angleDepth--; }
                else if ((c == ';' || c == '{') && parenDepth == 0 && bracketDepth == 0 && angleDepth == 0)
                {
                    terminatorIdx = j;
                    break;
                }
            }

            if (terminatorIdx >= 0)
            {
                sb.Append(line, 0, terminatorIdx);
                return (i + 1, sb.ToString());
            }

            sb.Append(line);
            sb.Append('\n');
        }

        return (structuralLines.Length, sb.ToString());
    }

    /// <summary>
    /// Returns true when the C# type header text carries a base-list entry that looks like a
    /// primary-constructor call (contains `(`). Accepts multi-line header text already joined by
    /// <see cref="CollectCSharpRecordHeader"/>.
    /// C# 型ヘッダー（複数行連結後でも可）の base-list 先頭エントリが `(` を含むかを判定する。
    /// </summary>
    private static bool HasCSharpRecordBasePrimaryCtorCall(string headerText)
    {
        var text = headerText.TrimEnd();
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
    /// 例: `class B extends A implements IFoo` → `A`、
    /// `class Leaf extends Outer<Integer>.Base {` → `Base`。
    /// </summary>
    internal static string? ParseJavaBaseType(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return null;

        var match = JavaExtendsRegex.Match(signature);
        if (!match.Success)
            return null;

        int start = match.Index + match.Length;
        int i = start;
        int angleDepth = 0;
        int parenDepth = 0;
        while (i < signature.Length)
        {
            char c = signature[i];
            if (c == '<')
            {
                angleDepth++;
            }
            else if (c == '>')
            {
                if (angleDepth > 0) angleDepth--;
            }
            else if (c == '(')
            {
                // Track `(...)` depth so that commas inside annotation arguments such as
                // `@Ann(a = 1, b = 2) Root` or `@Ann({A.class, B.class}) Root` are not mistaken
                // for top-level base-list separators. Without this the scanner breaks at the
                // inner `,`, feeds a truncated segment to the annotation stripper, and the
                // super(...) edge gets misattributed or dropped entirely.
                // annotation 引数内のカンマ（`@Ann(a = 1, b = 2) Root` や
                // `@Ann({A.class, B.class}) Root`）が base-list 区切りと誤認されないよう `(...)` の
                // 深さも追跡する。これをやらないと内側の `,` で走査が切れ、annotation stripper に
                // 壊れたセグメントが渡って super(...) の連鎖エッジが落ちる。
                parenDepth++;
            }
            else if (c == ')')
            {
                if (parenDepth > 0) parenDepth--;
            }
            else if (angleDepth == 0 && parenDepth == 0)
            {
                if (c == '{' || c == ',' || c == ';')
                    break;
                // Stop at a word-boundary `implements` or `permits` (Java 17+ sealed types).
                // 単語境界の `implements` / `permits` (Java 17+ sealed 型) で停止する。
                if (IsJavaBaseListTerminatorKeyword(signature, i, start, "implements") ||
                    IsJavaBaseListTerminatorKeyword(signature, i, start, "permits"))
                {
                    break;
                }
            }
            i++;
        }

        var segment = signature.Substring(start, i - start).Trim();
        if (segment.Length == 0)
            return null;

        // Strip Java type-use annotations (JLS 9.7.4): `@Ann`, `@pkg.Ann`, `@Ann(value=1)` can
        // appear before the type itself (`extends @Ann Root`) or between nested-type segments
        // (`Outer<Integer>.@Ann Base`). Without this pass the base resolver returns a phantom
        // type name like `@Ann Root` that misattributes references / callers / impact.
        // Java の type-use annotation (JLS 9.7.4) を剥がす。`extends @Ann Root` や
        // `Outer<Integer>.@Ann Base` のような形で基底型の直前やセグメント間に現れるため、
        // 先に除去しないと `@Ann Root` のような幽霊シンボルへ参照が張られてしまう。
        segment = StripJavaTypeAnnotations(segment);
        return segment.Length == 0 ? null : ExtractBareTypeName(segment);
    }

    private static string StripJavaTypeAnnotations(string text)
    {
        if (text.IndexOf('@') < 0)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '@')
            {
                // Skip `@` + qualified identifier (`@pkg.Ann`) + optional balanced `(...)`.
                i++;
                while (i < text.Length && (IsJavaIdentifierPart(text[i]) || text[i] == '.'))
                    i++;
                if (i < text.Length && text[i] == '(')
                {
                    int parenDepth = 1;
                    i++;
                    while (i < text.Length && parenDepth > 0)
                    {
                        if (text[i] == '(') parenDepth++;
                        else if (text[i] == ')') parenDepth--;
                        i++;
                    }
                }
                // Drop a single trailing whitespace run so `@Ann Root` collapses to `Root`.
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                continue;
            }
            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool IsJavaIdentifierPart(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static bool IsJavaBaseListTerminatorKeyword(string signature, int i, int start, string keyword)
    {
        if (i + keyword.Length > signature.Length)
            return false;
        if (i != start && IsJavaIdentifierPart(signature[i - 1]))
            return false;
        if (string.CompareOrdinal(signature, i, keyword, 0, keyword.Length) != 0)
            return false;
        if (i + keyword.Length < signature.Length && IsJavaIdentifierPart(signature[i + keyword.Length]))
            return false;
        return true;
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

        // Split on `.` / `::` at generic depth 0, then return the last segment with generic
        // args stripped. Naive "first `<`, then last `.`" slicing loses nested types such as
        // `Outer<int>.Base`, `Outer<Integer>.Base`, or `global::Ns.Outer<T>.Inner`.
        // 最初の `<` で切ってから末尾 `.` を探す素朴な方法では `Outer<int>.Base` のような
        // ネスト型を取り違えるため、generic 深度 0 の `.` / `::` でセグメント分割して末尾だけ返す。
        int lastSegmentStart = 0;
        int angleDepth = 0;
        int endIndex = trimmed.Length;
        for (int i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '<')
            {
                angleDepth++;
            }
            else if (c == '>')
            {
                if (angleDepth > 0) angleDepth--;
            }
            else if (angleDepth == 0)
            {
                if (c == '(')
                {
                    // Strip record primary-ctor args at top level: `A(...)` → `A`.
                    // record のプライマリコンストラクタ引数を剥がす。
                    endIndex = i;
                    break;
                }
                if (c == '.')
                {
                    lastSegmentStart = i + 1;
                }
                else if (c == ':' && i + 1 < trimmed.Length && trimmed[i + 1] == ':')
                {
                    lastSegmentStart = i + 2;
                    i++;
                }
            }
        }

        var segment = trimmed.Substring(lastSegmentStart, endIndex - lastSegmentStart).Trim();
        var ltIndex = segment.IndexOf('<');
        if (ltIndex >= 0)
            segment = segment.Substring(0, ltIndex);

        segment = segment.Trim();
        return segment.Length > 0 ? segment : null;
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
