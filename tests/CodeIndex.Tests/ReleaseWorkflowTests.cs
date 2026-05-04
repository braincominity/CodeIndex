namespace CodeIndex.Tests;

public class ReleaseWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_PublishesTrimmedSelfContainedBinariesAndVerifiesCliJson()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("-p:PublishTrimmed=true", workflow);
        Assert.DoesNotContain("-p:PublishTrimmed=false", workflow);
        Assert.Contains("status --json", workflow);
        Assert.Contains("Expected status --json to exit 0", workflow);
        Assert.Contains("'\"files\":'", workflow);
        Assert.Contains("'\"version\":'", workflow);
    }

    private static string GetRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeIndex.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root / リポジトリルートを特定できませんでした");
    }
}
