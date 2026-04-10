using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for console usage output.
/// コンソールの使い方出力のテスト。
/// </summary>
public class ConsoleUiTests
{
    [Fact]
    public void PrintUsage_WithBanner_IncludesAsciiArt()
    {
        var output = CaptureUsageOutput();

        Assert.Contains("██████╗", output);
        Assert.Contains("Usage:", output);
        Assert.Contains("cdidx index <projectPath> --commits <id> [id ...]", output);
    }

    [Fact]
    public void PrintUsage_WithoutBanner_HidesAsciiArtAndEasterEggFlags()
    {
        var output = CaptureUsageOutput(showBanner: false);

        Assert.DoesNotContain("██████╗", output);
        Assert.Contains("Usage:", output);
        Assert.Contains("cdidx index <projectPath> [--db <path>] [--rebuild] [--verbose] [--json]", output);
        Assert.Contains("cdidx definition <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--kind <kind>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body]", output);
        Assert.Contains("cdidx references <query>", output);
        Assert.Contains("cdidx callers <query>", output);
        Assert.Contains("cdidx callees <query>", output);
        Assert.Contains("cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--snippet-lines <n>] [--fts]", output);
        Assert.Contains("--snippet-lines <n>        Search snippet length (1-20, default: 8)", output);
        Assert.Contains("cdidx excerpt <path> --start <line>", output);
        Assert.Contains("cdidx map [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests]", output);
        Assert.Contains("cdidx inspect <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body]", output);
        Assert.DoesNotContain("Easter eggs", output);
        Assert.DoesNotContain("--sushi", output);
        Assert.DoesNotContain("--random-spinner", output);
    }

    [Fact]
    public void PrintUsage_ShowsCommitUpdateWorkflowClearly()
    {
        var output = CaptureUsageOutput(showBanner: false);

        Assert.Contains("Update workflows:", output);
        Assert.Contains("Use --commits with a project path", output);
        Assert.Contains("cdidx index ./myproject --commits abc123", output);
    }

    [Fact]
    public void GetSpinnerFrames_Default_UsesRotatingBrailleSequence()
    {
        var frames = ConsoleUi.GetSpinnerFrames(null);

        Assert.Equal(["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"], frames);
    }

    private static string CaptureUsageOutput(bool showBanner = true)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);
                ConsoleUi.PrintUsage(showBanner);
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
