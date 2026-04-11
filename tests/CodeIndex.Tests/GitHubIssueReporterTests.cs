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
    public void ResolveToken_GithubTokenSet_ReturnsGithubToken()
    {
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "ghp_github_test_token");

        Assert.Equal("ghp_github_test_token", GitHubIssueReporter.ResolveToken());
    }

    [Fact]
    public void ResolveToken_BothSet_PrefersCdidxToken()
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

    public void Dispose()
    {
        // Restore original env vars / 元の環境変数をリストア
        Environment.SetEnvironmentVariable("CDIDX_GITHUB_TOKEN", _originalCdidxToken);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", _originalGhToken);
    }
}
