using CodeIndex.Cli;
using CodeIndex.Database;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for workspace-level metadata enrichment.
/// ワークスペース単位メタデータ補強のテスト。
/// </summary>
public class WorkspaceMetadataEnricherTests
{
    [Fact]
    public void Enrich_StatusResult_PopulatesProjectRootHeadAndDirty()
    {
        var (projectRoot, dbPath, expectedHead) = CreateDirtyGitProject("cdidx_workspace_status");
        try
        {
            var status = new StatusResult();

            WorkspaceMetadataEnricher.Enrich(status, dbPath);

            Assert.Equal(projectRoot, status.ProjectRoot);
            Assert.Equal(expectedHead, status.GitHead);
            Assert.True(status.GitIsDirty);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Enrich_RepoMapResult_PopulatesProjectRootHeadAndDirty()
    {
        var (projectRoot, dbPath, expectedHead) = CreateDirtyGitProject("cdidx_workspace_map");
        try
        {
            var map = new RepoMapResult();

            WorkspaceMetadataEnricher.Enrich(map, dbPath);

            Assert.Equal(projectRoot, map.ProjectRoot);
            Assert.Equal(expectedHead, map.GitHead);
            Assert.True(map.GitIsDirty);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Enrich_SymbolAnalysisResult_PopulatesProjectRootHeadAndDirty()
    {
        var (projectRoot, dbPath, expectedHead) = CreateDirtyGitProject("cdidx_workspace_analysis");
        try
        {
            var analysis = new SymbolAnalysisResult();

            WorkspaceMetadataEnricher.Enrich(analysis, dbPath);

            Assert.Equal(projectRoot, analysis.ProjectRoot);
            Assert.Equal(expectedHead, analysis.GitHead);
            Assert.True(analysis.GitIsDirty);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Enrich_StatusResult_LeavesGitMetadataNullOutsideGitRepo()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_workspace_plain");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var status = new StatusResult();

            WorkspaceMetadataEnricher.Enrich(status, dbPath);

            Assert.Equal(projectRoot, status.ProjectRoot);
            Assert.Null(status.GitHead);
            Assert.Null(status.GitIsDirty);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    private static (string ProjectRoot, string DbPath, string HeadCommit) CreateDirtyGitProject(string prefix)
    {
        var projectRoot = TestProjectHelper.CreateTempProject(prefix);

        TestProjectHelper.InitializeGitRepo(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "src"));

        var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
        File.WriteAllText(sourcePath, "class App {}\n");
        TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
        TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();

        File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

        return (projectRoot, dbPath, expectedHead);
    }
}
