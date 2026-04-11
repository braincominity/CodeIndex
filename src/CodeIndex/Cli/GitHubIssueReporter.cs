using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeIndex.Models;

namespace CodeIndex.Cli;

/// <summary>
/// Reports improvement suggestions as GitHub Issues using the GitHub REST API.
/// GitHub REST APIを使用して改善提案をGitHub Issueとして報告する。
///
/// This class is part of the AI feedback system. It only transmits structured
/// gap descriptions — never source code. The data sent is limited to:
///   - Category (one of 8 fixed enum values)
///   - Language name (e.g. "typescript")
///   - Description text (validated by SourceCodeDetector to not contain code)
///   - Context text (also validated)
///   - cdidx version and suggestion hash
///
/// このクラスはAIフィードバックシステムの一部である。構造化されたギャップ記述のみを
/// 送信し、ソースコードは一切送信しない。送信データは以下に限定される:
///   - カテゴリ（8つの固定 enum 値のいずれか）
///   - 言語名（例: "typescript"）
///   - 説明テキスト（SourceCodeDetector によりコードが含まれないことを検証済み）
///   - コンテキストテキスト（同様に検証済み）
///   - cdidx バージョンと提案ハッシュ
///
/// GitHub Issues are only created when the user has explicitly configured
/// a GitHub token via the CDIDX_GITHUB_TOKEN environment variable.
/// The generic GITHUB_TOKEN is NOT used — this prevents ambient tokens
/// (e.g. from CI environments) from silently publishing to an external repo.
/// If CDIDX_GITHUB_TOKEN is not set, this class does nothing.
/// The destination repository is hardcoded to widthdom/CodeIndex.
/// GitHub Issue は、ユーザーが CDIDX_GITHUB_TOKEN 環境変数で
/// GitHub トークンを明示的に設定した場合にのみ作成される。
/// 汎用の GITHUB_TOKEN は使用しない — CI 等の環境トークンが意図せず
/// 外部リポジトリに公開されることを防ぐ。
/// CDIDX_GITHUB_TOKEN が未設定の場合、このクラスは何もしない。
/// 送信先リポジトリは widthdom/CodeIndex に固定されている。
/// </summary>
internal static class GitHubIssueReporter
{
    // Static HttpClient singleton — .NET best practice for reuse.
    // 静的 HttpClient シングルトン — .NET の再利用ベストプラクティス。
    private static readonly HttpClient s_httpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "cdidx" },
            { "Accept", "application/vnd.github+json" },
            { "X-GitHub-Api-Version", "2022-11-28" },
        }
    };

    // Target repository for issue creation / Issue 作成先リポジトリ
    private const string RepoOwner = "widthdom";
    private const string RepoName = "CodeIndex";
    private const string ApiBase = "https://api.github.com";

    /// <summary>
    /// Try to create a GitHub Issue for the given suggestion.
    /// Returns the issue URL on success, null if no token is set or on failure.
    /// This method is best-effort — it never throws.
    /// 指定された提案の GitHub Issue 作成を試みる。
    /// 成功時は Issue URL を返し、トークン未設定時や失敗時は null を返す。
    /// ベストエフォート — 例外を投げない。
    /// </summary>
    public static async Task<string?> TryCreateIssueAsync(SuggestionRecord record, string version)
    {
        var token = ResolveToken();
        if (token == null)
            return null;

        try
        {
            return await CreateIssueAsync(record, version, token);
        }
        catch (Exception ex)
        {
            // Best-effort: log to stderr but do not propagate.
            // ベストエフォート: stderr にログ出力するが伝播しない。
            Console.Error.WriteLine($"[cdidx] GitHub issue creation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve the GitHub token from the CDIDX_GITHUB_TOKEN environment variable.
    /// Only CDIDX_GITHUB_TOKEN is accepted — generic GITHUB_TOKEN is NOT used.
    /// This prevents ambient tokens (e.g. from CI) from silently publishing
    /// suggestions to an external repository without explicit user intent.
    /// Returns null if the variable is not set.
    /// 環境変数 CDIDX_GITHUB_TOKEN からGitHubトークンを解決する。
    /// CDIDX_GITHUB_TOKEN のみを受け付ける — 汎用の GITHUB_TOKEN は使用しない。
    /// CI等で設定された環境トークンが、ユーザーの明示的な意図なしに
    /// 外部リポジトリに提案を公開してしまうことを防ぐ。
    /// 未設定の場合は null を返す。
    /// </summary>
    internal static string? ResolveToken()
    {
        var cdidxToken = Environment.GetEnvironmentVariable("CDIDX_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(cdidxToken))
            return cdidxToken;

        return null;
    }

    /// <summary>
    /// Create the GitHub Issue via the REST API.
    /// REST API 経由で GitHub Issue を作成する。
    /// </summary>
    private static async Task<string?> CreateIssueAsync(SuggestionRecord record, string version, string token)
    {
        var url = $"{ApiBase}/repos/{RepoOwner}/{RepoName}/issues";

        // Build the issue title — truncate description to 60 chars for readability.
        // Issue タイトルを構築 — 可読性のため description を60文字に切り詰める。
        var shortDesc = record.Description.Length > 60
            ? record.Description[..60] + "..."
            : record.Description;
        var title = $"[AI Suggestion] {record.Category}: {shortDesc}";

        // Build the issue body — ONLY structured fields, NEVER source code.
        // Issue 本文を構築 — 構造化フィールドのみ、ソースコードは一切含まない。
        var body = new StringBuilder();
        body.AppendLine("## Category");
        body.AppendLine(record.Category);
        body.AppendLine();
        body.AppendLine("## Language");
        body.AppendLine(record.Language ?? "N/A");
        body.AppendLine();
        body.AppendLine("## Description");
        body.AppendLine(record.Description);
        body.AppendLine();
        body.AppendLine("## Context");
        body.AppendLine(record.Context ?? "N/A");
        body.AppendLine();
        body.AppendLine("---");
        body.AppendLine($"_Submitted by cdidx v{version}. Hash: `{record.Hash}`_");

        // Build the request payload / リクエストペイロードを構築
        var payload = new JsonObject
        {
            ["title"] = title,
            ["body"] = body.ToString(),
            ["labels"] = new JsonArray { "ai-suggestion" },
        };

        var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        // Set auth header per-request (not on the shared HttpClient instance)
        // リクエスト単位で認証ヘッダーを設定（共有 HttpClient インスタンスには設定しない）
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await s_httpClient.SendAsync(requestMessage);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"[cdidx] GitHub API responded {(int)response.StatusCode}: {errorBody}");
            return null;
        }

        // Parse response to extract the issue URL / レスポンスを解析して Issue URL を抽出
        var responseJson = await response.Content.ReadAsStringAsync();
        var responseNode = JsonNode.Parse(responseJson);
        return responseNode?["html_url"]?.GetValue<string>();
    }
}
