namespace CodeIndex.Tests;

public class DbSymbolReaderSqlTests
{
    [Fact]
    public void HotspotFilteredCandidates_UseExplicitProjection()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "src", "CodeIndex", "Database", "DbSymbolReader.cs"));
        var filteredBlocks = source.Split("filtered_candidates AS (", StringSplitOptions.None).Skip(1).ToList();

        Assert.Equal(2, filteredBlocks.Count);
        foreach (var block in filteredBlocks)
        {
            var projection = block[..block.IndexOf("FROM all_candidate_symbols", StringComparison.Ordinal)];
            Assert.DoesNotContain("SELECT *", projection, StringComparison.Ordinal);
            Assert.Contains("SELECT id,", projection, StringComparison.Ordinal);
            Assert.Contains("file_id,", projection, StringComparison.Ordinal);
            Assert.Contains("name,", projection, StringComparison.Ordinal);
            Assert.Contains("kind,", projection, StringComparison.Ordinal);
            Assert.Contains("path,", projection, StringComparison.Ordinal);
            Assert.Contains("lang,", projection, StringComparison.Ordinal);
            Assert.Contains("line,", projection, StringComparison.Ordinal);
            Assert.Contains("visibility,", projection, StringComparison.Ordinal);
            Assert.Contains("container_name,", projection, StringComparison.Ordinal);
            Assert.Contains("logical_target_key", projection, StringComparison.Ordinal);
        }
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")) || Directory.Exists(Path.Combine(dir.FullName, "src", "CodeIndex")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
