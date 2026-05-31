using CodeIndex.Cli;
using System.Text.Json;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class WorkspaceCommandRunnerTests
{
    private readonly JsonSerializerOptions _jsonOptions = ProgramRunner.CreateDefaultJsonOptions();

    [Fact]
    public void WorkspaceList_ReadsManifestMembers()
    {
        var root = TestProjectHelper.CreateTempProject("cdidx_workspace_manifest");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "A"));
            File.WriteAllText(Path.Combine(root, "cdidx.workspace.json"), """
                {
                  "members": ["src/A"],
                  "index_strategy": "per_member",
                  "default_db_name": "index.db"
                }
                """);

            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.Combine(root, "src", "A");
                var (exitCode, stdout, _) = ConsoleCapture.Capture(() => WorkspaceCommandRunner.Run(["list", "--json"], _jsonOptions));

                Assert.Equal(CommandExitCodes.Success, exitCode);
                Assert.Contains("cdidx.workspace.json", stdout);
                Assert.Contains("index.db", stdout);
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(root);
        }
    }

    [Fact]
    public void ConfigShow_PrintsPrecedence()
    {
        var (exitCode, stdout, _) = ConsoleCapture.Capture(() => CdidxConfigFile.RunShow(["--json"], _jsonOptions));

        Assert.Equal(CommandExitCodes.Success, exitCode);
        Assert.Contains("precedence", stdout);
        Assert.Contains("active_workspace", stdout);
    }

    [Fact]
    public void ActiveWorkspace_AffectsQueryResolutionButNotIndexResolution()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_active_workspace_project");
        var activeRoot = TestProjectHelper.CreateTempProject("cdidx_active_workspace_state");
        var activeDb = Path.Combine(activeRoot, ".cdidx", "codeindex.db");
        try
        {
            using var env = EnvironmentVariableScope.Capture(ActiveWorkspace.EnvironmentVariable);
            Environment.SetEnvironmentVariable(ActiveWorkspace.EnvironmentVariable, activeDb);

            var query = DbPathResolver.ResolveForQuery(projectRoot, explicitDbPath: null, explicitDataDir: null);
            var index = DbPathResolver.ResolveForIndex(projectRoot, explicitDbPath: null, explicitDataDir: null);

            Assert.Equal(Path.GetFullPath(activeDb), query.DbPath);
            Assert.Equal(DbPathResolver.DataDirSourceActiveWorkspace, query.DataDirSource);
            Assert.Equal(Path.Combine(projectRoot, ".cdidx", "codeindex.db"), index.DbPath);
            Assert.Equal(DbPathResolver.DataDirSourceWorkspace, index.DataDirSource);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(activeRoot);
        }
    }

    [Fact]
    public void WorkspaceUse_RejectsUnknownManifestMember()
    {
        var root = TestProjectHelper.CreateTempProject("cdidx_workspace_use_unknown");
        var configHome = TestProjectHelper.CreateTempProject("cdidx_workspace_use_config");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src", "A"));
            File.WriteAllText(Path.Combine(root, "cdidx.workspace.json"), """{ "members": ["src/A"] }""");
            using var env = EnvironmentVariableScope.Capture("XDG_CONFIG_HOME");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = root;
                var (exitCode, _, stderr) = ConsoleCapture.Capture(() => WorkspaceCommandRunner.Run(["use", "typo"], _jsonOptions));

                Assert.Equal(CommandExitCodes.UsageError, exitCode);
                Assert.Contains("workspace member was not found", stderr);
                Assert.False(File.Exists(ActiveWorkspace.StatePath));
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(root);
            TestProjectHelper.DeleteDirectory(configHome);
        }
    }
}
