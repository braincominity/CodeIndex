using System.Text.RegularExpressions;
using CodeIndex.Models;

namespace CodeIndex.Indexer;

/// <summary>
/// Extracts symbols (functions, classes, imports) using regex patterns.
/// 正規表現を使ってシンボル（関数、クラス、インポート）を抽出する。
/// </summary>
public static class SymbolExtractor
{
    private enum BodyStyle
    {
        None,
        Brace,
        Indent,
        RubyEnd,
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

    private readonly record struct JavaScriptClassScanTarget(
        int StartIndex,
        int ScanStartIndex,
        int ScanEndExclusive,
        int FirstLineScanOffset);

    private static readonly Regex JavaScriptBareMethodRegex = new(
        @"^\s*(?:(?<visibility>public|private|protected|static)\s+)*(?:async\s+)?(?<name>\w+)\s*\([^;=]*\)\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex TypeScriptBareMethodRegex = new(
        @"^\s*(?:(?<visibility>public|private|protected|static|readonly|abstract|override)\s+)*(?:async\s+)?(?<name>\w+)\s*\([^;=]*\)\s*(?::\s*(?<returnType>[^={]+))?\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex JavaScriptTypeScriptAnonymousDefaultClassRegex = new(
        @"^\s*(?<visibility>export)\s+default\s+class\b(?=\s+(?:extends|implements)\b|\s*\{)",
        RegexOptions.Compiled);

    private static readonly Regex JavaScriptTypeScriptClassExpressionRegex = new(
        @"^\s*(?:(?<visibility>export)\s+)?(?:const|let|var)\s+(?<alias>\w+)\s*=\s*class(?:\s+(?<name>\w+))?\b",
        RegexOptions.Compiled);

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
            // Negative lookahead excludes call-site lines (await/return/throw/yield/var/typeof/sizeof/nameof/default/if/for/while/switch/catch/lock/using).
            // Note: `new` is NOT excluded because `new void Hidden()` is a valid C# member-hiding declaration.
            // 戻り値型付きメソッド — 明示的インターフェース実装やネストメンバー向けに visibility 省略可。
            // negative lookahead で呼び出し行（await/return/throw/yield/var/typeof 等）を除外する。
            // 注意: `new` は除外しない。`new void Hidden()` は C# のメンバー隠蔽宣言として有効。
            new("function",  new Regex(@"^\s*(?!(?:await|return|throw|yield|var|typeof|sizeof|nameof|default|if|for|foreach|while|switch|catch|lock|using|case|else|when|break|continue|goto)\b)(?:(?<visibility>public|private|protected\s+internal|private\s+protected|protected|internal)\s+)?(?:(?:static|sealed|partial|readonly|unsafe|extern|virtual|override|abstract|async|new|file)\s+)*(?<returnType>\([^)]+\)|(?:global::)?[\w?.<>\[\],:]+)\s+(?<name>\w+)\s*(?:<[^>]+>\s*)?\(", RegexOptions.Compiled), BodyStyle.Brace, "visibility", "returnType"),
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
            new("function", new Regex(@"^\s*(?<visibility>(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?)\s*(?:(?:Shared|Overrides|Overridable|MustOverride|Async)\s+)*(?:Sub|Function)\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("interface", new Regex(@"^\s*(?<visibility>(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?)\s*Interface\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("enum",     new Regex(@"^\s*(?<visibility>(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?)\s*Enum\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("struct",   new Regex(@"^\s*(?<visibility>(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?)\s*(?:(?:Partial)\s+)*Structure\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
            new("class",    new Regex(@"^\s*(?<visibility>(?:Public|Private|Protected|Friend)(?:\s+(?:Protected|Friend))?)\s*(?:(?:Partial|MustInherit|NotInheritable)\s+)*(?:Class|Module)\s+(?<name>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None, "visibility"),
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
            new("class",    new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:TABLE|VIEW)\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("function", new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:PROCEDURE|FUNCTION|TRIGGER)\s+(?<name>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
            new("class",    new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:INDEX|UNIQUE\s+INDEX)\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[\w.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase), BodyStyle.None),
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
        var symbols = new List<SymbolRecord>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matchLine = lang == "csharp" ? StripLeadingCSharpAttributeLists(line) : line;
            foreach (var pattern in patterns)
            {
                var match = pattern.Regex.Match(matchLine);
                if (!match.Success)
                    continue;

                var name = match.Groups["name"].Success
                    ? match.Groups["name"].Value.Trim()
                    : match.Value.Trim();

                    var (endLine, bodyStartLine, bodyEndLine) = ResolveRange(lines, i, pattern.BodyStyle, lang);
                var startLine = i + 1;

                // Python @property decorator: reclassify the def as property
                // Python @property デコレータ: def を property に再分類
                var kind = pattern.Kind;
                if (kind == "function" && lang == "python" && i > 0 && lines[i - 1].TrimStart().StartsWith("@property"))
                    kind = "property";

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
                    Signature = line.Trim(),
                    Visibility = TryGetGroup(match, pattern.VisibilityGroup),
                    ReturnType = NormalizeMetadata(TryGetGroup(match, pattern.ReturnTypeGroup)),
                });
                // Stop after first match per line to avoid duplicate symbols
                // (e.g. C# method pattern + constructor pattern both matching)
                // 1行につき最初のマッチのみ採用し重複を防ぐ
                break;
            }
        }

        if (lang is "javascript" or "typescript")
            ExtractJavaScriptTypeScriptBareMethods(fileId, lang, lines, symbols);

        AssignContainers(symbols);
        return symbols;
    }

    private static void ExtractJavaScriptTypeScriptBareMethods(long fileId, string lang, string[] lines, List<SymbolRecord> symbols)
    {
        var methodRegex = lang == "javascript" ? JavaScriptBareMethodRegex : TypeScriptBareMethodRegex;
        var classScanTargets = CollectJavaScriptTypeScriptClassScanTargets(fileId, lang, lines, symbols);

        foreach (var classScanTarget in classScanTargets)
            ExtractJavaScriptTypeScriptBareMethodsInClass(fileId, lang, lines, symbols, classScanTarget, methodRegex);
    }

    private static List<JavaScriptClassScanTarget> CollectJavaScriptTypeScriptClassScanTargets(long fileId, string lang, string[] lines, List<SymbolRecord> symbols)
    {
        var targets = symbols
            .Where(s => s.Kind == "class" && s.BodyStartLine != null && s.BodyEndLine != null)
            .OrderBy(s => s.StartLine)
            .ThenByDescending(s => s.EndLine)
            .Select(s => CreateJavaScriptClassScanTarget(lines, lang, s.StartLine - 1, s.BodyStartLine, s.BodyEndLine))
            .ToList();

        var lexState = new JavaScriptLexState();
        var lexicalDepth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;

            if (lexicalDepth == 0)
            {
                TryAddJavaScriptTypeScriptSyntheticClassTarget(fileId, lang, lines, symbols, targets, i, sanitizedLine);
            }

            lexicalDepth += CountBraces(sanitizedLine);
            if (lexicalDepth < 0)
                lexicalDepth = 0;
        }

        return targets
            .OrderBy(t => t.StartIndex)
            .ThenByDescending(t => t.ScanEndExclusive)
            .ToList();
    }

    private static void TryAddJavaScriptTypeScriptSyntheticClassTarget(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        List<JavaScriptClassScanTarget> targets,
        int startIndex,
        string sanitizedLine)
    {
        var anonymousDefaultMatch = JavaScriptTypeScriptAnonymousDefaultClassRegex.Match(sanitizedLine);
        if (anonymousDefaultMatch.Success)
        {
            AddJavaScriptTypeScriptSyntheticClassTarget(
                fileId,
                lang,
                lines,
                symbols,
                targets,
                startIndex,
                containerName: "default",
                visibility: TryGetGroup(anonymousDefaultMatch, "visibility"));
            return;
        }

        var classExpressionMatch = JavaScriptTypeScriptClassExpressionRegex.Match(sanitizedLine);
        if (!classExpressionMatch.Success)
            return;

        var containerName = TryGetGroup(classExpressionMatch, "alias")
            ?? TryGetGroup(classExpressionMatch, "name")
            ?? "class";
        AddJavaScriptTypeScriptSyntheticClassTarget(
            fileId,
            lang,
            lines,
            symbols,
            targets,
            startIndex,
            containerName,
            TryGetGroup(classExpressionMatch, "visibility"));
    }

    private static void AddJavaScriptTypeScriptSyntheticClassTarget(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        List<JavaScriptClassScanTarget> targets,
        int startIndex,
        string containerName,
        string? visibility)
    {
        var (endLine, bodyStartLine, bodyEndLine) = ResolveRange(lines, startIndex, BodyStyle.Brace, lang);
        if (bodyStartLine == null || bodyEndLine == null)
            return;

        var existingClass = symbols.FirstOrDefault(s => s.Kind == "class" && s.Line == startIndex + 1 && s.Name == containerName);
        if (existingClass == null)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = containerName,
                Line = startIndex + 1,
                StartLine = startIndex + 1,
                EndLine = Math.Max(startIndex + 1, endLine),
                BodyStartLine = bodyStartLine,
                BodyEndLine = bodyEndLine,
                Signature = lines[startIndex].Trim(),
                Visibility = visibility,
            });
        }

        var candidate = CreateJavaScriptClassScanTarget(lines, lang, startIndex, bodyStartLine, bodyEndLine);
        if (!targets.Any(t => t.StartIndex == candidate.StartIndex
            && t.ScanStartIndex == candidate.ScanStartIndex
            && t.ScanEndExclusive == candidate.ScanEndExclusive
            && t.FirstLineScanOffset == candidate.FirstLineScanOffset))
        {
            targets.Add(candidate);
        }
    }

    private static JavaScriptClassScanTarget CreateJavaScriptClassScanTarget(string[] lines, string lang, int startIndex, int? bodyStartLine, int? bodyEndLine)
    {
        var scanStartIndex = bodyStartLine!.Value - 1;
        var scanEndExclusive = bodyEndLine!.Value;
        var firstLineScanOffset = FindJavaScriptBodyOpenBraceIndex(lines, startIndex, scanStartIndex, lang);
        if (firstLineScanOffset >= 0)
            firstLineScanOffset++;
        else
            firstLineScanOffset = 0;

        return new JavaScriptClassScanTarget(
            startIndex,
            scanStartIndex,
            scanEndExclusive,
            firstLineScanOffset);
    }

    private static void ExtractJavaScriptTypeScriptBareMethodsInClass(
        long fileId,
        string lang,
        string[] lines,
        List<SymbolRecord> symbols,
        JavaScriptClassScanTarget classScanTarget,
        Regex methodRegex)
    {
        if (classScanTarget.ScanStartIndex >= classScanTarget.ScanEndExclusive)
            return;

        var scanStartIndex = classScanTarget.ScanStartIndex;
        var scanEndExclusive = classScanTarget.ScanEndExclusive;
        var nestedBraceDepth = 0;
        var lexState = new JavaScriptLexState();

        for (int i = scanStartIndex; i < scanEndExclusive; i++)
        {
            var line = lines[i];
            var lexedLine = LexJavaScriptLine(line, lexState);
            lexState = lexedLine.EndState;
            var matchInput = lexedLine.SanitizedLine;
            var braceCountInput = lexedLine.SanitizedLine;
            var signatureInput = line;
            var methodStartColumn = 0;

            if (i == scanStartIndex && classScanTarget.FirstLineScanOffset > 0)
            {
                if (classScanTarget.FirstLineScanOffset >= matchInput.Length)
                    matchInput = string.Empty;
                else
                    matchInput = matchInput[classScanTarget.FirstLineScanOffset..];

                braceCountInput = matchInput;

                if (classScanTarget.FirstLineScanOffset >= signatureInput.Length)
                    signatureInput = string.Empty;
                else
                    signatureInput = signatureInput[classScanTarget.FirstLineScanOffset..];

                methodStartColumn = classScanTarget.FirstLineScanOffset;
            }

            if (nestedBraceDepth == 0)
            {
                var match = methodRegex.Match(matchInput);
                if (match.Success)
                {
                    var name = match.Groups["name"].Success
                        ? match.Groups["name"].Value.Trim()
                        : match.Value.Trim();

                    methodStartColumn += match.Index;

                    var startLine = i + 1;
                    if (!symbols.Any(s => s.Line == startLine && s.Kind == "function" && s.Name == name))
                    {
                        var (endLine, bodyStartLine, bodyEndLine) = ResolveRange(lines, i, BodyStyle.Brace, lang, methodStartColumn);
                        symbols.Add(new SymbolRecord
                        {
                            FileId = fileId,
                            Kind = "function",
                            Name = name,
                            Line = startLine,
                            StartLine = startLine,
                            EndLine = Math.Max(startLine, endLine),
                            BodyStartLine = bodyStartLine,
                            BodyEndLine = bodyEndLine,
                            Signature = signatureInput[match.Index..].Trim(),
                            Visibility = TryGetGroup(match, "visibility"),
                            ReturnType = NormalizeMetadata(TryGetGroup(match, "returnType")),
                        });
                    }
                }
            }

            nestedBraceDepth += CountBraces(braceCountInput);
            if (nestedBraceDepth < 0)
                nestedBraceDepth = 0;
        }
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
                sanitized[i] = ' ';

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
                sanitized[i] = ' ';

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
                sanitized[i] = ' ';

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
                sanitized[i] = ' ';
                state = state with { Mode = JavaScriptLexMode.SingleQuote, EscapeNext = false };
                i++;
                continue;
            }

            if (ch == '"')
            {
                sanitized[i] = ' ';
                state = state with { Mode = JavaScriptLexMode.DoubleQuote, EscapeNext = false };
                i++;
                continue;
            }

            if (ch == '`')
            {
                sanitized[i] = ' ';
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

    private static (int EndLine, int? BodyStartLine, int? BodyEndLine) ResolveRange(string[] lines, int startIndex, BodyStyle bodyStyle, string? lang = null, int startColumn = 0)
    {
        return bodyStyle switch
        {
            BodyStyle.Brace when lang is "javascript" or "typescript" => FindJavaScriptBraceRange(lines, startIndex, lang, startColumn),
            BodyStyle.Brace => FindBraceRange(lines, startIndex, startColumn),
            BodyStyle.Indent => FindIndentRange(lines, startIndex),
            BodyStyle.RubyEnd => FindRubyRange(lines, startIndex),
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
        int? bodyStartLine = null;
        var lexState = new JavaScriptLexState();

        for (int i = startIndex; i < lines.Length; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;

            var scanLine = i == startIndex && startColumn > 0 && startColumn < lexedLine.SanitizedLine.Length
                ? lexedLine.SanitizedLine[startColumn..]
                : i == startIndex && startColumn >= lexedLine.SanitizedLine.Length
                    ? string.Empty
                    : lexedLine.SanitizedLine;

            foreach (var ch in scanLine)
            {
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

            if (!opened && scanLine.TrimEnd().EndsWith(';'))
                return (startIndex + 1, null, null);
        }

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

    private static int FindJavaScriptBodyOpenBraceIndex(string[] lines, int startIndex, int bodyStartIndex, string? lang)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var angleDepth = 0;
        var lexState = new JavaScriptLexState();

        for (int i = startIndex; i <= bodyStartIndex; i++)
        {
            var lexedLine = LexJavaScriptLine(lines[i], lexState);
            lexState = lexedLine.EndState;
            var sanitizedLine = lexedLine.SanitizedLine;

            for (int column = 0; column < sanitizedLine.Length; column++)
            {
                var ch = sanitizedLine[column];
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

    private static void AssignContainers(List<SymbolRecord> symbols)
    {
        var ordered = symbols
            .OrderBy(s => s.StartLine)
            .ThenByDescending(s => s.EndLine)
            .ToList();

        var stack = new Stack<SymbolRecord>();
        foreach (var symbol in ordered)
        {
            while (stack.Count > 0 && symbol.StartLine > stack.Peek().EndLine)
                stack.Pop();

            while (stack.Count > 0 && !ContainsSymbol(stack.Peek(), symbol))
                stack.Pop();

            if (stack.Count > 0)
            {
                var container = stack.Peek();
                symbol.ContainerKind = container.Kind;
                symbol.ContainerName = container.Name;
            }

            if (CanContainSymbols(symbol))
                stack.Push(symbol);
        }
    }

    private static bool CanContainSymbols(SymbolRecord symbol)
    {
        if (!ContainerKinds.Contains(symbol.Kind))
            return false;

        return symbol.BodyStartLine != null && symbol.BodyEndLine != null;
    }

    private static bool ContainsSymbol(SymbolRecord container, SymbolRecord candidate)
    {
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
