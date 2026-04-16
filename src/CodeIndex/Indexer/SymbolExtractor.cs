using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts symbols (functions, classes, imports) using regex patterns.
/// 正規表現を使ってシンボル（関数、クラス、インポート）を抽出する。
/// </summary>
public static class SymbolExtractor
{
    private static readonly Regex PartialModifierRegex = new(@"\bpartial\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private enum BodyStyle
    {
        None,
        Brace,
        Indent,
        RubyEnd,
        VisualBasicEnd,
    }

    private sealed record SymbolPattern(
        string Kind,
        Regex Regex,
        BodyStyle BodyStyle,
        string? VisibilityGroup = null,
        string? ReturnTypeGroup = null);

    private enum JavaScriptLexMode
    {
        Code,
        SingleQuote,
        DoubleQuote,
        TemplateString,
        BlockComment,
    }

    private enum JavaScriptPrevTokenKind
    {
        None,
        Identifier,
        Number,
        CloseParen,
        CloseBracket,
        CloseBrace,
        Other,
    }

    private enum CSharpLexMode
    {
        Code,
        String,
        Char,
        VerbatimString,
        RawString,
        BlockComment,
    }

    private enum JavaScriptScopeKind
    {
        Other,
        Block,
        Function,
        StaticBlock,
        Class,
        Namespace,
        Object,
    }

    [Flags]
    private enum JavaScriptScopePrivacyFlags
    {
        None = 0,
        FunctionLike = 1,
        Block = 2,
        Namespace = 4,
    }

    private readonly record struct JavaScriptLexState(
        JavaScriptLexMode Mode = JavaScriptLexMode.Code,
        bool EscapeNext = false,
        JavaScriptPrevTokenKind PreviousTokenKind = JavaScriptPrevTokenKind.None,
        string? PreviousIdentifier = null,
        bool ExpectingControlFlowOpenParen = false,
        int ControlFlowParenDepth = 0,
        bool RegexAllowedAfterControlFlowParen = false);

    private readonly record struct JavaScriptLexedLine(
        string SanitizedLine,
        JavaScriptLexState EndState);

    private readonly record struct CSharpLexState(
        CSharpLexMode Mode = CSharpLexMode.Code,
        bool EscapeNext = false,
        int RawDelimiterLength = 0);

    private readonly record struct CSharpLexedLine(
        string SanitizedLine,
        CSharpLexState EndState);

    private readonly record struct RecordPrimaryComponent(
        string Name,
        string Type,
        string Signature,
        int Line);

    private readonly record struct RecordPrimaryComponentSlice(
        string Text,
        int Line);

    private readonly record struct PendingRecordPrimaryComponents(
        long FileId,
        string Kind,
        string RecordName,
        int RecordStartLine,
        List<RecordPrimaryComponent> Components);

    private readonly record struct StrippedRecordComponentText(
        string Text,
        int ConsumedNewlines);

    private readonly record struct JavaScriptClassScanTarget(
        int StartIndex,
        int StartColumn,
        int ScanStartIndex,
        int ScanEndExclusive,
        int FirstLineScanOffset,
        string ContainerKind,
        string ContainerName);

    private static readonly HashSet<string> TypeScriptBareMethodModifiers =
    [
        "public", "private", "protected", "static", "readonly", "abstract", "override", "async", "get", "set"
    ];

    private static readonly HashSet<string> JavaScriptTypeScriptControlFlowHeaderKeywords =
    [
        "if", "for", "while", "switch", "catch", "with"
    ];

    private readonly record struct JavaScriptTypeScriptMethodHeaderInfo(
        string Name,
        int BodyStartColumn,
        string? Visibility = null,
        int? GenericStartColumn = null,
        int? GenericEndColumn = null,
        int? ReturnTypeStartColumn = null,
        int? ReturnTypeEndColumn = null,
        int? HeaderEndColumn = null,
        bool HasBody = true);

    private readonly record struct JavaScriptTypeScriptMethodHeaderCapture(
        string SourceHeader,
        JavaScriptTypeScriptMethodHeaderInfo HeaderInfo,
        int HeaderEndLineIndex,
        int HeaderEndColumn,
        int BodyStartLineIndex,
        int BodyStartColumn);

    private struct JavaScriptTypeScriptFunctionHeaderState
    {
        public bool Active;
        public bool SawParameterList;
        public bool InReturnType;
        public int ParenDepth;
        public int BracketDepth;
        public int BraceDepth;
        public int ReturnParenDepth;
        public int ReturnBracketDepth;
        public int ReturnAngleDepth;
        public int ReturnBraceDepth;
        public bool ReturnSawToken;
        public string? PreviousReturnToken;
    }

    private enum JavaScriptTypeScriptMethodHeaderParseStatus
    {
        IncompleteOrInvalid = 0,
        Parsed = 1,
        DeclarationOnly = 2,
    }

    private enum JavaScriptTypeScriptFunctionHeaderConsumeResult
    {
        NotActive = 0,
        Consumed = 1,
        BodyStart = 2,
    }

    private const string JavaScriptTypeScriptIdentifierPattern = @"[$\p{L}_][$\p{L}\p{Nd}_]*";

    private static readonly Regex JavaScriptTypeScriptAnonymousDefaultExportRegex = new(
        @"^\s*(?<visibility>export)\s+default\b",
        RegexOptions.Compiled);

    private static readonly Regex JavaScriptTypeScriptClassExpressionBindingRegex = new(
        $@"^\s*(?:(?<visibility>export)\s+)?(?:(?<bindingKind>const|let|var)\s+(?<alias>{JavaScriptTypeScriptIdentifierPattern})|exports\.(?<exportsAlias>{JavaScriptTypeScriptIdentifierPattern})|module\.exports\.(?<moduleExportsAlias>{JavaScriptTypeScriptIdentifierPattern})|(?<moduleExports>module\.exports))\s*=",
        RegexOptions.Compiled);

    private static readonly Regex TypeScriptExportEqualsRegex = new(
        @"^\s*export\s*=",
        RegexOptions.Compiled);

    private const string VbVisibilityPattern = @"(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?";
    private const string VbTypeModifierPattern = @"(?:Partial|MustInherit|NotInheritable)";
    private const string VbMemberModifierPattern = @"(?:Shared|Overrides|Overridable|MustOverride|Async|Partial)";
    private const string VbPropertyModifierPattern = @"(?:Shared|Overrides|Overridable|MustOverride|Default|ReadOnly|WriteOnly)";
    private const string VbEventModifierPattern = @"(?:Shared|Custom)";

    private static readonly Dictionary<string, List<SymbolPattern>> PatternCache = new()
    {
        ["python"] =
        [
            new("function", new Regex(@"^\s*(?:async\s+)?def\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Indent),
            new("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Indent),
            new("import",   new Regex(@"^\s*(?:from\s+(?<name>[\w.]+)\s+import\b|import\s+(?<name>[\w.]+))", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["javascript"] =
        [
            new("function", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:async\s+)?function\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("function", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:const|let|var)\s+(?<name>\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[^=])\s*=>", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("class",    new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:default\s+)?class\s+(?<name>(?!extends\b)\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+?)\s+from\s+", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["typescript"] =
        [
            new("function", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:async\s+)?function\s+(?<name>\w+)\s*[\(<]", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("function", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:const|let|var)\s+(?<name>\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[^=])\s*=>", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Abstract class, declare class / 抽象クラス、declare クラス
            new("class",    new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:default\s+)?(?:(?:abstract|declare)\s+)*class\s+(?<name>(?!(?:extends|implements)\b)\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // namespace/module — supports both identifier (namespace Foo) and quoted ambient (declare module 'express')
            // 名前空間・モジュール — 識別子形式と引用符付きアンビエント形式の両方に対応
            new("namespace", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:declare\s+)?(?:namespace|module)\s+['""](?<name>[^'""]+)['""]", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("namespace", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:declare\s+)?(?:namespace|module)\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("interface", new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:declare\s+)?interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("class",    new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:declare\s+)?type\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("enum",     new Regex(@"^\s*(?:(?<visibility>export)\s+)?(?:declare\s+)?(?:const\s+)?enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+?)\s+from\s+", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["csharp"] =
        [
            new("namespace", new Regex(@"^\s*namespace\s+(?<name>[\w.]+)\s*;", RegexOptions.Compiled), BodyStyle.None),  // file-scoped namespace (C# 10+)
            new("namespace", new Regex(@"^\s*namespace\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.Brace),  // block-scoped namespace
            // using alias (using X = Y;) — must come before general using to capture alias name
            // using エイリアス — 一般 using より前に配置しエイリアス名を取得
            new("import",    new Regex(@"^\s*(?:global\s+)?using\s+(?<name>\w+)\s*=\s*[^;]+;", RegexOptions.Compiled), BodyStyle.None),
            new("import",    new Regex(@"^\s*(?:global\s+)?using\s+(?:static\s+)?(?<name>[^;=]+);", RegexOptions.Compiled), BodyStyle.None),
            // Const field — must come before class/method patterns to avoid misclassification
            // const フィールド — クラス/メソッドパターンより前に配置し誤分類を防ぐ
            new("function",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:new|static)\s+)*const\s+(?<returnType>[\w?.<>\[\],:]+)\s+(?<name>\w+)\s*=", RegexOptions.Compiled), BodyStyle.None, "visibility", "returnType"),
            // Static readonly field / static readonly フィールド
            new("function",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:new)\s+)?static\s+readonly\s+(?<returnType>[\w?.<>\[\],:\s]+?)\s+(?<name>\w+)\s*[=;]", RegexOptions.Compiled), BodyStyle.None, "visibility", "returnType"),
            // Interface — visibility optional / インターフェース — visibility 省略可
            new("interface", new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:partial|unsafe)\s+)*interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Enum — visibility optional / enum — visibility 省略可
            new("enum",      new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:file)\s+)*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Struct (including record struct, ref struct, readonly struct) — visibility optional
            // 構造体（record struct, ref struct, readonly struct を含む）— visibility 省略可
            new("struct",    new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|partial|readonly|file|new|ref|unsafe)\s+)*(?:record\s+)?struct\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Class (including record, record class) — visibility optional (defaults to internal for top-level)
            // クラス（record, record class を含む）— visibility は省略可能（トップレベルでは internal がデフォルト）
            new("class",     new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|partial|abstract|sealed|readonly|file|new|unsafe)\s+)*(?:record\s+class\s+|record\s+|class\s+)(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Implicit/explicit conversion operator — must come before general operator pattern
            // 暗黙的/明示的変換演算子 — 一般のoperatorパターンより先に配置
            new("function",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?static\s+(?<name>implicit|explicit)\s+operator\b", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Operator overload (+ - * / == != < > etc.) — must come before method pattern
            // 演算子オーバーロード — メソッドパターンより前に配置
            new("function",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?static\s+\S+\s+operator\s+(?<name>\S+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Method with return type — visibility optional for explicit interface impl and nested members.
            // Negative lookahead excludes call-site lines (await/return/throw/yield/var/typeof/sizeof/nameof/default/if/for/while/switch/catch/lock/using)
            // and ternary continuation branches (`? Foo(...)` / `: Foo(...)`) that would otherwise resemble returnType + name.
            // Note: `new` is NOT excluded because `new void Hidden()` is a valid C# member-hiding declaration.
            // 戻り値型付きメソッド — 明示的インターフェース実装やネストメンバー向けに visibility 省略可。
            // negative lookahead で呼び出し行（await/return/throw/yield/var/typeof 等）と ternary continuation を除外する。
            // 注意: `new` は除外しない。`new void Hidden()` は C# のメンバー隠蔽宣言として有効。
            new("function",  new Regex(@"^\s*(?![?:])(?!(?:await|return|throw|yield|var|typeof|sizeof|nameof|default|if|for|foreach|while|switch|catch|lock|using|case|else|when|break|continue|goto)\b)(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|sealed|partial|readonly|unsafe|extern|virtual|override|abstract|async|new|file)\s+)*(?<returnType>\([^)]+\)|(?:global::)?[\w?.<>\[\],:]+)\s+(?<name>\w+)\s*(?:<[^>]+>\s*)?\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
            // Constructor (no return type, name followed by parenthesis) — needs visibility
            // コンストラクタ（戻り値なし、名前の後に括弧）— visibility 必須
            new("function",  new Regex(@"^\s*(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Property with get/set/init — visibility optional
            // プロパティ（get/set/init）— visibility 省略可
            new("property",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|virtual|override|abstract|sealed|new|required)\s+)*(?<returnType>(?:global::)?[\w?.<>\[\],:]+)\s+(?<name>\w+)\s*\{\s*(?:get|set|init)", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
            // Expression-bodied property (public int X => ...) — must come before delegate
            // 式本体プロパティ (public int X => ...) — delegate の前に配置
            new("property",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|virtual|override|abstract|sealed|new|required)\s+)*(?<returnType>(?:global::)?[\w?.<>\[\],:]+)\s+(?<name>\w+)\s*=>\s*", RegexOptions.Compiled), BodyStyle.None, "visibility", "returnType"),
            // Delegate — visibility optional / デリゲート — visibility 省略可
            new("delegate",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|unsafe)\s+)?delegate\s+\S+\s+(?<name>\w+)\s*[\(<]", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            // Event — visibility optional / イベント — visibility 省略可
            new("event",     new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static)\s+)?event\s+\S+\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            // Explicit interface implementation (e.g. void IDisposable.Dispose())
            // Requires a valid return type (not a statement keyword) and interface name before the dot.
            // Reject named-argument labels only when they are followed by a qualified call site,
            // so alias-qualified types like `global::System.String` and `Alias::Type` still match.
            // 明示的インターフェース実装 (例: void IDisposable.Dispose())
            // 有効な戻り値型（ステートメントキーワードではない）とドット前のインターフェース名を要求。
            // qualified call site を伴う named-argument label のみ除外し、
            // `global::System.String` や `Alias::Type` のような alias-qualified 型は許可する。
            new("function",  new Regex(@"^\s*(?!(?:await|return|throw|yield|var|typeof|sizeof|nameof|default|if|for|foreach|while|switch|catch|lock|using|case|else|when|break|continue|goto)\b)(?!\w+\s*:\s*(?:global::)?[\w.<>:]+\.\w+\s*(?:<[^>]+>\s*)?[\(\[])(?<returnType>(?:global::)?[\w?.<>\[\],:]+)\s+(?:global::)?[\w.<>:]+\.(?<name>\w+)\s*(?:<[^>]+>\s*)?[\(\[]", RegexOptions.Compiled), BodyStyle.Brace, ReturnTypeGroup: "returnType"),
            // Indexer (this[...]) / インデクサ (this[...])
            new("function",  new Regex(@"^\s*(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|virtual|override|abstract|sealed|new)\s+)*(?<returnType>(?:global::)?[\w?.<>\[\],:]+)\s+(?<name>this)\s*\[", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
            // Static constructor / 静的コンストラクタ
            new("function",  new Regex(@"^\s*static\s+(?<name>\w+)\s*\(\s*\)\s*\{?", RegexOptions.Compiled), BodyStyle.Brace),
            // Finalizer (destructor) / ファイナライザ（デストラクタ）
            new("function",  new Regex(@"^\s*~(?<name>\w+)\s*\(\s*\)", RegexOptions.Compiled), BodyStyle.Brace),
            // Enum member (e.g. Red, Green = 1,) — requires 4+ spaces indent, name only,
            // and optional = with numeric/hex/identifier value. Does NOT match string/object assignments.
            // enum メンバー（例: Red, Green = 1,）— 4+スペースインデント必須、名前のみ、
            // 数値/16進/識別子の値指定はオプション。文字列/オブジェクト代入にはマッチしない。
            new("function",  new Regex(@"^\s{2,}(?<name>[A-Z]\w*)\s*(?:=\s*(?:-?\d|0x|[A-Z]\w*(?:\s*\|))[^""']*)?,?\s*$", RegexOptions.Compiled), BodyStyle.None),
            // #region for navigation / ナビゲーション用 #region
            new("namespace", new Regex(@"^\s*#region\s+(?<name>.+)$", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["go"] =
        [
            new("function", new Regex(@"^func\s+(?:\([^)]+\)\s+)?(?<name>\w+)\s*[\(\[]", RegexOptions.Compiled), BodyStyle.Brace),
            new("struct",   new Regex(@"^type\s+(?<name>\w+)\s+struct\b", RegexOptions.Compiled), BodyStyle.Brace),
            new("interface", new Regex(@"^type\s+(?<name>\w+)\s+interface\b", RegexOptions.Compiled), BodyStyle.Brace),
            // Type alias (type Name = OtherType or type Name OtherType) / 型エイリアス
            new("class",    new Regex(@"^type\s+(?<name>\w+)\s+[=\w]", RegexOptions.Compiled), BodyStyle.None),
            // Const declaration inside const block / const ブロック内の定数宣言
            new("function", new Regex(@"^\s+(?<name>[A-Z]\w*)\s*=\s*", RegexOptions.Compiled), BodyStyle.None),
            // Package-level var / パッケージレベル変数
            new("function", new Regex(@"^var\s+(?<name>\w+)\s", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["rust"] =
        [
            // macro_rules! / マクロ定義
            new("function", new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?macro_rules!\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // const/static items / 定数・静的変数
            new("function", new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?(?:const|static)\s+(?<name>\w+)\s*:", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            // fn with expanded modifiers: async, const, unsafe, extern / 拡張修飾子: async, const, unsafe, extern
            new("function", new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?(?:(?:async|const|unsafe|extern\s+""C"")\s+)*fn\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("struct",   new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?(?:struct|union)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("enum",     new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("interface", new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?trait\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("class",    new Regex(@"^\s*impl(?:<[^>]+>)?\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            // mod / モジュール
            new("namespace", new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?mod\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // type alias / 型エイリアス
            new("class",    new Regex(@"^\s*(?:(?<visibility>pub(?:\([^)]*\))?)\s+)?type\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            new("import",   new Regex(@"^\s*use\s+(?<name>.+);", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["java"] =
        [
            // Annotation type (@interface) / アノテーション型
            new("class",    new Regex(@"^\s*(?<visibility>public|private|protected)?\s*@interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // record (Java 16+) — must come before general class pattern / record は一般クラスパターンの前に配置
            new("class",    new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|final|abstract|sealed|non-sealed|strictfp)\s+)*record\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Interface / インターフェース
            new("interface", new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|abstract|sealed|non-sealed|strictfp)\s+)*interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Enum / enum
            new("enum",     new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|strictfp)\s+)*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Class — with extended modifiers (final, sealed, static, abstract, strictfp)
            // クラス — 拡張修飾子対応（final, sealed, static, abstract, strictfp）
            new("class",    new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|final|abstract|sealed|non-sealed|strictfp)\s+)*class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Static final field (Java equivalent of C# const) — order-flexible (static final or final static), generic types with spaces
            // static final フィールド — 語順柔軟（static final / final static）、スペース含むジェネリック型対応
            new("function", new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|final)\s+){2}(?<returnType>[\w?.<>\[\],\s]+?)\s+(?<name>[A-Z_]\w*)\s*=", RegexOptions.Compiled), BodyStyle.None, "visibility", "returnType"),
            // Method with return type — expanded modifiers (default, native, synchronized, final)
            // 戻り値型付きメソッド — 拡張修飾子対応（default, native, synchronized, final）
            new("function", new Regex(@"^\s*(?<visibility>public|private|protected)?\s*(?:(?:static|abstract|synchronized|final|default|native|strictfp)\s+)*(?<returnType>\w+(?:<[^>]+>)?(?:\[\])?)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
            // Enum member (uppercase constant) — 2+ whitespace for any indent style (2-space, 4-space, tab)
            // enum メンバー（大文字定数）— 任意のインデントスタイル対応（2スペース、4スペース、タブ）
            new("function", new Regex(@"^\s{2,}(?<name>[A-Z]\w*)\s*(?:\([^)]*\))?\s*(?:,|\{|;)\s*$", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+);", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["kotlin"] =
        [
            // Companion object / コンパニオンオブジェクト
            new("class",    new Regex(@"^\s*companion\s+object\s*(?<name>\w*)", RegexOptions.Compiled), BodyStyle.Brace),
            // Interface / インターフェース
            new("interface", new Regex(@"^\s*(?<visibility>public|private|protected|internal)?\s*(?:(?:sealed|expect|actual)\s+)*interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Enum class / enum クラス
            new("enum",     new Regex(@"^\s*(?<visibility>public|private|protected|internal)?\s*(?:(?:expect|actual)\s+)*enum\s+class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Class/object with expanded modifiers: data, sealed, value, inner, annotation, expect, actual
            // クラス/オブジェクト — 拡張修飾子対応: data, sealed, value, inner, annotation, expect, actual
            new("class",    new Regex(@"^\s*(?<visibility>public|private|protected|internal)?\s*(?:(?:abstract|data|sealed|open|inner|value|annotation|expect|actual)\s+)*(?:class|object)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Extension function (fun Type.name) / 拡張関数
            new("function", new Regex(@"^\s*(?<visibility>public|private|protected|internal)?\s*(?:(?:suspend|inline|infix|operator|tailrec|external|expect|actual)\s+)*fun\s+(?:\w+(?:<[^>]+>)?\.)?(?<name>\w+)\s*[\(<](?:.*?\))?(?::\s*(?<returnType>[^ {=]+))?", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
            // Top-level val/var property / トップレベルプロパティ
            new("property", new Regex(@"^\s*(?<visibility>public|private|protected|internal)?\s*(?:(?:const|lateinit|override)\s+)?(?:val|var)\s+(?<name>\w+)\s*[=:]", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["ruby"] =
        [
            // attr_accessor/attr_reader/attr_writer as property declarations / プロパティ宣言
            new("property", new Regex(@"^\s*attr_(?:accessor|reader|writer)\s+:(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None),
            // scope/has_many/belongs_to (Rails DSL) — extracted as function for navigation
            new("function", new Regex(@"^\s*(?:scope|has_many|has_one|belongs_to)\s+:(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None),
            new("function", new Regex(@"^\s*def\s+(?:self\.)?(?<name>\w+[?!=]?)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("class",    new Regex(@"^\s*module\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("import",   new Regex(@"^\s*require(?:_relative)?\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["c"] =
        [
            new("function", new Regex(@"^(?!\s*(?:if|else|for|while|switch|return|sizeof|typedef)\s*[\(\{;])(?<returnType>(?:\w+[\s*]+)+)(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, ReturnTypeGroup: "returnType"),
            new("struct",   new Regex(@"^\s*(?:typedef\s+)?struct\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("enum",     new Regex(@"^\s*(?:typedef\s+)?enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("import",   new Regex(@"^\s*#include\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["cpp"] =
        [
            new("function", new Regex(@"^(?!\s*(?:if|else|for|while|switch|return|sizeof|typedef|using|namespace)\s*[\(\{;<])(?<returnType>(?:[\w:<>]+[\s*&]+)+)(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, ReturnTypeGroup: "returnType"),
            new("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("struct",   new Regex(@"^\s*struct\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("namespace", new Regex(@"^\s*namespace\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("enum",     new Regex(@"^\s*(?:typedef\s+)?enum\s+(?:class\s+)?(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("import",   new Regex(@"^\s*#include\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["php"] =
        [
            // Const declaration / 定数宣言
            new("function", new Regex(@"^\s*(?:(?<visibility>public|private|protected)\s+)?const\s+(?<name>\w+)\s*=", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            new("function", new Regex(@"^\s*(?:(?<visibility>public|private|protected)\s+)?(?:(?:static|abstract|final)\s+)*function\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Class with expanded modifiers: abstract, final, readonly (PHP 8.2+)
            // 拡張修飾子対応: abstract, final, readonly (PHP 8.2+)
            new("class",    new Regex(@"^\s*(?:(?:abstract|final|readonly)\s+)*class\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("interface", new Regex(@"^\s*interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("interface", new Regex(@"^\s*trait\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("enum",     new Regex(@"^\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            // Namespace / 名前空間
            new("namespace", new Regex(@"^\s*namespace\s+(?<name>[\w\\]+)", RegexOptions.Compiled), BodyStyle.Brace),
            // use (import) / use（インポート）
            new("import",   new Regex(@"^\s*use\s+(?<name>[\w\\]+)", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*(?:require|include)(?:_once)?\s*\(?\s*(?<name>.+?)\s*\)?;", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["swift"] =
        [
            new("function", new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*(?:(?:static|class|nonisolated|mutating|nonmutating)\s+)*(?:override\s+)?func\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("struct",    new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*(?:(?:final)\s+)*struct\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("enum",      new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("interface", new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*protocol\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // actor (Swift 5.5+) / アクター
            new("class",    new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*(?:(?:final|distributed)\s+)*(?:class|actor)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Type alias / 型エイリアス
            new("class",    new Regex(@"^\s*(?<visibility>public|private|internal|open|fileprivate)?\s*typealias\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None, "visibility"),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["fsharp"] =
        [
            new("function", new Regex(@"^\s*let\s+(?:rec\s+)?(?:(?:private|internal)\s+)?(?<name>\w+)\s+(?:\w|\()", RegexOptions.Compiled), BodyStyle.None),
            new("class",    new Regex(@"^\s*type\s+(?:(?:private|internal)\s+)?(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None),
            new("class",    new Regex(@"^\s*module\s+(?:(?:private|internal)\s+)?(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*open\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["vb"] =
        [
            new("namespace", new Regex(@"^\s*Namespace\s+(?<name>(?:Global\.)?[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.VisualBasicEnd),
            new("function", new Regex(@$"^\s*(?:(?:{VbMemberModifierPattern})\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:{VbMemberModifierPattern})\s+)*(?:Sub|Function)\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("property", new Regex(@$"^\s*(?:(?:{VbPropertyModifierPattern})\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:{VbPropertyModifierPattern})\s+)*Property\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("event",    new Regex(@$"^\s*(?:(?:{VbEventModifierPattern})\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:{VbEventModifierPattern})\s+)*Event\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("interface", new Regex(@$"^\s*(?:(?:{VbTypeModifierPattern})\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:{VbTypeModifierPattern})\s+)*Interface\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.VisualBasicEnd, "visibility"),
            new("enum",     new Regex(@$"^\s*(?:(?<visibility>{VbVisibilityPattern})\s+)?Enum\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("struct",   new Regex(@$"^\s*(?:(?:Partial)\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:Partial)\s+)*Structure\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.VisualBasicEnd, "visibility"),
            new("class",    new Regex(@$"^\s*(?:(?:{VbTypeModifierPattern})\s+)*(?:(?<visibility>{VbVisibilityPattern})\s+)?(?:(?:{VbTypeModifierPattern})\s+)*(?:Class|Module)\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.VisualBasicEnd, "visibility"),
            new("import",   new Regex(@"^\s*Imports\s+(?<name>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
        ],
        ["scala"] =
        [
            new("function", new Regex(@"^\s*(?<visibility>private|protected)?\s*(?:override\s+)?def\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("interface", new Regex(@"^\s*(?<visibility>private|protected)?\s*(?:sealed\s+)?trait\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("enum",     new Regex(@"^\s*(?<visibility>private|protected)?\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("class",    new Regex(@"^\s*(?<visibility>private|protected)?\s*(?:abstract\s+|sealed\s+|final\s+)?(?:case\s+)?(?:class|object)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("import",   new Regex(@"^\s*import\s+(?<name>.+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["haskell"] =
        [
            new("function", new Regex(@"^(?:>\s+|\s*)(?<name>[a-z_]\w*)\s+::", RegexOptions.Compiled), BodyStyle.None),
            new("interface", new Regex(@"^\s*class\s+(?<name>[A-Z]\w*)", RegexOptions.Compiled), BodyStyle.None),
            new("class",    new Regex(@"^\s*(?:data|newtype|type|instance)\s+(?<name>[A-Z]\w*)", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*import\s+(?:qualified\s+)?(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["r"] =
        [
            new("function", new Regex(@"^\s*(?<name>[\w.]+)\s*<-\s*function\s*\(", RegexOptions.Compiled), BodyStyle.Brace),
            new("import",   new Regex(@"^\s*(?:library|require)\s*\(\s*(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["lua"] =
        [
            new("function", new Regex(@"^\s*(?:local\s+)?function\s+(?<name>[\w.:]+)\s*\(", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*(?:local\s+\w+\s*=\s*)?require\s*\(?['""](?<name>[^'""]+)['""]", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["elixir"] =
        [
            new("function", new Regex(@"^\s*(?:def|defp)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("class",    new Regex(@"^\s*defmodule\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("interface", new Regex(@"^\s*defprotocol\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.RubyEnd),
            new("import",   new Regex(@"^\s*(?:import|alias|use|require)\s+(?<name>[\w.]+)", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["dart"] =
        [
            new("function", new Regex(@"^\s*(?!return\b|await\b|const\b|new\b|throw\b|yield\b|if\b|for\b|while\b|switch\b|catch\b)(?:(?:static|abstract|override|external)\s+)*(?<rt>\w[\w<>,\s\?]*?)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, ReturnTypeGroup: "rt"),
            new("enum",     new Regex(@"^\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*(?:abstract\s+)?(?:class|mixin)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*extension\s+(?<name>\w+)\s+on\s+", RegexOptions.Compiled), BodyStyle.Brace),
            new("import",   new Regex(@"^\s*import\s+'(?<name>[^']+)'", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["graphql"] =
        [
            new("interface", new Regex(@"^\s*interface\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("enum",     new Regex(@"^\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*(?:type|union|scalar|input)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*(?:query|mutation|subscription)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*(?:extend\s+type)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*schema\s*\{", RegexOptions.Compiled), BodyStyle.Brace),
        ],
        ["gradle"] =
        [
            new("function", new Regex(@"^\s*(?:task|def)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("import",   new Regex(@"^\s*(?:apply\s+plugin\s*:\s*|id\s*[\s(]\s*)['""](?<name>[^'""]+)['""]", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["makefile"] =
        [
            new("function", new Regex(@"^(?<name>[\w.-]+)\s*:", RegexOptions.Compiled), BodyStyle.None),  // Makefile targets / Makefileターゲット
        ],
        ["dockerfile"] =
        [
            new("function", new Regex(@"^\s*FROM\s+\S+\s+(?:AS|as)\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None),  // Named stage / 名前付きステージ
            new("class",    new Regex(@"^\s*FROM\s+(?<name>\S+)", RegexOptions.Compiled), BodyStyle.None),  // Base image / ベースイメージ
        ],
        ["protobuf"] =
        [
            new("class",    new Regex(@"^\s*message\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*enum\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*service\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*rpc\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.None),
            new("import",   new Regex(@"^\s*import\s+""(?<name>[^""]+)"";", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["shell"] =
        [
            // Bash/Zsh function declarations / Bash/Zsh 関数宣言
            new("function", new Regex(@"^\s*(?:function\s+)?(?<name>\w+)\s*\(\s*\)\s*\{?", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*function\s+(?<name>\w+)", RegexOptions.Compiled), BodyStyle.Brace),
        ],
        ["sql"] =
        [
            // CREATE TABLE/VIEW/PROCEDURE/FUNCTION / テーブル・ビュー・プロシージャ・関数の定義
            new("class",    new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:(?:(?:GLOBAL|LOCAL)\s+)?(?:TEMP|TEMPORARY)\s+|UNLOGGED\s+)?(?:TABLE|(?:MATERIALIZED\s+)?VIEW)\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("function", new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:PROCEDURE|FUNCTION|TRIGGER)\s+(?<name>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("enum",     new Regex(@"^\s*CREATE\s+TYPE\s+(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)\s+AS\s+ENUM\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("class",    new Regex(@"^\s*CREATE\s+TYPE\s+(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("namespace", new Regex(@"^\s*CREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:(?<name>(?!AUTHORIZATION\b)(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)|AUTHORIZATION\s+(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*))", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("class",    new Regex(@"^\s*CREATE\s+(?:SEQUENCE|DOMAIN)\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("import",   new Regex(@"^\s*CREATE\s+EXTENSION\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("class",    new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:UNIQUE\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?(?!ON\b)(?<name>(?:""[^""]+""|[\w]+)(?:\.(?:""[^""]+""|[\w]+))*)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("class",    new Regex(@"^\s*ALTER\s+TABLE\s+(?<name>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
        ],
        ["terraform"] =
        [
            // Terraform resource/data: capture the logical name (second quoted token), not the type
            // Terraform resource/data: 型ではなく論理名（第2引用トークン）をキャプチャ
            new("class",    new Regex(@"^\s*resource\s+""[^""]+""\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*data\s+""[^""]+""\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            new("class",    new Regex(@"^\s*module\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*variable\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*output\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            new("function", new Regex(@"^\s*locals\s*\{", RegexOptions.Compiled), BodyStyle.Brace),
        ],
        ["css"] =
        [
            // @import / @use (SCSS) / インポート
            new("import",   new Regex(@"^\s*@(?:import|use)\s+(?<name>.+?)\s*;", RegexOptions.Compiled), BodyStyle.None),
            // @mixin (SCSS) / ミックスイン
            new("function", new Regex(@"^\s*@mixin\s+(?<name>[\w-]+)", RegexOptions.Compiled), BodyStyle.Brace),
            // @keyframes / キーフレーム
            new("function", new Regex(@"^\s*@keyframes\s+(?<name>[\w-]+)", RegexOptions.Compiled), BodyStyle.Brace),
            // CSS class selector at top level (not nested) / トップレベルのCSSクラスセレクタ
            new("class",    new Regex(@"^\.(?<name>[\w-]+)\s*[,{]", RegexOptions.Compiled), BodyStyle.Brace),
            // CSS ID selector at top level / トップレベルのIDセレクタ
            new("function", new Regex(@"^#(?<name>[\w-]+)\s*[,{]", RegexOptions.Compiled), BodyStyle.Brace),
            // SCSS $variable declaration / SCSS 変数宣言
            new("property", new Regex(@"^\$(?<name>[\w-]+)\s*:", RegexOptions.Compiled), BodyStyle.None),
        ],
        ["powershell"] =
        [
            // Function/filter declarations / 関数・フィルタ宣言
            new("function", new Regex(@"^\s*(?:function|filter)\s+(?<name>[\w-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.Brace),
            // Class (PowerShell 5+) / クラス (PowerShell 5+)
            new("class",    new Regex(@"^\s*class\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.Brace),
            // Enum (PowerShell 5+) / enum (PowerShell 5+)
            new("enum",     new Regex(@"^\s*enum\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.Brace),
            // Import-Module / using module / モジュールインポート
            new("import",   new Regex(@"^\s*(?:Import-Module|using\s+module)\s+(?<name>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
        ],
        ["zig"] =
        [
            // Public and private function declarations / 公開・非公開の関数宣言
            new("function", new Regex(@"^\s*(?:(?<visibility>pub)\s+)?(?:inline\s+)?fn\s+(?<name>\w+)\s*\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Struct/union/enum defined via const / const による struct/union/enum 定義
            new("struct",   new Regex(@"^\s*(?:(?<visibility>pub)\s+)?const\s+(?<name>\w+)\s*=\s*(?:extern\s+|packed\s+)?struct\b", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("enum",     new Regex(@"^\s*(?:(?<visibility>pub)\s+)?const\s+(?<name>\w+)\s*=\s*(?:extern\s+)?enum\b", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            new("class",    new Regex(@"^\s*(?:(?<visibility>pub)\s+)?const\s+(?<name>\w+)\s*=\s*(?:extern\s+|packed\s+)?union\b", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Error set / エラーセット
            new("class",    new Regex(@"^\s*(?:(?<visibility>pub)\s+)?const\s+(?<name>\w+)\s*=\s*error\b", RegexOptions.Compiled), BodyStyle.Brace, "visibility"),
            // Test declarations / テスト宣言
            new("function", new Regex(@"^\s*test\s+""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.Brace),
            // @import / インポート
            new("import",   new Regex(@"^\s*(?:(?:pub)\s+)?const\s+\w+\s*=\s*@import\s*\(\s*""(?<name>[^""]+)""", RegexOptions.Compiled), BodyStyle.None),
        ],
    };

    /// <summary>
    /// Return the set of languages that have symbol-extraction patterns.
    /// シンボル抽出パターンを持つ言語のセットを返す。
    /// </summary>
    public static IReadOnlyCollection<string> GetSupportedLanguages() => PatternCache.Keys;

    private static readonly HashSet<string> ContainerKinds =
    [
        "class", "namespace"
    ];

    private static readonly Regex RubyBlockStartRegex = new(@"^\s*(?:class|module|def|if|unless|case|begin|do|while|until|for)\b", RegexOptions.Compiled);
    private static readonly Regex RubyBlockTokenRegex = new(@"\b(?:class|module|def|if|unless|case|begin|do|while|until|for|end)\b", RegexOptions.Compiled);
    private static readonly Regex VisualBasicContainerStartRegex = new(@$"^(?:Namespace\b|(?:(?:{VbTypeModifierPattern})\s+)*(?:(?:{VbVisibilityPattern})\s+)?(?:(?:{VbTypeModifierPattern})\s+)*(?:Class|Module|Structure|Interface)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VisualBasicContainerEndRegex = new(@"^End\s+(?:Namespace|Class|Module|Structure|Interface)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Extract symbols from the given source content.
    /// 指定されたソース内容からシンボルを抽出する。
    /// </summary>
    /// <param name="fileId">The file ID in the database / データベース上のファイルID</param>
    /// <param name="lang">Detected language / 検出された言語</param>
    /// <param name="content">Full file content / ファイル全体の内容</param>
    /// <returns>List of extracted symbols / 抽出されたシンボルのリスト</returns>
    public static List<SymbolRecord> Extract(long fileId, string? lang, string content)
    {
        if (lang == null || !PatternCache.TryGetValue(lang, out var patterns))
            return [];

        var lines = content.Split('\n');
        var structuralLines = StructuralLineMasker.MaskLines(lang, lines);
        var privateScopeColumns = lang is "javascript" or "typescript"
            ? BuildJavaScriptTypeScriptPrivateScopeColumns(lines, lang)
            : null;
        var csharpSwitchExpressionLines = lang == "csharp"
            ? FindCSharpSwitchExpressionLines(structuralLines)
            : null;
        var symbols = new List<SymbolRecord>();
        var pendingRecordPrimaryComponents = new List<PendingRecordPrimaryComponents>();
        var csharpLexState = new CSharpLexState();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var structuralLine = structuralLines[i];
            var matchLine = structuralLine;
            if (lang == "csharp")
            {
                var lexedLine = LexCSharpLine(structuralLine, csharpLexState);
                csharpLexState = lexedLine.EndState;
                matchLine = StripLeadingCSharpAttributeLists(lexedLine.SanitizedLine);
            }

            var stopAfterFirstPatternMatch = false;
            foreach (var pattern in patterns)
            {
                var lineOffset = lang is "javascript" or "typescript"
                    ? FindNextJavaScriptTypeScriptStatementStart(matchLine, 0)
                    : 0;
                while (lineOffset >= 0 && lineOffset < matchLine.Length)
                {
                    var match = pattern.Regex.Match(matchLine[lineOffset..]);
                    if (!match.Success)
                    {
                        if (lang is "javascript" or "typescript")
                        {
                            lineOffset = FindNextJavaScriptTypeScriptStatementStart(matchLine, lineOffset + 1);
                            continue;
                        }

                        break;
                    }

                    if (ShouldSkipCSharpSwitchExpressionPropertyCandidate(lang, pattern, matchLine, csharpSwitchExpressionLines, i))
                        break;

                    var absoluteStartColumn = lineOffset + match.Index;
                    if (privateScopeColumns != null
                        && pattern.Kind == "class"
                        && IsJavaScriptTypeScriptMatchInPrivateScope(privateScopeColumns, i, absoluteStartColumn, matchLine, includeBlockScope: true))
                    {
                        if (lang is "javascript" or "typescript")
                        {
                            var skippedEndColumn = pattern.BodyStyle == BodyStyle.Brace
                                ? FindJavaScriptTypeScriptSameLineBraceEndColumn(line, absoluteStartColumn, lang)
                                : -1;
                            lineOffset = skippedEndColumn >= absoluteStartColumn
                                ? FindNextJavaScriptTypeScriptStatementStart(matchLine, skippedEndColumn + 1)
                                : FindNextJavaScriptTypeScriptStatementStart(matchLine, absoluteStartColumn + Math.Max(1, match.Length));
                            continue;
                        }

                        break;
                    }

                    if (privateScopeColumns != null
                        && pattern.Kind == "class"
                        && TryGetGroup(match, pattern.VisibilityGroup) != "export"
                        && IsJavaScriptTypeScriptMatchInNamespaceScope(privateScopeColumns, i, absoluteStartColumn, matchLine))
                    {
                        if (lang is "javascript" or "typescript")
                        {
                            var skippedEndColumn = pattern.BodyStyle == BodyStyle.Brace
                                ? FindJavaScriptTypeScriptSameLineBraceEndColumn(line, absoluteStartColumn, lang)
                                : -1;
                            lineOffset = skippedEndColumn >= absoluteStartColumn
                                ? FindNextJavaScriptTypeScriptStatementStart(matchLine, skippedEndColumn + 1)
                                : FindNextJavaScriptTypeScriptStatementStart(matchLine, absoluteStartColumn + Math.Max(1, match.Length));
                            continue;
                        }

                        break;
                    }

                    var name = match.Groups["name"].Success
                        ? match.Groups["name"].Value.Trim()
                        : match.Value.Trim();

                    var (endLine, bodyStartLine, bodyEndLine) = ResolveRange(structuralLines, i, pattern.BodyStyle, lang, absoluteStartColumn);
                    var startLine = i + 1;

                    // Python @property decorator: reclassify the def as property
                    // Python @property デコレータ: def を property に再分類
                    var kind = pattern.Kind;
                    if (kind == "function" && lang == "python" && i > 0 && lines[i - 1].TrimStart().StartsWith("@property"))
                        kind = "property";

                    var sameLineEndColumn = lang is "javascript" or "typescript"
                        && pattern.BodyStyle == BodyStyle.Brace
                        && bodyEndLine == startLine
                        ? FindJavaScriptTypeScriptSameLineBraceEndColumn(line, absoluteStartColumn, lang)
                        : -1;

                    symbols.Add(new SymbolRecord
                    {
                        FileId = fileId,
                        Kind = kind,
                        Name = name,
                        Line = startLine,
                        StartLine = startLine,
                        EndLine = Math.Max(startLine, endLine),
                        BodyStartLine = bodyStartLine,
                        BodyEndLine = bodyEndLine,
                        Signature = sameLineEndColumn >= absoluteStartColumn
                            ? line[absoluteStartColumn..(sameLineEndColumn + 1)].Trim()
                            : line[absoluteStartColumn..].Trim(),
                        Visibility = TryGetGroup(match, pattern.VisibilityGroup),
                        ReturnType = NormalizeMetadata(TryGetGroup(match, pattern.ReturnTypeGroup)),
                    });

                    CollectRecordPrimaryComponentSymbols(
                        fileId,
                        lang,
                        lines,
                        i,
                        absoluteStartColumn,
                        kind,
                        name,
                        pendingRecordPrimaryComponents,
                        symbols);

                    if (lang is not "javascript" and not "typescript"
                        || pattern.BodyStyle != BodyStyle.Brace
                        || bodyEndLine != startLine
                        || sameLineEndColumn < absoluteStartColumn)
                    {
                        // Stop after first match per line to avoid duplicate symbols
                        // (e.g. C# method pattern + constructor pattern both matching)
                        // 1行につき最初のマッチのみ採用し重複を防ぐ
                        stopAfterFirstPatternMatch = true;
                        break;
                    }

                    lineOffset = FindNextJavaScriptTypeScriptStatementStart(matchLine, sameLineEndColumn + 1);
                }

                if (stopAfterFirstPatternMatch)
                    break;
            }
        }

        if (lang is "javascript" or "typescript")
            ExtractJavaScriptTypeScriptBareMethods(fileId, lang, lines, symbols, privateScopeColumns!);

        AssignContainers(symbols);
        MaterializeRecordPrimaryComponentSymbols(symbols, pendingRecordPrimaryComponents);
        PopulateDeclaredContainerQualifiedNames(symbols);
        return symbols;
    }

    private static void ExtractJavaScriptTypeScriptBareMethods(long fileId, string lang, string[] lines, List<SymbolRecord> symbols, JavaScriptScopePrivacyFlags[][] privateScopeColumns)
    {
        var existingClassTargets = GetJavaScriptTypeScriptExistingClassScanTargets(lang, lines, symbols);
        ExtractJavaScriptTypeScriptBareMethodsInTargets(fileId, lang, lines, symbols, existingClassTargets);

        var syntheticClassTargets = CollectJavaScriptTypeScriptSyntheticClassScanTargets(fileId, lang, lines, symbols, privateScopeColumns);
        ExtractJavaScriptTypeScriptBareMethodsInTargets(fileId, lang, lines, symbols, syntheticClassTargets);
    }

    private static List<JavaScriptClassScanTarget> GetJavaScriptTypeScriptExistingClassScanTargets(string lang, string[] lines, List<SymbolRecord> symbols)
    {
        return symbols
            .Where(s => s.Kind is "class" or "interface" && s.BodyStartLine != null && s.BodyEndLine != null)
            .OrderBy(s => s.StartLine)
            .ThenByDescending(s => s.EndLine)
            .Select(s => CreateJavaScriptClassScanTarget(
                lines,
                lang,
                s.StartLine - 1,
                FindJavaScriptTypeScriptSymbolStartColumn(lines[s.StartLine - 1], s.Signature),
                s.BodyStartLine,
                s.BodyEndLine,
                s.Kind,
                s.Name))
            .ToList();
    }

    private static List<JavaScriptClassScanTarget> CollectJavaScriptTypeScriptSyntheticClassScanTargets(long fileId, string lang, string[] lines, List<SymbolRecord> symbols, JavaScriptScopePrivacyFlags[][] privateScopeColumns)
    {
        var targets = new List<JavaScriptClassScanTarget>();
        var lexState = new JavaScriptLexState();
        for (int i = 0; i < lines.Length; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var lineOffset = FindNextJavaScriptTypeScriptStatementStart(sanitizedLine, 0);
            while (lineOffset >= 0 && lineOffset < sanitizedLine.Length)
            {
                TryAddJavaScriptTypeScriptSyntheticClassTarget(fileId, lang, lines, symbols, targets, i, lineOffset, sanitizedLine, privateScopeColumns);
                lineOffset = FindNextJavaScriptTypeScriptStatementStart(sanitizedLine, lineOffset + 1);
            }
        }

        return targets
            .OrderBy(t => t.StartIndex)
            .ThenByDescending(t => t.ScanEndExclusive)
            .ToList();
    }

    private static bool IsInsideJavaScriptTypeScriptPrivateScope(Stack<JavaScriptScopeKind> scopeStack)
    {
        return scopeStack.Any(scopeKind => scopeKind is JavaScriptScopeKind.Function or JavaScriptScopeKind.StaticBlock);
    }

    private static JavaScriptScopePrivacyFlags GetJavaScriptTypeScriptPrivacyFlags(Stack<JavaScriptScopeKind> scopeStack, bool arrowExpressionActive)
    {
        var flags = JavaScriptScopePrivacyFlags.None;
        if (arrowExpressionActive || IsInsideJavaScriptTypeScriptPrivateScope(scopeStack))
            flags |= JavaScriptScopePrivacyFlags.FunctionLike;
        if (scopeStack.Any(scopeKind => scopeKind == JavaScriptScopeKind.Block))
            flags |= JavaScriptScopePrivacyFlags.Block;
        if (scopeStack.Any(scopeKind => scopeKind == JavaScriptScopeKind.Namespace))
            flags |= JavaScriptScopePrivacyFlags.Namespace;

        return flags;
    }

    private static bool IsInsideJavaScriptTypeScriptMethodContainer(Stack<JavaScriptScopeKind> scopeStack)
    {
        return scopeStack.Count > 0 && scopeStack.Peek() is JavaScriptScopeKind.Class or JavaScriptScopeKind.Object;
    }

    private static void BeginJavaScriptTypeScriptFunctionHeader(ref JavaScriptTypeScriptFunctionHeaderState state)
    {
        state = new JavaScriptTypeScriptFunctionHeaderState
        {
            Active = true,
        };
    }

    private static JavaScriptTypeScriptFunctionHeaderConsumeResult ConsumeJavaScriptTypeScriptFunctionHeaderChar(
        ref JavaScriptTypeScriptFunctionHeaderState state,
        string sanitizedLine,
        int column,
        string lang,
        out int advanceColumns)
    {
        advanceColumns = 0;
        if (!state.Active)
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.NotActive;

        var ch = sanitizedLine[column];
        if (char.IsWhiteSpace(ch))
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;

        if (state.InReturnType)
        {
            if (ch == ';'
                && state.ReturnParenDepth == 0
                && state.ReturnBracketDepth == 0
                && state.ReturnAngleDepth == 0
                && state.ReturnBraceDepth == 0)
            {
                state = default;
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '(')
            {
                state.ReturnParenDepth++;
                state.ReturnSawToken = true;
                state.PreviousReturnToken = "(";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == ')' && state.ReturnParenDepth > 0)
            {
                state.ReturnParenDepth--;
                state.PreviousReturnToken = ")";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '[')
            {
                state.ReturnBracketDepth++;
                state.ReturnSawToken = true;
                state.PreviousReturnToken = "[";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == ']' && state.ReturnBracketDepth > 0)
            {
                state.ReturnBracketDepth--;
                state.PreviousReturnToken = "]";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '<')
            {
                state.ReturnAngleDepth++;
                state.ReturnSawToken = true;
                state.PreviousReturnToken = "<";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '>' && state.ReturnAngleDepth > 0)
            {
                state.ReturnAngleDepth--;
                state.PreviousReturnToken = ">";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '{')
            {
                if (state.ReturnParenDepth == 0
                    && state.ReturnBracketDepth == 0
                    && state.ReturnAngleDepth == 0
                    && state.ReturnBraceDepth == 0)
                {
                    if (CanStartJavaScriptTypeScriptReturnTypeObjectLiteral(state.PreviousReturnToken))
                    {
                        state.ReturnBraceDepth++;
                        state.ReturnSawToken = true;
                        state.PreviousReturnToken = "{";
                        return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
                    }

                    if (state.ReturnSawToken)
                    {
                        state = default;
                        return JavaScriptTypeScriptFunctionHeaderConsumeResult.BodyStart;
                    }
                }

                state.ReturnBraceDepth++;
                state.ReturnSawToken = true;
                state.PreviousReturnToken = "{";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '}' && state.ReturnBraceDepth > 0)
            {
                state.ReturnBraceDepth--;
                state.PreviousReturnToken = "}";
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch is '?' or ':' or '|' or '&' or ',')
            {
                state.ReturnSawToken = true;
                state.PreviousReturnToken = ch.ToString();
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            if (ch == '=' && column + 1 < sanitizedLine.Length && sanitizedLine[column + 1] == '>')
            {
                state.ReturnSawToken = true;
                state.PreviousReturnToken = "=>";
                advanceColumns = 1;
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            var returnTypeIndex = column;
            if (TrySkipTypeScriptTypeToken(sanitizedLine, ref returnTypeIndex, out var returnTypeToken))
            {
                state.ReturnSawToken = true;
                state.PreviousReturnToken = returnTypeToken;
                advanceColumns = returnTypeIndex - column - 1;
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
            }

            state.ReturnSawToken = true;
            state.PreviousReturnToken = ch.ToString();
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (lang == "typescript" && state.SawParameterList && ch == ':')
        {
            state.InReturnType = true;
            state.ReturnParenDepth = 0;
            state.ReturnBracketDepth = 0;
            state.ReturnAngleDepth = 0;
            state.ReturnBraceDepth = 0;
            state.ReturnSawToken = false;
            state.PreviousReturnToken = ":";
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == '(')
        {
            state.ParenDepth++;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == ')' && state.ParenDepth > 0)
        {
            state.ParenDepth--;
            if (state.ParenDepth == 0 && state.BracketDepth == 0 && state.BraceDepth == 0)
                state.SawParameterList = true;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == '[' && (state.ParenDepth > 0 || state.BracketDepth > 0 || state.BraceDepth > 0 || !state.SawParameterList))
        {
            state.BracketDepth++;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == ']' && state.BracketDepth > 0)
        {
            state.BracketDepth--;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == '{')
        {
            if (state.SawParameterList && state.ParenDepth == 0 && state.BracketDepth == 0 && state.BraceDepth == 0)
            {
                state = default;
                return JavaScriptTypeScriptFunctionHeaderConsumeResult.BodyStart;
            }

            state.BraceDepth++;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == '}' && state.BraceDepth > 0)
        {
            state.BraceDepth--;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        if (ch == ';')
        {
            state = default;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        var tokenIndex = column;
        if (TrySkipTypeScriptTypeToken(sanitizedLine, ref tokenIndex, out _))
        {
            advanceColumns = tokenIndex - column - 1;
            return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
        }

        return JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed;
    }

    private static JavaScriptScopePrivacyFlags[][] BuildJavaScriptTypeScriptPrivateScopeColumns(string[] lines, string lang)
    {
        var privateColumns = new JavaScriptScopePrivacyFlags[lines.Length][];
        var lexState = new JavaScriptLexState();
        var scopeStack = new Stack<JavaScriptScopeKind>();
        var pendingFunctionScope = false;
        var functionHeaderState = new JavaScriptTypeScriptFunctionHeaderState();
        var pendingStaticBlockScope = false;
        var pendingClassScope = false;
        var pendingNamespaceScope = false;
        var pendingConciseMethodScope = false;
        var pendingConciseMethodReturnType = false;
        var conciseMethodReturnParenDepth = 0;
        var conciseMethodReturnBracketDepth = 0;
        var conciseMethodReturnAngleDepth = 0;
        var conciseMethodReturnBraceDepth = 0;
        var conciseMethodReturnSawToken = false;
        string? previousConciseMethodReturnToken = null;
        var pendingArrowBody = false;
        var arrowExpressionActive = false;
        var arrowExpressionParenDepth = 0;
        var arrowExpressionBracketDepth = 0;
        var arrowExpressionBraceDepth = 0;
        var previousTokenKind = JavaScriptPrevTokenKind.None;
        string? previousIdentifier = null;
        char previousSignificantChar = '\0';

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var linePrivateColumns = new JavaScriptScopePrivacyFlags[sanitizedLine.Length];

            if (arrowExpressionActive
                && arrowExpressionBraceDepth == 0
                && arrowExpressionParenDepth == 0
                && arrowExpressionBracketDepth == 0
                && !StartsJavaScriptTypeScriptExpressionContinuation(sanitizedLine))
            {
                arrowExpressionActive = false;
            }

            for (int column = 0; column < sanitizedLine.Length; column++)
            {
                var ch = sanitizedLine[column];
                linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);

                if (char.IsWhiteSpace(ch))
                    continue;

                if (pendingArrowBody)
                {
                    if (ch == '{')
                    {
                        scopeStack.Push(JavaScriptScopeKind.Function);
                        linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                        pendingArrowBody = false;
                        continue;
                    }

                    arrowExpressionActive = true;
                    linePrivateColumns[column] = JavaScriptScopePrivacyFlags.FunctionLike;
                    pendingArrowBody = false;
                }

                if (pendingConciseMethodReturnType)
                {
                    if (ch == '(')
                    {
                        conciseMethodReturnParenDepth++;
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = "(";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == ')' && conciseMethodReturnParenDepth > 0)
                    {
                        conciseMethodReturnParenDepth--;
                        previousConciseMethodReturnToken = ")";
                        previousTokenKind = JavaScriptPrevTokenKind.CloseParen;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '[')
                    {
                        conciseMethodReturnBracketDepth++;
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = "[";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == ']' && conciseMethodReturnBracketDepth > 0)
                    {
                        conciseMethodReturnBracketDepth--;
                        previousConciseMethodReturnToken = "]";
                        previousTokenKind = JavaScriptPrevTokenKind.CloseBracket;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '<')
                    {
                        conciseMethodReturnAngleDepth++;
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = "<";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '>' && conciseMethodReturnAngleDepth > 0)
                    {
                        conciseMethodReturnAngleDepth--;
                        previousConciseMethodReturnToken = ">";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '{')
                    {
                        if (conciseMethodReturnParenDepth == 0
                            && conciseMethodReturnBracketDepth == 0
                            && conciseMethodReturnAngleDepth == 0
                            && conciseMethodReturnBraceDepth == 0)
                        {
                            if (CanStartJavaScriptTypeScriptReturnTypeObjectLiteral(previousConciseMethodReturnToken))
                            {
                                conciseMethodReturnBraceDepth++;
                                conciseMethodReturnSawToken = true;
                                previousConciseMethodReturnToken = "{";
                                previousTokenKind = JavaScriptPrevTokenKind.Other;
                                previousIdentifier = null;
                                previousSignificantChar = ch;
                                continue;
                            }

                            if (conciseMethodReturnSawToken)
                            {
                                linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                                scopeStack.Push(JavaScriptScopeKind.Function);
                                pendingConciseMethodScope = false;
                                pendingConciseMethodReturnType = false;
                                conciseMethodReturnParenDepth = 0;
                                conciseMethodReturnBracketDepth = 0;
                                conciseMethodReturnAngleDepth = 0;
                                conciseMethodReturnBraceDepth = 0;
                                conciseMethodReturnSawToken = false;
                                previousConciseMethodReturnToken = null;
                                previousTokenKind = JavaScriptPrevTokenKind.CloseBrace;
                                previousIdentifier = null;
                                previousSignificantChar = ch;
                                continue;
                            }
                        }

                        conciseMethodReturnBraceDepth++;
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = "{";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '}' && conciseMethodReturnBraceDepth > 0)
                    {
                        conciseMethodReturnBraceDepth--;
                        previousConciseMethodReturnToken = "}";
                        previousTokenKind = JavaScriptPrevTokenKind.CloseBrace;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '?' || ch == ':' || ch == '|' || ch == '&' || ch == ',')
                    {
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = ch.ToString();
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = ch;
                        continue;
                    }

                    if (ch == '=' && column + 1 < sanitizedLine.Length && sanitizedLine[column + 1] == '>')
                    {
                        linePrivateColumns[column + 1] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = "=>";
                        previousTokenKind = JavaScriptPrevTokenKind.Other;
                        previousIdentifier = null;
                        previousSignificantChar = '>';
                        column++;
                        continue;
                    }

                    if (IsJavaScriptTypeScriptIdentifierStart(ch))
                    {
                        var returnTokenStart = column;
                        column++;
                        while (column < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[column]))
                        {
                            linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                            column++;
                        }

                        conciseMethodReturnSawToken = true;
                        previousConciseMethodReturnToken = sanitizedLine[returnTokenStart..column];
                        previousTokenKind = JavaScriptPrevTokenKind.Identifier;
                        previousIdentifier = previousConciseMethodReturnToken;
                        previousSignificantChar = sanitizedLine[column - 1];
                        column--;
                        continue;
                    }

                    conciseMethodReturnSawToken = true;
                    previousConciseMethodReturnToken = ch.ToString();
                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (pendingFunctionScope)
                {
                    var functionHeaderResult = ConsumeJavaScriptTypeScriptFunctionHeaderChar(
                        ref functionHeaderState,
                        sanitizedLine,
                        column,
                        lang,
                        out var functionHeaderAdvanceColumns);
                    if (functionHeaderResult == JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed)
                    {
                        previousTokenKind = ch switch
                        {
                            ')' => JavaScriptPrevTokenKind.CloseParen,
                            ']' => JavaScriptPrevTokenKind.CloseBracket,
                            _ => JavaScriptPrevTokenKind.Other,
                        };
                        previousIdentifier = null;
                        previousSignificantChar = sanitizedLine[Math.Min(column + functionHeaderAdvanceColumns, sanitizedLine.Length - 1)];
                        column += functionHeaderAdvanceColumns;
                        continue;
                    }
                }

                if (IsJavaScriptTypeScriptIdentifierStart(ch))
                {
                    var tokenStart = column;
                    column++;
                    while (column < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[column]))
                    {
                        linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);

                        column++;
                    }

                    var token = sanitizedLine[tokenStart..column];
                    if (token == "function")
                    {
                        pendingFunctionScope = true;
                        BeginJavaScriptTypeScriptFunctionHeader(ref functionHeaderState);
                        pendingStaticBlockScope = false;
                        pendingConciseMethodScope = false;
                        pendingConciseMethodReturnType = false;
                    }
                    else if (token == "static")
                    {
                        pendingStaticBlockScope = IsInsideJavaScriptTypeScriptMethodContainer(scopeStack);
                    }
                    else if (token == "class")
                    {
                        pendingClassScope = true;
                        pendingStaticBlockScope = false;
                        pendingConciseMethodScope = false;
                        pendingConciseMethodReturnType = false;
                    }
                    else if (lang == "typescript" && token is "namespace" or "module")
                    {
                        pendingNamespaceScope = true;
                        pendingStaticBlockScope = false;
                    }
                    else
                    {
                        pendingStaticBlockScope = false;
                    }

                    previousTokenKind = JavaScriptPrevTokenKind.Identifier;
                    previousIdentifier = token;
                    previousSignificantChar = sanitizedLine[column - 1];
                    column--;
                    continue;
                }

                if (ch == '=' && column + 1 < sanitizedLine.Length && sanitizedLine[column + 1] == '>')
                {
                    linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                    linePrivateColumns[column + 1] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                    pendingArrowBody = true;
                    pendingFunctionScope = false;
                    functionHeaderState = default;
                    pendingStaticBlockScope = false;
                    pendingClassScope = false;
                    pendingNamespaceScope = false;
                    pendingConciseMethodScope = false;
                    pendingConciseMethodReturnType = false;
                    conciseMethodReturnParenDepth = 0;
                    conciseMethodReturnBracketDepth = 0;
                    conciseMethodReturnAngleDepth = 0;
                    conciseMethodReturnBraceDepth = 0;
                    conciseMethodReturnSawToken = false;
                    previousConciseMethodReturnToken = null;
                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = '>';
                    column++;
                    continue;
                }

                if (ch == '(')
                {
                    if (arrowExpressionActive)
                        arrowExpressionParenDepth++;

                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == ')')
                {
                    if (arrowExpressionActive && arrowExpressionParenDepth > 0)
                        arrowExpressionParenDepth--;

                    if (IsInsideJavaScriptTypeScriptMethodContainer(scopeStack))
                    {
                        pendingConciseMethodScope = true;
                        pendingConciseMethodReturnType = false;
                        conciseMethodReturnParenDepth = 0;
                        conciseMethodReturnBracketDepth = 0;
                        conciseMethodReturnAngleDepth = 0;
                        conciseMethodReturnBraceDepth = 0;
                        conciseMethodReturnSawToken = false;
                        previousConciseMethodReturnToken = null;
                    }

                    previousTokenKind = JavaScriptPrevTokenKind.CloseParen;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == '[')
                {
                    if (arrowExpressionActive)
                        arrowExpressionBracketDepth++;

                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == ']')
                {
                    if (arrowExpressionActive && arrowExpressionBracketDepth > 0)
                        arrowExpressionBracketDepth--;

                    previousTokenKind = JavaScriptPrevTokenKind.CloseBracket;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == '{')
                {
                    var scopeKind = JavaScriptScopeKind.Other;
                    if (pendingFunctionScope)
                        scopeKind = JavaScriptScopeKind.Function;
                    else if (pendingConciseMethodScope)
                        scopeKind = JavaScriptScopeKind.Function;
                    else if (pendingStaticBlockScope)
                        scopeKind = JavaScriptScopeKind.StaticBlock;
                    else if (pendingClassScope)
                        scopeKind = JavaScriptScopeKind.Class;
                    else if (pendingNamespaceScope)
                        scopeKind = JavaScriptScopeKind.Namespace;
                    else if (CanStartJavaScriptTypeScriptObjectLiteral(previousTokenKind, previousIdentifier, previousSignificantChar))
                        scopeKind = JavaScriptScopeKind.Object;
                    else
                        scopeKind = JavaScriptScopeKind.Block;

                    linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);

                    scopeStack.Push(scopeKind);
                    if (arrowExpressionActive)
                        arrowExpressionBraceDepth++;

                    pendingFunctionScope = false;
                    functionHeaderState = default;
                    pendingStaticBlockScope = false;
                    pendingClassScope = false;
                    pendingNamespaceScope = false;
                    pendingConciseMethodScope = false;
                    pendingConciseMethodReturnType = false;
                    conciseMethodReturnParenDepth = 0;
                    conciseMethodReturnBracketDepth = 0;
                    conciseMethodReturnAngleDepth = 0;
                    conciseMethodReturnBraceDepth = 0;
                    conciseMethodReturnSawToken = false;
                    previousConciseMethodReturnToken = null;
                    previousTokenKind = JavaScriptPrevTokenKind.CloseBrace;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == '}')
                {
                    if (arrowExpressionActive)
                    {
                        linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                        if (arrowExpressionBraceDepth > 0)
                            arrowExpressionBraceDepth--;
                    }

                    if (scopeStack.Count > 0)
                        scopeStack.Pop();

                    pendingFunctionScope = false;
                    functionHeaderState = default;
                    pendingStaticBlockScope = false;
                    pendingClassScope = false;
                    pendingNamespaceScope = false;
                    pendingConciseMethodScope = false;
                    pendingConciseMethodReturnType = false;
                    conciseMethodReturnParenDepth = 0;
                    conciseMethodReturnBracketDepth = 0;
                    conciseMethodReturnAngleDepth = 0;
                    conciseMethodReturnBraceDepth = 0;
                    conciseMethodReturnSawToken = false;
                    previousConciseMethodReturnToken = null;
                    previousTokenKind = JavaScriptPrevTokenKind.CloseBrace;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch is ';' or ',')
                {
                    if (arrowExpressionActive
                        && arrowExpressionBraceDepth == 0
                        && arrowExpressionParenDepth == 0
                        && arrowExpressionBracketDepth == 0)
                    {
                        linePrivateColumns[column] = GetJavaScriptTypeScriptPrivacyFlags(scopeStack, arrowExpressionActive);
                        arrowExpressionActive = false;
                    }

                    pendingFunctionScope = false;
                    functionHeaderState = default;
                    pendingStaticBlockScope = false;
                    pendingClassScope = false;
                    pendingNamespaceScope = false;
                    pendingConciseMethodScope = false;
                    pendingConciseMethodReturnType = false;
                    conciseMethodReturnParenDepth = 0;
                    conciseMethodReturnBracketDepth = 0;
                    conciseMethodReturnAngleDepth = 0;
                    conciseMethodReturnBraceDepth = 0;
                    conciseMethodReturnSawToken = false;
                    previousConciseMethodReturnToken = null;
                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (ch == ':')
                {
                    if (pendingConciseMethodScope && lang == "typescript")
                    {
                        pendingConciseMethodReturnType = true;
                        conciseMethodReturnParenDepth = 0;
                        conciseMethodReturnBracketDepth = 0;
                        conciseMethodReturnAngleDepth = 0;
                        conciseMethodReturnBraceDepth = 0;
                        conciseMethodReturnSawToken = false;
                        previousConciseMethodReturnToken = ":";
                    }

                    previousTokenKind = JavaScriptPrevTokenKind.Other;
                    previousIdentifier = null;
                    previousSignificantChar = ch;
                    continue;
                }

                if (pendingStaticBlockScope && ch != '{')
                    pendingStaticBlockScope = false;
                if (pendingNamespaceScope && ch is not '{' and not '.')
                    pendingNamespaceScope = false;

                previousTokenKind = JavaScriptPrevTokenKind.Other;
                previousIdentifier = null;
                previousSignificantChar = ch;
            }

            privateColumns[lineIndex] = linePrivateColumns;
        }

        return privateColumns;
    }

    private static bool CanStartJavaScriptTypeScriptObjectLiteral(
        JavaScriptPrevTokenKind previousTokenKind,
        string? previousIdentifier,
        char previousSignificantChar)
    {
        if (previousSignificantChar is '=' or '(' or '[' or ',' or ':' or '?' or '!')
            return true;

        if (previousIdentifier is "return" or "throw" or "case" or "else")
            return true;

        return previousTokenKind == JavaScriptPrevTokenKind.None;
    }

    private static bool StartsJavaScriptTypeScriptExpressionContinuation(string sanitizedLine)
    {
        var index = 0;
        while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
            index++;

        if (index >= sanitizedLine.Length)
            return false;

        var remaining = sanitizedLine[index..];
        if (remaining.StartsWith(".", StringComparison.Ordinal)
            || remaining.StartsWith("?.", StringComparison.Ordinal)
            || remaining.StartsWith("[", StringComparison.Ordinal)
            || remaining.StartsWith("(", StringComparison.Ordinal)
            || remaining.StartsWith("`", StringComparison.Ordinal)
            || remaining.StartsWith("?.[", StringComparison.Ordinal))
            return true;

        if (Regex.IsMatch(remaining, @"^(?:\+\+|--|\+|-|\*|/|%|\*\*|&&|\|\||\?\?|\?|:|==|===|!=|!==|<=|>=|<|>|=>|&|\||\^|<<|>>|>>>|,)\b?"))
            return true;

        if (Regex.IsMatch(remaining, @"^(?:instanceof|in|as|satisfies)\b"))
            return true;

        return false;
    }

    private static bool IsAnonymousJavaScriptTypeScriptDefaultClassDeclaration(string[] lines, int startIndex, int startColumn)
    {
        if (!TryGetAnonymousJavaScriptTypeScriptDefaultClassToken(lines, startIndex, startColumn, "javascript", out var tokenLineIndex, out var tokenStartColumn))
            return false;

        startIndex = tokenLineIndex;
        startColumn = tokenStartColumn + "class".Length;
        if (!TryAdvanceJavaScriptTypeScriptClassHeaderContinuation(lines, startIndex, startColumn, "javascript", out startIndex, out startColumn))
            return false;

        var lexState = new JavaScriptLexState();
        for (int lineIndex = startIndex; lineIndex < lines.Length; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var column = lineIndex == startIndex ? startColumn : 0;

            while (column < sanitizedLine.Length)
            {
                var ch = sanitizedLine[column];
                if (char.IsWhiteSpace(ch))
                {
                    column++;
                    continue;
                }

                if (IsJavaScriptTypeScriptIdentifierStart(ch))
                {
                    var tokenStart = column;
                    column++;
                    while (column < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[column]))
                        column++;

                    var nextToken = sanitizedLine[tokenStart..column];
                    return nextToken is "extends" or "implements";
                }

                return ch == '{';
            }
        }

        return false;
    }

    private static bool TryGetAnonymousJavaScriptTypeScriptDefaultClassToken(
        string[] lines,
        int startIndex,
        int startColumn,
        string lang,
        out int classLineIndex,
        out int classStartColumn)
    {
        classLineIndex = -1;
        classStartColumn = -1;

        if (!TryGetJavaScriptTypeScriptNextToken(lines, startIndex, startColumn, skipWrappingParens: true, out classLineIndex, out classStartColumn, out var token))
            return false;

        if (lang == "typescript" && token == "abstract")
        {
            if (!TryGetJavaScriptTypeScriptNextToken(lines, classLineIndex, classStartColumn + token.Length, skipWrappingParens: false, out classLineIndex, out classStartColumn, out token))
                return false;
        }

        if (token != "class")
            return false;

        var inspectLineIndex = classLineIndex;
        var inspectStartColumn = classStartColumn + token.Length;
        if (!TryAdvanceJavaScriptTypeScriptClassHeaderContinuation(lines, inspectLineIndex, inspectStartColumn, lang, out inspectLineIndex, out inspectStartColumn))
            return false;

        var lexState = new JavaScriptLexState();
        for (int lineIndex = inspectLineIndex; lineIndex < lines.Length; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var column = lineIndex == inspectLineIndex ? inspectStartColumn : 0;

            while (column < sanitizedLine.Length)
            {
                var ch = sanitizedLine[column];
                if (char.IsWhiteSpace(ch))
                {
                    column++;
                    continue;
                }

                if (IsJavaScriptTypeScriptIdentifierStart(ch))
                {
                    var tokenStart = column;
                    column++;
                    while (column < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[column]))
                        column++;

                    var nextToken = sanitizedLine[tokenStart..column];
                    return nextToken is "extends" or "implements";
                }

                return ch == '{';
            }
        }

        return false;
    }

    private static bool TryAdvanceJavaScriptTypeScriptClassHeaderContinuation(
        string[] lines,
        int startIndex,
        int startColumn,
        string lang,
        out int nextLineIndex,
        out int nextColumn)
    {
        nextLineIndex = startIndex;
        nextColumn = startColumn;
        if (lang != "typescript")
            return true;

        var lexState = new JavaScriptLexState();
        var sawTypeParameterList = false;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (int lineIndex = startIndex; lineIndex < lines.Length; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var column = lineIndex == startIndex ? startColumn : 0;

            while (column < sanitizedLine.Length)
            {
                var ch = sanitizedLine[column];
                if (char.IsWhiteSpace(ch))
                {
                    column++;
                    continue;
                }

                if (!sawTypeParameterList)
                {
                    if (ch != '<')
                    {
                        nextLineIndex = lineIndex;
                        nextColumn = column;
                        return true;
                    }

                    sawTypeParameterList = true;
                    angleDepth = 1;
                    column++;
                    continue;
                }

                if (ch == '(')
                {
                    parenDepth++;
                    column++;
                    continue;
                }

                if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                    column++;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    column++;
                    continue;
                }

                if (ch == ']' && bracketDepth > 0)
                {
                    bracketDepth--;
                    column++;
                    continue;
                }

                if (ch == '{')
                {
                    braceDepth++;
                    column++;
                    continue;
                }

                if (ch == '}' && braceDepth > 0)
                {
                    braceDepth--;
                    column++;
                    continue;
                }

                if (ch == '<')
                {
                    angleDepth++;
                    column++;
                    continue;
                }

                if (ch == '=' && column + 1 < sanitizedLine.Length && sanitizedLine[column + 1] == '>')
                {
                    column += 2;
                    continue;
                }

                if (ch == '>' && angleDepth > 0 && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    angleDepth--;
                    column++;
                    if (angleDepth == 0)
                    {
                        while (column < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[column]))
                            column++;

                        nextLineIndex = lineIndex;
                        nextColumn = column;
                        return true;
                    }

                    continue;
                }

                column++;
            }
        }

        return !sawTypeParameterList;
    }

    private static bool IsJavaScriptTypeScriptMatchInPrivateScope(
        JavaScriptScopePrivacyFlags[][] privateScopeColumns,
        int lineIndex,
        int startColumn,
        string matchLine,
        bool includeBlockScope)
    {
        if (lineIndex < 0 || lineIndex >= privateScopeColumns.Length)
            return false;

        var linePrivateColumns = privateScopeColumns[lineIndex];
        if (linePrivateColumns.Length == 0)
            return false;

        var column = Math.Max(0, startColumn);
        while (column < matchLine.Length && char.IsWhiteSpace(matchLine[column]))
            column++;

        if (column >= linePrivateColumns.Length)
            return false;

        var flags = linePrivateColumns[column];
        if ((flags & JavaScriptScopePrivacyFlags.FunctionLike) != 0)
            return true;

        return includeBlockScope && (flags & JavaScriptScopePrivacyFlags.Block) != 0;
    }

    private static bool IsJavaScriptTypeScriptMatchInNamespaceScope(
        JavaScriptScopePrivacyFlags[][] privateScopeColumns,
        int lineIndex,
        int startColumn,
        string matchLine)
    {
        if (lineIndex < 0 || lineIndex >= privateScopeColumns.Length)
            return false;

        var linePrivateColumns = privateScopeColumns[lineIndex];
        if (linePrivateColumns.Length == 0)
            return false;

        var column = Math.Max(0, startColumn);
        while (column < matchLine.Length && char.IsWhiteSpace(matchLine[column]))
            column++;

        if (column >= linePrivateColumns.Length)
            return false;

        return (linePrivateColumns[column] & JavaScriptScopePrivacyFlags.Namespace) != 0;
    }

    private static bool IsJavaScriptTypeScriptControlFlowHeader(string sanitizedLine, int startColumn)
    {
        var index = Math.Max(0, startColumn);
        if (index >= sanitizedLine.Length || !IsJavaScriptTypeScriptIdentifierStart(sanitizedLine[index]))
            return false;

        var tokenStart = index;
        index++;
        while (index < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[index]))
            index++;

        var token = sanitizedLine[tokenStart..index];
        if (!JavaScriptTypeScriptControlFlowHeaderKeywords.Contains(token))
            return false;

        if (index >= sanitizedLine.Length || !char.IsWhiteSpace(sanitizedLine[index]))
            return false;

        while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
            index++;

        return index < sanitizedLine.Length && sanitizedLine[index] == '(';
    }

    private static void ExtractJavaScriptTypeScriptBareMethodsInTargets(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        List<JavaScriptClassScanTarget> classScanTargets)
    {
        foreach (var classScanTarget in classScanTargets)
            ExtractJavaScriptTypeScriptBareMethodsInClass(fileId, lang, lines, symbols, classScanTarget);
    }

    private static void TryAddJavaScriptTypeScriptSyntheticClassTarget(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        List<JavaScriptClassScanTarget> targets,
        int startIndex,
        int startColumn,
        string sanitizedLine,
        JavaScriptScopePrivacyFlags[][] privateScopeColumns)
    {
        var lineRemainder = sanitizedLine[startColumn..];
        var anonymousDefaultMatch = JavaScriptTypeScriptAnonymousDefaultExportRegex.Match(lineRemainder);
        if (anonymousDefaultMatch.Success)
        {
            if (!TryGetAnonymousJavaScriptTypeScriptDefaultClassToken(
                lines,
                startIndex,
                startColumn + anonymousDefaultMatch.Index + anonymousDefaultMatch.Length,
                lang,
                out var classTokenLineIndex,
                out var classTokenStartColumn))
                return;

            if (IsJavaScriptTypeScriptMatchInPrivateScope(privateScopeColumns, startIndex, startColumn + anonymousDefaultMatch.Index, sanitizedLine, includeBlockScope: false))
                return;

            if (TryGetGroup(anonymousDefaultMatch, "visibility") != "export"
                && IsJavaScriptTypeScriptMatchInNamespaceScope(privateScopeColumns, startIndex, startColumn + anonymousDefaultMatch.Index, sanitizedLine))
            {
                return;
            }

        AddJavaScriptTypeScriptSyntheticClassTarget(
            fileId,
            lang,
            lines,
            symbols,
            targets,
            startIndex,
            startColumn + anonymousDefaultMatch.Index,
            classTokenLineIndex,
            classTokenStartColumn,
            containerName: "default",
            visibility: TryGetGroup(anonymousDefaultMatch, "visibility"));
            return;
        }

        if (lang == "typescript")
        {
            var exportEqualsMatch = TypeScriptExportEqualsRegex.Match(lineRemainder);
            if (exportEqualsMatch.Success)
            {
                if (!IsJavaScriptTypeScriptClassExpressionDeclaration(lines, startIndex, startColumn + exportEqualsMatch.Index + exportEqualsMatch.Length))
                    return;

                if (!TryGetJavaScriptTypeScriptNextToken(
                    lines,
                    startIndex,
                    startColumn + exportEqualsMatch.Index + exportEqualsMatch.Length,
                    skipWrappingParens: true,
                    out var exportEqualsClassTokenLineIndex,
                    out var exportEqualsClassTokenStartColumn,
                    out _))
                {
                    return;
                }

                if (IsJavaScriptTypeScriptMatchInPrivateScope(privateScopeColumns, startIndex, startColumn + exportEqualsMatch.Index, sanitizedLine, includeBlockScope: false))
                    return;

                if (IsJavaScriptTypeScriptMatchInNamespaceScope(privateScopeColumns, startIndex, startColumn + exportEqualsMatch.Index, sanitizedLine))
                    return;

                AddJavaScriptTypeScriptSyntheticClassTarget(
                    fileId,
                    lang,
                    lines,
                    symbols,
                    targets,
                    startIndex,
                    startColumn + exportEqualsMatch.Index,
                    exportEqualsClassTokenLineIndex,
                    exportEqualsClassTokenStartColumn,
                    containerName: "default",
                    visibility: "export");
                return;
            }
        }

        var classExpressionBindingMatch = JavaScriptTypeScriptClassExpressionBindingRegex.Match(lineRemainder);
        if (!classExpressionBindingMatch.Success)
            return;

        if (!IsJavaScriptTypeScriptClassExpressionDeclaration(lines, startIndex, startColumn + classExpressionBindingMatch.Index + classExpressionBindingMatch.Length))
            return;

        if (!TryGetJavaScriptTypeScriptNextToken(
            lines,
            startIndex,
            startColumn + classExpressionBindingMatch.Index + classExpressionBindingMatch.Length,
            skipWrappingParens: true,
            out var classExpressionTokenLineIndex,
            out var classExpressionTokenStartColumn,
            out _))
        {
            return;
        }

        var includeBlockScope = classExpressionBindingMatch.Groups["bindingKind"].Success
            && classExpressionBindingMatch.Groups["bindingKind"].Value is "const" or "let";
        if (IsJavaScriptTypeScriptMatchInPrivateScope(privateScopeColumns, startIndex, startColumn + classExpressionBindingMatch.Index, sanitizedLine, includeBlockScope))
            return;

        if (TryGetGroup(classExpressionBindingMatch, "visibility") != "export"
            && IsJavaScriptTypeScriptMatchInNamespaceScope(privateScopeColumns, startIndex, startColumn + classExpressionBindingMatch.Index, sanitizedLine))
        {
            return;
        }

        var containerName = TryGetGroup(classExpressionBindingMatch, "alias")
            ?? TryGetGroup(classExpressionBindingMatch, "exportsAlias")
            ?? TryGetGroup(classExpressionBindingMatch, "moduleExportsAlias")
            ?? (classExpressionBindingMatch.Groups["moduleExports"].Success ? "default" : null)
            ?? "class";
        AddJavaScriptTypeScriptSyntheticClassTarget(
            fileId,
            lang,
            lines,
            symbols,
            targets,
            startIndex,
            startColumn + classExpressionBindingMatch.Index,
            classExpressionTokenLineIndex,
            classExpressionTokenStartColumn,
            containerName,
            TryGetGroup(classExpressionBindingMatch, "visibility"));
    }

    private static bool IsJavaScriptTypeScriptClassExpressionDeclaration(string[] lines, int startIndex, int startColumn)
    {
        return TryGetJavaScriptTypeScriptNextToken(lines, startIndex, startColumn, skipWrappingParens: true, out _, out _, out var token)
            && token == "class";
    }

    private static void AddJavaScriptTypeScriptSyntheticClassTarget(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        List<JavaScriptClassScanTarget> targets,
        int declarationStartIndex,
        int declarationStartColumn,
        int classTokenLineIndex,
        int classTokenStartColumn,
        string containerName,
        string? visibility)
    {
        var (endLine, bodyStartLine, bodyEndLine) = ResolveRange(lines, classTokenLineIndex, BodyStyle.Brace, lang, classTokenStartColumn);
        if (bodyStartLine == null || bodyEndLine == null)
            return;

        var existingClass = symbols.FirstOrDefault(s => s.Kind == "class" && s.Line == declarationStartIndex + 1 && s.Name == containerName);
        if (existingClass == null)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = containerName,
                Line = declarationStartIndex + 1,
                StartLine = declarationStartIndex + 1,
                EndLine = Math.Max(declarationStartIndex + 1, endLine),
                BodyStartLine = bodyStartLine,
                BodyEndLine = bodyEndLine,
                Signature = BuildJavaScriptTypeScriptSyntheticClassSignature(lines, declarationStartIndex, declarationStartColumn, classTokenLineIndex, classTokenStartColumn, bodyStartLine, bodyEndLine, lang),
                Visibility = visibility,
            });
        }

        var candidate = CreateJavaScriptClassScanTarget(lines, lang, classTokenLineIndex, classTokenStartColumn, bodyStartLine, bodyEndLine, "class", containerName);
        if (!targets.Any(t => t.StartIndex == candidate.StartIndex
            && t.StartColumn == candidate.StartColumn
            && t.ScanStartIndex == candidate.ScanStartIndex
            && t.ScanEndExclusive == candidate.ScanEndExclusive
            && t.FirstLineScanOffset == candidate.FirstLineScanOffset
            && t.ContainerKind == candidate.ContainerKind
            && t.ContainerName == candidate.ContainerName))
        {
            targets.Add(candidate);
        }
    }

    private static string BuildJavaScriptTypeScriptSyntheticClassSignature(
        string[] lines,
        int declarationStartIndex,
        int declarationStartColumn,
        int classTokenLineIndex,
        int classTokenStartColumn,
        int? bodyStartLine,
        int? bodyEndLine,
        string lang)
    {
        var line = lines[declarationStartIndex];
        if (declarationStartColumn >= line.Length)
            return string.Empty;

        if (bodyEndLine == declarationStartIndex + 1)
        {
            var sameLineEndColumn = FindJavaScriptTypeScriptSameLineBraceEndColumn(line, classTokenStartColumn, lang);
            if (sameLineEndColumn >= declarationStartColumn)
                return line[declarationStartColumn..(sameLineEndColumn + 1)].Trim();
        }

        if (bodyStartLine == null)
            return line[declarationStartColumn..].Trim();

        var bodyStartIndex = bodyStartLine.Value - 1;
        var bodyOpenBraceColumn = FindJavaScriptBodyOpenBraceIndex(lines, classTokenLineIndex, bodyStartIndex, lang, classTokenStartColumn);
        if (bodyOpenBraceColumn < 0)
            return line[declarationStartColumn..].Trim();

        var signatureBuilder = new System.Text.StringBuilder();
        for (int lineIndex = declarationStartIndex; lineIndex <= bodyStartIndex; lineIndex++)
        {
            var sourceLine = lines[lineIndex];
            var startColumn = lineIndex == declarationStartIndex
                ? Math.Min(declarationStartColumn, sourceLine.Length)
                : 0;
            var endExclusive = lineIndex == bodyStartIndex
                ? Math.Min(bodyOpenBraceColumn + 1, sourceLine.Length)
                : sourceLine.Length;
            if (startColumn >= endExclusive)
                continue;

            var segment = sourceLine[startColumn..endExclusive].Trim();
            if (segment.Length == 0)
                continue;

            if (signatureBuilder.Length > 0)
                signatureBuilder.Append(' ');

            signatureBuilder.Append(segment);
        }

        return signatureBuilder.Length > 0
            ? signatureBuilder.ToString()
            : line[declarationStartColumn..].Trim();
    }

    private static JavaScriptClassScanTarget CreateJavaScriptClassScanTarget(string[] lines, string lang, int startIndex, int startColumn, int? bodyStartLine, int? bodyEndLine, string containerKind, string containerName)
    {
        var scanStartIndex = bodyStartLine!.Value - 1;
        var scanEndExclusive = bodyEndLine!.Value;
        var firstLineScanOffset = FindJavaScriptBodyOpenBraceIndex(lines, startIndex, scanStartIndex, lang, startColumn);
        if (firstLineScanOffset >= 0)
            firstLineScanOffset++;
        else
            firstLineScanOffset = 0;

        return new JavaScriptClassScanTarget(
            startIndex,
            startColumn,
            scanStartIndex,
            scanEndExclusive,
            firstLineScanOffset,
            containerKind,
            containerName);
    }

    private static void ExtractJavaScriptTypeScriptBareMethodsInClass(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        JavaScriptClassScanTarget classScanTarget)
    {
        if (classScanTarget.ScanStartIndex >= classScanTarget.ScanEndExclusive)
            return;

        var scanStartIndex = classScanTarget.ScanStartIndex;
        var scanEndExclusive = classScanTarget.ScanEndExclusive;
        var nestedBraceDepth = 0;
        var inFieldInitializer = false;
        var initializerParenDepth = 0;
        var initializerBracketDepth = 0;
        var initializerBraceDepth = 0;
        var lexState = new JavaScriptLexState();
        var seenMethodStarts = new HashSet<(int Line, int Column)>();
        var pendingHeaderEndLineIndex = -1;
        var pendingHeaderEndColumn = -1;
        var pendingBodyStartLineIndex = -1;
        var pendingBodyStartColumn = -1;

        for (int i = scanStartIndex; i < scanEndExclusive; i++)
        {
            var line = lines[i];
            var lexedLine = LexJavaScriptLine(line, lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;

            if (pendingHeaderEndLineIndex >= 0)
            {
                if (i < pendingHeaderEndLineIndex)
                    continue;
            }

            if (pendingBodyStartLineIndex >= 0)
            {
                if (i < pendingBodyStartLineIndex)
                    continue;

                if (i == pendingBodyStartLineIndex)
                {
                    if (pendingBodyStartColumn >= 0 && pendingBodyStartColumn < sanitizedLine.Length)
                    {
                        nestedBraceDepth += CountBraces(sanitizedLine[pendingBodyStartColumn..]);
                        if (nestedBraceDepth < 0)
                            nestedBraceDepth = 0;
                    }

                    pendingBodyStartLineIndex = -1;
                    pendingBodyStartColumn = -1;
                    continue;
                }
            }

            var scanStartColumn = i == scanStartIndex
                ? Math.Min(classScanTarget.FirstLineScanOffset, sanitizedLine.Length)
                : 0;
            if (pendingHeaderEndLineIndex == i)
            {
                scanStartColumn = Math.Max(scanStartColumn, Math.Min(pendingHeaderEndColumn + 1, sanitizedLine.Length));
                pendingHeaderEndLineIndex = -1;
                pendingHeaderEndColumn = -1;
            }

            if (inFieldInitializer
                && initializerParenDepth == 0
                && initializerBracketDepth == 0
                && initializerBraceDepth == 0)
            {
                var continuationInput = scanStartColumn >= sanitizedLine.Length
                    ? string.Empty
                    : sanitizedLine[scanStartColumn..];
                if (continuationInput.Any(ch => !char.IsWhiteSpace(ch))
                    && !StartsJavaScriptTypeScriptFieldInitializerContinuation(continuationInput, lang))
                {
                    inFieldInitializer = false;
                }
            }

            var column = scanStartColumn;
            while (column < sanitizedLine.Length)
            {
                var ch = sanitizedLine[column];
                if (char.IsWhiteSpace(ch))
                {
                    column++;
                    continue;
                }

                if (inFieldInitializer)
                {
                    AdvanceJavaScriptTypeScriptFieldInitializerState(
                        ref inFieldInitializer,
                        ref initializerParenDepth,
                        ref initializerBracketDepth,
                        ref initializerBraceDepth,
                        ch);
                    column++;
                    continue;
                }

                if (nestedBraceDepth == 0
                    && IsJavaScriptTypeScriptMethodCandidateStart(sanitizedLine, column)
                    && !IsJavaScriptTypeScriptControlFlowHeader(sanitizedLine, column))
                {
                    if (TryCaptureJavaScriptTypeScriptMethodHeader(
                        lines,
                        i,
                        column,
                        scanEndExclusive,
                        sanitizedLine,
                        lexState,
                        lang,
                        out var methodCapture))
                    {
                        var methodHeader = methodCapture.HeaderInfo;
                        var startLine = i + 1;
                        if (seenMethodStarts.Add((startLine, column)))
                        {
                            var (endLine, bodyStartLine, bodyEndLine) = methodHeader.HasBody
                                ? ResolveRange(lines, i, BodyStyle.Brace, lang, column)
                                : (methodCapture.HeaderEndLineIndex + 1, null, null);
                            var sameLineMethodEndColumn = methodHeader.HasBody && bodyEndLine == startLine
                                ? FindJavaScriptSameLineBodyEndColumn(line, column, lang)
                                : methodCapture.HeaderEndLineIndex == i
                                    ? methodCapture.HeaderEndColumn
                                    : -1;
                            symbols.Add(new SymbolRecord
                            {
                                FileId = fileId,
                                Kind = "function",
                                Name = GetJavaScriptTypeScriptMethodNameFromSource(methodCapture.SourceHeader, 0) ?? methodHeader.Name,
                                Line = startLine,
                                StartLine = startLine,
                                EndLine = Math.Max(startLine, endLine),
                                BodyStartLine = bodyStartLine,
                                BodyEndLine = bodyEndLine,
                                Signature = BuildJavaScriptTypeScriptBareMethodSignature(
                                    lines,
                                    i,
                                    column,
                                    bodyEndLine,
                                    sameLineMethodEndColumn,
                                    methodCapture,
                                    lang),
                                ContainerKind = classScanTarget.ContainerKind,
                                ContainerName = classScanTarget.ContainerName,
                                Visibility = methodHeader.Visibility,
                                ReturnType = GetJavaScriptTypeScriptBareMethodReturnType(methodCapture.SourceHeader, methodHeader, lang),
                            });

                            if (sameLineMethodEndColumn >= column)
                            {
                                column = sameLineMethodEndColumn + 1;
                                continue;
                            }

                            if (methodHeader.HasBody && methodCapture.BodyStartLineIndex > i)
                            {
                                pendingBodyStartLineIndex = methodCapture.BodyStartLineIndex;
                                pendingBodyStartColumn = methodCapture.BodyStartColumn;
                                break;
                            }

                            if (methodCapture.HeaderEndLineIndex > i)
                            {
                                pendingHeaderEndLineIndex = methodCapture.HeaderEndLineIndex;
                                pendingHeaderEndColumn = methodCapture.HeaderEndColumn;
                                break;
                            }
                        }

                        if (methodHeader.HasBody
                            && methodCapture.BodyStartLineIndex == i
                            && methodCapture.BodyStartColumn >= 0
                            && methodCapture.BodyStartColumn < sanitizedLine.Length)
                        {
                            nestedBraceDepth += CountBraces(sanitizedLine[methodCapture.BodyStartColumn..]);
                            if (nestedBraceDepth < 0)
                                nestedBraceDepth = 0;
                            break;
                        }

                        column++;
                        continue;
                    }
                }

                if (nestedBraceDepth == 0 && CanStartJavaScriptTypeScriptClassFieldInitializer(sanitizedLine, column))
                {
                    inFieldInitializer = true;
                    initializerParenDepth = 0;
                    initializerBracketDepth = 0;
                    initializerBraceDepth = 0;
                    column++;
                    continue;
                }

                if (ch == '{')
                {
                    nestedBraceDepth++;
                }
                else if (ch == '}' && nestedBraceDepth > 0)
                {
                    nestedBraceDepth--;
                }

                column++;
            }
        }
    }

    private static void AdvanceJavaScriptTypeScriptFieldInitializerState(
        ref bool inFieldInitializer,
        ref int initializerParenDepth,
        ref int initializerBracketDepth,
        ref int initializerBraceDepth,
        char ch)
    {
        if (ch == '(')
        {
            initializerParenDepth++;
            return;
        }

        if (ch == ')' && initializerParenDepth > 0)
        {
            initializerParenDepth--;
            return;
        }

        if (ch == '[')
        {
            initializerBracketDepth++;
            return;
        }

        if (ch == ']' && initializerBracketDepth > 0)
        {
            initializerBracketDepth--;
            return;
        }

        if (ch == '{')
        {
            initializerBraceDepth++;
            return;
        }

        if (ch == '}' && initializerBraceDepth > 0)
        {
            initializerBraceDepth--;
            return;
        }

        if (ch == ';'
            && initializerParenDepth == 0
            && initializerBracketDepth == 0
            && initializerBraceDepth == 0)
        {
            inFieldInitializer = false;
        }
    }

    private static bool StartsJavaScriptTypeScriptFieldInitializerContinuation(string continuationInput, string? lang)
    {
        var firstNonWhitespace = 0;
        while (firstNonWhitespace < continuationInput.Length && char.IsWhiteSpace(continuationInput[firstNonWhitespace]))
            firstNonWhitespace++;

        if (firstNonWhitespace >= continuationInput.Length)
            return false;

        if (IsJavaScriptTypeScriptMethodCandidateStart(continuationInput, firstNonWhitespace))
        {
            var matchCandidate = lang == "typescript"
                ? NormalizeTypeScriptBareMethodMatchInput(continuationInput[firstNonWhitespace..])
                : continuationInput[firstNonWhitespace..];
            if (TryParseJavaScriptTypeScriptMethodHeader(matchCandidate, 0, lang, out _))
                return false;
        }

        return StartsJavaScriptTypeScriptExpressionContinuation(continuationInput);
    }

    private static bool CanStartJavaScriptTypeScriptClassFieldInitializer(string sanitizedLine, int index)
    {
        if (index < 0 || index >= sanitizedLine.Length || sanitizedLine[index] != '=')
            return false;

        return index + 1 >= sanitizedLine.Length || sanitizedLine[index + 1] != '>';
    }

    private static bool IsJavaScriptTypeScriptMethodCandidateStart(string sanitizedLine, int index)
    {
        if (index < 0 || index >= sanitizedLine.Length)
            return false;

        var ch = sanitizedLine[index];
        if (ch != '#'
            && ch != '*'
            && ch != '['
            && ch != '\''
            && ch != '"'
            && !char.IsDigit(ch)
            && !IsJavaScriptTypeScriptIdentifierStart(ch))
            return false;

        return index == 0 || !IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[index - 1]);
    }

    private static JavaScriptLexedLine LexJavaScriptLine(string line, JavaScriptLexState state)
    {
        var sanitized = new char[line.Length];
        var i = 0;

        while (i < line.Length)
        {
            var ch = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (state.Mode == JavaScriptLexMode.BlockComment)
            {
                sanitized[i] = ' ';
                if (ch == '*' && next == '/')
                {
                    sanitized[i + 1] = ' ';
                    state = state with { Mode = JavaScriptLexMode.Code };
                    i++;
                }

                i++;
                continue;
            }

            if (state.Mode == JavaScriptLexMode.SingleQuote)
            {
                sanitized[i] = ch is '\'' or '\\' ? ch : ' ';

                if (state.EscapeNext)
                {
                    state = state with { EscapeNext = false };
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    state = state with { EscapeNext = true };
                    i++;
                    continue;
                }

                if (ch == '\'')
                    state = state with { Mode = JavaScriptLexMode.Code };

                i++;
                continue;
            }

            if (state.Mode == JavaScriptLexMode.DoubleQuote)
            {
                sanitized[i] = ch is '"' or '\\' ? ch : ' ';

                if (state.EscapeNext)
                {
                    state = state with { EscapeNext = false };
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    state = state with { EscapeNext = true };
                    i++;
                    continue;
                }

                if (ch == '"')
                    state = state with { Mode = JavaScriptLexMode.Code };

                i++;
                continue;
            }

            if (state.Mode == JavaScriptLexMode.TemplateString)
            {
                sanitized[i] = ch is '`' or '\\' ? ch : ' ';

                if (state.EscapeNext)
                {
                    state = state with { EscapeNext = false };
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    state = state with { EscapeNext = true };
                    i++;
                    continue;
                }

                if (ch == '`')
                    state = state with { Mode = JavaScriptLexMode.Code };

                i++;
                continue;
            }

            if (ch == '/' && next == '/')
            {
                while (i < line.Length)
                {
                    sanitized[i] = ' ';
                    i++;
                }

                break;
            }

            if (ch == '/' && next == '*')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = ' ';
                state = state with { Mode = JavaScriptLexMode.BlockComment };
                i++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sanitized[i] = ch;
                i++;
                continue;
            }

            if (state.ExpectingControlFlowOpenParen && ch != '(')
                state = state with { ExpectingControlFlowOpenParen = false };

            if (state.RegexAllowedAfterControlFlowParen && ch != '/')
            {
                state = state with
                {
                    RegexAllowedAfterControlFlowParen = false
                };
            }

            if (ch == '\'')
            {
                sanitized[i] = ch;
                state = state with { Mode = JavaScriptLexMode.SingleQuote, EscapeNext = false };
                i++;
                continue;
            }

            if (ch == '"')
            {
                sanitized[i] = ch;
                state = state with { Mode = JavaScriptLexMode.DoubleQuote, EscapeNext = false };
                i++;
                continue;
            }

            if (ch == '`')
            {
                sanitized[i] = ch;
                state = state with { Mode = JavaScriptLexMode.TemplateString, EscapeNext = false };
                i++;
                continue;
            }

            if (ch == '/' && CanStartJavaScriptRegexLiteral(state))
            {
                sanitized[i] = ' ';
                i = SkipJavaScriptRegexLiteral(line, sanitized, i);
                state = state with
                {
                    PreviousTokenKind = JavaScriptPrevTokenKind.Other,
                    PreviousIdentifier = null
                };
                i++;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_' || ch == '$')
            {
                var tokenStart = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '$'))
                {
                    sanitized[i] = line[i];
                    i++;
                }

                state = state with
                {
                    PreviousTokenKind = JavaScriptPrevTokenKind.Identifier,
                    PreviousIdentifier = line[tokenStart..i],
                    ExpectingControlFlowOpenParen = IsJavaScriptControlFlowKeyword(line[tokenStart..i])
                };
                continue;
            }

            if (char.IsDigit(ch))
            {
                sanitized[i] = ch;
                i++;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '.'))
                {
                    sanitized[i] = line[i];
                    i++;
                }

                state = state with
                {
                    PreviousTokenKind = JavaScriptPrevTokenKind.Number,
                    PreviousIdentifier = null,
                    ExpectingControlFlowOpenParen = false
                };
                continue;
            }

            sanitized[i] = ch;
            if (!char.IsWhiteSpace(ch))
            {
                var controlFlowParenDepth = state.ControlFlowParenDepth;
                var regexAllowedAfterControlFlowParen = state.RegexAllowedAfterControlFlowParen;

                if (ch == '(')
                {
                    if (state.ExpectingControlFlowOpenParen)
                    {
                        controlFlowParenDepth = 1;
                        regexAllowedAfterControlFlowParen = false;
                    }
                    else if (controlFlowParenDepth > 0)
                    {
                        controlFlowParenDepth++;
                    }
                }
                else if (ch == ')' && controlFlowParenDepth > 0)
                {
                    controlFlowParenDepth--;
                    if (controlFlowParenDepth == 0)
                        regexAllowedAfterControlFlowParen = true;
                }

                state = state with
                {
                    PreviousTokenKind = ch switch
                    {
                        ')' => JavaScriptPrevTokenKind.CloseParen,
                        ']' => JavaScriptPrevTokenKind.CloseBracket,
                        '}' => JavaScriptPrevTokenKind.CloseBrace,
                        _ => JavaScriptPrevTokenKind.Other
                    },
                    PreviousIdentifier = null,
                    ExpectingControlFlowOpenParen = false,
                    ControlFlowParenDepth = controlFlowParenDepth,
                    RegexAllowedAfterControlFlowParen = regexAllowedAfterControlFlowParen
                };
            }

            i++;
        }

        return new JavaScriptLexedLine(new string(sanitized), state);
    }

    private static CSharpLexedLine LexCSharpLine(string line, CSharpLexState state)
    {
        var sanitized = new char[line.Length];
        var i = 0;

        while (i < line.Length)
        {
            var ch = line[i];
            var next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (state.Mode == CSharpLexMode.BlockComment)
            {
                sanitized[i] = ' ';
                if (ch == '*' && next == '/')
                {
                    sanitized[i + 1] = ' ';
                    state = state with { Mode = CSharpLexMode.Code };
                    i += 2;
                    continue;
                }

                i++;
                continue;
            }

            if (state.Mode == CSharpLexMode.String)
            {
                sanitized[i] = ch is '"' or '\\' ? ch : ' ';

                if (state.EscapeNext)
                {
                    state = state with { EscapeNext = false };
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    state = state with { EscapeNext = true };
                    i++;
                    continue;
                }

                if (ch == '"')
                    state = state with { Mode = CSharpLexMode.Code };

                i++;
                continue;
            }

            if (state.Mode == CSharpLexMode.Char)
            {
                sanitized[i] = ch is '\'' or '\\' ? ch : ' ';

                if (state.EscapeNext)
                {
                    state = state with { EscapeNext = false };
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    state = state with { EscapeNext = true };
                    i++;
                    continue;
                }

                if (ch == '\'')
                    state = state with { Mode = CSharpLexMode.Code };

                i++;
                continue;
            }

            if (state.Mode == CSharpLexMode.VerbatimString)
            {
                sanitized[i] = ch == '"' ? '"' : ' ';
                if (ch == '"' && next == '"')
                {
                    sanitized[i + 1] = '"';
                    i += 2;
                    continue;
                }

                if (ch == '"')
                    state = state with { Mode = CSharpLexMode.Code };

                i++;
                continue;
            }

            if (state.Mode == CSharpLexMode.RawString)
            {
                sanitized[i] = ' ';
                if (ch == '"' && HasCSharpQuoteRun(line, i, state.RawDelimiterLength))
                {
                    var quoteRunLength = GetCSharpQuoteRunLength(line, i);
                    for (var j = 0; j < quoteRunLength && i + j < line.Length; j++)
                        sanitized[i + j] = ' ';

                    state = state with { Mode = CSharpLexMode.Code, RawDelimiterLength = 0 };
                    i += quoteRunLength;
                    continue;
                }

                i++;
                continue;
            }

            if (ch == '/' && next == '/')
            {
                while (i < line.Length)
                {
                    sanitized[i] = ' ';
                    i++;
                }

                break;
            }

            if (ch == '/' && next == '*')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = ' ';
                state = state with { Mode = CSharpLexMode.BlockComment };
                i += 2;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                sanitized[i] = ch;
                i++;
                continue;
            }

            if (TryReadCSharpRawStringStart(line, i, out var rawPrefixLength, out var rawDelimiterLength))
            {
                for (var j = 0; j < rawPrefixLength + rawDelimiterLength && i + j < line.Length; j++)
                    sanitized[i + j] = ' ';

                state = state with { Mode = CSharpLexMode.RawString, RawDelimiterLength = rawDelimiterLength };
                i += rawPrefixLength + rawDelimiterLength;
                continue;
            }

            if (ch == '@' && next == '"')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = '"';
                state = state with { Mode = CSharpLexMode.VerbatimString };
                i += 2;
                continue;
            }

            if (ch == '$' && next == '@' && i + 2 < line.Length && line[i + 2] == '"')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = ' ';
                sanitized[i + 2] = '"';
                state = state with { Mode = CSharpLexMode.VerbatimString };
                i += 3;
                continue;
            }

            if (ch == '@' && next == '$' && i + 2 < line.Length && line[i + 2] == '"')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = ' ';
                sanitized[i + 2] = '"';
                state = state with { Mode = CSharpLexMode.VerbatimString };
                i += 3;
                continue;
            }

            if (ch == '$' && next == '"')
            {
                sanitized[i] = ' ';
                sanitized[i + 1] = '"';
                state = state with { Mode = CSharpLexMode.String };
                i += 2;
                continue;
            }

            if (ch == '"')
            {
                sanitized[i] = '"';
                state = state with { Mode = CSharpLexMode.String };
                i++;
                continue;
            }

            if (ch == '\'')
            {
                sanitized[i] = '\'';
                state = state with { Mode = CSharpLexMode.Char };
                i++;
                continue;
            }

            sanitized[i] = ch;
            i++;
        }

        return new CSharpLexedLine(new string(sanitized), state);
    }

    private static bool TryReadCSharpRawStringStart(string line, int index, out int prefixLength, out int delimiterLength)
    {
        prefixLength = 0;
        delimiterLength = 0;
        var probe = index;

        while (probe < line.Length && line[probe] == '$')
        {
            prefixLength++;
            probe++;
        }

        delimiterLength = GetCSharpQuoteRunLength(line, probe);
        return delimiterLength >= 3;
    }

    private static int GetCSharpQuoteRunLength(string line, int index)
    {
        var length = 0;
        while (index + length < line.Length && line[index + length] == '"')
            length++;

        return length;
    }

    private static bool HasCSharpQuoteRun(string line, int index, int requiredLength)
    {
        if (requiredLength <= 0)
            return false;

        return GetCSharpQuoteRunLength(line, index) >= requiredLength;
    }

    private static bool CanStartJavaScriptRegexLiteral(JavaScriptLexState state)
    {
        if (state.PreviousTokenKind == JavaScriptPrevTokenKind.None)
            return true;

        if (state.PreviousTokenKind == JavaScriptPrevTokenKind.Other)
            return true;

        if (state.PreviousTokenKind == JavaScriptPrevTokenKind.Identifier)
        {
            return IsJavaScriptRegexPrefixKeyword(state.PreviousIdentifier);
        }

        if (state.PreviousTokenKind == JavaScriptPrevTokenKind.CloseParen)
            return state.RegexAllowedAfterControlFlowParen;

        return false;
    }

    private static bool IsJavaScriptControlFlowKeyword(string identifier)
    {
        return identifier is "if" or "for" or "while" or "switch" or "catch" or "with";
    }

    private static bool IsJavaScriptRegexPrefixKeyword(string? identifier)
    {
        return identifier is
            "return" or "throw" or "case" or "delete" or "typeof" or "void" or "new" or
            "in" or "of" or "instanceof" or "yield" or "await" or "else" or "do" or "finally";
    }

    private static int SkipJavaScriptRegexLiteral(string line, char[] sanitized, int slashIndex)
    {
        var i = slashIndex + 1;
        var inCharacterClass = false;

        while (i < line.Length)
        {
            sanitized[i] = ' ';
            var ch = line[i];
            if (ch == '\\')
            {
                if (i + 1 < line.Length)
                {
                    sanitized[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                return i;
            }

            if (ch == '[')
            {
                inCharacterClass = true;
                i++;
                continue;
            }

            if (ch == ']' && inCharacterClass)
            {
                inCharacterClass = false;
                i++;
                continue;
            }

            if (ch == '/' && !inCharacterClass)
            {
                i++;
                while (i < line.Length && char.IsLetter(line[i]))
                {
                    sanitized[i] = ' ';
                    i++;
                }

                return i - 1;
            }

            i++;
        }

        return line.Length - 1;
    }

    private static int CountBraces(string sanitizedLine)
    {
        var delta = 0;
        foreach (var ch in sanitizedLine)
        {
            if (ch == '{')
                delta++;
            else if (ch == '}')
                delta--;
        }

        return delta;
    }

    public static void ApplyFamilyScope(IEnumerable<SymbolRecord> symbols, string scopeKey)
    {
        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol.FamilyKey))
                continue;

            symbol.FamilyKey = $"{scopeKey}|{symbol.FamilyKey}";
        }
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) ResolveRange(string[] lines, int startIndex, BodyStyle bodyStyle) =>
        ResolveRange(lines, startIndex, bodyStyle, null, 0);

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) ResolveRange(string[] lines, int startIndex, BodyStyle bodyStyle, string? lang = null, int startColumn = 0)
    {
        return bodyStyle switch
        {
            BodyStyle.Brace when lang is "javascript" or "typescript" => FindJavaScriptBraceRange(lines, startIndex, lang, startColumn),
            BodyStyle.Brace when lang == "csharp" => FindCSharpBraceRange(lines, startIndex, startColumn),
            BodyStyle.Brace => FindBraceRange(lines, startIndex, startColumn),
            BodyStyle.Indent => FindIndentRange(lines, startIndex),
            BodyStyle.RubyEnd => FindRubyRange(lines, startIndex),
            BodyStyle.VisualBasicEnd => FindVisualBasicRange(lines, startIndex),
            _ => (startIndex + 1, null, null),
        };
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindJavaScriptBraceRange(string[] lines, int startIndex, string? lang, int startColumn = 0)
    {
        var depth = 0;
        var opened = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;
        var pendingArrowBody = false;
        var arrowExpressionActive = false;
        var arrowExpressionParenDepth = 0;
        var arrowExpressionBracketDepth = 0;
        var arrowExpressionBraceDepth = 0;
        var functionHeaderState = new JavaScriptTypeScriptFunctionHeaderState();
        int? bodyStartLine = null;
        var lexState = new JavaScriptLexState();

        for (int i = startIndex; i < lines.Length; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var effectiveStartColumn = startColumn;
            if (i == startIndex
                && startColumn > 0
                && TryParseJavaScriptTypeScriptMethodHeader(lexedLine.SanitizedLine, startColumn, lang, out var methodHeader))
            {
                effectiveStartColumn = methodHeader.BodyStartColumn;
            }

            var scanLine = i == startIndex && effectiveStartColumn > 0 && effectiveStartColumn < lexedLine.SanitizedLine.Length
                ? lexedLine.SanitizedLine[effectiveStartColumn..]
                : i == startIndex && effectiveStartColumn >= lexedLine.SanitizedLine.Length
                    ? string.Empty
                    : lexedLine.SanitizedLine;

            if (arrowExpressionActive
                && arrowExpressionBraceDepth == 0
                && arrowExpressionParenDepth == 0
                && arrowExpressionBracketDepth == 0
                && !StartsJavaScriptTypeScriptExpressionContinuation(scanLine))
            {
                return (i, bodyStartLine ?? startIndex + 1, i);
            }

            for (int column = 0; column < scanLine.Length; column++)
            {
                var ch = scanLine[column];
                if (!opened
                    && !arrowExpressionActive
                    && !functionHeaderState.Active
                    && IsJavaScriptTypeScriptIdentifierStart(ch))
                {
                    var tokenStart = column;
                    var tokenEnd = column + 1;
                    while (tokenEnd < scanLine.Length && IsJavaScriptTypeScriptIdentifierPart(scanLine[tokenEnd]))
                        tokenEnd++;

                    if (scanLine[tokenStart..tokenEnd] == "function")
                    {
                        BeginJavaScriptTypeScriptFunctionHeader(ref functionHeaderState);
                        column = tokenEnd - 1;
                        continue;
                    }
                }

                if (!opened && !arrowExpressionActive)
                {
                    var functionHeaderResult = ConsumeJavaScriptTypeScriptFunctionHeaderChar(
                        ref functionHeaderState,
                        scanLine,
                        column,
                        lang ?? "javascript",
                        out var functionHeaderAdvanceColumns);
                    if (functionHeaderResult == JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed)
                    {
                        column += functionHeaderAdvanceColumns;
                        continue;
                    }
                }

                if (!opened && !arrowExpressionActive && i == startIndex && ch == '=' && column + 1 < scanLine.Length && scanLine[column + 1] == '>')
                {
                    pendingArrowBody = true;
                    column++;
                    continue;
                }

                if (pendingArrowBody)
                {
                    if (char.IsWhiteSpace(ch))
                        continue;

                    bodyStartLine ??= i + 1;
                    if (ch == '{')
                        pendingArrowBody = false;
                    else
                    {
                        arrowExpressionActive = true;
                        pendingArrowBody = false;
                    }
                }

                if (arrowExpressionActive)
                {
                    if (ch == '(')
                    {
                        arrowExpressionParenDepth++;
                        continue;
                    }

                    if (ch == ')' && arrowExpressionParenDepth > 0)
                    {
                        arrowExpressionParenDepth--;
                        continue;
                    }

                    if (ch == '[')
                    {
                        arrowExpressionBracketDepth++;
                        continue;
                    }

                    if (ch == ']' && arrowExpressionBracketDepth > 0)
                    {
                        arrowExpressionBracketDepth--;
                        continue;
                    }

                    if (ch == '{')
                    {
                        arrowExpressionBraceDepth++;
                        continue;
                    }

                    if (ch == '}' && arrowExpressionBraceDepth > 0)
                    {
                        arrowExpressionBraceDepth--;
                        continue;
                    }

                    if (ch == ';'
                        && arrowExpressionParenDepth == 0
                        && arrowExpressionBracketDepth == 0
                        && arrowExpressionBraceDepth == 0)
                    {
                        return (i + 1, bodyStartLine ?? startIndex + 1, i + 1);
                    }

                    continue;
                }

                if (!opened)
                {
                    if (ch == '(')
                    {
                        parenDepth++;
                        continue;
                    }

                    if (ch == ')' && parenDepth > 0)
                    {
                        parenDepth--;
                        continue;
                    }

                    if (ch == '[')
                    {
                        bracketDepth++;
                        continue;
                    }

                    if (ch == ']' && bracketDepth > 0)
                    {
                        bracketDepth--;
                        continue;
                    }

                    if (ch == '<')
                    {
                        if (lang == "typescript" && parenDepth == 0 && bracketDepth == 0)
                            angleDepth++;
                        continue;
                    }

                    if (ch == '>' && angleDepth > 0)
                    {
                        angleDepth--;
                        continue;
                    }
                }

                if (ch == '{')
                {
                    if (!opened && (parenDepth > 0 || bracketDepth > 0 || angleDepth > 0))
                        continue;

                    depth++;
                    if (!opened)
                    {
                        opened = true;
                        bodyStartLine = i + 1;
                    }
                }
                else if (ch == '}' && opened)
                {
                    depth--;
                    if (depth == 0)
                        return (i + 1, bodyStartLine, i + 1);
                }
            }

            if (!opened
                && !arrowExpressionActive
                && !functionHeaderState.Active
                && parenDepth == 0
                && bracketDepth == 0
                && angleDepth == 0
                && scanLine.TrimEnd().EndsWith(';'))
                return (startIndex + 1, null, null);
        }

        if (arrowExpressionActive)
            return (lines.Length, bodyStartLine ?? startIndex + 1, lines.Length);

        return opened
            ? (lines.Length, bodyStartLine, lines.Length)
            : (startIndex + 1, null, null);
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindBraceRange(string[] lines, int startIndex, int startColumn = 0)
    {
        int depth = 0;
        bool opened = false;
        int? bodyStartLine = null;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var scanLine = i == startIndex && startColumn > 0 && startColumn < lines[i].Length
                ? lines[i][startColumn..]
                : i == startIndex && startColumn >= lines[i].Length
                    ? string.Empty
                    : lines[i];

            foreach (var c in scanLine)
            {
                if (c == '{')
                {
                    depth++;
                    if (!opened)
                    {
                        opened = true;
                        bodyStartLine = i + 1;
                    }
                }
                else if (c == '}' && opened)
                {
                    depth--;
                    if (depth == 0)
                        return (i + 1, bodyStartLine, i + 1);
                }
            }

            if (!opened && scanLine.TrimEnd().EndsWith(';'))
                return (startIndex + 1, null, null);
        }

        return opened
            ? (lines.Length, bodyStartLine, lines.Length)
            : (startIndex + 1, null, null);
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindCSharpBraceRange(string[] lines, int startIndex, int startColumn = 0)
    {
        int depth = 0;
        bool opened = false;
        int? bodyStartLine = null;
        var lexState = new CSharpLexState();

        for (int i = startIndex; i < lines.Length; i++)
        {
            var lexedLine = LexCSharpLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var scanLine = i == startIndex && startColumn > 0 && startColumn < sanitizedLine.Length
                ? sanitizedLine[startColumn..]
                : i == startIndex && startColumn >= sanitizedLine.Length
                    ? string.Empty
                    : sanitizedLine;

            foreach (var c in scanLine)
            {
                if (c == '{')
                {
                    depth++;
                    if (!opened)
                    {
                        opened = true;
                        bodyStartLine = i + 1;
                    }
                }
                else if (c == '}' && opened)
                {
                    depth--;
                    if (depth == 0)
                        return (i + 1, bodyStartLine, i + 1);
                }
            }

            if (!opened && scanLine.TrimEnd().EndsWith(';'))
                return (startIndex + 1, null, null);
        }

        return opened
            ? (lines.Length, bodyStartLine, lines.Length)
            : (startIndex + 1, null, null);
    }

    private static int FindJavaScriptBodyOpenBraceIndex(string[] lines, int startIndex, int bodyStartIndex, string? lang, int startColumn = 0)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;
        var functionHeaderState = new JavaScriptTypeScriptFunctionHeaderState();
        var lexState = new JavaScriptLexState();

        for (int i = startIndex; i <= bodyStartIndex; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;

            var initialColumn = i == startIndex ? Math.Max(0, startColumn) : 0;
            for (int column = initialColumn; column < sanitizedLine.Length; column++)
            {
                var ch = sanitizedLine[column];
                if (!functionHeaderState.Active && IsJavaScriptTypeScriptIdentifierStart(ch))
                {
                    var tokenStart = column;
                    var tokenEnd = column + 1;
                    while (tokenEnd < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[tokenEnd]))
                        tokenEnd++;

                    if (sanitizedLine[tokenStart..tokenEnd] == "function")
                    {
                        BeginJavaScriptTypeScriptFunctionHeader(ref functionHeaderState);
                        column = tokenEnd - 1;
                        continue;
                    }
                }

                var functionHeaderResult = ConsumeJavaScriptTypeScriptFunctionHeaderChar(
                    ref functionHeaderState,
                    sanitizedLine,
                    column,
                    lang ?? "javascript",
                    out var functionHeaderAdvanceColumns);
                if (functionHeaderResult == JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed)
                {
                    column += functionHeaderAdvanceColumns;
                    continue;
                }

                if (!char.IsWhiteSpace(ch))
                {
                    if (ch == '(')
                    {
                        parenDepth++;
                        continue;
                    }

                    if (ch == ')' && parenDepth > 0)
                    {
                        parenDepth--;
                        continue;
                    }

                    if (ch == '[')
                    {
                        bracketDepth++;
                        continue;
                    }

                    if (ch == ']' && bracketDepth > 0)
                    {
                        bracketDepth--;
                        continue;
                    }

                    if (ch == '<')
                    {
                        if (lang == "typescript" && parenDepth == 0 && bracketDepth == 0)
                            angleDepth++;
                        continue;
                    }

                    if (ch == '>' && angleDepth > 0)
                    {
                        angleDepth--;
                        continue;
                    }
                }

                if (ch != '{')
                    continue;

                if (parenDepth > 0 || bracketDepth > 0 || angleDepth > 0)
                    continue;

                return column;
            }
        }

        return -1;
    }

    private static int FindJavaScriptSameLineBodyEndColumn(string line, int startColumn, string? lang)
    {
        var sanitizedLine = LexJavaScriptLine(line, new JavaScriptLexState()).SanitizedLine;
        if (!TryParseJavaScriptTypeScriptMethodHeader(sanitizedLine, startColumn, lang, out var methodHeader))
            return -1;

        var depth = 0;
        var opened = false;

        for (int column = Math.Max(0, methodHeader.BodyStartColumn); column < sanitizedLine.Length; column++)
        {
            var ch = sanitizedLine[column];
            if (ch == '{')
            {
                depth++;
                opened = true;
            }
            else if (ch == '}' && opened)
            {
                depth--;
                if (depth == 0)
                    return column;
            }
        }

        return -1;
    }

    private static int FindJavaScriptTypeScriptSymbolStartColumn(string line, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return 0;

        var startColumn = line.IndexOf(signature, StringComparison.Ordinal);
        return startColumn >= 0 ? startColumn : 0;
    }

    private static int FindJavaScriptTypeScriptSameLineBraceEndColumn(string line, int startColumn, string? lang)
    {
        var sanitizedLine = LexJavaScriptLine(line, new JavaScriptLexState()).SanitizedLine;
        var bodyStartColumn = FindJavaScriptTypeScriptBodyOpenBraceColumn(sanitizedLine, startColumn, lang);
        if (bodyStartColumn < 0)
            return -1;

        var depth = 0;
        var opened = false;

        for (int column = bodyStartColumn; column < sanitizedLine.Length; column++)
        {
            var ch = sanitizedLine[column];
            if (ch == '{')
            {
                depth++;
                opened = true;
            }
            else if (ch == '}' && opened)
            {
                depth--;
                if (depth == 0)
                    return column;
            }
        }

        return -1;
    }

    private static int FindJavaScriptTypeScriptBodyOpenBraceColumn(string sanitizedLine, int startColumn, string? lang)
    {
        if (TryParseJavaScriptTypeScriptMethodHeader(sanitizedLine, startColumn, lang, out var methodHeader))
            return methodHeader.BodyStartColumn;

        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;
        var functionHeaderState = new JavaScriptTypeScriptFunctionHeaderState();
        for (int column = Math.Max(0, startColumn); column < sanitizedLine.Length; column++)
        {
            var ch = sanitizedLine[column];
            if (!functionHeaderState.Active && IsJavaScriptTypeScriptIdentifierStart(ch))
            {
                var tokenStart = column;
                var tokenEnd = column + 1;
                while (tokenEnd < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[tokenEnd]))
                    tokenEnd++;

                if (sanitizedLine[tokenStart..tokenEnd] == "function")
                {
                    BeginJavaScriptTypeScriptFunctionHeader(ref functionHeaderState);
                    column = tokenEnd - 1;
                    continue;
                }
            }

            var functionHeaderResult = ConsumeJavaScriptTypeScriptFunctionHeaderChar(
                ref functionHeaderState,
                sanitizedLine,
                column,
                lang ?? "javascript",
                out var functionHeaderAdvanceColumns);
            if (functionHeaderResult == JavaScriptTypeScriptFunctionHeaderConsumeResult.Consumed)
            {
                column += functionHeaderAdvanceColumns;
                continue;
            }

            if (!char.IsWhiteSpace(ch))
            {
                if (ch == '(')
                {
                    parenDepth++;
                    continue;
                }

                if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    continue;
                }

                if (ch == ']' && bracketDepth > 0)
                {
                    bracketDepth--;
                    continue;
                }

                if (ch == '<')
                {
                    if (lang == "typescript" && parenDepth == 0 && bracketDepth == 0)
                        angleDepth++;
                    continue;
                }

                if (ch == '>' && angleDepth > 0)
                {
                    angleDepth--;
                    continue;
                }
            }

            if (ch != '{')
                continue;

            if (parenDepth > 0 || bracketDepth > 0 || angleDepth > 0)
                continue;

            return column;
        }

        return -1;
    }

    private static string NormalizeTypeScriptBareMethodMatchInput(string input)
    {
        if (!input.Contains('<', StringComparison.Ordinal) && !input.Contains('{', StringComparison.Ordinal))
            return input;

        if (!TryParseJavaScriptTypeScriptMethodHeader(input, 0, "typescript", out var methodHeader))
            return input;

        var chars = input.ToCharArray();
        if (methodHeader.GenericStartColumn != null && methodHeader.GenericEndColumn != null)
        {
            for (int replaceIndex = methodHeader.GenericStartColumn.Value; replaceIndex <= methodHeader.GenericEndColumn.Value; replaceIndex++)
                chars[replaceIndex] = ' ';
        }

        if (methodHeader.ReturnTypeStartColumn != null && methodHeader.ReturnTypeEndColumn != null)
        {
            for (int replaceIndex = methodHeader.ReturnTypeStartColumn.Value; replaceIndex <= methodHeader.ReturnTypeEndColumn.Value; replaceIndex++)
            {
                if (chars[replaceIndex] == '{')
                    chars[replaceIndex] = '(';
                else if (chars[replaceIndex] == '}')
                    chars[replaceIndex] = ')';
            }
        }

        return new string(chars);
    }

    private static bool TryParseJavaScriptTypeScriptMethodHeader(string sanitizedLine, int startColumn, string? lang, out JavaScriptTypeScriptMethodHeaderInfo methodHeader)
    {
        return ParseJavaScriptTypeScriptMethodHeader(sanitizedLine, startColumn, lang, out methodHeader)
            == JavaScriptTypeScriptMethodHeaderParseStatus.Parsed;
    }

    private static JavaScriptTypeScriptMethodHeaderParseStatus ParseJavaScriptTypeScriptMethodHeader(string sanitizedLine, int startColumn, string? lang, out JavaScriptTypeScriptMethodHeaderInfo methodHeader)
    {
        methodHeader = default;
        var index = Math.Max(0, startColumn);
        string? visibility = null;

        while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
            index++;

        while (index < sanitizedLine.Length)
        {
            while (true)
            {
                if (!TryReadJavaScriptTypeScriptMethodToken(sanitizedLine, ref index, out var token))
                    return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
                    index++;

                if (TypeScriptBareMethodModifiers.Contains(token)
                    && CanTreatJavaScriptTypeScriptMethodTokenAsModifier(sanitizedLine, index))
                {
                    if (token is "public" or "private" or "protected")
                        visibility = token;
                    continue;
                }

                var isGenerator = token == "*";
                if (!isGenerator && index < sanitizedLine.Length && sanitizedLine[index] == '*')
                {
                    isGenerator = true;
                    index++;
                    while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
                        index++;
                }

                if (isGenerator)
                {
                    if (!TryReadJavaScriptTypeScriptMethodName(sanitizedLine, ref index, out var generatorName))
                        return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                    token = generatorName;
                }

                var name = token;

                int? genericStartColumn = null;
                int? genericEndColumn = null;
                if (lang == "typescript" && index < sanitizedLine.Length && sanitizedLine[index] == '<')
                {
                    genericStartColumn = index;
                    var angleDepth = 0;
                    while (index < sanitizedLine.Length)
                    {
                        if (sanitizedLine[index] == '<')
                        {
                            angleDepth++;
                        }
                        else if (sanitizedLine[index] == '=' && index + 1 < sanitizedLine.Length && sanitizedLine[index + 1] == '>')
                        {
                            index += 2;
                            continue;
                        }
                        else if (sanitizedLine[index] == '>')
                        {
                            angleDepth--;
                            if (angleDepth == 0)
                            {
                                genericEndColumn = index;
                                index++;
                                break;
                            }
                        }

                        index++;
                    }

                    if (genericEndColumn == null)
                        return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                    while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
                        index++;
                }

                if (index >= sanitizedLine.Length || sanitizedLine[index] != '(')
                    return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                var parenDepth = 0;
                while (index < sanitizedLine.Length)
                {
                    if (sanitizedLine[index] == '(')
                    {
                        parenDepth++;
                    }
                    else if (sanitizedLine[index] == ')')
                    {
                        parenDepth--;
                        if (parenDepth == 0)
                        {
                            index++;
                            break;
                        }
                    }

                    index++;
                }

                if (parenDepth != 0)
                    return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                while (index < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[index]))
                    index++;

                int? returnTypeStartColumn = null;
                int? returnTypeEndColumn = null;
                if (lang == "typescript" && index < sanitizedLine.Length && sanitizedLine[index] == ':')
                {
                    returnTypeStartColumn = index;
                    index++;
                    var returnParenDepth = 0;
                    var returnBracketDepth = 0;
                    var returnAngleDepth = 0;
                    var returnBraceDepth = 0;
                    var sawReturnTypeToken = false;
                    string? previousReturnToken = ":";

                    while (index < sanitizedLine.Length)
                    {
                        var ch = sanitizedLine[index];
                        if (char.IsWhiteSpace(ch))
                        {
                            index++;
                            continue;
                        }

                        if (ch == ';'
                            && returnParenDepth == 0
                            && returnBracketDepth == 0
                            && returnAngleDepth == 0
                            && returnBraceDepth == 0)
                        {
                            returnTypeEndColumn ??= index - 1;
                            methodHeader = new JavaScriptTypeScriptMethodHeaderInfo(name, -1, visibility, genericStartColumn, genericEndColumn, returnTypeStartColumn, returnTypeEndColumn, index, false);
                            return JavaScriptTypeScriptMethodHeaderParseStatus.DeclarationOnly;
                        }

                        if (ch == '(')
                        {
                            returnParenDepth++;
                            sawReturnTypeToken = true;
                            previousReturnToken = "(";
                            index++;
                            continue;
                        }

                        if (ch == ')' && returnParenDepth > 0)
                        {
                            returnParenDepth--;
                            previousReturnToken = ")";
                            index++;
                            continue;
                        }

                        if (ch == '[')
                        {
                            returnBracketDepth++;
                            sawReturnTypeToken = true;
                            previousReturnToken = "[";
                            index++;
                            continue;
                        }

                        if (ch == ']' && returnBracketDepth > 0)
                        {
                            returnBracketDepth--;
                            previousReturnToken = "]";
                            index++;
                            continue;
                        }

                        if (ch == '<')
                        {
                            returnAngleDepth++;
                            sawReturnTypeToken = true;
                            previousReturnToken = "<";
                            index++;
                            continue;
                        }

                        if (ch == '>' && returnAngleDepth > 0)
                        {
                            returnAngleDepth--;
                            previousReturnToken = ">";
                            index++;
                            continue;
                        }

                        if (ch == '{')
                        {
                            if (returnParenDepth == 0 && returnBracketDepth == 0 && returnAngleDepth == 0 && returnBraceDepth == 0)
                            {
                                if (CanStartJavaScriptTypeScriptReturnTypeObjectLiteral(previousReturnToken))
                                {
                                    returnBraceDepth++;
                                    sawReturnTypeToken = true;
                                    previousReturnToken = "{";
                                    index++;
                                    continue;
                                }

                                if (sawReturnTypeToken)
                                {
                                    returnTypeEndColumn = index - 1;
                                    methodHeader = new JavaScriptTypeScriptMethodHeaderInfo(name, index, visibility, genericStartColumn, genericEndColumn, returnTypeStartColumn, returnTypeEndColumn, index);
                                    return JavaScriptTypeScriptMethodHeaderParseStatus.Parsed;
                                }
                            }

                            returnBraceDepth++;
                            sawReturnTypeToken = true;
                            previousReturnToken = "{";
                            index++;
                            continue;
                        }

                        if (ch == '}' && returnBraceDepth > 0)
                        {
                            returnBraceDepth--;
                            previousReturnToken = "}";
                            index++;
                            continue;
                        }

                        if (ch == '?' || ch == ':' || ch == '|' || ch == '&' || ch == ',')
                        {
                            sawReturnTypeToken = true;
                            previousReturnToken = ch.ToString();
                            index++;
                            continue;
                        }

                        if (ch == '=' && index + 1 < sanitizedLine.Length && sanitizedLine[index + 1] == '>')
                        {
                            sawReturnTypeToken = true;
                            previousReturnToken = "=>";
                            index += 2;
                            continue;
                        }

                        if (TrySkipTypeScriptTypeToken(sanitizedLine, ref index, out var typeToken))
                        {
                            sawReturnTypeToken = true;
                            previousReturnToken = typeToken;
                            continue;
                        }

                        sawReturnTypeToken = true;
                        previousReturnToken = ch.ToString();
                        index++;
                    }

                    return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;
                }

                if (lang == "typescript" && index < sanitizedLine.Length && sanitizedLine[index] == ';')
                {
                    methodHeader = new JavaScriptTypeScriptMethodHeaderInfo(name, -1, visibility, genericStartColumn, genericEndColumn, returnTypeStartColumn, returnTypeEndColumn, index, false);
                    return JavaScriptTypeScriptMethodHeaderParseStatus.DeclarationOnly;
                }

                if (index >= sanitizedLine.Length || sanitizedLine[index] != '{')
                    return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;

                methodHeader = new JavaScriptTypeScriptMethodHeaderInfo(name, index, visibility, genericStartColumn, genericEndColumn, returnTypeStartColumn, returnTypeEndColumn, index);
                return JavaScriptTypeScriptMethodHeaderParseStatus.Parsed;
            }
        }

        return JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid;
    }

    private static bool TryCaptureJavaScriptTypeScriptMethodHeader(
        string[] lines,
        int startIndex,
        int startColumn,
        int scanEndExclusive,
        string firstSanitizedLine,
        JavaScriptLexState nextLineLexState,
        string? lang,
        out JavaScriptTypeScriptMethodHeaderCapture methodCapture)
    {
        methodCapture = default;
        var sourceBuilder = new System.Text.StringBuilder();
        var sanitizedBuilder = new System.Text.StringBuilder();

        var firstSourceSegment = startColumn < lines[startIndex].Length
            ? lines[startIndex][startColumn..]
            : string.Empty;
        var firstSanitizedSegment = startColumn < firstSanitizedLine.Length
            ? firstSanitizedLine[startColumn..]
            : string.Empty;
        sourceBuilder.Append(firstSourceSegment);
        sanitizedBuilder.Append(lang == "typescript"
            ? NormalizeTypeScriptBareMethodMatchInput(firstSanitizedSegment)
            : firstSanitizedSegment);

        if (TryFinalizeJavaScriptTypeScriptMethodHeaderCapture(
            sourceBuilder.ToString(),
            sanitizedBuilder.ToString(),
            startIndex,
            startColumn,
            lang,
            out methodCapture))
        {
            return true;
        }

        var lexState = nextLineLexState;
        for (int lineIndex = startIndex + 1; lineIndex < scanEndExclusive; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;

            sourceBuilder.Append('\n');
            sourceBuilder.Append(lines[lineIndex]);
            sanitizedBuilder.Append('\n');
            sanitizedBuilder.Append(lang == "typescript"
                ? NormalizeTypeScriptBareMethodMatchInput(lexedLine.SanitizedLine)
                : lexedLine.SanitizedLine);

            if (TryFinalizeJavaScriptTypeScriptMethodHeaderCapture(
                sourceBuilder.ToString(),
                sanitizedBuilder.ToString(),
                startIndex,
                startColumn,
                lang,
                out methodCapture))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFinalizeJavaScriptTypeScriptMethodHeaderCapture(
        string sourceHeader,
        string sanitizedHeader,
        int startIndex,
        int startColumn,
        string? lang,
        out JavaScriptTypeScriptMethodHeaderCapture methodCapture)
    {
        methodCapture = default;
        var parseStatus = ParseJavaScriptTypeScriptMethodHeader(sanitizedHeader, 0, lang, out var methodHeader);
        if (parseStatus == JavaScriptTypeScriptMethodHeaderParseStatus.IncompleteOrInvalid)
            return false;

        var headerEndLocationColumn = methodHeader.HasBody
            ? methodHeader.BodyStartColumn
            : methodHeader.HeaderEndColumn ?? -1;
        if (!TryMapJavaScriptTypeScriptHeaderColumnToSourceLocation(
            sourceHeader,
            startIndex,
            startColumn,
            headerEndLocationColumn,
            out var headerEndLineIndex,
            out var headerEndColumn))
        {
            return false;
        }

        var bodyStartLineIndex = -1;
        var bodyStartColumn = -1;
        if (methodHeader.HasBody && !TryMapJavaScriptTypeScriptHeaderColumnToSourceLocation(
            sourceHeader,
            startIndex,
            startColumn,
            methodHeader.BodyStartColumn,
            out bodyStartLineIndex,
            out bodyStartColumn))
        {
            return false;
        }

        methodCapture = new JavaScriptTypeScriptMethodHeaderCapture(
            sourceHeader,
            methodHeader,
            headerEndLineIndex,
            headerEndColumn,
            bodyStartLineIndex,
            bodyStartColumn);
        return true;
    }

    private static bool TryMapJavaScriptTypeScriptHeaderColumnToSourceLocation(
        string sourceHeader,
        int startIndex,
        int startColumn,
        int headerColumn,
        out int lineIndex,
        out int column)
    {
        lineIndex = startIndex;
        column = startColumn;
        if (headerColumn < 0 || headerColumn >= sourceHeader.Length)
            return false;

        for (int i = 0; i < headerColumn; i++)
        {
            if (sourceHeader[i] == '\n')
            {
                lineIndex++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return true;
    }

    private static string BuildJavaScriptTypeScriptBareMethodSignature(
        string[] lines,
        int startIndex,
        int startColumn,
        int? bodyEndLine,
        int sameLineMethodEndColumn,
        JavaScriptTypeScriptMethodHeaderCapture methodCapture,
        string? lang)
    {
        if (!methodCapture.HeaderInfo.HasBody)
        {
            if (methodCapture.HeaderEndLineIndex == startIndex && methodCapture.HeaderEndColumn >= startColumn)
                return lines[startIndex][startColumn..(methodCapture.HeaderEndColumn + 1)].Trim();

            if (methodCapture.HeaderInfo.HeaderEndColumn != null
                && methodCapture.HeaderInfo.HeaderEndColumn.Value >= 0
                && methodCapture.HeaderInfo.HeaderEndColumn.Value < methodCapture.SourceHeader.Length)
            {
                return methodCapture.SourceHeader[..(methodCapture.HeaderInfo.HeaderEndColumn.Value + 1)].Trim();
            }

            return methodCapture.SourceHeader.Trim();
        }

        if (bodyEndLine == startIndex + 1 && sameLineMethodEndColumn >= startColumn)
            return lines[startIndex][startColumn..(sameLineMethodEndColumn + 1)].Trim();

        if (methodCapture.HeaderInfo.BodyStartColumn < 0
            || methodCapture.HeaderInfo.BodyStartColumn >= methodCapture.SourceHeader.Length)
        {
            return methodCapture.SourceHeader.Trim();
        }

        return methodCapture.SourceHeader[..(methodCapture.HeaderInfo.BodyStartColumn + 1)].Trim();
    }

    private static string? GetJavaScriptTypeScriptBareMethodReturnType(string sourceHeader, JavaScriptTypeScriptMethodHeaderInfo methodHeader, string? lang)
    {
        if (lang != "typescript"
            || methodHeader.ReturnTypeStartColumn == null
            || methodHeader.ReturnTypeEndColumn == null)
            return null;

        var returnTypeStartColumn = methodHeader.ReturnTypeStartColumn.Value + 1;
        var returnTypeEndColumn = methodHeader.ReturnTypeEndColumn.Value;
        if (returnTypeEndColumn < returnTypeStartColumn || returnTypeEndColumn >= sourceHeader.Length)
            return null;

        return NormalizeMetadata(sourceHeader[returnTypeStartColumn..(returnTypeEndColumn + 1)]);
    }

    private static bool TryGetJavaScriptTypeScriptNextToken(
        string[] lines,
        int startIndex,
        int startColumn,
        bool skipWrappingParens,
        out int tokenLineIndex,
        out int tokenStartColumn,
        out string? token)
    {
        tokenLineIndex = -1;
        tokenStartColumn = -1;
        token = null;

        var lexState = new JavaScriptLexState();
        for (int lineIndex = startIndex; lineIndex < lines.Length; lineIndex++)
        {
            var lexedLine = LexJavaScriptLine(lines[lineIndex], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;
            var column = lineIndex == startIndex ? startColumn : 0;

            while (column < sanitizedLine.Length)
            {
                var ch = sanitizedLine[column];
                if (char.IsWhiteSpace(ch))
                {
                    column++;
                    continue;
                }

                if (skipWrappingParens && ch == '(')
                {
                    column++;
                    continue;
                }

                if (!IsJavaScriptTypeScriptIdentifierStart(ch))
                    return false;

                var tokenStart = column;
                column++;
                while (column < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[column]))
                    column++;

                tokenLineIndex = lineIndex;
                tokenStartColumn = tokenStart;
                token = sanitizedLine[tokenStart..column];
                return true;
            }
        }

        return false;
    }

    private static bool IsJavaScriptTypeScriptIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_' || ch == '$';

    private static bool IsJavaScriptTypeScriptIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';

    private static bool TrySkipTypeScriptTypeToken(string sanitizedLine, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= sanitizedLine.Length)
            return false;

        return TryReadJavaScriptTypeScriptQuotedLiteralToken(sanitizedLine, ref index, out token)
            || TryReadJavaScriptTypeScriptNumericLiteralToken(sanitizedLine, ref index, out token)
            || TryReadJavaScriptTypeScriptIdentifierToken(sanitizedLine, ref index, out token);
    }

    private static bool TryReadJavaScriptTypeScriptIdentifierToken(string input, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= input.Length || !IsJavaScriptTypeScriptIdentifierStart(input[index]))
            return false;

        var tokenStart = index;
        index++;
        while (index < input.Length && IsJavaScriptTypeScriptIdentifierPart(input[index]))
            index++;

        token = input[tokenStart..index];
        return true;
    }

    private static bool TryReadJavaScriptTypeScriptQuotedLiteralToken(string input, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= input.Length || input[index] is not ('\'' or '"' or '`'))
            return false;

        var probe = index;
        var delimiter = input[probe];
        var tokenStart = probe;
        var escapeNext = false;
        probe++;
        while (probe < input.Length)
        {
            var ch = input[probe];
            if (escapeNext)
            {
                escapeNext = false;
                probe++;
                continue;
            }

            if (ch == '\\')
            {
                escapeNext = true;
                probe++;
                continue;
            }

            if (ch == delimiter)
            {
                probe++;
                index = probe;
                token = input[tokenStart..index];
                return true;
            }

            probe++;
        }

        return false;
    }

    private static bool TryReadJavaScriptTypeScriptNumericLiteralToken(string input, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= input.Length || !char.IsDigit(input[index]))
            return false;

        var tokenStart = index;
        if (input[index] == '0' && index + 1 < input.Length && input[index + 1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
        {
            index += 2;
            while (index < input.Length && IsJavaScriptTypeScriptNumericLiteralPart(input[index], allowDecimalPoint: false))
                index++;
        }
        else
        {
            while (index < input.Length && IsJavaScriptTypeScriptNumericLiteralPart(input[index], allowDecimalPoint: true))
                index++;
        }

        token = input[tokenStart..index];
        return true;
    }

    private static bool IsJavaScriptTypeScriptNumericLiteralPart(char ch, bool allowDecimalPoint)
    {
        if (char.IsLetterOrDigit(ch) || ch == '_')
            return true;

        return allowDecimalPoint && ch == '.';
    }

    private static bool TryReadJavaScriptTypeScriptMethodToken(string sanitizedLine, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= sanitizedLine.Length)
            return false;

        if (sanitizedLine[index] == '*')
        {
            token = "*";
            index++;
            return true;
        }

        return TryReadJavaScriptTypeScriptMethodName(sanitizedLine, ref index, out token);
    }

    private static bool TryReadJavaScriptTypeScriptMethodName(string sanitizedLine, ref int index, out string name)
    {
        name = string.Empty;
        if (index >= sanitizedLine.Length)
            return false;

        var tokenStart = index;
        if (TryReadJavaScriptTypeScriptQuotedLiteralToken(sanitizedLine, ref index, out name)
            || TryReadJavaScriptTypeScriptNumericLiteralToken(sanitizedLine, ref index, out name))
        {
            return true;
        }

        if (sanitizedLine[index] == '[')
        {
            var bracketDepth = 0;
            while (index < sanitizedLine.Length)
            {
                if (sanitizedLine[index] == '[')
                    bracketDepth++;
                else if (sanitizedLine[index] == ']')
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        index++;
                        name = sanitizedLine[tokenStart..index];
                        return true;
                    }
                }

                index++;
            }

            return false;
        }

        if (sanitizedLine[index] == '#')
        {
            index++;
            if (index >= sanitizedLine.Length || !IsJavaScriptTypeScriptIdentifierStart(sanitizedLine[index]))
                return false;
        }
        else if (!IsJavaScriptTypeScriptIdentifierStart(sanitizedLine[index]))
        {
            return false;
        }

        index++;
        while (index < sanitizedLine.Length && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[index]))
            index++;

        name = sanitizedLine[tokenStart..index];
        return true;
    }

    private static string? GetJavaScriptTypeScriptMethodNameFromSource(string line, int startColumn)
    {
        var index = Math.Max(0, startColumn);
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        while (index < line.Length)
        {
            if (!TryReadJavaScriptTypeScriptSourceMethodToken(line, ref index, out var token))
                return null;

            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;

            if (TypeScriptBareMethodModifiers.Contains(token)
                && CanTreatJavaScriptTypeScriptMethodTokenAsModifier(line, index))
            {
                continue;
            }

            var isGenerator = token == "*";
            if (!isGenerator && index < line.Length && line[index] == '*')
            {
                isGenerator = true;
                index++;
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
            }

            if (isGenerator)
                return TryReadJavaScriptTypeScriptSourceMethodName(line, ref index, out var generatorName) ? generatorName : null;

            return token;
        }

        return null;
    }

    private static bool TryReadJavaScriptTypeScriptSourceMethodToken(string line, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= line.Length)
            return false;

        if (line[index] == '*')
        {
            token = "*";
            index++;
            return true;
        }

        return TryReadJavaScriptTypeScriptSourceMethodName(line, ref index, out token);
    }

    private static bool TryReadJavaScriptTypeScriptSourceQuotedLiteralToken(string line, ref int index, out string token)
    {
        token = string.Empty;
        if (index >= line.Length || line[index] is not ('\'' or '"' or '`'))
            return false;

        var delimiter = line[index];
        var tokenStart = index;
        var escapeNext = false;
        index++;
        while (index < line.Length)
        {
            var ch = line[index];
            if (escapeNext)
            {
                escapeNext = false;
                index++;
                continue;
            }

            if (ch == '\\')
            {
                escapeNext = true;
                index++;
                continue;
            }

            if (ch == delimiter)
            {
                index++;
                token = line[tokenStart..index];
                return true;
            }

            index++;
        }

        return false;
    }

    private static bool TryReadJavaScriptTypeScriptSourceMethodName(string line, ref int index, out string name)
    {
        name = string.Empty;
        if (index >= line.Length)
            return false;

        var tokenStart = index;
        if (TryReadJavaScriptTypeScriptSourceQuotedLiteralToken(line, ref index, out name)
            || TryReadJavaScriptTypeScriptNumericLiteralToken(line, ref index, out name))
        {
            return true;
        }

        if (line[index] == '[')
        {
            var bracketDepth = 0;
            var inSingleQuote = false;
            var inDoubleQuote = false;
            var inTemplateString = false;
            var escapeNext = false;
            while (index < line.Length)
            {
                var ch = line[index];
                if (escapeNext)
                {
                    escapeNext = false;
                    index++;
                    continue;
                }

                if (inSingleQuote)
                {
                    if (ch == '\\')
                        escapeNext = true;
                    else if (ch == '\'')
                        inSingleQuote = false;
                    index++;
                    continue;
                }

                if (inDoubleQuote)
                {
                    if (ch == '\\')
                        escapeNext = true;
                    else if (ch == '"')
                        inDoubleQuote = false;
                    index++;
                    continue;
                }

                if (inTemplateString)
                {
                    if (ch == '\\')
                        escapeNext = true;
                    else if (ch == '`')
                        inTemplateString = false;
                    index++;
                    continue;
                }

                if (ch == '\'')
                {
                    inSingleQuote = true;
                    index++;
                    continue;
                }

                if (ch == '"')
                {
                    inDoubleQuote = true;
                    index++;
                    continue;
                }

                if (ch == '`')
                {
                    inTemplateString = true;
                    index++;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                }
                else if (ch == ']')
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        index++;
                        name = line[tokenStart..index];
                        return true;
                    }
                }

                index++;
            }

            return false;
        }

        if (line[index] == '#')
        {
            index++;
            if (index >= line.Length || !IsJavaScriptTypeScriptIdentifierStart(line[index]))
                return false;
        }
        else if (!IsJavaScriptTypeScriptIdentifierStart(line[index]))
        {
            return false;
        }

        index++;
        while (index < line.Length && IsJavaScriptTypeScriptIdentifierPart(line[index]))
            index++;

        name = line[tokenStart..index];
        return true;
    }

    private static int FindNextJavaScriptTypeScriptTokenStart(string sanitizedLine, int startIndex)
    {
        var index = Math.Max(0, startIndex);
        while (index < sanitizedLine.Length)
        {
            if (!IsJavaScriptTypeScriptIdentifierStart(sanitizedLine[index]))
            {
                index++;
                continue;
            }

            if (index > 0 && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[index - 1]))
            {
                index++;
                continue;
            }

            return index;
        }

        return -1;
    }

    private static int FindNextJavaScriptTypeScriptMethodCandidateStart(string sanitizedLine, int startIndex)
    {
        var index = Math.Max(0, startIndex);
        while (index < sanitizedLine.Length)
        {
            var ch = sanitizedLine[index];
            if (ch != '#'
                && ch != '*'
                && ch != '['
                && ch != '\''
                && ch != '"'
                && !char.IsDigit(ch)
                && !IsJavaScriptTypeScriptIdentifierStart(ch))
            {
                index++;
                continue;
            }

            if (index > 0 && IsJavaScriptTypeScriptIdentifierPart(sanitizedLine[index - 1]))
            {
                index++;
                continue;
            }

            return index;
        }

        return -1;
    }

    private static int FindNextJavaScriptTypeScriptStatementStart(string sanitizedLine, int startIndex)
    {
        var index = Math.Max(0, startIndex);
        while (index < sanitizedLine.Length)
        {
            index = FindNextJavaScriptTypeScriptTokenStart(sanitizedLine, index);
            if (index < 0)
                return -1;

            var previous = index - 1;
            while (previous >= 0 && char.IsWhiteSpace(sanitizedLine[previous]))
                previous--;

            if (previous < 0 || sanitizedLine[previous] is ';' or '{' or '}')
                return index;

            index++;
        }

        return -1;
    }

    private static bool CanTreatJavaScriptTypeScriptMethodTokenAsModifier(string sanitizedLine, int index)
    {
        var lookahead = index;
        while (lookahead < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[lookahead]))
            lookahead++;

        if (lookahead >= sanitizedLine.Length)
            return false;

        var ch = sanitizedLine[lookahead];
        if (ch is '(' or '<')
            return false;

        if (ch == '*')
        {
            lookahead++;
            while (lookahead < sanitizedLine.Length && char.IsWhiteSpace(sanitizedLine[lookahead]))
                lookahead++;

            if (lookahead >= sanitizedLine.Length)
                return false;

            return sanitizedLine[lookahead] is '[' or '#'
                || IsJavaScriptTypeScriptIdentifierStart(sanitizedLine[lookahead]);
        }

        return ch is '[' or '#'
            || IsJavaScriptTypeScriptIdentifierStart(ch);
    }

    private static bool CanStartJavaScriptTypeScriptReturnTypeObjectLiteral(string? previousReturnToken)
    {
        return previousReturnToken is ":" or "?" or "|" or "&" or "," or "(" or "[" or "=>" or "extends";
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindIndentRange(string[] lines, int startIndex)
    {
        var currentLine = lines[startIndex];
        var currentIndent = CountIndent(currentLine);
        var trimmedCurrent = currentLine.Trim();

        if (trimmedCurrent.Contains(':'))
        {
            var suffix = trimmedCurrent[(trimmedCurrent.IndexOf(':') + 1)..].Trim();
            if (suffix.Length > 0 && !suffix.StartsWith('#'))
                return (startIndex + 1, startIndex + 1, startIndex + 1);
        }

        int? bodyStartLine = null;
        int endLine = startIndex + 1;

        for (int i = startIndex + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            var indent = CountIndent(lines[i]);
            if (bodyStartLine == null)
            {
                if (indent <= currentIndent)
                    return (endLine, null, null);

                bodyStartLine = i + 1;
                endLine = i + 1;
                continue;
            }

            if (indent <= currentIndent)
                return (endLine, bodyStartLine, endLine);

            endLine = i + 1;
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (endLine, bodyStartLine, endLine);
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindRubyRange(string[] lines, int startIndex)
    {
        var firstLine = lines[startIndex];
        if (!RubyBlockStartRegex.IsMatch(firstLine))
            return (startIndex + 1, null, null);

        int depth = 0;
        int? bodyStartLine = null;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            if (i > startIndex && bodyStartLine == null)
                bodyStartLine = i + 1;

            foreach (Match token in RubyBlockTokenRegex.Matches(trimmed))
            {
                if (token.Value == "end")
                    depth--;
                else
                    depth++;
            }

            if (depth <= 0)
            {
                if (bodyStartLine == null || bodyStartLine > i + 1)
                    return (i + 1, null, null);

                return (i + 1, bodyStartLine, i + 1);
            }
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (lines.Length, bodyStartLine, lines.Length);
    }

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) FindVisualBasicRange(string[] lines, int startIndex)
    {
        int depth = 0;
        int? bodyStartLine = null;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            if (VisualBasicContainerStartRegex.IsMatch(trimmed))
            {
                depth++;
                if (i > startIndex && bodyStartLine == null)
                    bodyStartLine = i + 1;
                continue;
            }

            if (VisualBasicContainerEndRegex.IsMatch(trimmed))
            {
                depth--;
                if (depth <= 0)
                {
                    if (bodyStartLine == null || bodyStartLine > i + 1)
                        return (i + 1, null, null);

                    return (i + 1, bodyStartLine, i + 1);
                }
            }
            else if (i > startIndex && bodyStartLine == null)
            {
                bodyStartLine = i + 1;
            }
        }

        return bodyStartLine == null
            ? (startIndex + 1, null, null)
            : (lines.Length, bodyStartLine, lines.Length);
    }

    private static string StripLeadingCSharpAttributeLists(string line)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        if (index >= line.Length || line[index] != '[')
            return line;

        var cursor = index;
        while (cursor < line.Length && line[cursor] == '[')
        {
            var depth = 0;
            var sawBracket = false;
            while (cursor < line.Length)
            {
                var ch = line[cursor++];
                if (ch == '[')
                {
                    depth++;
                    sawBracket = true;
                }
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0 && sawBracket)
                        break;
                }
            }

            if (depth != 0)
                return line;

            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                cursor++;
        }

        return cursor < line.Length ? line[cursor..] : line;
    }

    private static bool ShouldSkipCSharpSwitchExpressionPropertyCandidate(
        string? lang,
        SymbolPattern pattern,
        string matchLine,
        bool[]? csharpSwitchExpressionLines,
        int lineIndex) =>
        lang == "csharp"
        && pattern.Kind == "property"
        && csharpSwitchExpressionLines != null
        && csharpSwitchExpressionLines[lineIndex]
        && matchLine.Contains("=>", StringComparison.Ordinal);

    private static bool[] FindCSharpSwitchExpressionLines(string[] structuralLines)
    {
        var switchExpressionLines = new bool[structuralLines.Length];
        var braceKinds = new Stack<bool>();
        var activeSwitchExpressionDepth = 0;
        var pendingSwitchExpression = 0;
        var pendingSwitchKeyword = false;
        var insideBlockComment = false;

        for (int lineIndex = 0; lineIndex < structuralLines.Length; lineIndex++)
        {
            if (activeSwitchExpressionDepth > 0)
                switchExpressionLines[lineIndex] = true;

            var line = structuralLines[lineIndex];
            for (int cursor = 0; cursor < line.Length; cursor++)
            {
                if (insideBlockComment)
                {
                    if (cursor + 1 < line.Length && line[cursor] == '*' && line[cursor + 1] == '/')
                    {
                        insideBlockComment = false;
                        cursor++;
                    }

                    continue;
                }

                if (cursor + 1 < line.Length && line[cursor] == '/' && line[cursor + 1] == '/')
                    break;

                if (cursor + 1 < line.Length && line[cursor] == '/' && line[cursor + 1] == '*')
                {
                    insideBlockComment = true;
                    cursor++;
                    continue;
                }

                if (char.IsWhiteSpace(line[cursor]))
                    continue;

                if (pendingSwitchKeyword)
                {
                    if (line[cursor] == '(')
                    {
                        pendingSwitchKeyword = false;
                    }
                    else if (line[cursor] == '{')
                    {
                        pendingSwitchExpression++;
                        pendingSwitchKeyword = false;
                    }
                    else
                    {
                        pendingSwitchKeyword = false;
                    }
                }

                if (IsCSharpKeywordAt(line, cursor, "switch"))
                {
                    pendingSwitchKeyword = true;
                    cursor += "switch".Length - 1;
                    continue;
                }

                if (line[cursor] == '{')
                {
                    var startsSwitchExpression = pendingSwitchExpression > 0;
                    braceKinds.Push(startsSwitchExpression);
                    if (startsSwitchExpression)
                    {
                        pendingSwitchExpression--;
                        activeSwitchExpressionDepth++;
                    }

                    continue;
                }

                if (line[cursor] == '}' && braceKinds.Count > 0)
                {
                    if (braceKinds.Pop())
                        activeSwitchExpressionDepth--;
                }
            }
        }

        return switchExpressionLines;
    }

    private static bool IsCSharpKeywordAt(string line, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > line.Length)
            return false;

        if (!line.AsSpan(index, keyword.Length).SequenceEqual(keyword))
            return false;

        var previous = index > 0 ? line[index - 1] : '\0';
        if (previous == '@' || previous == '_' || char.IsLetterOrDigit(previous))
            return false;

        var nextIndex = index + keyword.Length;
        if (nextIndex >= line.Length)
            return true;

        var next = line[nextIndex];
        return next != '_' && !char.IsLetterOrDigit(next);
    }

    private static void CollectRecordPrimaryComponentSymbols(
        long fileId,
        string lang,
        string[] lines,
        int declarationLineIndex,
        int declarationStartColumn,
        string kind,
        string recordName,
        List<PendingRecordPrimaryComponents> pendingRecordPrimaryComponents,
        List<SymbolRecord> symbols)
    {
        if (kind is not "class" and not "struct")
            return;

        if (!TryGetRecordPrimaryComponents(
            lang,
            lines,
            declarationLineIndex,
            declarationStartColumn,
            kind,
            recordName,
            out var components,
            out var declarationEndLine))
            return;

        var parentSymbol = symbols.LastOrDefault(symbol =>
            symbol.FileId == fileId
            && symbol.Kind == kind
            && symbol.Name == recordName
            && symbol.StartLine == declarationLineIndex + 1);
        if (parentSymbol != null)
            parentSymbol.EndLine = Math.Max(parentSymbol.EndLine, declarationEndLine);

        if (components.Count > 0)
        {
            pendingRecordPrimaryComponents.Add(new PendingRecordPrimaryComponents(
                fileId,
                kind,
                recordName,
                declarationLineIndex + 1,
                components));
        }
    }

    private static void MaterializeRecordPrimaryComponentSymbols(
        List<SymbolRecord> symbols,
        List<PendingRecordPrimaryComponents> pendingRecordPrimaryComponents)
    {
        foreach (var pending in pendingRecordPrimaryComponents)
        {
            var parentSymbol = symbols.LastOrDefault(symbol =>
                symbol.FileId == pending.FileId
                && symbol.Kind == pending.Kind
                && symbol.Name == pending.RecordName
                && symbol.StartLine == pending.RecordStartLine);
            if (parentSymbol == null)
                continue;

            foreach (var component in pending.Components)
            {
                if (symbols.Any(symbol =>
                    symbol.FileId == pending.FileId
                    && symbol.Kind == "property"
                    && symbol.Name == component.Name
                    && symbol.ContainerKind == pending.Kind
                    && symbol.ContainerName == pending.RecordName
                    && symbol.StartLine >= parentSymbol.StartLine
                    && symbol.EndLine <= parentSymbol.EndLine))
                {
                    continue;
                }

                symbols.Add(new SymbolRecord
                {
                    FileId = pending.FileId,
                    Kind = "property",
                    Name = component.Name,
                    Line = component.Line,
                    StartLine = component.Line,
                    EndLine = component.Line,
                    Signature = component.Signature,
                    ContainerKind = pending.Kind,
                    ContainerName = pending.RecordName,
                    Visibility = "public",
                    ReturnType = component.Type,
                });
            }
        }
    }

    private static bool TryGetRecordPrimaryComponents(
        string lang,
        string[] lines,
        int declarationLineIndex,
        int declarationStartColumn,
        string kind,
        string recordName,
        out List<RecordPrimaryComponent> components,
        out int declarationEndLine)
    {
        components = [];
        declarationEndLine = declarationLineIndex + 1;

        if (lang is not "csharp" and not "java")
            return false;

        var declaration = CollectRecordDeclarationText(lines, declarationLineIndex, declarationStartColumn);
        if (string.IsNullOrWhiteSpace(declaration))
            return false;

        var recordRegex = GetCurrentDeclarationRecordRegex(lang, kind, recordName);
        var recordMatch = recordRegex.Match(declaration);
        if (!recordMatch.Success)
            return false;

        var parameterOpenIndex = FindRecordPrimaryComponentListStart(declaration, recordMatch.Index + recordMatch.Length);
        if (parameterOpenIndex < 0)
            return false;

        var parameterCloseIndex = FindMatchingRecordPrimaryComponentListEnd(declaration, parameterOpenIndex);
        if (parameterCloseIndex <= parameterOpenIndex)
            return false;
        var declarationTerminatorIndex = FindRecordDeclarationTerminatorIndex(declaration, parameterCloseIndex + 1);
        var declarationLineSpanEnd = declarationTerminatorIndex >= 0 ? declarationTerminatorIndex + 1 : parameterCloseIndex + 1;
        declarationEndLine = declarationLineIndex + 1 + declaration[..declarationLineSpanEnd].Count(ch => ch == '\n');

        var rawParameterList = StripRecordComponentComments(declaration[(parameterOpenIndex + 1)..parameterCloseIndex]);
        foreach (var rawComponent in SplitTopLevelRecordPrimaryComponents(rawParameterList, declarationLineIndex + 1))
        {
            if (TryParseRecordPrimaryComponent(lang, rawComponent, out var component))
                components.Add(component);
        }

        return true;
    }

    private static string CollectRecordDeclarationText(string[] lines, int declarationLineIndex, int declarationStartColumn)
    {
        var builder = new System.Text.StringBuilder();
        var parameterOpenIndex = -1;
        var parameterCloseIndex = -1;
        for (int i = declarationLineIndex; i < lines.Length; i++)
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(i == declarationLineIndex
                ? lines[i][Math.Min(declarationStartColumn, lines[i].Length)..]
                : lines[i]);

            var declaration = builder.ToString();
            if (parameterOpenIndex < 0)
            {
                parameterOpenIndex = FindRecordPrimaryComponentListStart(declaration, 0);
                if (parameterOpenIndex < 0)
                    continue;
            }

            if (parameterCloseIndex < 0)
            {
                parameterCloseIndex = FindMatchingRecordPrimaryComponentListEnd(declaration, parameterOpenIndex);
                if (parameterCloseIndex <= parameterOpenIndex)
                    continue;
            }

            if (FindRecordDeclarationTerminatorIndex(declaration, parameterCloseIndex + 1) >= 0)
                return declaration;
        }

        return builder.ToString();
    }

    private static Regex GetCurrentDeclarationRecordRegex(string lang, string kind, string recordName)
    {
        if (lang == "csharp")
        {
            return kind == "struct"
                ? new Regex(@"^\s*(?:(?:public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|partial|readonly|file|new|ref|unsafe)\s+)*record\s+struct\s+" + Regex.Escape(recordName) + @"\b", RegexOptions.CultureInvariant)
                : new Regex(@"^\s*(?:(?:public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|partial|abstract|sealed|readonly|file|new|unsafe)\s+)*record(?:\s+class)?\s+" + Regex.Escape(recordName) + @"\b", RegexOptions.CultureInvariant);
        }

        return new Regex(@"^\s*(?:public|private|protected)?\s*(?:(?:static|final|abstract|sealed|non-sealed|strictfp)\s+)*record\s+" + Regex.Escape(recordName) + @"\b", RegexOptions.CultureInvariant);
    }

    private static int FindRecordPrimaryComponentListStart(string declaration, int startIndex)
    {
        var angleDepth = 0;
        for (int i = Math.Max(0, startIndex); i < declaration.Length; i++)
        {
            var ch = declaration[i];
            if (ch == '<')
            {
                angleDepth++;
                continue;
            }

            if (ch == '>')
            {
                if (angleDepth > 0)
                    angleDepth--;
                continue;
            }

            if (ch == '(' && angleDepth == 0)
                return i;
        }

        return -1;
    }

    private static int FindMatchingRecordPrimaryComponentListEnd(string declaration, int openIndex)
    {
        var parenDepth = 0;
        var angleDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (int i = openIndex; i < declaration.Length; i++)
        {
            var ch = declaration[i];
            var next = i + 1 < declaration.Length ? declaration[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inSingleQuote = true;
                    continue;
                case '"':
                    inDoubleQuote = true;
                    continue;
                case '/' when next == '/':
                    inLineComment = true;
                    i++;
                    continue;
                case '/' when next == '*':
                    inBlockComment = true;
                    i++;
                    continue;
                case '(':
                    parenDepth++;
                    continue;
                case ')':
                    parenDepth--;
                    if (parenDepth == 0)
                        return i;
                    continue;
                case '<':
                    angleDepth++;
                    continue;
                case '>':
                    if (angleDepth > 0)
                        angleDepth--;
                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    continue;
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    continue;
            }
        }

        return -1;
    }

    private static int FindRecordDeclarationTerminatorIndex(string declaration, int startIndex)
    {
        var parenDepth = 0;
        var angleDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (int i = Math.Max(0, startIndex); i < declaration.Length; i++)
        {
            var ch = declaration[i];
            var next = i + 1 < declaration.Length ? declaration[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inSingleQuote = true;
                    continue;
                case '"':
                    inDoubleQuote = true;
                    continue;
                case '/' when next == '/':
                    inLineComment = true;
                    i++;
                    continue;
                case '/' when next == '*':
                    inBlockComment = true;
                    i++;
                    continue;
                case '(':
                    parenDepth++;
                    continue;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    continue;
                case '<':
                    angleDepth++;
                    continue;
                case '>':
                    if (angleDepth > 0)
                        angleDepth--;
                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    continue;
                case '{' when parenDepth == 0 && angleDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    return i;
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    continue;
                case ';' when parenDepth == 0 && angleDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    return i;
            }
        }

        return -1;
    }

    private static List<RecordPrimaryComponentSlice> SplitTopLevelRecordPrimaryComponents(string parameterList, int firstLineNumber)
    {
        var components = new List<RecordPrimaryComponentSlice>();
        var builder = new System.Text.StringBuilder();
        var parenDepth = 0;
        var angleDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;
        var currentLineNumber = firstLineNumber;
        var componentLineNumber = firstLineNumber;
        var componentHasToken = false;

        foreach (var ch in parameterList)
        {
            if (!componentHasToken && !char.IsWhiteSpace(ch))
            {
                componentHasToken = true;
                componentLineNumber = currentLineNumber;
            }

            if (escapeNext)
            {
                builder.Append(ch);
                escapeNext = false;
                if (ch == '\n')
                    currentLineNumber++;
                continue;
            }

            if (inSingleQuote)
            {
                builder.Append(ch);
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                else if (ch == '\n')
                    currentLineNumber++;
                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(ch);
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                else if (ch == '\n')
                    currentLineNumber++;
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inSingleQuote = true;
                    builder.Append(ch);
                    continue;
                case '"':
                    inDoubleQuote = true;
                    builder.Append(ch);
                    continue;
                case '(':
                    parenDepth++;
                    builder.Append(ch);
                    continue;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    builder.Append(ch);
                    continue;
                case '<':
                    angleDepth++;
                    builder.Append(ch);
                    continue;
                case '>':
                    if (angleDepth > 0)
                        angleDepth--;
                    builder.Append(ch);
                    continue;
                case '[':
                    bracketDepth++;
                    builder.Append(ch);
                    continue;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    builder.Append(ch);
                    continue;
                case '{':
                    braceDepth++;
                    builder.Append(ch);
                    continue;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    builder.Append(ch);
                    continue;
                case ',' when parenDepth == 0 && angleDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    var component = builder.ToString().Trim();
                    if (component.Length > 0)
                        components.Add(new RecordPrimaryComponentSlice(component, componentLineNumber));
                    builder.Clear();
                    componentHasToken = false;
                    componentLineNumber = currentLineNumber;
                    continue;
                default:
                    builder.Append(ch);
                    if (ch == '\n')
                        currentLineNumber++;
                    continue;
            }
        }

        var trailingComponent = builder.ToString().Trim();
        if (trailingComponent.Length > 0)
            components.Add(new RecordPrimaryComponentSlice(trailingComponent, componentLineNumber));

        return components;
    }

    private static string StripRecordComponentComments(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var inLineComment = false;
        var inBlockComment = false;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                    builder.Append(ch);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                    builder.Append(' ');
                }
                else if (ch == '\n')
                {
                    builder.Append(ch);
                }

                continue;
            }

            if (escapeNext)
            {
                builder.Append(ch);
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                builder.Append(ch);
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(ch);
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

            if (ch == '\'' )
            {
                inSingleQuote = true;
                builder.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                builder.Append(ch);
                continue;
            }

            if (ch == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool TryParseRecordPrimaryComponent(string lang, RecordPrimaryComponentSlice rawComponent, out RecordPrimaryComponent component)
    {
        component = default;
        if (string.IsNullOrWhiteSpace(rawComponent.Text))
            return false;

        var normalized = TrimAfterTopLevelEquals(rawComponent.Text).Trim();
        if (normalized.Length == 0)
            return false;

        var componentLine = rawComponent.Line;
        var stripped = lang == "csharp"
            ? StripLeadingCSharpRecordComponentAttributes(normalized)
            : StripLeadingJavaRecordComponentAnnotations(normalized);
        normalized = stripped.Text;
        componentLine += stripped.ConsumedNewlines;

        stripped = StripLeadingRecordComponentModifiers(lang, normalized);
        normalized = stripped.Text;
        componentLine += stripped.ConsumedNewlines;
        if (normalized.Length == 0)
            return false;

        var nameMatch = Regex.Match(normalized, @"(?<name>@?[\p{L}_$][\p{L}\p{Nd}_$]*)\s*$", RegexOptions.CultureInvariant);
        if (!nameMatch.Success)
            return false;

        var componentName = nameMatch.Groups["name"].Value.TrimStart('@');
        var componentType = normalized[..nameMatch.Index].Trim();
        if (componentName.Length == 0 || componentType.Length == 0)
            return false;

        component = new RecordPrimaryComponent(componentName, componentType, normalized, componentLine);
        return true;
    }

    private static string TrimAfterTopLevelEquals(string text)
    {
        var parenDepth = 0;
        var angleDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inSingleQuote = true;
                    continue;
                case '"':
                    inDoubleQuote = true;
                    continue;
                case '(':
                    parenDepth++;
                    continue;
                case ')':
                    if (parenDepth > 0)
                        parenDepth--;
                    continue;
                case '<':
                    angleDepth++;
                    continue;
                case '>':
                    if (angleDepth > 0)
                        angleDepth--;
                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                        bracketDepth--;
                    continue;
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                        braceDepth--;
                    continue;
                case '=' when parenDepth == 0 && angleDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    return text[..i].TrimEnd();
            }
        }

        return text;
    }

    private static StrippedRecordComponentText StripLeadingCSharpRecordComponentAttributes(string component)
    {
        var consumedNewlines = 0;
        var trimmed = TrimLeadingWhitespaceAndCountNewlines(component, ref consumedNewlines);
        while (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var endIndex = FindMatchingBracket(trimmed, 0, '[', ']');
            if (endIndex < 0)
                return new(component.Trim(), 0);

            consumedNewlines += CountNewlines(trimmed.AsSpan(0, endIndex + 1));
            trimmed = TrimLeadingWhitespaceAndCountNewlines(trimmed[(endIndex + 1)..], ref consumedNewlines);
        }

        return new(trimmed, consumedNewlines);
    }

    private static StrippedRecordComponentText StripLeadingJavaRecordComponentAnnotations(string component)
    {
        var consumedNewlines = 0;
        var trimmed = TrimLeadingWhitespaceAndCountNewlines(component, ref consumedNewlines);
        while (trimmed.StartsWith("@", StringComparison.Ordinal))
        {
            var index = 1;
            while (index < trimmed.Length && (char.IsLetterOrDigit(trimmed[index]) || trimmed[index] is '_' or '$' or '.'))
                index++;

            while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
                index++;

            if (index < trimmed.Length && trimmed[index] == '(')
            {
                var endIndex = FindMatchingBracket(trimmed, index, '(', ')');
                if (endIndex < 0)
                    return new(component.Trim(), 0);

                index = endIndex + 1;
            }

            consumedNewlines += CountNewlines(trimmed.AsSpan(0, index));
            trimmed = TrimLeadingWhitespaceAndCountNewlines(trimmed[index..], ref consumedNewlines);
        }

        return new(trimmed, consumedNewlines);
    }

    private static StrippedRecordComponentText StripLeadingRecordComponentModifiers(string lang, string component)
    {
        ReadOnlySpan<string> modifiers = lang == "csharp"
            ? ["params", "this", "ref", "out", "in", "scoped", "readonly"]
            : ["final"];

        var consumedNewlines = 0;
        var trimmed = TrimLeadingWhitespaceAndCountNewlines(component, ref consumedNewlines);
        var removedModifier = true;
        while (removedModifier)
        {
            removedModifier = false;
            foreach (var modifier in modifiers)
            {
                if (trimmed.StartsWith(modifier, StringComparison.Ordinal)
                    && trimmed.Length > modifier.Length
                    && char.IsWhiteSpace(trimmed[modifier.Length]))
                {
                    trimmed = TrimLeadingWhitespaceAndCountNewlines(trimmed[(modifier.Length + 1)..], ref consumedNewlines);
                    removedModifier = true;
                    break;
                }
            }
        }

        return new(trimmed, consumedNewlines);
    }

    private static string TrimLeadingWhitespaceAndCountNewlines(string text, ref int consumedNewlines)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            if (text[index] == '\n')
                consumedNewlines++;
            index++;
        }

        return text[index..];
    }

    private static int CountNewlines(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
                count++;
        }

        return count;
    }

    private static int FindMatchingBracket(string text, int openIndex, char openBracket, char closeBracket)
    {
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escapeNext = false;

        for (int i = openIndex; i < text.Length; i++)
        {
            var ch = text[i];
            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '\\')
                    escapeNext = true;
                else if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

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

            if (ch == openBracket)
            {
                depth++;
                continue;
            }

            if (ch == closeBracket)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static void PopulateDeclaredContainerQualifiedNames(List<SymbolRecord> symbols)
    {
        foreach (var symbol in symbols)
        {
            if (symbol.ContainerKind == null
                || symbol.ContainerName == null)
            {
                continue;
            }

            var container = symbols
                .Where(candidate =>
                candidate.FileId == symbol.FileId
                && candidate.Kind == symbol.ContainerKind
                && candidate.Name == symbol.ContainerName
                && candidate.StartLine <= symbol.StartLine
                && candidate.EndLine >= symbol.EndLine)
                .OrderByDescending(candidate => candidate.StartLine)
                .ThenBy(candidate => candidate.EndLine)
                .FirstOrDefault();
            if (container == null)
                continue;

            symbol.ContainerQualifiedName = container.ContainerQualifiedName != null
                ? $"{container.ContainerQualifiedName}.{container.Name}"
                : container.Name;
        }
    }

    private static void AssignContainers(List<SymbolRecord> symbols)
    {
        var ordered = symbols
            .OrderBy(s => s.StartLine)
            .ThenByDescending(s => s.EndLine)
            .ToList();

        var stack = new Stack<SymbolRecord>();
        foreach (var symbol in ordered)
        {
            while (stack.Count > 0 && !IsFileScopedNamespace(stack.Peek()) && symbol.StartLine > stack.Peek().EndLine)
                stack.Pop();

            while (stack.Count > 0 && !ContainsSymbol(stack.Peek(), symbol))
                stack.Pop();

            if (stack.Count > 0)
            {
                var container = stack.Peek();
                symbol.ContainerKind ??= container.Kind;
                symbol.ContainerName ??= container.Name;
                var qualifiedContainerName = BuildQualifiedContainerName(stack);
                symbol.ContainerQualifiedName = qualifiedContainerName;
                symbol.FamilyKey = BuildInheritedFamilyKey(container, qualifiedContainerName);
            }

            symbol.FamilyKey ??= BuildSelfFamilyKey(symbol, stack);

            if (CanContainSymbols(symbol))
                stack.Push(symbol);
        }
    }

    private static string? BuildQualifiedContainerName(IEnumerable<SymbolRecord> containers)
    {
        var names = containers
            .Reverse()
            .Select(container => container.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return names.Count > 0
            ? string.Join(".", names)
            : null;
    }

    private static string? BuildInheritedFamilyKey(SymbolRecord container, string? qualifiedContainerName) =>
        SupportsCrossFileFamily(container)
            ? qualifiedContainerName
            : null;

    private static string? BuildSelfFamilyKey(SymbolRecord symbol, IEnumerable<SymbolRecord> containers)
    {
        if (!SupportsCrossFileFamily(symbol))
            return null;

        var names = containers
            .Reverse()
            .Select(container => container.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Append(symbol.Name)
            .ToList();

        return names.Count > 0
            ? string.Join(".", names)
            : null;
    }

    private static bool SupportsCrossFileFamily(SymbolRecord symbol) =>
        symbol.Kind is "class" or "interface" or "struct"
        && !string.IsNullOrWhiteSpace(symbol.Signature)
        && PartialModifierRegex.IsMatch(symbol.Signature);

    private static bool CanContainSymbols(SymbolRecord symbol)
    {
        if (!ContainerKinds.Contains(symbol.Kind))
            return false;

        if (IsFileScopedNamespace(symbol))
            return true;

        return symbol.BodyStartLine != null && symbol.BodyEndLine != null;
    }

    private static bool ContainsSymbol(SymbolRecord container, SymbolRecord candidate)
    {
        if (IsFileScopedNamespace(container))
            return candidate.StartLine > container.StartLine;

        if (container.BodyStartLine == null || container.BodyEndLine == null)
            return false;

        if (candidate.StartLine == container.StartLine)
        {
            return container.Kind == "class"
                && candidate.Kind == "function"
                && container.Signature != null
                && candidate.Signature != null
                && container.Signature.Contains(candidate.Signature, StringComparison.Ordinal);
        }

        return candidate.StartLine >= container.BodyStartLine
            && candidate.StartLine <= container.BodyEndLine
            && candidate.StartLine > container.StartLine;
    }

    private static bool IsFileScopedNamespace(SymbolRecord symbol) =>
        symbol.Kind == "namespace" &&
        symbol.BodyStartLine == null &&
        symbol.BodyEndLine == null;

    private static int CountIndent(string line)
    {
        int indent = 0;
        foreach (var c in line)
        {
            if (c == ' ')
                indent++;
            else if (c == '\t')
                indent += 4;
            else
                break;
        }

        return indent;
    }

    private static string? TryGetGroup(Match match, string? groupName)
    {
        if (groupName == null || !match.Groups[groupName].Success)
            return null;

        return NormalizeMetadata(match.Groups[groupName].Value);
    }

    private static string? NormalizeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static readonly Regex ComplexityRegex = new(
        @"\b(?:if|else\s+if|elif|elsif|elseif|case|catch|except|when|while|for|foreach|guard)\b|(?:\?\?|&&|\|\||[?:](?!=))",
        RegexOptions.Compiled);

    /// <summary>
    /// Estimate cyclomatic complexity of a code body using keyword counting.
    /// This is a heuristic — not a true control-flow-graph analysis.
    /// Baseline is 1 (a straight-line function has complexity 1).
    /// コードボディのサイクロマティック複雑度をキーワードカウントで推定する。
    /// 真の制御フローグラフ解析ではなくヒューリスティック。基準値は1（直線的関数の複雑度）。
    /// </summary>
    public static int EstimateComplexity(string bodyContent)
    {
        if (string.IsNullOrWhiteSpace(bodyContent))
            return 1;
        return 1 + ComplexityRegex.Matches(bodyContent).Count;
    }
}
