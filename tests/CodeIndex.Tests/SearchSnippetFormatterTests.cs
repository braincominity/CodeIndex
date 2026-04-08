using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for human-readable search snippet formatting.
/// 人間向け検索スニペット整形のテスト。
/// </summary>
public class SearchSnippetFormatterTests
{
    [Fact]
    public void Format_CentersSnippetAroundMatch()
    {
        var content = string.Join('\n', Enumerable.Range(1, 9).Select(i => $"line {i}"));

        var snippet = SearchSnippetFormatter.Format(content, "line 6");

        Assert.Equal(["...", "line 4", "line 5", "line 6", "line 7", "line 8", "..."], snippet);
    }

    [Fact]
    public void Format_FallsBackToStartWhenNoMatchFound()
    {
        var content = string.Join('\n', Enumerable.Range(1, 7).Select(i => $"line {i}"));

        var snippet = SearchSnippetFormatter.Format(content, "missing");

        Assert.Equal(["line 1", "line 2", "line 3", "line 4", "line 5", "..."], snippet);
    }

    [Fact]
    public void Format_ShowsTailWhenMatchIsNearEnd()
    {
        var content = string.Join('\n', Enumerable.Range(1, 7).Select(i => $"line {i}"));

        var snippet = SearchSnippetFormatter.Format(content, "line 7");

        Assert.Equal(["...", "line 3", "line 4", "line 5", "line 6", "line 7"], snippet);
    }
}
