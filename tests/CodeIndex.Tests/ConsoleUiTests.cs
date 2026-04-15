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
        Assert.Contains("cdidx references <query>", output);
        Assert.Contains("cdidx callers <query>", output);
        Assert.Contains("cdidx callees <query>", output);
        Assert.Contains("cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--snippet-lines <n>] [--fts] [--exact|--exact-substring] [--count]", output);
        Assert.Contains("cdidx definition <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--kind <kind>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body] [--exact|--exact-name]", output);
        Assert.Contains("cdidx inspect <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--body] [--max-line-width <n>] [--exact|--exact-name]", output);
        Assert.Contains("--snippet-lines <n>        Search snippet length (1-20, default: 8)", output);
        Assert.Contains("cdidx find <query> --path <pattern>", output);
        Assert.Contains("--exact-substring          Search only: case-sensitive exact substring (no FTS5)", output);
        Assert.Contains("--exact-name               symbols/definition/references/callers/callees/inspect: NFKC + Unicode CaseFold exact name match", output);
        Assert.Contains("--commits <id> [id ...]    Update only files changed in the specified git commits (preferred after commits)", output);
        Assert.Contains("--files <path> [path ...]  Update only the specified files; old rename/delete paths are not purged unless also listed", output);
        Assert.Contains("cdidx excerpt <path> --start <line> [--end <line>] [--before <n>] [--after <n>] [--max-line-width <n>] [--focus-line <line>] [--focus-column <n>] [--focus-length <n>] [--db <path>] [--json]", output);
        Assert.Contains("--max-line-width <n>       Clamp very long single-line context/excerpt payloads", output);
        Assert.Contains("--focus-column <n>         excerpt: column to keep centered when clamping", output);
        Assert.Contains("--focus-line <line>        excerpt: line whose focused column should stay visible", output);
        Assert.Contains("cdidx map [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests]", output);
        Assert.Contains("cdidx unused [--db <path>] [--json] [--limit <n>] [--kind <kind>] [--lang <lang>] [--path <pattern>] [--exclude-path <pattern>] [--exclude-tests] [--count]", output);
        Assert.Contains("--json                     Output as JSON (streaming hits use JSON lines; counts/summaries use one object)", output);
        Assert.Contains("cdidx search \"Run();\" --exact-substring        Case-sensitive exact substring search", output);
        Assert.Contains("cdidx symbols Run --exact-name                Exact symbol-name match", output);
        Assert.Contains("backfill-fold", output);
        Assert.Contains("find <query>               Find literal substring matches inside known indexed files", output);
        Assert.Contains("Prefer --exact-substring for search, keep --exact for find", output);
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
        Assert.Contains("Use --commits with a project path after normal commits", output);
        Assert.Contains("Use --files only for known in-place edits or new files", output);
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

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public void PrintCompletions_KnownShell_ReturnsTrue(string shell)
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
                var exactSubstringToken = shell == "fish" ? "exact-substring" : "--exact-substring";
                var exactNameToken = shell == "fish" ? "exact-name" : "--exact-name";
                Assert.Contains(exactSubstringToken, output);
                Assert.Contains(exactNameToken, output);
                if (shell is "bash" or "zsh")
                {
                    // Should contain dynamically generated languages, including newly added ones
                    // 動的生成の言語リストに新しく追加した言語が含まれているか検証
                    Assert.Contains("elixir", output);
                    Assert.Contains("graphql", output);
                    Assert.Contains("protobuf", output);
                }
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
    public void PrintCompletions_BashAndZshKeepFocusOptionsExcerptOnly(string shell)
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
                Assert.Contains("--max-line-width", output);
                Assert.Contains("--exact", output);
                Assert.Contains("--query", output);
                if (shell == "bash")
                {
                    Assert.Contains("if [ \"$cmd\" = \"find\" ]", output);
                    Assert.Contains("elif [ \"$cmd\" = \"excerpt\" ]; then", output);
                    var findBranch = ExtractBetween(output, "if [ \"$cmd\" = \"find\" ]", "elif [ \"$cmd\" = \"excerpt\" ]; then");
                    var excerptBranch = ExtractBetween(output, "elif [ \"$cmd\" = \"excerpt\" ]; then", "elif [ \"$cmd\" = \"references\" ]; then");
                    Assert.DoesNotContain("--focus-column", findBranch);
                    Assert.Contains("--focus-column", excerptBranch);
                }
                else
                {
                    Assert.Contains("if [[ $subcmd == find ]]; then", output);
                    Assert.Contains("elif [[ $subcmd == excerpt ]]; then", output);
                    var findBranch = ExtractBetween(output, "if [[ $subcmd == find ]]; then", "elif [[ $subcmd == excerpt ]]; then");
                    var excerptBranch = ExtractBetween(output, "elif [[ $subcmd == excerpt ]]; then", "elif [[ $subcmd == references ]]; then");
                    Assert.DoesNotContain("focus-column", findBranch);
                    Assert.Contains("focus-column", excerptBranch);
                }
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
    [InlineData("backfillfold", "backfill-fold")]
    public void FindClosestCommand_Typo_ReturnsCorrectSuggestion(string input, string expected)
    {
        Assert.Equal(expected, ConsoleUi.FindClosestCommand(input));
    }

    [Theory]
    [InlineData("xyzabc")]
    [InlineData("foobarqux")]
    [InlineData("fold")]
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

    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        start += startMarker.Length;
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Missing end marker: {endMarker}");
        return text[start..end];
    }
}
