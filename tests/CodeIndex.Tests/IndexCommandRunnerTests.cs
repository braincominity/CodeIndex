using System.Text.Json;
using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for indexing command argument handling.
/// インデックスコマンドの引数処理テスト。
/// </summary>
public class IndexCommandRunnerTests
{
    [Fact]
    public void ParseArgs_HelpFlagSetsShowHelp()
    {
        var options = IndexCommandRunner.ParseArgs(["--help"]);

        Assert.True(options.ShowHelp);
        Assert.Null(options.ProjectPath);
    }

    [Fact]
    public void Run_HelpFlagReturnsSuccess()
    {
        var exitCode = IndexCommandRunner.Run(["--help"], new JsonSerializerOptions());

        Assert.Equal(CommandExitCodes.Success, exitCode);
    }
}
