using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for GitHubIssueReporter token resolution logic.
/// GitHubIssueReporter のトークン解決ロジックのテスト。
///
/// Note: The actual GitHub API call is not tested here (would require
/// a real token and network access). Only the token resolution logic
/// is covered.
/// 注: 実際の GitHub API 呼び出しはここではテストしない（実トークンと
/// ネットワークアクセスが必要）。トークン解決ロジックのみをカバーする。
/// </summary>
[Collection("SQLite pool sensitive")]
public class GitHubIssueReporterTests : IDisposable
{
    // Save original env vars to restore after each test
    // 各テスト後にリストアするため元の環境変数を保存
    private readonly string? _originalCdidxToken;
    private readonly string? _originalGhToken;

    public GitHubIssueReporterTests()
    {
        _originalCdidxToken = Environment.GetEnvironmentVariable("CDIDX_GITHUB_TOKEN");
        _originalGhToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    [Fact]
    public void ResolveToken_NeitherSet_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        Assert.Null(GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_CdidxTokenSet_ReturnsCdidxToken()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", "ghp_cdidx_test_token");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        Assert.Equal("ghp_cdidx_test_token", GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_GenericGithubToken_IsIgnored()
    {
        // Generic GITHUB_TOKEN must NOT be used — prevents ambient CI tokens
        // from silently publishing to an external repository.
        // 汎用 GITHUB_TOKEN は使わない — CI の環境トークンが意図せず
        // 外部リポジトリに公開されることを防ぐ。
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "ghp_github_test_token");

        Assert.Null(GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_CdidxTokenSet_IgnoresGenericToken()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", "ghp_cdidx_preferred");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "ghp_github_fallback");

        Assert.Equal("ghp_cdidx_preferred", GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_EmptyString_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", "");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "");

        Assert.Null(GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_WhitespaceOnly_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", "   ");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "   ");

        Assert.Null(GitHubIssueReporter.ResolveToken());
    }

    // --- ScrubInlineCode tests / ScrubInlineCode テスト ---

    [Fact]
    public void ScrubInlineCode_RemovesBacktickSpans()
    {
        var input = "Arrow functions like `const foo = () => {}` are not detected";
        var result = GitHubIssueReporter.ScrubInlineCode(input);
        Assert.Equal("Arrow functions like [code example removed] are not detected", result);
    }

    [Fact]
    public void ScrubInlineCode_RemovesMultipleSpans()
    {
        var input = "Both `import React` and `require('foo')` are missed";
        var result = GitHubIssueReporter.ScrubInlineCode(input);
        Assert.Equal("Both [code example removed] and [code example removed] are missed", result);
    }

    [Fact]
    public void ScrubInlineCode_PreservesPlainText()
    {
        var input = "Symbol extraction misses Kotlin data classes";
        var result = GitHubIssueReporter.ScrubInlineCode(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void ScrubInlineCode_HandlesEmptyAndNull()
    {
        Assert.Equal("", GitHubIssueReporter.ScrubInlineCode(""));
        Assert.Null(GitHubIssueReporter.ScrubInlineCode(null!));
    }

    [Fact]
    public void BuildSubmissionFailureMessage_IsActionable()
    {
        var message = GitHubIssueReporter.BuildSubmissionFailureMessage("network timeout");

        Assert.Contains("GitHub issue creation failed: network timeout", message);
        Assert.Contains("stays recorded locally", message);
        Assert.Contains("CDIDX_GITHUB_TOKEN", message);
        Assert.Contains("retry `suggest_improvement`", message);
    }

    [Fact]
    public void BuildApiFailureMessage_IsActionable()
    {
        var message = GitHubIssueReporter.BuildApiFailureMessage(403, "{\"message\":\"forbidden\"}");

        Assert.Contains("GitHub API responded 403", message);
        Assert.Contains("stays local", message);
        Assert.Contains("repository permissions", message);
        Assert.Contains("retry `suggest_improvement`", message);
    }

    public void Dispose()
    {
        // Restore original env vars / 元の環境変数をリストア
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", _originalCdidxToken);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", _originalGhToken);
    }
}
