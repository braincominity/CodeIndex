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
        Assert.Contains("cdidx backfill-fold [--db <path>] [--json]", output);
        Assert.Contains("cdidx definition <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--kind <kind>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body]", output);
        Assert.Contains("cdidx references <query>", output);
        Assert.Contains("cdidx callers <query>", output);
        Assert.Contains("cdidx callees <query>", output);
        Assert.Contains("cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--snippet-lines <n>] [--fts] [--count]", output);
        Assert.Contains("--snippet-lines <n>        Search snippet length (1-20, default: 8)", output);
        Assert.Contains("cdidx find <query> --path <pattern>", output);
        Assert.Contains("cdidx excerpt <path> --start <line>", output);
        Assert.Contains("cdidx map [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests]", output);
        Assert.Contains("cdidx inspect <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body]", output);
        Assert.Contains("backfill-fold", output);
        Assert.Contains("find <query>               Find literal substring matches inside known indexed files", output);
        Assert.Contains("search/find: case-sensitive exact substring", output);
        Assert.Contains("impact <query>             Show transitive callers; type queries may return heuristic file-level dependency hints", output);
        Assert.Contains("cdidx find guard --path src/Auth.cs --after 2", output);
        Assert.Contains("cdidx impact FolderDiffService --json           Type query may return heuristic file-level dependency hints", output);
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

    [Fact]
    public void LoadVersion_ReturnsActualVersion_NotFallback()
    {
        // In the test environment the built version.json should be present.
        // Verify the returned value is a real version, not the "0.0.0" fallback.
        // テスト環境ではビルド済みの version.json が存在するはず。
        // フォールバックの "0.0.0" ではなく実際のバージョンが返ることを検証。
        var version = ConsoleUi.LoadVersion();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.NotEqual("0.0.0", version);
        Assert.Contains('.', version);
    }

    [Fact]
    public void PrintCompletions_KnownShell_ReturnsTrue()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                Assert.True(ConsoleUi.PrintCompletions("bash"));
                var output = writer.ToString();
                // Should contain dynamically generated languages, including newly added ones
                // 動的生成の言語リストに新しく追加した言語が含まれているか検証
                Assert.Contains("elixir", output);
                Assert.Contains("graphql", output);
                Assert.Contains("protobuf", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void PrintCompletions_FishIncludesFindOptions()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                Assert.True(ConsoleUi.PrintCompletions("fish"));
                var output = writer.ToString();
                Assert.Contains("__fish_seen_subcommand_from search definition references callers callees symbols files find", output);
                Assert.Contains("__fish_seen_subcommand_from find excerpt", output);
                Assert.Contains("__fish_seen_subcommand_from search find", output);
                Assert.Contains("-l query -r -d 'Literal query'", output);
                Assert.Contains("-l before -r -d 'Context lines before'", output);
                Assert.Contains("-l after -r -d 'Context lines after'", output);
                Assert.Contains("-l exact -d 'Exact match'", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    public void PrintCompletions_BashAndZshIncludeFindSpecificOptions(string shell)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                Assert.True(ConsoleUi.PrintCompletions(shell));
                var output = writer.ToString();
                Assert.Contains("find", output);
                Assert.Contains("--before", output);
                Assert.Contains("--after", output);
                Assert.Contains("--exact", output);
                Assert.Contains("--query", output);
                if (shell == "bash")
                    Assert.Contains("if [ \"$cmd\" = \"find\" ]", output);
                else
                    Assert.Contains("if [[ $subcmd == find ]]; then", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void PrintUsage_ShowsWorkingFindDashedLiteralExample()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                ConsoleUi.PrintUsage();
                var output = writer.ToString();
                Assert.Contains("cdidx find --path README.md -- --path", output);
                Assert.DoesNotContain("cdidx find -- --path --path README.md", output);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void PrintCompletions_UnknownShell_ReturnsFalse()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalErr = Console.Error;
            using var writer = new StringWriter();
            try
            {
                Console.SetError(writer);
                Assert.False(ConsoleUi.PrintCompletions("powershell"));
                Assert.Contains("Unknown shell", writer.ToString());
            }
            finally
            {
                Console.SetError(originalErr);
            }
        }
    }

    [Theory]
    [InlineData("serach", "search")]
    [InlineData("statu", "status")]
    [InlineData("defnition", "definition")]
    [InlineData("refernces", "references")]
    [InlineData("indx", "index")]
    [InlineData("mpa", "map")]
    public void FindClosestCommand_Typo_ReturnsCorrectSuggestion(string input, string expected)
    {
        Assert.Equal(expected, ConsoleUi.FindClosestCommand(input));
    }

    [Theory]
    [InlineData("xyzabc")]
    [InlineData("foobarqux")]
    public void FindClosestCommand_GarbageInput_ReturnsNull(string input)
    {
        Assert.Null(ConsoleUi.FindClosestCommand(input));
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
