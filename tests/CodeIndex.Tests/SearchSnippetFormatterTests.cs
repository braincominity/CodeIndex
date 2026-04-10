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
}
