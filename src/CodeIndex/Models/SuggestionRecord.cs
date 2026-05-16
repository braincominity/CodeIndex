namespace CodeIndex.Models;

/// <summary>
/// Represents a structured improvement suggestion from an AI agent.
/// AIエージェントからの構造化された改善提案を表す。
/// </summary>
public class SuggestionRecord
{
    /// <summary>
    /// Suggestion category. One of: symbol_extraction, reference_extraction,
    /// search_ranking, language_support, output_format, crash_report,
    /// unexpected_error, other.
    /// 提案カテゴリ。symbol_extraction, reference_extraction, search_ranking,
    /// language_support, output_format, crash_report, unexpected_error, other のいずれか。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Target programming language (optional) / 対象プログラミング言語（任意）</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Description of the gap, improvement, or error situation.
    /// Must NOT contain source code — only natural language descriptions.
    /// ギャップ、改善、またはエラー状況の説明。
    /// ソースコードを含んではならない — 自然言語の記述のみ。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// What the AI was trying to do when it noticed the gap (optional).
    /// Must NOT contain source code.
    /// AIがギャップに気づいたとき何をしようとしていたか（任意）。
    /// ソースコードを含んではならない。
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Agent or tool name that recorded the suggestion (optional).
    /// 提案を記録したエージェントまたはツール名（任意）。
    /// </summary>
    public string? Agent { get; set; }

    /// <summary>
    /// SHA256 dedup hash of (category + language + normalized description).
    /// Prevents the same suggestion from being recorded twice.
    /// (category + language + 正規化済み description) のSHA256重複排除ハッシュ。
    /// 同一提案の二重記録を防ぐ。
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the suggestion was recorded / 提案が記録されたUTCタイムスタンプ</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Whether the suggestion has been submitted to GitHub / GitHubに送信済みか</summary>
    public bool SubmittedToGitHub { get; set; }

    /// <summary>GitHub Issue URL if submitted / 送信済みの場合のGitHub Issue URL</summary>
    public string? GitHubIssueUrl { get; set; }

    /// <summary>
    /// All valid category values.
    /// 有効なカテゴリ値の一覧。
    /// </summary>
    public static readonly string[] ValidCategories =
    {
        "symbol_extraction",
        "reference_extraction",
        "search_ranking",
        "language_support",
        "output_format",
        "crash_report",
        "unexpected_error",
        "other"
    };
}
