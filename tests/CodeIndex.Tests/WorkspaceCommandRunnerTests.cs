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
}
