namespace CodeIndex;

/// <summary>
/// Base exception for CodeIndex hot-path failures that need to surface a
/// stable machine-readable Code, a Category (Database, Filesystem, ...),
/// the offending file/DB Path, and an optional recovery Hint. CLI and MCP
/// formatters route on these fields so users see the offending path and
/// automation can branch on Code/Category without parsing free-form
/// Message text (#1580).
/// 機械可読な Code / Category / Path / Hint を伴う CodeIndex のホットパス例外。
/// CLI と MCP のフォーマッタはこれらのフィールドを参照して構造化エラーを返し、
/// ユーザーは失敗したパスを認識でき、自動化側は Message を文字列解析しなくても
/// 分岐できる (#1580)。
///
/// IMPORTANT (#1530 / #1580 contract): the <c>Path</c> and <c>Hint</c> fields
/// are echoed verbatim to MCP clients by <c>McpServer.BuildSanitizedToolErrorMessage</c>
/// and <c>BuildSanitizedLoopErrorMessage</c>. The <c>BuildSanitizedToolErrorMessage</c>
/// sanitizer for #1530 strips <c>ex.Message</c> precisely because user-bound
/// SQLite parameters or matched content can leak into it; <see cref="CodeIndexException"/>
/// fields bypass that sanitizer. Throwers MUST only populate <c>Path</c> and
/// <c>Hint</c> with tool-defined strings (DB paths, file paths, fixed recovery
/// copy). Never echo a search query, SQL parameter binding, or other
/// user-supplied content into these fields.
/// 重要 (#1530 / #1580 規約): <c>Path</c> / <c>Hint</c> は MCP 出力に素通しされる。
/// #1530 は <c>ex.Message</c> を MCP に出さないことで SQL バインド値や検索結果の
/// 漏えいを封じている。CodeIndexException の構造化フィールドはその sanitizer を
/// 通らないため、ツールが固定した DB パス / ファイルパス / 回復メッセージ以外
/// （ユーザー検索文字列・SQL バインド値・マッチ内容など）を Path / Hint に
/// 載せてはならない。
/// </summary>
public class CodeIndexException : Exception
{
    public string Code { get; }
    public string Category { get; }
    public string? Path { get; }
    public string? Hint { get; }

    public CodeIndexException(
        string code,
        string category,
        string message,
        string? path = null,
        string? hint = null,
        Exception? innerException = null)
        : base(BuildMessage(message, path), innerException)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Path = path;
        Hint = hint;
    }

    private static string BuildMessage(string message, string? path)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("message must be non-empty", nameof(message));
        return string.IsNullOrWhiteSpace(path) ? message : $"{message} (path: {path})";
    }
}

/// <summary>
/// Stable Category labels used by <see cref="CodeIndexException"/>. Treated like
/// the <c>CommandErrorCodes</c> taxonomy: published values stay published.
/// CodeIndexException で使う安定した Category ラベル。一度公開した値は変更しない。
/// </summary>
public static class CodeIndexExceptionCategory
{
    public const string Database = "database";
    public const string Filesystem = "filesystem";
    public const string Indexer = "indexer";
}
