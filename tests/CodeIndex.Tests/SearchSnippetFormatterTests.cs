using CodeIndex.Cli;
using CodeIndex.Database;

namespace CodeIndex.Tests;

public class SearchSnippetFormatterTests
{
    [Fact]
    public void BuildExcerpt_CentersOnMatchAndTracksContext()
    {
        const string content = "line 1\nline 2\ncall Target()\nline 4\nline 5";

        var excerpt = SearchSnippetFormatter.BuildExcerpt(content, "Target", absoluteStartLine: 10, maxLines: 3);

        Assert.Equal(11, excerpt.StartLine);
        Assert.Equal(13, excerpt.EndLine);
        Assert.Equal([12], excerpt.MatchLines);
        Assert.Equal(1, excerpt.ContextBefore);
        Assert.Equal(1, excerpt.ContextAfter);
        Assert.Single(excerpt.Highlights);
        Assert.Contains("Target", excerpt.Highlights[0].Terms);
    }

    [Fact]
    public void BuildExcerpt_CapturesMultipleMatchLinesWhenTheyFit()
    {
        const string content = "Target()\nnoop()\nTarget()\nline 4";

        var excerpt = SearchSnippetFormatter.BuildExcerpt(content, "Target", absoluteStartLine: 1, maxLines: 4);

        Assert.Equal([1, 3], excerpt.MatchLines);
        Assert.Equal(2, excerpt.Highlights.Count);
    }

    [Fact]
    public void ToCompactResult_UsesSnippetInsteadOfFullChunkContent()
    {
        var result = new SearchResult
        {
            Path = "src/app.cs",
            Lang = "csharp",
            StartLine = 20,
            EndLine = 24,
            Content = "line 20\nline 21\nRun(App)\nline 23\nline 24",
            Score = -1.5,
        };

        var compact = SearchSnippetFormatter.ToCompactResult(result, "App", maxLines: 3);

        Assert.Equal("src/app.cs", compact.Path);
        Assert.Equal(20, compact.ChunkStartLine);
        Assert.Equal(24, compact.ChunkEndLine);
        Assert.Equal(21, compact.SnippetStartLine);
        Assert.Equal(23, compact.SnippetEndLine);
        Assert.Contains("Run(App)", compact.Snippet);
        Assert.Equal([22], compact.MatchLines);
        Assert.Single(compact.Highlights);
        Assert.Equal(-1.5, compact.Score);
    }

    [Fact]
    public void Format_AddsTruncationMarkers_WhenExcerptIsTruncated()
    {
        // 10 lines, match on line 5 — with maxLines=3, both ends should be truncated
        // 10行、5行目に一致 — maxLines=3 で両端がトランケートされるはず
        var lines = Enumerable.Range(1, 10).Select(i => i == 5 ? "call Target()" : $"line {i}");
        var content = string.Join('\n', lines);

        var formatted = SearchSnippetFormatter.Format(content, "Target", maxLines: 3);

        Assert.Equal("...", formatted[0]);
        Assert.Equal("...", formatted[^1]);
        Assert.True(formatted.Count <= 5); // 3 content lines + up to 2 markers / 3コンテンツ行 + 最大2マーカー
        Assert.Contains(formatted, line => line.Contains("Target"));
    }

    [Fact]
    public void Format_NoMarkers_WhenContentFitsEntirely()
    {
        const string content = "line 1\ncall Target()\nline 3";

        var formatted = SearchSnippetFormatter.Format(content, "Target", maxLines: 8);

        Assert.DoesNotContain("...", formatted);
        Assert.Contains(formatted, line => line.Contains("Target"));
    }

    [Fact]
    public void Format_TruncatedAfterOnly_WhenMatchIsNearStart()
    {
        // Match on line 1 with maxLines=2 out of 6 — truncated after only
        // 1行目に一致、maxLines=2/全6行 — 後ろだけトランケート
        var lines = Enumerable.Range(1, 6).Select(i => i == 1 ? "call Target()" : $"line {i}");
        var content = string.Join('\n', lines);

        var formatted = SearchSnippetFormatter.Format(content, "Target", maxLines: 2);

        Assert.NotEqual("...", formatted[0]);
        Assert.Equal("...", formatted[^1]);
    }

    [Fact]
    public void Format_TruncatedBeforeOnly_WhenMatchIsNearEnd()
    {
        // Match on last line with maxLines=2 out of 6 — truncated before only
        // 最終行に一致、maxLines=2/全6行 — 前だけトランケート
        var lines = Enumerable.Range(1, 6).Select(i => i == 6 ? "call Target()" : $"line {i}");
        var content = string.Join('\n', lines);

        var formatted = SearchSnippetFormatter.Format(content, "Target", maxLines: 2);

        Assert.Equal("...", formatted[0]);
        Assert.NotEqual("...", formatted[^1]);
    }
}
