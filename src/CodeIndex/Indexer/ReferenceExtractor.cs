using System.Text;
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
        // SQL keywords. Case-insensitive because SQL is written both upper- and lowercase in real code,
        // and the `EXEC|EXECUTE|CALL` extractor preserves the original casing of the captured name.
        // The entries themselves stay uppercase for readability.
        // SQL のキーワード。実コードでは大文字・小文字が混在するうえ、`EXEC|EXECUTE|CALL` 抽出が
        // 元のケースをそのまま保持するため、比較は大文字小文字非依存にする（リストは読みやすさのため大文字表記）。
        ["sql"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "JOIN", "INTO",
            "VALUES", "ORDER", "GROUP", "HAVING", "LIMIT", "OFFSET", "UNION",
            "EXISTS", "BETWEEN", "LIKE", "CASE", "WHEN", "THEN", "ELSE",
            "AS", "ON", "AND", "OR", "NOT", "NULL", "IN", "IS",
            "CREATE", "ALTER", "DROP", "TABLE", "INDEX", "VIEW", "IF",
            // `EXECUTE IMMEDIATE 'dynamic SQL'` (Oracle / PL/pgSQL) — `IMMEDIATE` is not a call target.
            // `EXECUTE IMMEDIATE '動的SQL'` (Oracle / PL/pgSQL) — `IMMEDIATE` は呼び出し対象ではない。
            "IMMEDIATE",
            // The keywords that introduce a stored-procedure call themselves. The no-parens form is
            // captured by SqlProcCallRegex; the rare `EXEC(@sql)` / `EXEC('...')` dynamic-SQL form has
            // no identifier argument, so the generic CallRegex would otherwise emit a phantom
            // `call EXEC` / `call EXECUTE` / `call CALL` edge pointing at the keyword itself.
            // ストアドプロシージャ呼び出しを導入するキーワード自身。括弧なし形は SqlProcCallRegex で捕捉し、
            // 動的 SQL 形の `EXEC(@sql)` / `EXEC('...')` は識別子を持たないため、汎用 CallRegex に任せると
            // キーワード自体を指す `call EXEC` / `call EXECUTE` / `call CALL` の幽霊エッジが生まれる。
            "EXEC", "EXECUTE", "CALL",
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
    // SQL-specific string-literal stripper: preserve backticks because MySQL / MariaDB use them
    // for identifier quoting (`` CALL `proc-name`; ``) rather than string literals. Strip only
    // ANSI single-quoted and double-quoted literals plus comments. ANSI / SQL-PSM delimited
    // identifiers (`"..."`) are intentionally treated as string literals here because the dominant
    // real-world SQL codebase uses `"..."` for string content when double-quoted identifiers are
    // not in effect, and #232 focuses on T-SQL brackets + MySQL backticks — not ANSI delimited
    // identifiers.
    // SQL 専用の文字列リテラル除去: MySQL / MariaDB はバッククォートを識別子引用に使うため保持し、
    // `'...'` / `"..."` のみを除去する。ANSI / SQL-PSM の `"..."` delimited identifier は
    // 実運用で文字列として使われる頻度が高く、今回の #232 の対象から外れるため意図的に文字列扱いとする。
    private static readonly Regex SqlStringLiteralRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'",
        RegexOptions.Compiled);
    private static readonly Regex InlineBlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled);
    // The `(?:\?\.)?` segment captures JavaScript / TypeScript optional chaining calls such as
    // `callback?.()` and `callback?.<T>()`. Without it the `?.` stops the regex from reaching the
    // trailing `(`, and the call reference to `callback` is silently dropped. Other supported
    // languages that use `?.` (C# / Kotlin / Swift / Dart) place an identifier between `?.` and
    // `(`, so their existing call sites continue to match via the identifier itself. See issue #294.
    // `(?:\?\.)?` は JavaScript / TypeScript の optional chaining 呼び出し (`callback?.()` や
    // `callback?.<T>()`) を捕捉するための segment。これが無いと `?.` の存在で末尾 `(` に到達できず、
    // `callback` への call 参照が黙って欠落する。C# / Kotlin / Swift / Dart などの `?.` は後ろに
    // 識別子が続くため、従来通り識別子自身が CallRegex にマッチして影響を受けない。issue #294 参照。
    // Nested generic call sites such as `Foo<Bar<int>>()` / `new Dict<K, List<V>>()` are
    // recovered by a depth-aware fallback scanner because the flat `<[^>\n]+>` segment cannot
    // balance the closing `>>`. See issue #263.
    // `Foo<Bar<int>>()` や `new Dict<K, List<V>>()` のようなネスト generic 呼び出しは、
    // 平坦な `<[^>\n]+>` では末尾 `>>` を釣り合わせられないため、depth-aware な fallback scanner
    // で補完する。issue #263 参照。
    private static readonly Regex CallRegex = new(@"(?<![\w$])(?<name>[A-Za-z_]\w*)(?:\?\.)?(?:<[^>\n]+>)?\s*\(", RegexOptions.Compiled);
    // SQL stored-procedure call without parentheses: T-SQL `EXEC` / `EXECUTE` and MySQL / MariaDB `CALL`.
    // The shared CallRegex requires a trailing `(`, which misses the dominant real-world form such as
    // `EXEC dbo.sp_Target;`, `EXEC dbo.sp_Target @x = 1, @y = 2;`, `CALL sp_Helper;`, and the bracketed
    // form `EXEC [dbo].[sp_Target]`. The regex captures only the final identifier (schema prefixes are
    // consumed as a prefix) and tolerates the optional T-SQL return-value assignment
    // `EXEC @retval = dbo.sp_Target ...`. Bracket handling is done at emission time so `[sp_Target]`
    // is normalized back to `sp_Target`. See issue #232.
    // SQL のストアドプロシージャを `(` なしで呼び出す T-SQL `EXEC` / `EXECUTE` と MySQL / MariaDB `CALL`。
    // 共通 CallRegex は末尾 `(` を要求するため、`EXEC dbo.sp_Target;` など実運用で圧倒的に多い形を取りこぼす。
    // 先頭側の schema prefix は吸収し、末端の識別子だけを `name` として捕捉する。T-SQL 固有の
    // `EXEC @retval = dbo.sp_Target ...` 形にも対応し、`[sp_Target]` のような角括弧識別子は発行時に除去する。
    // Bracketed identifiers inside the qualifier and name groups accept any character except `[`,
    // `]`, or a line terminator. T-SQL allows `#` (temp procedure), `-` (hyphenated names),
    // spaces, Unicode symbols, and punctuation inside bracket quoting, and the narrower `[\w ]+`
    // would silently drop `EXEC [#tempProc]`, `EXEC [dbo].[proc-name]`, and similar legitimate
    // forms while falsely misattributing the qualifier `[dbo]` as the proc name.
    // Qualifier segments are optional (the inner `?`) so SQL Server's linked-server form with
    // an omitted database or schema part — `EXEC AdventureWorks..sp_GetCustomer;` /
    // `EXEC [AdventureWorks]..[proc-name];` — terminates on the real procedure name instead of
    // falling back to the first segment. Identifier alternatives also accept backtick-quoted
    // names (`` `proc-name` ``) to cover MySQL / MariaDB identifier quoting.
    // 角括弧識別子は `[` / `]` / 改行以外の任意文字を許可する。T-SQL では `#`（一時プロシージャ）、
    // `-`（ハイフン名）、空白、Unicode 記号などが正当に現れるため、`[\w ]+` ではそれらの合法な
    // 形を取りこぼし、修飾子 `[dbo]` を proc 名として誤発行してしまう。
    // 修飾子の各セグメントを optional にすることで、`EXEC AdventureWorks..sp_GetCustomer;` のように
    // SQL Server が許す省略形（`..`）でも末尾の proc 名まで到達できる。識別子候補にはバッククォート引用も含め、
    // MySQL / MariaDB の `` `proc-name` `` 形にも対応する。
    private static readonly Regex SqlProcCallRegex = new(
        @"(?<![\w$])(?:EXEC|EXECUTE|CALL)\b\s+(?:@\w+\s*=\s*)?(?:(?:\[[^\[\]\r\n]+\]|`[^`\r\n]+`|\w+)?\.)*(?<name>\[[^\[\]\r\n]+\]|`[^`\r\n]+`|\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
    // C# / Java parenless object / collection / dictionary / array initializer such as
    // `new Foo { X = 1 }`, `new List<int> { 1, 2, 3 }`, `new Dictionary<K, V> { [k] = v }`,
    // `new Foo[] { ... }`, `new Foo[N] { ... }`, `new Foo[,] { ... }`, `new Foo[][] { ... }`,
    // and qualified type names like `new N.Foo { X = 1 }` / `new global::N.Foo { X = 1 }`.
    // CallRegex requires a trailing `(`, so these forms are otherwise dropped from the
    // reference table even though the type is genuinely instantiated. Anonymous types
    // (`new { Name = ... }`), target-typed `new()`, and collection expressions (`new[] { ... }`)
    // intentionally do not match because they have no named target. Nested generics deeper than
    // one `<...>` level (e.g. `new Dictionary<string, List<int>> { ... }`) follow the same
    // limitation as the existing CallRegex generics handling. See issue #286.
    // C# / Java の括弧省略インスタンス化（オブジェクト / コレクション / ディクショナリ /
    // 配列イニシャライザ）。CallRegex は `(` が必須なため取りこぼすが、実体は型のインスタンス化なので
    // `instantiate` として拾う。匿名型 `new { ... }`、target-typed `new()`、
    // collection expression `new[] { ... }` は対象を持たないため意図的にマッチさせない。
    // 1 段を超えるネストした generic（`Dictionary<string, List<int>>` 等）は既存 CallRegex と同様の制限。issue #286 参照。
    private static readonly Regex CSharpJavaInitializerRegex = new(
        @"\bnew\s+(?:[A-Za-z_]\w*(?:\s*::\s*|\s*\.\s*))*(?<name>[A-Za-z_]\w*)(?:\s*<[^>\n]+>)?(?:\s*\[[^\[\]\n]*\])*\s*\{",
        RegexOptions.Compiled);
    // Allman-style C# / Java parenless initializer where `{` sits on the next non-empty
    // line. The trailing regex captures `new <Type>` ending the current line (with optional
    // generic + array shape), and the caller peeks forward to confirm the next non-blank
    // prepared line begins with `{` before emitting an `instantiate` edge. See issue #286.
    // Allman スタイルの多行 parenless initializer。`new <Type>` が行末で終わり、次の非空 prepared line が
    // `{` から始まる場合にだけ `instantiate` を発行する。issue #286 参照。
    private static readonly Regex CSharpJavaInitializerTrailingRegex = new(
        @"\bnew\s+(?:[A-Za-z_]\w*(?:\s*::\s*|\s*\.\s*))*(?<name>[A-Za-z_]\w*)(?:\s*<[^>\n]+>)?(?:\s*\[[^\[\]\n]*\])*\s*$",
        RegexOptions.Compiled);
    private static readonly Regex CSharpUsingAliasRegex = new(
        @"^\s*(?:global\s+)?using\s+(?!static\b)(?<alias>@?[A-Za-z_]\w*)\s*=\s*(?<target>[^;]+)",
        RegexOptions.Compiled);
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
    // Same intent as CSharpRecordPrimaryCtorSignatureRegex but applied to the joined multi-line
    // header produced by CollectCSharpRecordHeader, so split-line forms like
    // `public record Child\n(\n    int Value\n)\n    : Parent(Value);` still match.
    // Also covers C# 12 `class` / `struct` primary constructors such as
    // `public class Child(int value) : Parent(value) { }` and
    // `public struct Child(int value) : IParent { }` so their `Parent(value)` chain edges are
    // also attributed to the synthetic function-kind container named after the declaring type.
    // CollectCSharpRecordHeader で連結された複数行ヘッダーに対しても当てるため、`record` / `class` /
    // `struct` と `(` が別行に分かれる書式でも primary-ctor 宣言と判定できるようにする。
    // C# 12 以降の class / struct primary constructor にも同じ合成コンテナ経路を適用する。
    private static readonly Regex CSharpPrimaryCtorHeaderRegex = new(
        @"\b(?:record\s+(?:class\s+|struct\s+)?|class\s+|struct\s+)\w+(?:\s*<[^>]+>)?\s*\(",
        RegexOptions.Compiled);
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
        @"(?<!\w)(?:[A-Za-z_]\w*\s*:\s*)?(?:[A-Za-z_]\w*\s*(?:\.|::)\s*)*(?<name>[A-Za-z_]\w*)(?:\s*<[^\n]+?>)?\s*(?=[\],]|$)",
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

    public static bool? SupportsSymbolGraph(string? lang, string? kind, string? containerKind)
    {
        if (lang == null)
            return null;

        return SupportsLanguage(lang);
    }

    public static string? GetUnsupportedSymbolKind(string? lang, string? kind, string? containerKind)
    {
        return null;
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
        return false;
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

        // Synthetic function-kind container for C# primary-ctor declarations with a base
        // primary-ctor call such as `record Child(int x) : Parent(x)` or C# 12 `class Child(int x) : Parent(x)`.
        // The range spans the entire declaration header so multi-line forms where `: Parent(x)` sits on a
        // later line are covered. Later lines inside the body keep their real innermost containers.
        // C# のプライマリコンストラクタ宣言（record / class / struct）で base primary-ctor を呼んでいる場合、
        // 宣言ヘッダー全体を合成コンテナで上書きする。`{` / `;` 以降の本体行は通常の container に戻す。
        var recordPrimaryCtorRanges = BuildCSharpPrimaryCtorContainers(language, symbols, structuralLines);
        var csharpQualifiedEnumMemberLookup = BuildCSharpQualifiedEnumMemberLookup(language, symbols);
        var csharpUsingAliasMap = BuildCSharpUsingAliasMap(language, structuralLines);

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

            // Per-line Java same-line ctor synthesis. When `public Leaf(){super(0); doWork();}`
            // is entirely on one line, SymbolExtractor does not emit a function symbol for the
            // ctor (its method regex requires the line to end with `{`), so `FindInnermostContainer`
            // returns the enclosing `class:Leaf`. Body-level calls such as `doWork()` would then
            // attach to the class rather than the ctor. We pre-compute a synthetic function-kind
            // container covering the body `{ ... }` region on the current line, so those calls
            // land on `function:Leaf` and `callers Leaf` reflects what the ctor actually does.
            // 同一行 ctor は function symbol が作られないため、body `{ ... }` 内の通常 call が
            // 外側クラスに吸われてしまう。合成 function コンテナを per-line で構築して差し替える。
            (SymbolRecord Synthetic, int NameIndex, int OpenBraceIndex, int CloseBraceIndex)? javaSameLineCtor = null;
            if (language == "java")
            {
                javaSameLineCtor = TryBuildJavaSameLineCtorSpan(preparedLine, lineNumber, enclosingTypeCandidates);
            }

            // Per-call-site record primary-ctor override: only calls whose column sits inside the
            // record header (not in a braced body on the same line) should land on the synthetic
            // ctor. Overriding `container` for the whole line would steal body-level calls such as
            // `record Child(int V) : Parent(V) { public int Sum() => Add(V, 1); }` where `Add(...)`
            // lives past the header-terminating `{` and must stay with its real innermost container.
            // 同一行 record で `{` より後ろの本体呼び出しまで合成 ctor に奪われないよう、コール単位で
            // ヘッダ範囲（end line の end column より前）に入っているかを判定して差し替える。
            SymbolRecord? ResolveContainerForCall(int column)
            {
                foreach (var (rangeStart, rangeStartColumn, rangeEnd, rangeEndColumn, syntheticRecordCtor) in recordPrimaryCtorRanges)
                {
                    if (lineNumber < rangeStart || lineNumber > rangeEnd)
                        continue;
                    if (lineNumber == rangeStart && column < rangeStartColumn)
                        continue;
                    if (lineNumber == rangeEnd && column >= rangeEndColumn)
                        continue;
                    return syntheticRecordCtor;
                }

                // Java same-line ctor body override: calls whose column sits strictly inside the
                // `{ ... }` block on the ctor declaration line attach to the synthetic function-kind
                // ctor instead of the enclosing class container. When no matching `}` is found on
                // the same line (CloseBraceIndex < 0), the body extends beyond the current line —
                // in that case SymbolExtractor emits a real ctor function symbol (its regex matches
                // because the line ends with `{`), so this override is only needed for the fully
                // same-line shape where the matching `}` exists on the same line.
                // Java の same-line ctor では `{ ... }` 内の call を合成 function コンテナに振り向ける。
                if (javaSameLineCtor != null)
                {
                    var info = javaSameLineCtor.Value;
                    if (info.CloseBraceIndex >= 0
                        && column > info.OpenBraceIndex
                        && column < info.CloseBraceIndex)
                    {
                        return info.Synthetic;
                    }
                }

                return container;
            }

            // Event subscription/unsubscription (C#) / イベント購読・解除 (C#)
            if (language is "csharp")
            {
                foreach (Match match in EventSubscriptionRegex.Matches(preparedLine))
                {
                    var eventContainer = ResolveContainerForCall(match.Groups["name"].Index);
                    AddReference(references, seen, fileId, match, "subscribe", context, lineNumber, eventContainer);
                }
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

            // SQL stored-procedure calls without parentheses: `EXEC`, `EXECUTE`, and `CALL` forms that
            // the shared CallRegex cannot see because there is no trailing `(`. Emits the same
            // reference kind ("call") so the edge merges with the rare parenthesized form via dedupe.
            // See issue #232.
            // 末尾 `(` が無いため汎用 CallRegex で取れない SQL の `EXEC` / `EXECUTE` / `CALL` 形。
            // 参照 kind は "call" で揃え、稀な `(...)` 付き形式との重複は dedupe で吸収する。issue #232 参照。
            if (language is "sql")
            {
                // The shared PrepareLine strips backtick-quoted content because several other
                // languages use backticks for string literals (shell command substitution,
                // JavaScript template literals, Go raw strings). In SQL, backticks quote
                // identifiers (MySQL / MariaDB), so we re-prepare the raw line with
                // a SQL-aware stripper that only drops `'...'` / `"..."` literals and comments.
                // 共有 PrepareLine はバッククォート内容を文字列として除去する（他言語のテンプレート
                // リテラル等に対応するため）。SQL ではバッククォートは識別子引用なので、SQL 向けに
                // `'...'` / `"..."` とコメントだけを除去する sanitization を別途適用する。
                var sqlScanLine = PrepareSqlLineForIdentifierScan(structuralLines[i]);
                if (!string.IsNullOrWhiteSpace(sqlScanLine))
                {
                    foreach (Match match in SqlProcCallRegex.Matches(sqlScanLine))
                    {
                        var nameGroup = match.Groups["name"];
                        var rawName = nameGroup.Value;
                        int nameIndex = nameGroup.Index;
                        string resolvedName;
                        bool wasQuoted;
                        if (rawName.Length >= 2
                            && ((rawName[0] == '[' && rawName[^1] == ']')
                                || (rawName[0] == '`' && rawName[^1] == '`')))
                        {
                            // Normalize `[sp_Target]` / `` `sp_Target` `` to `sp_Target` so it matches the
                            // indexed symbol name, and point the column at the inner identifier instead
                            // of the opening quote.
                            // `[sp_Target]` / `` `sp_Target` `` は識別子名と一致させるため引用を除去し、
                            // 列位置は中身の先頭に寄せる。
                            resolvedName = rawName.Substring(1, rawName.Length - 2);
                            nameIndex += 1;
                            wasQuoted = true;
                        }
                        else
                        {
                            resolvedName = rawName;
                            wasQuoted = false;
                        }

                        // Bracketed / backtick-quoted identifiers are explicitly quoted to allow reserved
                        // words (`[ORDER]`, `[USER]`, `[AS]`, `[IMMEDIATE]`, `` `order` ``) as real object
                        // names. Skip the keyword ignore list so a legitimate `EXEC [ORDER]` or
                        // `` CALL `order` `` is not silently dropped.
                        // 角括弧 / バッククォート付き識別子は予約語を識別子として使うための引用形。
                        // `[ORDER]` / `` `order` `` のような正当な名前を落とさないため keyword ignore list をスキップする。
                        if (!wasQuoted && IsIgnoredCallName(language, resolvedName))
                            continue;
                        if (definitionNames != null && definitionNames.Contains(resolvedName))
                            continue;

                        var sqlCallContainer = ResolveContainerForCall(nameGroup.Index);
                        AddChainReference(
                            references, seen, fileId, resolvedName, nameIndex + 1,
                            "call", context, lineNumber, sqlCallContainer);
                    }
                }
            }

            // C# / Java parenless initializers: `new T { ... }` / `new T<U> { ... }` /
            // `new T[] { ... }` etc. CallRegex requires a trailing `(`, so these forms slip
            // through and the type is otherwise never recorded as instantiated. Emit an
            // `instantiate` row here so `references` / `callers` / `impact` see the edge.
            // See issue #286.
            // 括弧省略の C# / Java インスタンス化 (`new T { ... }` 等) は CallRegex で拾えないため、
            // 専用パスで `instantiate` を発行する。issue #286 参照。
            if (language is "csharp" or "java")
            {
                var matchedInitializerIndices = new HashSet<int>();
                foreach (Match match in CSharpJavaInitializerRegex.Matches(preparedLine))
                {
                    var name = match.Groups["name"].Value;
                    var nameIndex = match.Groups["name"].Index;
                    matchedInitializerIndices.Add(nameIndex);
                    if (ShouldSkipInitializerName(language, name))
                        continue;
                    // Do NOT skip when the type is defined in the same file — the CallRegex
                    // `IsConstructorCallName` path emits `instantiate` without a definitionNames
                    // filter, so `new Foo { ... }` and `new Foo()` should behave the same way.
                    // 同一ファイル内定義でもスキップしない。`IsConstructorCallName` 経路の
                    // `instantiate` が同様の扱いをしているため、括弧あり/なしで挙動を揃える。
                    var initContainer = ResolveContainerForCall(nameIndex);
                    AddReference(references, seen, fileId, match, "instantiate", context, lineNumber, initContainer);
                }

                // The initializer regex has the same one-level generic ceiling as CallRegex,
                // so nested generic targets like `new Dictionary<string, List<int>> { ... }`
                // need a depth-aware fallback to keep the outer `instantiate` edge.
                // initializer regex も CallRegex と同じく generic を 1 段までしか見ないため、
                // `new Dictionary<string, List<int>> { ... }` の外側型は depth-aware fallback
                // で補って `instantiate` を落とさないようにする。
                if (language == "csharp")
                {
                    foreach (var candidate in EnumerateNestedGenericInitializerCandidates(
                                 preparedLine,
                                 matchedInitializerIndices,
                                 requireOpeningBrace: true))
                    {
                        if (ShouldSkipInitializerName(language, candidate.Name))
                            continue;

                        var initContainer = ResolveContainerForCall(candidate.NameIndex);
                        AddReference(
                            references,
                            seen,
                            fileId,
                            candidate.Name,
                            candidate.NameIndex,
                            "instantiate",
                            context,
                            lineNumber,
                            initContainer);
                    }
                }

                // Allman-style multi-line form: `new T` at end of current line with the
                // opening `{` on the next non-blank prepared line. Peek forward to confirm
                // before emitting, so trailing `new T` patterns that are not followed by `{`
                // (e.g. `var a = new Foo\n;` or `var a = new Foo\n(1, 2);`) do not produce
                // phantom `instantiate` rows.
                // Allman スタイルの多行形式: 現在行末の `new T` と次の非空 prepared line 冒頭の
                // `{` を合わせて 1 つの instantiate として扱う。`{` が続かない場合（`;` や `(` が
                // 後続する等）には幻行を出さないため、peek で確認してから発行する。
                var trailingMatch = CSharpJavaInitializerTrailingRegex.Match(preparedLine);
                var peek = i + 1;
                while (peek < preparedLines.Length && string.IsNullOrWhiteSpace(preparedLines[peek]))
                    peek++;
                if (peek < preparedLines.Length)
                {
                    var nextContent = preparedLines[peek].TrimStart();
                    if (nextContent.Length > 0 && nextContent[0] == '{')
                    {
                        if (trailingMatch.Success)
                        {
                            var name = trailingMatch.Groups["name"].Value;
                            var nameIndex = trailingMatch.Groups["name"].Index;
                            matchedInitializerIndices.Add(nameIndex);
                            if (!ShouldSkipInitializerName(language, name))
                            {
                                var initContainer = ResolveContainerForCall(nameIndex);
                                AddReference(references, seen, fileId, trailingMatch, "instantiate", context, lineNumber, initContainer);
                            }

                        }

                        if (language == "csharp")
                        {
                            foreach (var candidate in EnumerateNestedGenericInitializerCandidates(
                                         preparedLine,
                                         matchedInitializerIndices,
                                         requireOpeningBrace: false))
                            {
                                if (ShouldSkipInitializerName(language, candidate.Name))
                                    continue;

                                var initContainer = ResolveContainerForCall(candidate.NameIndex);
                                AddReference(
                                    references,
                                    seen,
                                    fileId,
                                    candidate.Name,
                                    candidate.NameIndex,
                                    "instantiate",
                                    context,
                                    lineNumber,
                                    initContainer);
                            }
                        }
                    }
                }
            }

            void AddCallLikeReference(string name, int callIndex)
            {
                // Suppress the same-line Java ctor declarator's self-call. CallRegex matches
                // `CtorName(` at the declarator once per same-line ctor, but it is a declaration
                // site — not a call — so attributing it to `class:CtorName` produces a phantom
                // `CtorName|call|class|CtorName` edge. `definitionNames` does not cover this
                // because same-line ctors do not appear in the symbol table.
                // 同一行 ctor の宣言子 `CtorName(` は呼び出しではないため CallRegex の対象から除外する。
                if (javaSameLineCtor != null
                    && callIndex == javaSameLineCtor.Value.NameIndex
                    && string.Equals(name, javaSameLineCtor.Value.Synthetic.Name, StringComparison.Ordinal))
                {
                    return;
                }

                var callContainer = ResolveContainerForCall(callIndex);
                if (IsConstructorCallName(language, preparedLine, callIndex))
                {
                    AddReference(references, seen, fileId, name, callIndex, "instantiate", context, lineNumber, callContainer);
                    return;
                }
                if (IsIgnoredCallName(language, name))
                    return;
                if (definitionNames != null && definitionNames.Contains(name))
                    return;

                // issue #293: reclassify C# attribute / Java/Kotlin/Scala/TypeScript annotation
                // usages with arguments so they do not pollute the call-graph as phantom `call` rows.
                // issue #293: 引数付きの C# attribute と Java/Kotlin/Scala/TypeScript annotation 使用を
                // `call` ではなく専用の種別に分類し、call-graph の phantom エッジを防ぐ。
                var insideCSharpAttributeRange = csharpAttrRangesOnLine != null
                    && IsInsideCSharpAttributeRange(csharpAttrRangesOnLine, callIndex);
                var metadataKind = TryClassifyMetadataReference(language, preparedLine, callIndex, insideCSharpAttributeRange);
                AddReference(references, seen, fileId, name, callIndex, metadataKind ?? "call", context, lineNumber, callContainer);
            }

            var matchedCallIndices = new HashSet<int>();
            foreach (Match match in CallRegex.Matches(preparedLine))
            {
                var name = match.Groups["name"].Value;
                var callIndex = match.Groups["name"].Index;
                matchedCallIndices.Add(callIndex);
                AddCallLikeReference(name, callIndex);
            }

            // The flat CallRegex misses nested generic tails like `>>(` because `<[^>\n]+>`
            // stops at the first `>`. Add a depth-aware fallback so `Foo<Bar<int>>()` and
            // `new Dict<K, List<V>>()` still emit call/instantiate rows. See issue #263.
            // 平坦な CallRegex は `<[^>\n]+>` が最初の `>` で止まるため `>>(` 形を取りこぼす。
            // depth-aware な fallback を足し、`Foo<Bar<int>>()` や `new Dict<K, List<V>>()` でも
            // `call` / `instantiate` を発行する。issue #263 参照。
            foreach (var candidate in EnumerateNestedGenericCallCandidates(preparedLine, matchedCallIndices))
                AddCallLikeReference(candidate.Name, candidate.NameIndex);

            // Qualified C# enum-member access such as `Nested.A` or `Outer.First.None` is not
            // a method call, but downstream symbol workflows (`references`, `callers`,
            // `callees`, `inspect`, `impact`) still need an edge anchored to the narrowest
            // real owner symbol. Ordinary code paths stay `call` so existing graph readers
            // keep working, while C# attribute metadata sites are downgraded to `attribute`
            // to stay out of runtime call-graph traversals (issue #293 / #492).
            // `Nested.A` や `Outer.First.None` のような C# enum member の修飾アクセスは
            // メソッド呼び出しではないが、下流の symbol workflow では実 owner に紐づく edge が必要。
            // 通常コードでは既存 reader / SQL 契約を守るため kind は `call` を維持し、C# 属性メタデータ内だけ
            // `attribute` に落として runtime call-graph への混入を防ぐ (issue #293 / #492)。
            if (language == "csharp" && csharpQualifiedEnumMemberLookup.Count > 0)
            {
                EmitCSharpQualifiedEnumMemberReferences(
                    preparedLine,
                    csharpQualifiedEnumMemberLookup,
                    csharpAttrRangesOnLine,
                    csharpUsingAliasMap,
                    references,
                    seen,
                    fileId,
                    context,
                    lineNumber,
                    ResolveContainerForCall);
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
        AddReference(
            references,
            seen,
            fileId,
            match.Groups["name"].Value,
            match.Groups["name"].Index,
            referenceKind,
            context,
            lineNumber,
            container);
    }

    private static void AddReference(
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string name,
        int nameIndex,
        string referenceKind,
        string context,
        int lineNumber,
        SymbolRecord? container)
    {
        var column = nameIndex + 1;
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
    /// per identifier segment while handling generic `&lt;...&gt;`, array `[...]`,
    /// parenthesized/tuple groups `(...)`, and `global::` / `Alias::` qualifier skipping so nested
    /// paths like `nameof(List&lt;int&gt;.Count)`, `nameof(global::System.String)`,
    /// and `typeof((Foo, Bar))` are indexed correctly.
    /// C# の nameof/typeof/sizeof/default の引数を `(` 直後から lexer で走査し、
    /// generic `&lt;...&gt;`・配列 `[...]`・タプル `(...)` 群・`global::` / `Alias::` 修飾子を
    /// 跨ぎながら識別子セグメントごとに type_reference を発行する。
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
        int parenDepth = 0;
        bool expectSegment = true;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == ')')
            {
                if (parenDepth == 0)
                    return;
                parenDepth--;
                i++;
                expectSegment = false;
                continue;
            }

            if (c == ',')
            {
                if (parenDepth == 0)
                    return;
                // Tuple element separator inside `typeof((Foo, Bar))` — keep scanning.
                // `typeof((Foo, Bar))` のタプル要素区切りは続けて走査する。
                i++;
                expectSegment = true;
                continue;
            }

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
                // Track paren depth instead of skipping the body so tuple/parenthesized
                // type groups like `typeof((Foo, Bar))` still yield inner segments.
                // タプル型 `typeof((Foo, Bar))` の中身も拾えるよう、括弧はスキップせず
                // 深さだけ追跡する。
                parenDepth++;
                i++;
                expectSegment = true;
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

    private static Dictionary<string, string> BuildCSharpUsingAliasMap(string language, IReadOnlyList<string> structuralLines)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language != "csharp")
            return aliases;

        foreach (var line in structuralLines)
        {
            var match = CSharpUsingAliasRegex.Match(line);
            if (!match.Success)
                continue;

            var alias = NormalizeCSharpIdentifier(match.Groups["alias"].Value);
            var target = TryNormalizeCSharpQualifiedName(match.Groups["target"].Value);
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(target))
                continue;

            aliases[alias] = target;
        }

        return aliases;
    }

    private static Dictionary<string, List<(string EnumName, string? QualifiedEnumName, bool AllowShortNameFallback)>> BuildCSharpQualifiedEnumMemberLookup(
        string language,
        IReadOnlyList<SymbolRecord> symbols)
    {
        var lookup = new Dictionary<string, List<(string EnumName, string? QualifiedEnumName, bool AllowShortNameFallback)>>(StringComparer.Ordinal);
        if (language != "csharp")
            return lookup;

        var conflictingNonEnumTypeNames = new HashSet<string>(
            symbols
                .Where(symbol => symbol.Kind is "class" or "struct" or "interface" or "delegate")
                .Select(symbol => symbol.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))!,
            StringComparer.Ordinal);

        foreach (var symbol in symbols)
        {
            if (symbol.Kind != "enum" || symbol.ContainerKind != "enum")
                continue;
            if (string.IsNullOrWhiteSpace(symbol.Name) || string.IsNullOrWhiteSpace(symbol.ContainerName))
                continue;

            if (!lookup.TryGetValue(symbol.Name, out var targets))
            {
                targets = [];
                lookup[symbol.Name] = targets;
            }

            bool exists = false;
            foreach (var target in targets)
            {
                if (string.Equals(target.EnumName, symbol.ContainerName, StringComparison.Ordinal)
                    && string.Equals(target.QualifiedEnumName, symbol.ContainerQualifiedName, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                targets.Add((
                    symbol.ContainerName!,
                    symbol.ContainerQualifiedName,
                    AllowShortNameFallback: !conflictingNonEnumTypeNames.Contains(symbol.ContainerName!)));
        }

        return lookup;
    }

    private static void EmitCSharpQualifiedEnumMemberReferences(
        string preparedLine,
        IReadOnlyDictionary<string, List<(string EnumName, string? QualifiedEnumName, bool AllowShortNameFallback)>> enumMemberLookup,
        IReadOnlyList<(int start, int end)>? csharpAttrRangesOnLine,
        IReadOnlyDictionary<string, string> usingAliases,
        List<ReferenceRecord> references,
        HashSet<string> seen,
        long fileId,
        string context,
        int lineNumber,
        Func<int, SymbolRecord?> resolveContainerForCall)
    {
        var scan = 0;
        while (scan < preparedLine.Length)
        {
            if (!TryReadCSharpQualifiedAccess(preparedLine, scan, out var parsed))
            {
                scan++;
                continue;
            }

            scan = Math.Max(scan + 1, parsed.NextIndex);
            if (!parsed.LastSeparatorWasDot || parsed.Segments.Count < 2)
                continue;

            var member = parsed.Segments[^1];
            var memberName = preparedLine.Substring(member.Start, member.End - member.Start);
            if (!enumMemberLookup.TryGetValue(memberName, out var targets))
                continue;

            var qualifier = NormalizeCSharpQualifiedSegments(preparedLine, parsed.Segments, parsed.Segments.Count - 1);
            var resolvedQualifier = ResolveCSharpQualifiedAliasTarget(qualifier, usingAliases);
            if (!MatchesQualifiedEnumType(resolvedQualifier, targets))
                continue;

            var nextTokenIndex = SkipWhitespace(preparedLine, member.End);
            if (nextTokenIndex < preparedLine.Length && preparedLine[nextTokenIndex] == '(')
                continue;

            var insideCSharpAttributeRange = csharpAttrRangesOnLine != null
                && IsInsideCSharpAttributeRange(csharpAttrRangesOnLine, member.Start);
            var referenceKind = TryClassifyMetadataReference("csharp", preparedLine, member.Start, insideCSharpAttributeRange) ?? "call";

            AddReference(
                references,
                seen,
                fileId,
                memberName,
                member.Start,
                referenceKind,
                context,
                lineNumber,
                resolveContainerForCall(member.Start));
        }
    }

    private static bool TryReadCSharpQualifiedAccess(
        string preparedLine,
        int start,
        out (List<(int Start, int End)> Segments, int NextIndex, bool LastSeparatorWasDot) parsed)
    {
        parsed = (new List<(int Start, int End)>(), start, false);

        if (start > 0 && IsCSharpIdentifierPart(preparedLine[start - 1]))
            return false;
        if (start >= preparedLine.Length || !IsCSharpIdentifierStart(preparedLine[start]))
            return false;

        var segments = new List<(int Start, int End)>();
        var cursor = start;
        var lastSeparatorWasDot = false;
        while (true)
        {
            if (!TryConsumeCSharpIdentifier(preparedLine, ref cursor, out var segmentStart, out var segmentEnd))
                return false;

            segments.Add((segmentStart, segmentEnd));

            var separatorStart = SkipWhitespace(preparedLine, cursor);
            if (separatorStart + 1 < preparedLine.Length
                && preparedLine[separatorStart] == ':'
                && preparedLine[separatorStart + 1] == ':')
            {
                cursor = SkipWhitespace(preparedLine, separatorStart + 2);
                lastSeparatorWasDot = false;
                continue;
            }

            if (separatorStart < preparedLine.Length && preparedLine[separatorStart] == '.')
            {
                cursor = SkipWhitespace(preparedLine, separatorStart + 1);
                lastSeparatorWasDot = true;
                continue;
            }

            parsed = (segments, cursor, lastSeparatorWasDot);
            return true;
        }
    }

    private static bool TryConsumeCSharpIdentifier(
        string preparedLine,
        ref int cursor,
        out int start,
        out int end)
    {
        start = cursor;
        if (cursor >= preparedLine.Length || !IsCSharpIdentifierStart(preparedLine[cursor]))
        {
            end = cursor;
            return false;
        }

        cursor++;
        while (cursor < preparedLine.Length && IsCSharpIdentifierPart(preparedLine[cursor]))
            cursor++;

        end = cursor;
        return true;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    private static string NormalizeCSharpIdentifier(string identifier) =>
        !string.IsNullOrEmpty(identifier) && identifier[0] == '@'
            ? identifier[1..]
            : identifier;

    private static string NormalizeCSharpQualifiedSegments(
        string preparedLine,
        IReadOnlyList<(int Start, int End)> segments,
        int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append('.');
            var (start, end) = segments[i];
            var segment = preparedLine.Substring(start, end - start);
            builder.Append(segment[0] == '@' ? segment[1..] : segment);
        }
        return builder.ToString();
    }

    private static string? TryNormalizeCSharpQualifiedName(string candidate)
    {
        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("global::", StringComparison.Ordinal))
            trimmed = trimmed["global::".Length..];
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;
        if (!TryReadCSharpQualifiedAccess(trimmed, 0, out var parsed))
            return null;
        if (SkipWhitespace(trimmed, parsed.NextIndex) != trimmed.Length)
            return null;
        return NormalizeCSharpQualifiedSegments(trimmed, parsed.Segments, parsed.Segments.Count);
    }

    private static string ResolveCSharpQualifiedAliasTarget(string qualifier, IReadOnlyDictionary<string, string> usingAliases)
    {
        if (string.IsNullOrWhiteSpace(qualifier) || usingAliases.Count == 0)
            return qualifier;

        var firstSegment = GetFirstQualifiedSegment(qualifier);
        if (!usingAliases.TryGetValue(firstSegment, out var aliasTarget))
            return qualifier;

        return qualifier.Length == firstSegment.Length
            ? aliasTarget
            : aliasTarget + qualifier[firstSegment.Length..];
    }

    private static string GetFirstQualifiedSegment(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return string.Empty;

        var firstDot = qualifiedName.IndexOf('.');
        return firstDot < 0 ? qualifiedName : qualifiedName[..firstDot];
    }

    private static bool MatchesQualifiedEnumType(
        string qualifier,
        IReadOnlyList<(string EnumName, string? QualifiedEnumName, bool AllowShortNameFallback)> targets)
    {
        var hasMultipleQualifierSegments = qualifier.Contains('.') || qualifier.Contains("::", StringComparison.Ordinal);
        foreach (var (enumName, qualifiedEnumName, allowShortNameFallback) in targets)
        {
            if (hasMultipleQualifierSegments
                && !string.IsNullOrWhiteSpace(qualifiedEnumName)
                && QualifiedNameHasSuffix(qualifiedEnumName!, qualifier))
            {
                return true;
            }

            if (allowShortNameFallback
                && string.Equals(GetLastQualifiedSegment(qualifier), enumName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool QualifiedNameHasSuffix(string fullName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(suffix))
            return false;
        if (string.Equals(fullName, suffix, StringComparison.Ordinal))
            return true;
        if (suffix.Length >= fullName.Length)
            return false;

        var start = fullName.Length - suffix.Length;
        return string.Compare(fullName, start, suffix, 0, suffix.Length, StringComparison.Ordinal) == 0
            && fullName[start - 1] == '.';
    }

    private static string GetLastQualifiedSegment(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return string.Empty;

        var lastDot = qualifiedName.LastIndexOf('.');
        var lastColon = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        var split = Math.Max(lastDot, lastColon);
        return split < 0 ? qualifiedName : qualifiedName[(split + (split == lastColon ? 2 : 1))..];
    }

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
                var (_, _, headerText) = CollectCSharpRecordHeader(structuralLines, enclosingType.StartLine);
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
                ?? TrySynthesizeSameLineJavaCtor(preparedLine, enclosingType, lineNumber)
                ?? FindEnclosingJavaConstructorFromStructure(structuralLines, enclosingType, lineNumber);
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
            var (_, _, headerText) = CollectCSharpRecordHeader(structuralLines, enclosingType.StartLine);
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
    /// Walk structural lines within the enclosing type body to recover a Java constructor
    /// whose header does not match SymbolExtractor's return-type-required method regex. Java
    /// constructors have no return type, so plain forms like `Leaf() {` / `Shade(int code) {`
    /// are not emitted as function symbols; this fallback parses them directly so chain calls
    /// can still be attributed to the owning ctor. Handles Allman style (`Leaf()\n{`),
    /// multi-line parameter lists, and multi-line `throws` clauses by delegating body range
    /// resolution to <see cref="SymbolExtractor.FindJavaBraceRange"/> (paren/bracket/angle
    /// depth aware, and string/comment/text-block aware).
    /// 外側型の body 内を走査して、return 型を持たない Java コンストラクタ
    /// （`Leaf() {` / `Shade(int code) {` / Allman 形式の `Leaf()\n{` / 複数行 parameter /
    /// 複数行 `throws` など）を復元する。SymbolExtractor のメソッド regex は戻り値型を必須
    /// とするため function シンボルが作られない。body 範囲の決定は FindJavaBraceRange に
    /// 委譲し、`()` / `[]` / `<>` の深さと文字列・コメント・text block を考慮する。
    /// </summary>
    private static SymbolRecord? FindEnclosingJavaConstructorFromStructure(
        string[] structuralLines,
        SymbolRecord enclosingType,
        int lineNumber)
    {
        var classBodyStart = enclosingType.BodyStartLine ?? enclosingType.StartLine;
        var classBodyEnd = enclosingType.BodyEndLine ?? enclosingType.EndLine;
        if (classBodyStart <= 0 || classBodyEnd < classBodyStart)
            return null;
        if (lineNumber < classBodyStart || lineNumber > classBodyEnd)
            return null;

        int i = classBodyStart - 1; // 0-based
        var lastIndex = Math.Min(structuralLines.Length - 1, classBodyEnd - 1);
        while (i <= lastIndex)
        {
            if (!TryMatchJavaCtorHeaderStart(structuralLines, i, lastIndex, enclosingType.Name))
            {
                i++;
                continue;
            }

            int declStart = i + 1; // 1-based

            // Let FindJavaBraceRange scan the entire header + body from the ctor start line.
            // It tracks paren / bracket / angle depth (so multi-line parameter lists,
            // annotations, and bounded generics don't mislead the body scan) and returns the
            // matching `}` as BodyEndLine. A `;`-terminated line without `{` yields
            // BodyEndLine == null; that shape is not a constructor body and is skipped.
            // FindJavaBraceRange がヘッダ + body を一気に走査する。`()`/`[]`/`<>` 深さを追跡
            // するため、複数行の parameter / annotation / bounded generic で誤検出しない。
            var (endLine, _, bodyEndLine) = SymbolExtractor.FindJavaBraceRange(structuralLines, i, 0);
            if (bodyEndLine is null)
            {
                // Not a body: advance past whatever the scanner consumed to avoid re-scanning.
                // body を持たない宣言（`;` 終端など）は ctor ではないので scanner が消費した
                // 範囲だけ進めて次行へ。
                i = Math.Max(i + 1, endLine);
                continue;
            }

            int bodyEnd = bodyEndLine.Value;
            if (lineNumber >= declStart && lineNumber <= bodyEnd)
            {
                return new SymbolRecord
                {
                    Kind = "function",
                    Name = enclosingType.Name,
                    Line = declStart,
                    StartLine = declStart,
                    EndLine = bodyEnd,
                    BodyStartLine = declStart,
                    BodyEndLine = bodyEnd,
                    ContainerKind = enclosingType.Kind,
                    ContainerName = enclosingType.Name,
                    ContainerQualifiedName = enclosingType.ContainerQualifiedName,
                    Visibility = enclosingType.Visibility,
                };
            }

            // Skip past this ctor's body to avoid re-parsing nested declarations inside it.
            // この ctor の body 以降に飛ばし、内部の宣言を誤って拾わないようにする。
            i = Math.Max(i + 1, bodyEnd);
        }

        return null;
    }

    /// <summary>
    /// Return true when line <paramref name="startIndex"/> starts a Java constructor header
    /// for <paramref name="ctorName"/>. Accepts both same-line forms (`Leaf() {`) and
    /// multi-line forms (Allman `Leaf()\n{`, multi-line parameter lists, multi-line `throws`).
    /// The detector confirms that after modifiers / annotations / optional generics, the ctor
    /// name appears immediately followed by `(` (possibly on the next non-blank line). The
    /// body resolver downstream (<see cref="SymbolExtractor.FindJavaBraceRange"/>) rejects
    /// `;`-terminated declarations without a body, so false positives like enum constants
    /// `Shade(1);` would still be discarded there even if this detector returns true.
    /// 指定行が Java コンストラクタヘッダの先頭かを判定する。modifier / annotation /
    /// optional generics を消費したあと ctor 名が続き、その直後（または次の非空行）に `(`
    /// が現れるかを検査する。body 側の判定は FindJavaBraceRange が担うため、`;` 終端の
    /// enum 定数 `Shade(1);` のような偽陽性は下流で落ちる。
    /// </summary>
    private static bool TryMatchJavaCtorHeaderStart(
        string[] lines,
        int startIndex,
        int endIndex,
        string ctorName)
    {
        if (startIndex < 0 || startIndex >= lines.Length)
            return false;

        var line = lines[startIndex];
        int i = 0;
        int n = line.Length;

        SkipWhitespace(line, ref i);

        // Consume modifiers and @annotations in any order (mirrors TryExtractJavaSameLineCtorSpan).
        // modifier と annotation は順不同で交互に現れ得るため、両方を同一ループで消費する。
        while (true)
        {
            SkipWhitespace(line, ref i);
            if (i < n && line[i] == '@')
            {
                i++;
                if (!ConsumeQualifiedIdentifier(line, ref i))
                    return false;
                SkipWhitespace(line, ref i);
                if (i < n && line[i] == '(')
                {
                    if (!SkipBalancedParens(line, ref i))
                        return false;
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
                return false;
            SkipWhitespace(line, ref i);
        }

        int nameStart = i;
        if (!ConsumeIdentifier(line, ref i))
            return false;
        var name = line.Substring(nameStart, i - nameStart);
        if (!string.Equals(name, ctorName, StringComparison.Ordinal))
            return false;

        // The next non-whitespace token (possibly on a later line) must be `(`. Anything else
        // means this is not a ctor header — e.g. `Leaf leaf = ...` (field/variable) or
        // `Leaf method() ...` (method with Leaf as return type).
        // ctor 名の直後の非空白（次行以降でも可）は `(` でなければならない。
        SkipWhitespace(line, ref i);
        if (i < n)
            return line[i] == '(';

        for (int j = startIndex + 1; j <= endIndex && j < lines.Length; j++)
        {
            var next = lines[j];
            int k = 0;
            while (k < next.Length && char.IsWhiteSpace(next[k]))
                k++;
            if (k == next.Length)
                continue;
            return next[k] == '(';
        }
        return false;
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

        return BuildSameLineJavaCtorContainer(enclosingType, lineNumber);
    }

    /// <summary>
    /// Per-line Java same-line ctor span resolved against the enclosing class. Returns the
    /// synthetic function-kind container paired with the 0-based indices of the ctor name,
    /// body-opening `{`, and body-closing `}` on the current line. Used by the main loop to
    /// (1) attribute body-level calls inside the `{ ... }` block to the synthetic ctor and
    /// (2) suppress the bogus declarator self-call on the ctor name.
    /// 同一行 Java ctor の span を外側クラスと突き合わせた結果を返す。合成 function コンテナと、
    /// ctor 名・body `{`・body `}` の 0-based 位置をセットで返し、call 帰属の振り向けと宣言子
    /// の自己 call 抑止に使う。
    /// </summary>
    private static (SymbolRecord Synthetic, int NameIndex, int OpenBraceIndex, int CloseBraceIndex)?
        TryBuildJavaSameLineCtorSpan(
            string preparedLine,
            int lineNumber,
            IReadOnlyList<SymbolRecord> enclosingTypeCandidates)
    {
        var span = TryExtractJavaSameLineCtorSpan(preparedLine);
        if (span is null)
            return null;
        var enclosingType = FindInnermostClassLike(enclosingTypeCandidates, lineNumber);
        if (enclosingType == null)
            return null;
        if (!string.Equals(span.Value.Name, enclosingType.Name, StringComparison.Ordinal))
            return null;

        var synthetic = BuildSameLineJavaCtorContainer(enclosingType, lineNumber);
        return (synthetic, span.Value.NameIndex, span.Value.OpenBraceIndex, span.Value.CloseBraceIndex);
    }

    private static SymbolRecord BuildSameLineJavaCtorContainer(SymbolRecord enclosingType, int lineNumber)
        => new SymbolRecord
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

    /// <summary>
    /// Same-line Java ctor span capturing the declarator name plus the 0-based indices of the
    /// ctor name, the opening `{` of the body, and the matching `}` on the same line (or -1
    /// when no matching close brace is found). Used to override the container for body-level
    /// calls and to suppress the bogus declarator self-call on the ctor name.
    /// same-line Java ctor の宣言情報。ctor 名位置・body `{` 位置・body `}` 位置を保持し、
    /// body 内の call に合成 function コンテナを流すのと、宣言子 `CtorName(` が誤って
    /// call として記録されるのを抑止するのに使う。
    /// </summary>
    internal readonly record struct JavaSameLineCtorSpan(
        string Name,
        int NameIndex,
        int OpenBraceIndex,
        int CloseBraceIndex);

    /// <summary>
    /// Depth-aware scanner for `@Annot ... <T extends Comparable<Integer>> Ctor(...) { ... }`
    /// style declarations. Returns the constructor name when the line opens a ctor body, or
    /// null otherwise. Handles qualified annotations (`@demo.Ann`), annotation argument lists
    /// with nested parens, and nested generic bounds that a flat regex cannot balance.
    /// 修飾付きアノテーション・引数付きアノテーション・入れ子の generic 境界を含む
    /// same-line ctor 宣言を depth-aware にスキャンして ctor 名を返すヘルパー。
    /// </summary>
    internal static string? TryExtractJavaCtorNameFromLine(string line)
        => TryExtractJavaSameLineCtorSpan(line)?.Name;

    /// <summary>
    /// Same as <see cref="TryExtractJavaCtorNameFromLine"/> but also returns the ctor name
    /// index, body-open `{` index, and the matching body-close `}` index on the same line.
    /// `TryExtractJavaCtorNameFromLine` と同じスキャナだが、ctor 名位置・`{` 位置・対応する
    /// `}` 位置もまとめて返すバリアント。
    /// </summary>
    internal static JavaSameLineCtorSpan? TryExtractJavaSameLineCtorSpan(string line)
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
        if (i >= n || line[i] != '{')
            return null;

        int openBrace = i;
        int closeBrace = FindMatchingBraceClose(line, openBrace);
        return new JavaSameLineCtorSpan(name, nameStart, openBrace, closeBrace);
    }

    /// <summary>
    /// Find the `}` that closes the block opened at <paramref name="openBrace"/>, respecting
    /// string / char literals and nested `{ ... }` pairs. Returns -1 when no matching close is
    /// on the same line. Used to bound per-line Java same-line ctor body ranges.
    /// 同一行内で `{` と対応する `}` を探す。文字列・文字リテラルと入れ子の `{}` を尊重する。
    /// </summary>
    private static int FindMatchingBraceClose(string line, int openBrace)
    {
        int depth = 0;
        int n = line.Length;
        for (int i = openBrace; i < n; i++)
        {
            var c = line[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < n)
                {
                    var ch = line[i];
                    if (ch == '\\' && i + 1 < n) { i += 2; continue; }
                    if (ch == quote) break;
                    i++;
                }
                if (i >= n) return -1;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
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
            var c = text[i];
            // Skip string / char literals so an embedded `)` inside `@Ann(text=")")` does not
            // prematurely close the annotation argument list.
            // 文字列・文字リテラル内の `)` で annotation 引数を早期終了しないようスキップする。
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < text.Length)
                {
                    var ch = text[i];
                    if (ch == '\\' && i + 1 < text.Length) { i += 2; continue; }
                    if (ch == quote) break;
                    i++;
                }
                if (i >= text.Length) return false;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
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
    /// Build a list of line ranges paired with synthetic function-kind containers for C# primary
    /// constructor declarations that carry a base primary-constructor call. This covers records
    /// (`record Child(int x) : Parent(x)`), C# 12 classes (`class Child(int x) : Parent(x)`) and
    /// structs (`struct Child(int x) : Parent(x)`), including the multi-line form where
    /// `: Parent(x)` sits on a continuation line. SymbolExtractor does not synthesize a separate
    /// ctor symbol for the implicit primary constructor, so the `Parent(x)` reference would
    /// otherwise land on `container = null` (when the declaration line has no body range) or on
    /// the declaring type itself. The synthetic container covers the header range only; methods
    /// inside a braced body still resolve to their real containers via FindInnermostContainer,
    /// and within the end line the override is limited to columns before the terminator so body
    /// calls sharing the same line (e.g. `record Child(int V) : Parent(V) { ... Add(V, 1); }`)
    /// are not pulled onto the synthetic ctor.
    /// C# の primary constructor 宣言に対して合成 function コンテナの (start, end, endColumn, container)
    /// リストを作る。record だけでなく C# 12 の class / struct primary constructor も対象にし、
    /// 宣言ヘッダーの範囲（end line は終端 `;` / `{` のカラムまで）だけ合成 ctor に差し替えることで、
    /// 同一行 braced body の呼び出しや後続メソッドは本来の container に残る。
    /// </summary>
    private static List<(int StartLine, int StartColumn, int EndLine, int EndColumn, SymbolRecord Container)> BuildCSharpPrimaryCtorContainers(
        string language,
        IReadOnlyList<SymbolRecord> symbols,
        string[] structuralLines)
    {
        var ranges = new List<(int, int, int, int, SymbolRecord)>();
        if (language != "csharp")
            return ranges;

        foreach (var symbol in symbols)
        {
            // SymbolExtractor stores C# records as Kind=class and C# 12 structs as Kind=struct.
            // Interfaces / enums / delegates cannot have primary constructors in C# so skip them.
            // C# record は Kind=class、C# 12 struct は Kind=struct として登録されるため両方対象。
            if (symbol.Kind != "class" && symbol.Kind != "struct")
                continue;
            var signature = symbol.Signature;
            if (string.IsNullOrWhiteSpace(signature))
                continue;

            // SymbolRecord.Signature only captures the first declaration line, so the first-line
            // regex filter misses split-line primary-ctor forms such as
            // `public record Child\n(\n    int Value\n)\n    : Parent(Value);`. Walk the
            // structural-masked lines from StartLine until we hit `;` / `{` and run the
            // primary-ctor detection on the joined header text instead.
            // 宣言の signature は 1 行目だけしか持たないので、`record` / `class` / `struct` と
            // `(` を別行に分ける書式では先頭行 regex の前段フィルタが空振りする。ここでは
            // structuralLines から `;` / `{` までヘッダーを連結し、連結後のテキストで判定する。
            var (headerEndLine, headerEndColumn, headerText) = CollectCSharpRecordHeader(structuralLines, symbol.StartLine);
            if (!IsCSharpPrimaryCtorHeader(headerText))
                continue;
            if (!HasCSharpBasePrimaryCtorCall(headerText))
                continue;

            // Restrict the synthetic container to the actual declaration span, starting at the
            // `class` / `struct` / `record` keyword column on the start line. Without this
            // same-line tokens BEFORE the keyword (e.g. attribute arguments in
            // `[Attr(Helper.Get())] public class Child(int x) : Parent(x) {}`) would get
            // attributed to the synthetic ctor and pollute callers / impact with phantom
            // `Child` callers for `Attr` and `Helper.Get`.
            // 合成 ctor コンテナを本物の宣言範囲に限定する。`class` / `struct` / `record`
            // キーワード位置より前（同一行の属性呼び出しなど）は本来の container に残す。
            var startColumn = FindCSharpPrimaryCtorKeywordColumn(structuralLines, symbol.StartLine);

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

            ranges.Add((symbol.StartLine, startColumn, headerEndLine, headerEndColumn, synthetic));
        }

        return ranges;
    }

    private static int FindCSharpPrimaryCtorKeywordColumn(string[] structuralLines, int startLine)
    {
        var idx = Math.Max(0, startLine - 1);
        if (idx >= structuralLines.Length)
            return 0;
        var line = structuralLines[idx];
        foreach (var keyword in CSharpPrimaryCtorKeywords)
        {
            int pos = 0;
            while (pos < line.Length)
            {
                var found = line.IndexOf(keyword, pos, StringComparison.Ordinal);
                if (found < 0) break;
                var before = found == 0 ? ' ' : line[found - 1];
                var afterIdx = found + keyword.Length;
                var after = afterIdx < line.Length ? line[afterIdx] : ' ';
                if (!IsCSharpIdentifierPart(before) && !IsCSharpIdentifierPart(after))
                    return found;
                pos = found + 1;
            }
        }
        return 0;
    }

    private static readonly string[] CSharpPrimaryCtorKeywords = { "record", "class", "struct" };

    private static bool IsCSharpIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Walk structural-masked lines starting at the 1-based <paramref name="startLine"/> and collect
    /// the declaration header up to (but not including) the first `;` or `{` that sits outside a
    /// string or comment. Returns the 1-based line number where the terminator was found (or the
    /// final line index when none was found) and the joined header text for further parsing.
    /// Reused for record primary-ctor container synthesis and multi-line `: base(...)` resolution.
    /// structuralLines を使って、class / struct / record 宣言ヘッダーを最初の `;` / `{` まで連結する。
    /// record primary-ctor のコンテナ合成と、複数行 `: base(...)` 解決の両方で使う。
    /// </summary>
    internal static (int EndLine, int EndColumn, string Text) CollectCSharpRecordHeader(string[] structuralLines, int startLine)
    {
        var startIdx = Math.Max(0, startLine - 1);
        if (structuralLines.Length == 0)
            return (startLine, int.MaxValue, string.Empty);

        // Depth-aware termination so that `{` / `;` inside annotation arg lists (e.g. the `{` in
        // `@Ann({A.class, B.class})`) or attribute-argument brackets does not cut the header off
        // before the real base-list terminator, which would silently drop the base type.
        // We intentionally do NOT track `<` / `>` as generic depth here: comparison operators
        // inside annotation / attribute expressions (e.g. `[Attr(Flag = 1 < 2)]` or
        // `@Ann(flag = 1 < 2)`) are raised as `<` without a matching `>`, so angle-depth tracking
        // would leave the counter pinned above zero and silently drop the real top-level `{` / `;`
        // terminator, letting the synthetic primary-ctor container or the Java base-type parse
        // swallow everything up to EOF. `{` / `;` cannot legally appear inside a top-level
        // `<...>` generic arg list in either C# or Java, so paren/bracket masking is sufficient.
        // EndColumn tracks the column index of the top-level terminator on the end line, or
        // int.MaxValue when no terminator was found (end-of-file), so call-site-scoped container
        // overrides can restrict themselves to the header portion of the end line.
        // アノテーション引数の `{` などを本当のヘッダ終端と誤認しないよう、`()` / `[]` の深さを追いながら
        // 最初の top-level `;` / `{` でのみ終了する。`<` / `>` は annotation / attribute 式内の比較演算子で
        // 非対称に現れうるため generic 深度として扱わない。
        // EndColumn は end line 上の終端 `;` / `{` の位置を返す（終端が無ければ int.MaxValue）。
        var sb = new System.Text.StringBuilder();
        int parenDepth = 0;
        int bracketDepth = 0;
        // Comment / string awareness so unbalanced `(` / `[` / `{` / `;` inside a line
        // comment, block comment, or string literal never advances the depth counters,
        // fires the terminator, or leaks into the returned header text. For Java `extends`
        // headers the structuralLines array is an unmasked clone (StructuralLineMasker is a
        // no-op for Java), so this is what keeps `class Leaf extends Root /* ( stray [ */ {`
        // from pinning parenDepth / bracketDepth at 1 and skipping the real `{` terminator,
        // and it also prevents ParseJavaBaseType from seeing the comment body when it parses
        // the header text downstream.
        // コメント・文字列内の不均衡な `(` / `[` / `{` / `;` を terminator 判定・連結テキスト双方から除外する。
        bool inBlockComment = false;
        bool inString = false;
        for (int i = startIdx; i < structuralLines.Length; i++)
        {
            var line = structuralLines[i];
            var masked = line.ToCharArray();
            var terminatorIdx = -1;
            for (int j = 0; j < line.Length; j++)
            {
                var c = line[j];

                if (inBlockComment)
                {
                    masked[j] = ' ';
                    if (c == '*' && j + 1 < line.Length && line[j + 1] == '/')
                    {
                        inBlockComment = false;
                        masked[j + 1] = ' ';
                        j++;
                    }
                    continue;
                }

                if (inString)
                {
                    masked[j] = ' ';
                    if (c == '\\' && j + 1 < line.Length)
                    {
                        masked[j + 1] = ' ';
                        j++;
                        continue;
                    }
                    if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '/' && j + 1 < line.Length)
                {
                    if (line[j + 1] == '/')
                    {
                        for (int k = j; k < line.Length; k++)
                            masked[k] = ' ';
                        break;
                    }
                    if (line[j + 1] == '*')
                    {
                        inBlockComment = true;
                        masked[j] = ' ';
                        masked[j + 1] = ' ';
                        j++;
                        continue;
                    }
                }

                if (c == '"')
                {
                    inString = true;
                    masked[j] = ' ';
                    continue;
                }

                if (c == '\'')
                {
                    // Rust / OCaml lifetime annotation vs. char literal: only skip when a
                    // closing `'` exists within ~12 chars on this line.
                    // Rust の lifetime と char literal を短距離の閉じ `'` の有無で見分ける。
                    var closeIdx = -1;
                    var limit = Math.Min(line.Length, j + 12);
                    for (int k = j + 1; k < limit; k++)
                    {
                        if (line[k] == '\\' && k + 1 < line.Length)
                        {
                            k++;
                            continue;
                        }
                        if (line[k] == '\'')
                        {
                            closeIdx = k;
                            break;
                        }
                    }
                    if (closeIdx > 0)
                    {
                        for (int k = j; k <= closeIdx; k++)
                            masked[k] = ' ';
                        j = closeIdx;
                    }
                    continue;
                }

                if (c == '(') parenDepth++;
                else if (c == ')') { if (parenDepth > 0) parenDepth--; }
                else if (c == '[') bracketDepth++;
                else if (c == ']') { if (bracketDepth > 0) bracketDepth--; }
                else if ((c == ';' || c == '{') && parenDepth == 0 && bracketDepth == 0)
                {
                    terminatorIdx = j;
                    break;
                }
            }

            var maskedLine = new string(masked);
            if (terminatorIdx >= 0)
            {
                sb.Append(maskedLine, 0, terminatorIdx);
                return (i + 1, terminatorIdx, sb.ToString());
            }

            sb.Append(maskedLine);
            sb.Append('\n');
        }

        return (structuralLines.Length, int.MaxValue, sb.ToString());
    }

    /// <summary>
    /// Returns true when the C# type header text carries a base-list entry that looks like a
    /// primary-constructor call (contains `(`). Accepts multi-line header text already joined by
    /// <see cref="CollectCSharpRecordHeader"/>.
    /// C# 型ヘッダー（複数行連結後でも可）の base-list 先頭エントリが `(` を含むかを判定する。
    /// </summary>
    /// <summary>
    /// Return true when a joined C# type-declaration header (possibly spanning multiple lines,
    /// including line-broken primary-ctor parens) looks like a primary-constructor declaration.
    /// Accepts `record Child(...)`, `record class Child(...)`, `record struct Child(...)`,
    /// C# 12 `class Child(...)`, `struct Child(...)`, generic arity such as `class Child<T>(...)`,
    /// and the split-line form where `record Child\n(\n ... )` places the `(` on a continuation line.
    /// 連結済みの C# 宣言ヘッダーが primary-ctor 宣言かを判定する。`record` だけでなく C# 12 の
    /// `class` / `struct` primary constructor も対象にし、`(` が別行に分かれる書式にも対応する。
    /// </summary>
    private static bool IsCSharpPrimaryCtorHeader(string headerText)
    {
        if (string.IsNullOrWhiteSpace(headerText))
            return false;
        return CSharpPrimaryCtorHeaderRegex.IsMatch(headerText);
    }

    private static bool HasCSharpBasePrimaryCtorCall(string headerText)
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
        // Only count a `(` that sits at generic / bracket depth 0 — a primary-ctor base call
        // always puts its argument list directly after the bare type name, whereas generic args
        // and array ranks can legally contain `(` (tuple syntax `<(int, int)>`, function types
        // `<Func<(int, int)>>`, or attribute arg brackets). A naive `.Contains('(')` would treat
        // those as primary-ctor calls and synthesize a phantom record ctor container.
        // 先頭エントリのうち generic/bracket 深度 0 の `(` だけを primary-ctor 呼び出し扱いにする。
        // `IBox<(int, int)>` のような tuple を含む interface 実装を連鎖呼び出しと誤認させない。
        int angleDepth = 0;
        int squareDepth = 0;
        for (int i = 0; i < firstEntry.Length; i++)
        {
            var c = firstEntry[i];
            switch (c)
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0) angleDepth--;
                    break;
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0) squareDepth--;
                    break;
                case '(':
                    if (angleDepth == 0 && squareDepth == 0)
                        return true;
                    break;
            }
        }
        return false;
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

        // Locate `extends` at angle/paren depth 0 so bounded type parameters like
        // `class Leaf<T extends Number> extends Root {` do not resolve to the
        // parameter bound (`Number`) instead of the real base (`Root`).
        // 境界付き型パラメータ（`class Leaf<T extends Number> extends Root {`）で
        // 型パラメータ境界の `extends` を先に拾わないよう、angle / paren 深度 0 の
        // `extends` のみを検出する。
        int start = FindTopLevelExtendsEnd(signature!);
        if (start < 0)
            return null;

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

    /// <summary>
    /// Return the index past the first `extends` keyword that appears at angle/paren depth 0,
    /// or -1 when no such occurrence exists. Matches the semantics of the old `\bextends\s+`
    /// regex entrypoint but skips `extends` inside `<...>` (bounded type parameters) and
    /// `(...)` (annotation argument lists).
    /// </summary>
    private static int FindTopLevelExtendsEnd(string signature)
    {
        int angleDepth = 0;
        int parenDepth = 0;
        for (int i = 0; i < signature.Length; i++)
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
                parenDepth++;
            }
            else if (c == ')')
            {
                if (parenDepth > 0) parenDepth--;
            }
            else if (angleDepth == 0 && parenDepth == 0 && IsExtendsKeywordAt(signature, i))
            {
                int end = i + 7; // "extends".Length
                while (end < signature.Length && char.IsWhiteSpace(signature[end]))
                    end++;
                return end;
            }
        }
        return -1;
    }

    private static bool IsExtendsKeywordAt(string signature, int i)
    {
        const string Keyword = "extends";
        if (i + Keyword.Length > signature.Length)
            return false;
        if (i > 0 && IsJavaIdentifierPart(signature[i - 1]))
            return false;
        if (string.CompareOrdinal(signature, i, Keyword, 0, Keyword.Length) != 0)
            return false;
        int after = i + Keyword.Length;
        // `\bextends\s+` equivalence: must be followed by whitespace so that names like
        // `extendsFoo` or identifiers containing `extends` do not match.
        // `\bextends\s+` 相当: `extendsFoo` のような識別子や合成語を誤認しないよう、
        // 直後に空白が続くものだけを `extends` キーワードとして扱う。
        if (after >= signature.Length)
            return false;
        return char.IsWhiteSpace(signature[after]);
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
                        var ch = text[i];
                        // Skip string / char literals so `@Ann(text=")")` does not close early.
                        // 文字列・文字リテラル内の `)` で早期終了しないようスキップする。
                        if (ch == '"' || ch == '\'')
                        {
                            var quote = ch;
                            i++;
                            while (i < text.Length)
                            {
                                var lc = text[i];
                                if (lc == '\\' && i + 1 < text.Length) { i += 2; continue; }
                                if (lc == quote) { i++; break; }
                                i++;
                            }
                            continue;
                        }
                        if (ch == '(') parenDepth++;
                        else if (ch == ')') parenDepth--;
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

    // SQL-aware line sanitizer used only for the `EXEC` / `EXECUTE` / `CALL` no-parens scan.
    // Preserves backtick-quoted identifiers (MySQL / MariaDB) so `` CALL `proc-name`; `` can be
    // matched, while still stripping `'...'` / `"..."` string literals and SQL line / block
    // comments so text inside comments does not emit a phantom reference. Line-comment tokens
    // `--` (ANSI / T-SQL / MySQL) and `#` (MySQL / MariaDB) are scanned with awareness of
    // backtick and bracket identifier quoting, so a legitimate name containing `#` or `-` such
    // as `` CALL `proc#1`; `` or `EXEC [proc-name]` is not truncated mid-identifier.
    // `EXEC` / `EXECUTE` / `CALL` の括弧なし抽出向けの SQL 特化サニタイザ。バッククォート引用
    // （MySQL / MariaDB の識別子引用）は保持したまま、`'...'` / `"..."` と `--` / `#` / `/* ... */`
    // の SQL コメントを除去する。`--` と `#` の走査は backtick / bracket 引用を尊重するため、
    // `` CALL `proc#1`; `` や `EXEC [proc-name]` のような識別子が途中で切られない。
    private static string PrepareSqlLineForIdentifierScan(string line)
    {
        var result = SqlStringLiteralRegex.Replace(line, "\"\"");
        result = InlineBlockCommentRegex.Replace(result, " ");

        int commentStart = -1;
        for (int i = 0; i < result.Length; i++)
        {
            char c = result[i];
            if (c == '`')
            {
                var closing = result.IndexOf('`', i + 1);
                if (closing < 0)
                    break;
                i = closing;
                continue;
            }
            if (c == '[')
            {
                var closing = result.IndexOf(']', i + 1);
                if (closing < 0)
                    break;
                i = closing;
                continue;
            }
            if (c == '-' && i + 1 < result.Length && result[i + 1] == '-')
            {
                commentStart = i;
                break;
            }
            if (c == '#')
            {
                commentStart = i;
                break;
            }
        }
        if (commentStart >= 0)
            result = result[..commentStart];

        return result;
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

    private readonly record struct NestedGenericCallCandidate(string Name, int NameIndex);

    private static IEnumerable<NestedGenericCallCandidate> EnumerateNestedGenericCallCandidates(
        string preparedLine,
        HashSet<int> matchedCallIndices)
    {
        for (var i = 0; i < preparedLine.Length; i++)
        {
            if (!IsAsciiIdentifierStartChar(preparedLine[i]))
                continue;
            if (i > 0 && (IsIdentifierChar(preparedLine[i - 1]) || preparedLine[i - 1] == '$'))
                continue;

            var nameStart = i;
            i++;
            while (i < preparedLine.Length && IsIdentifierChar(preparedLine[i]))
                i++;

            if (matchedCallIndices.Contains(nameStart))
            {
                i--;
                continue;
            }

            var scan = i;
            if (scan + 1 < preparedLine.Length
                && preparedLine[scan] == '?'
                && preparedLine[scan + 1] == '.')
            {
                scan += 2;
            }

            if (scan >= preparedLine.Length || preparedLine[scan] != '<')
            {
                i--;
                continue;
            }

            if (!TrySkipBalancedGenericArgs(preparedLine, ref scan, out var sawNestedGeneric) || !sawNestedGeneric)
            {
                i--;
                continue;
            }

            while (scan < preparedLine.Length && char.IsWhiteSpace(preparedLine[scan]))
                scan++;

            if (scan < preparedLine.Length && preparedLine[scan] == '(')
                yield return new NestedGenericCallCandidate(preparedLine[nameStart..i], nameStart);

            i--;
        }
    }

    private static IEnumerable<NestedGenericCallCandidate> EnumerateNestedGenericInitializerCandidates(
        string preparedLine,
        HashSet<int> matchedInitializerIndices,
        bool requireOpeningBrace)
    {
        for (var i = 0; i < preparedLine.Length; i++)
        {
            if (!IsStandaloneNewKeyword(preparedLine, i))
                continue;

            var scan = i + 3;
            if (!TryReadQualifiedTypeName(preparedLine, ref scan, out var name, out var nameIndex))
            {
                i += 2;
                continue;
            }

            if (matchedInitializerIndices.Contains(nameIndex))
            {
                i = scan - 1;
                continue;
            }

            if (!TrySkipBalancedGenericArgs(preparedLine, ref scan, out var sawNestedGeneric) || !sawNestedGeneric)
            {
                i = scan - 1;
                continue;
            }

            if (!TrySkipArraySuffixes(preparedLine, ref scan))
            {
                i = scan - 1;
                continue;
            }

            while (scan < preparedLine.Length && char.IsWhiteSpace(preparedLine[scan]))
                scan++;

            if (requireOpeningBrace)
            {
                if (scan < preparedLine.Length && preparedLine[scan] == '{')
                    yield return new NestedGenericCallCandidate(name, nameIndex);
            }
            else if (scan == preparedLine.Length)
            {
                yield return new NestedGenericCallCandidate(name, nameIndex);
            }

            i = scan - 1;
        }
    }

    private static bool TryReadQualifiedTypeName(
        string preparedLine,
        ref int scan,
        out string name,
        out int nameIndex)
    {
        name = string.Empty;
        nameIndex = -1;

        while (true)
        {
            while (scan < preparedLine.Length && char.IsWhiteSpace(preparedLine[scan]))
                scan++;

            if (scan >= preparedLine.Length || !IsAsciiIdentifierStartChar(preparedLine[scan]))
                return false;

            var segmentStart = scan;
            scan++;
            while (scan < preparedLine.Length && IsIdentifierChar(preparedLine[scan]))
                scan++;

            name = preparedLine[segmentStart..scan];
            nameIndex = segmentStart;

            var separatorScan = scan;
            while (separatorScan < preparedLine.Length && char.IsWhiteSpace(preparedLine[separatorScan]))
                separatorScan++;

            if (separatorScan + 1 < preparedLine.Length
                && preparedLine[separatorScan] == ':'
                && preparedLine[separatorScan + 1] == ':')
            {
                scan = separatorScan + 2;
                continue;
            }

            if (separatorScan < preparedLine.Length && preparedLine[separatorScan] == '.')
            {
                scan = separatorScan + 1;
                continue;
            }

            scan = separatorScan;
            return true;
        }
    }

    private static bool TrySkipArraySuffixes(string preparedLine, ref int scan)
    {
        while (true)
        {
            while (scan < preparedLine.Length && char.IsWhiteSpace(preparedLine[scan]))
                scan++;

            if (scan >= preparedLine.Length || preparedLine[scan] != '[')
                return true;

            scan++;
            while (scan < preparedLine.Length && preparedLine[scan] != ']')
                scan++;

            if (scan >= preparedLine.Length || preparedLine[scan] != ']')
                return false;

            scan++;
        }
    }

    private static bool ShouldSkipInitializerName(string language, string name) =>
        (language == "csharp" && CSharpBuiltInTypeNames.Contains(name))
        || (language == "java" && JavaPrimitiveTypeNames.Contains(name))
        || IsIgnoredCallName(language, name);

    private static bool IsStandaloneNewKeyword(string preparedLine, int index)
    {
        if (index < 0 || index + 3 > preparedLine.Length)
            return false;
        if (preparedLine[index] != 'n'
            || preparedLine[index + 1] != 'e'
            || preparedLine[index + 2] != 'w')
        {
            return false;
        }

        if (index > 0 && IsIdentifierChar(preparedLine[index - 1]))
            return false;

        return index + 3 >= preparedLine.Length || !IsIdentifierChar(preparedLine[index + 3]);
    }

    private static bool TrySkipBalancedGenericArgs(string preparedLine, ref int scan, out bool sawNestedGeneric)
    {
        sawNestedGeneric = false;
        if (scan >= preparedLine.Length || preparedLine[scan] != '<')
            return false;

        var depth = 0;
        while (scan < preparedLine.Length)
        {
            var ch = preparedLine[scan++];
            if (ch == '<')
            {
                depth++;
                if (depth > 1)
                    sawNestedGeneric = true;
            }
            else if (ch == '>')
            {
                depth--;
                if (depth == 0)
                    return true;
                if (depth < 0)
                    return false;
            }
        }

        return false;
    }

    private static bool IsAsciiIdentifierStartChar(char ch) =>
        ch == '_' || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

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

    private static bool IsInsideCSharpAttributeRange(IReadOnlyList<(int start, int end)> ranges, int index)
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
