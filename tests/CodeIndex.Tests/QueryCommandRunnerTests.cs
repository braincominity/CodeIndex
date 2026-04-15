using System.Text.Json;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Models;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for query-style CLI command execution.
/// クエリ系CLIコマンド実行のテスト。
/// </summary>
[Collection("SQLite pool sensitive")]
public class QueryCommandRunnerTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void ParseArgs_ParsesFiltersFlagsAndClampsSnippetLines()
    {
        var options = QueryCommandRunner.ParseArgs(
        [
            "RunSearch",
            "--db", "/tmp/query.db",
            "--limit", "7",
            "--lang", "csharp",
            "--kind", "function",
            "--fts",
            "--body",
            "--path", "src/**",
            "--exclude-path", "tests/**",
            "--exclude-path", "docs/**",
            "--exclude-tests",
            "--start", "12",
            "--end", "18",
            "--before", "2",
            "--after", "4",
            "--focus-line", "9",
            "--focus-column", "33",
            "--focus-length", "6",
            "--snippet-lines", "99",
            "--max-line-width", "77",
        ], jsonDefault: true);

        Assert.Equal("/tmp/query.db", options.DbPath);
        Assert.True(options.Json);
        Assert.Equal(7, options.Limit);
        Assert.Equal("csharp", options.Lang);
        Assert.Equal("function", options.Kind);
        Assert.Equal("RunSearch", options.Query);
        Assert.True(options.RawFts);
        Assert.True(options.IncludeBody);
        Assert.Equal(new[] { "src/**" }, options.PathPatterns);
        Assert.Equal(["tests/**", "docs/**"], options.ExcludePaths);
        Assert.True(options.ExcludeTests);
        Assert.Equal(12, options.StartLine);
        Assert.Equal(18, options.EndLine);
        Assert.Equal(2, options.ContextBefore);
        Assert.Equal(4, options.ContextAfter);
        Assert.Equal(9, options.FocusLine);
        Assert.Equal(33, options.FocusColumn);
        Assert.Equal(6, options.FocusLength);
        Assert.Equal(SearchSnippetFormatter.MaxSnippetLines, options.SnippetLines);
        Assert.Equal(77, options.MaxLineWidth);
    }

    [Fact]
    public void ParseArgs_CountFlagParsed()
    {
        var options = QueryCommandRunner.ParseArgs(["myquery", "--count"], jsonDefault: false);
        Assert.True(options.CountOnly);
        Assert.Equal("myquery", options.Query);
    }

    [Theory]
    [InlineData("definition", "--focus-column", "10")]
    [InlineData("definition", "--max-line-width", "10")]
    [InlineData("search", "--focus-column", "10")]
    [InlineData("symbols", "--max-line-width", "10")]
    public void QueryCommands_RejectPreviewOptionsWhenUnsupported(string command, string option, string value)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_preview_reject_{command}");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var args = new List<string>();
            switch (command)
            {
                case "definition":
                case "search":
                case "symbols":
                    args.AddRange(["QueryCommandRunner", "--db", dbPath, option, value, "--count"]);
                    break;
            }

            var (exitCode, _, stderr) = command switch
            {
                "definition" => CaptureConsole(() => QueryCommandRunner.RunDefinition([.. args], _jsonOptions)),
                "search" => CaptureConsole(() => QueryCommandRunner.RunSearch([.. args], _jsonOptions)),
                "symbols" => CaptureConsole(() => QueryCommandRunner.RunSymbols([.. args], _jsonOptions)),
                _ => throw new InvalidOperationException($"Unexpected command: {command}")
            };

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains($"{option} is not supported for {command}", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_AllowsPathValueThatLooksLikePreviewOption()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_search_preview_like_path_value");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["foo", "--db", dbPath, "--path=--max-line-width", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.DoesNotContain("is not supported", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_AllowsExcludePathValueThatLooksLikePreviewOption()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_references_preview_like_exclude_path_value");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["target", "--db", dbPath, "--exclude-path=--focus-line", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.DoesNotContain("is not supported", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_AllowsPathValueThatLooksLikePreviewOption()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_inspect_preview_like_path_value");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["target", "--db", dbPath, "--path=--max-line-width", "--json"],
                _jsonOptions));

            Assert.NotEqual(CommandExitCodes.UsageError, exitCode);
            Assert.DoesNotContain("is not supported", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_RejectsMissingFocusColumnValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_missing_focus_column");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "README.md", "markdown", "sample");

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["README.md", "--db", dbPath, "--start", "1", "--focus-column", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--focus-column requires a positive integer", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_RejectsMissingMaxLineWidthValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_references_missing_max_line_width");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["target", "--db", dbPath, "--max-line-width", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--max-line-width requires a positive integer", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_RejectsMissingMaxLineWidthValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_inspect_missing_max_line_width");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["target", "--db", dbPath, "--max-line-width", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--max-line-width requires a positive integer", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_UsageIncludesCount()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
            [],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("[--count]", stderr);
    }

    [Fact]
    public void ParseArgs_InvalidNumbersAndUnknownOptionsAccumulateParseErrors()
    {
        var (options, _, stderr) = CaptureConsole(() => QueryCommandRunner.ParseArgs(
        [
            "RunSearch",
            "--limit", "0",
            "--start", "0",
            "--end", "-1",
            "--before", "-2",
            "--after", "-3",
            "--snippet-lines", "0",
            "--mystery",
        ], jsonDefault: false));

        Assert.Equal(20, options.Limit);
        Assert.Null(options.StartLine);
        Assert.Null(options.EndLine);
        Assert.Equal(0, options.ContextBefore);
        Assert.Equal(0, options.ContextAfter);
        Assert.Equal(SearchSnippetFormatter.DefaultSnippetLines, options.SnippetLines);
        Assert.NotNull(options.ParseError);
        Assert.Contains("Error: --limit requires a positive integer", options.ParseError);
        Assert.Contains("Hint: retry with `--limit 1` or another positive integer.", options.ParseError);
        Assert.Contains("Error: --start requires a positive integer", options.ParseError);
        Assert.Contains("Error: --end requires a positive integer", options.ParseError);
        Assert.Contains("Error: --before requires a non-negative integer", options.ParseError);
        Assert.Contains("Hint: retry with `--before 0` or another non-negative integer.", options.ParseError);
        Assert.Contains("Error: --after requires a non-negative integer", options.ParseError);
        Assert.Contains("Error: --snippet-lines requires a positive integer", options.ParseError);
        Assert.Equal(string.Empty, stderr);
    }

    [Fact]
    public void ParseArgs_ParsesExactAliases()
    {
        var search = QueryCommandRunner.ParseArgs(["needle", "--exact-substring"], jsonDefault: false);
        Assert.True(search.ExactSubstring);
        Assert.False(search.ExactName);

        var symbols = QueryCommandRunner.ParseArgs(["Run", "--exact-name"], jsonDefault: false);
        Assert.True(symbols.ExactName);
        Assert.False(symbols.ExactSubstring);
    }

    [Fact]
    public void ParseArgs_NameFlagCollectsValuesAndRejectsMissingValue()
    {
        var ok = QueryCommandRunner.ParseArgs(
            ["first", "--name", "Alpha", "--name", "Beta", "extraPositional"],
            jsonDefault: false);
        Assert.Null(ok.ParseError);
        Assert.Equal("first", ok.Query);
        Assert.Equal(new[] { "Alpha", "Beta", "extraPositional" }, ok.ExtraNames);

        // --name swallowing a following flag as data is a silent trust failure; must be rejected.
        // --name が直後のフラグを値として飲み込むのは暗黙の誤動作。拒否する。
        var bad = QueryCommandRunner.ParseArgs(
            ["--name", "--lang", "csharp"],
            jsonDefault: false);
        Assert.NotNull(bad.ParseError);
        Assert.Contains("--name requires a value", bad.ParseError!);

        var badTail = QueryCommandRunner.ParseArgs(["--name"], jsonDefault: false);
        Assert.NotNull(badTail.ParseError);
    }

    [Theory]
    [InlineData("search-limit-tail", "search", "Error: --limit requires a value.")]
    [InlineData("search-top-tail", "search", "Error: --limit requires a value.")]
    [InlineData("search-db-tail", "search", "Error: --db requires a value.")]
    [InlineData("search-db-swallow", "search", "Error: --db requires a value.")]
    [InlineData("search-db-unknown-double-dash", "search", "Error: --db requires a value.")]
    [InlineData("search-db-recognized-double-dash", "search", "Error: --db requires a value.")]
    [InlineData("search-lang-swallow", "search", "Error: --lang requires a value.")]
    [InlineData("search-lang-unknown-double-dash", "search", "Error: --lang requires a value.")]
    [InlineData("search-path-swallow", "search", "Error: --path requires a value.")]
    [InlineData("search-exclude-path-swallow", "search", "Error: --exclude-path requires a value.")]
    [InlineData("definition-kind-swallow", "definition", "Error: --kind requires a value.")]
    public void QueryEntrypoints_MissingOrSwallowedOptionValuesReturnUsageError(string scenario, string command, string expectedError)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithMissingOrSwallowedValue(scenario));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains(expectedError, stderr);
        Assert.Contains("Hint: fix the invalid or missing option value", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        Assert.DoesNotContain("database not found", stderr);
        Assert.DoesNotContain("Warning: unknown option", stderr);
    }

    [Theory]
    [InlineData("search-db-inline-empty", "search", "Error: --db requires a value.")]
    [InlineData("search-lang-inline-empty", "search", "Error: --lang requires a value.")]
    [InlineData("search-path-inline-empty", "search", "Error: --path requires a value.")]
    [InlineData("search-exclude-path-inline-empty", "search", "Error: --exclude-path requires a value.")]
    public void QueryEntrypoints_EmptyInlineStringOptionValuesReturnUsageError(string scenario, string command, string expectedError)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithEmptyInlineStringValue(scenario));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains(expectedError, stderr);
        Assert.Contains("Hint: fix the invalid or missing option value", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        Assert.DoesNotContain("Unhandled exception", stderr);
    }

    [Theory]
    [InlineData("search-extra", "unexpected extra positional argument(s) for search")]
    [InlineData("excerpt-extra", "unexpected extra positional argument(s) for excerpt")]
    [InlineData("map-extra", "map does not accept positional arguments")]
    [InlineData("outline-extra", "outline does not accept positional arguments")]
    [InlineData("status-extra", "status does not accept positional arguments")]
    [InlineData("validate-extra", "validate does not accept positional arguments")]
    [InlineData("languages-extra", "languages does not accept positional arguments")]
    public void QueryEntrypoints_UnexpectedPositionalsReturnUsageError(string scenario, string expectedError)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithUnexpectedPositionals(scenario));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains(expectedError, stderr);
        Assert.DoesNotContain("database not found", stderr);
    }

    [Fact]
    public void RunFiles_PathFilterAcceptsLeadingDashValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_files_path_leading_dash");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["--db", dbPath, "--path", "-foo", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ExcludePathFilterAcceptsLeadingDashValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_files_exclude_path_leading_dash");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["--db", dbPath, "--exclude-path", "-foo", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_PathFilterAcceptsRecognizedOptionTokenViaInlineValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_files_path_inline_recognized_option");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/--json-dir/Demo.cs",
                "csharp",
                "class Demo {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                [$"--db={dbPath}", "--path=--json-dir", "--count", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            using var document = ParseJsonOutput(stdout);
            Assert.Equal(1, document.RootElement.GetProperty("count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ExcludePathFilterAcceptsRecognizedOptionTokenViaInlineValue()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_files_exclude_path_inline_recognized_option");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/--count-dir/Demo.cs",
                "csharp",
                "class Demo {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                [$"--db={dbPath}", "--exclude-path=--count-dir", "--count", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            using var document = ParseJsonOutput(stdout);
            Assert.Equal(0, document.RootElement.GetProperty("count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("--db", "/tmp/does-not-matter.db")]
    [InlineData("--db=")]
    [InlineData("--mystery")]
    public void RunLanguages_UnsupportedOptionsReturnUsageError(string flag, string? value = null)
    {
        var args = value == null
            ? new[] { flag }
            : new[] { flag, value };

        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunLanguages(args, _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains($"Error: {flag} is not supported for languages.", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine("languages")}", stderr);
        Assert.DoesNotContain("requires a value", stderr);
        Assert.DoesNotContain("Warning: unknown option", stderr);
    }

    [Fact]
    public void RunLanguages_JsonListsModernNodeModuleExtensions()
    {
        var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunLanguages(["--json"], _jsonOptions));

        Assert.Equal(CommandExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);

        using var document = ParseJsonOutput(stdout);
        var languages = document.RootElement.GetProperty("languages");
        var javascript = languages.EnumerateArray().First(lang => lang.GetProperty("lang").GetString() == "javascript");
        var typescript = languages.EnumerateArray().First(lang => lang.GetProperty("lang").GetString() == "typescript");

        Assert.Contains(".cjs", javascript.GetProperty("extensions").EnumerateArray().Select(ext => ext.GetString()));
        Assert.Contains(".mjs", javascript.GetProperty("extensions").EnumerateArray().Select(ext => ext.GetString()));
        Assert.Contains(".cts", typescript.GetProperty("extensions").EnumerateArray().Select(ext => ext.GetString()));
        Assert.Contains(".mts", typescript.GetProperty("extensions").EnumerateArray().Select(ext => ext.GetString()));
    }

    [Theory]
    [InlineData("search")]
    [InlineData("definition")]
    [InlineData("symbols")]
    [InlineData("files")]
    public void QueryEntrypoints_InvalidSinceReturnUsageError(string command)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithInvalidSince(command));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("Error: could not parse --since value 'nope' as a date/time.", stderr);
        Assert.Contains("Hint: fix the invalid or missing option value", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        Assert.DoesNotContain("No ", stderr);
    }

    [Theory]
    [InlineData("references")]
    [InlineData("callers")]
    [InlineData("callees")]
    [InlineData("excerpt")]
    [InlineData("map")]
    [InlineData("inspect")]
    [InlineData("outline")]
    [InlineData("status")]
    [InlineData("impact")]
    [InlineData("deps")]
    [InlineData("hotspots")]
    [InlineData("unused")]
    [InlineData("validate")]
    public void QueryEntrypoints_UnsupportedSinceReturnUsageError(string command)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithUnsupportedSince(command, "nope"));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains($"Error: --since is not supported for {command}.", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        Assert.DoesNotContain("could not parse --since value", stderr);
        Assert.DoesNotContain("database not found", stderr);
    }

    [Theory]
    [InlineData("search", "--no-json")]
    [InlineData("map", "--count")]
    [InlineData("inspect", "--count")]
    [InlineData("status", "--count")]
    [InlineData("validate", "--exact")]
    [InlineData("validate", "--count")]
    [InlineData("validate", "--lang", "javascript")]
    [InlineData("validate", "--exclude-path", "src/")]
    [InlineData("validate", "--exclude-tests")]
    [InlineData("validate", "--limit", "nope")]
    [InlineData("validate", "--top", "nope")]
    public void QueryEntrypoints_UnsupportedOptionsReturnUsageError(string command, string flag, string? value = null)
    {
        var args = value == null
            ? new[] { flag }
            : new[] { flag, value };

        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithUnsupportedOption(command, args));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains($"Error: {flag} is not supported for {command}.", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        if (flag is "--limit" or "--top")
            Assert.DoesNotContain("requires a positive integer", stderr);
        Assert.DoesNotContain("database not found", stderr);
    }

    [Theory]
    [InlineData("search-limit", "search", "--limit requires a positive integer")]
    [InlineData("search-top", "search", "--limit requires a positive integer")]
    [InlineData("search-snippet-lines", "search", "--snippet-lines requires a positive integer")]
    [InlineData("impact-depth", "impact", "--depth requires a non-negative integer")]
    [InlineData("excerpt-start", "excerpt", "--start requires a positive integer")]
    [InlineData("excerpt-end", "excerpt", "--end requires a positive integer")]
    [InlineData("excerpt-before", "excerpt", "--before requires a non-negative integer")]
    [InlineData("excerpt-after", "excerpt", "--after requires a non-negative integer")]
    public void QueryEntrypoints_InvalidNumericOptionsReturnUsageError(string scenario, string command, string expectedErrorFragment)
    {
        var (exitCode, _, stderr) = CaptureConsole(() => RunCommandWithInvalidNumeric(scenario));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains(expectedErrorFragment, stderr);
        Assert.Contains("Hint: fix the invalid or missing option value", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine(command)}", stderr);
        Assert.DoesNotContain("database not found", stderr);
    }

    [Fact]
    public void RunValidate_KindFilterNarrowsIssues()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_validate_kind_filter");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllBytes(
                Path.Combine(projectRoot, "src", "bom.cs"),
                [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("class Bom {}\n")]);
            File.WriteAllText(
                Path.Combine(projectRoot, "src", "mixed.cs"),
                "class Mixed {}\r\nclass Other {}\n");

            var (indexExitCode, _, indexStderr) = CaptureConsole(() => IndexCommandRunner.Run(
                [projectRoot, "--db", dbPath, "--json"],
                _jsonOptions));
            Assert.Equal(CommandExitCodes.Success, indexExitCode);
            Assert.Equal(string.Empty, indexStderr);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunValidate(
                ["--db", dbPath, "--json", "--kind", "bom"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal("bom", json.GetProperty("issues")[0].GetProperty("kind").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void BuildSymbolQueryList_TreatsPipeAsLiteralNameCharacter()
    {
        // `|` is a legitimate character in operator symbols (C# `operator |`, etc.); it must not
        // be treated as OR syntax so those names stay searchable.
        // `|` は `operator |` など演算子名に出現する有効な文字。OR 構文として分割してはならない。
        var options = QueryCommandRunner.ParseArgs(["|"], jsonDefault: false);
        var (queries, hadInput) = QueryCommandRunner.BuildSymbolQueryList(options);
        Assert.True(hadInput);
        Assert.NotNull(queries);
        Assert.Equal(new[] { "|" }, queries!);

        var compound = QueryCommandRunner.ParseArgs(["operator|"], jsonDefault: false);
        var (compoundQueries, _) = QueryCommandRunner.BuildSymbolQueryList(compound);
        Assert.Equal(new[] { "operator|" }, compoundQueries!);
    }

    [Fact]
    public void BuildSymbolQueryList_EmptyNameNowFailsAtParseTime()
    {
        // Empty inline/separated string values are now rejected during argument parsing before
        // symbol-query normalization runs, so they cannot broaden into an all-symbols dump.
        // 空文字の値は symbol-query 正規化まで進む前に引数解析で拒否される。
        var rejected = QueryCommandRunner.ParseArgs(["--name", ""], jsonDefault: false);
        Assert.NotNull(rejected.ParseError);
        Assert.Contains("--name requires a value", rejected.ParseError);
    }

    [Fact]
    public void RunSymbols_EmptyAfterNormalizationFailsClosed()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_symbols_empty_norm");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exit, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(["--name", "", "--db", dbPath], _jsonOptions));
            Assert.Equal(1, exit);
            Assert.Contains("--name requires a value", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_RejectsOversizedMultiNameBatches()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_symbols_oversize");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var names = Enumerable.Range(0, QueryCommandRunner.MaxSymbolQueryNames + 5)
                .Select(i => $"Name{i}")
                .ToArray();
            var argv = names.Concat(new[] { "--db", dbPath }).ToArray();
            int exit;
            string stderr;
            (exit, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(argv, _jsonOptions));
            Assert.Equal(1, exit);
            Assert.Contains("too many symbol names", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_JsonZeroResults_ReturnEmptyStdout()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_symbols_zero_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["MissingSymbol", "--db", dbPath, "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_JsonZeroResults_ReturnEmptyStdout()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_references_zero_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "src/app.py",
                    Lang = "python",
                    Size = 32,
                    Lines = 1,
                    Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                });
                writer.InsertSymbols([
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "bootstrap", Line = 1, StartLine = 1, EndLine = 1 }
                ]);
                writer.MarkGraphReady();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["MissingRef", "--db", dbPath, "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_JsonClampsLongSingleLineContext()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_references_long_line");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var longLine = "const x = 0; " + new string('a', 320) + " target(); " + new string('b', 320);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "dist/app.js",
                    Lang = "javascript",
                    Size = longLine.Length,
                    Lines = 1,
                    Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                });
                writer.InsertChunks([
                    new ChunkRecord { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 1, Content = longLine }
                ]);
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "target",
                        ReferenceKind = "call",
                        Line = 1,
                        Column = longLine.IndexOf("target", StringComparison.Ordinal) + 1,
                        Context = longLine,
                    }
                ]);
                writer.MarkGraphReady();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["target", "--db", dbPath, "--json", "--max-line-width", "96"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(json.GetProperty("context_truncated").GetBoolean());
            Assert.Contains("target()", json.GetProperty("context").GetString());
            Assert.True(json.GetProperty("context").GetString()!.Length <= 96);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_JsonClampsLongSingleLineContentAroundFocus()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_long_line");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/data.txt", "text", longLine);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["dist/data.txt", "--db", dbPath, "--start", "1", "--end", "1", "--json", "--max-line-width", "96", "--focus-column", (longLine.IndexOf("TARGET", StringComparison.Ordinal) + 1).ToString(), "--focus-length", "6"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(json.GetProperty("content_truncated").GetBoolean());
            Assert.DoesNotContain(longLine, json.GetProperty("content").GetString());
            Assert.Contains("TARGET", json.GetProperty("content").GetString());
            Assert.True(json.GetProperty("content").GetString()!.Length <= 96);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_JsonClampsLongSingleLineContentWithoutFocus()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_long_line_no_focus");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/data.txt", "text", longLine);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["dist/data.txt", "--db", dbPath, "--start", "1", "--end", "1", "--json", "--max-line-width", "96"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(json.GetProperty("content_truncated").GetBoolean());
            Assert.DoesNotContain(longLine, json.GetProperty("content").GetString());
            Assert.True(json.GetProperty("content").GetString()!.Length <= 96);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_FocusLineWithoutFocusColumnReturnsUsageError()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_focus_dep");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/data.txt", "text", new string('a', 320) + "TARGET" + new string('b', 320));

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["dist/data.txt", "--db", dbPath, "--start", "1", "--end", "1", "--json", "--max-line-width", "96", "--focus-line", "1"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--focus-line and --focus-length require --focus-column", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_FocusLineOutsideReturnedRangeReturnsUsageError()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_focus_range");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "README.md", "markdown", "line one\nline two\nline three");

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["README.md", "--db", dbPath, "--start", "2", "--end", "2", "--focus-line", "999", "--focus-column", "1", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--focus-line (999) must be within the returned excerpt range", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_FocusColumnOutsideFocusedLineReturnsUsageError()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_excerpt_focus_column_range");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/data.txt", "text", new string('a', 320) + "TARGET" + new string('b', 320));

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["dist/data.txt", "--db", dbPath, "--start", "1", "--end", "1", "--focus-column", "9999", "--max-line-width", "40", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--focus-column (9999) must be within the focused line length", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_JsonClampsLongSingleLineSnippet()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_find_long_line");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var longLine = new string('a', 320) + "target" + new string('b', 320);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/search.txt", "text", longLine);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["target", "--db", dbPath, "--path", "dist/search.txt", "--json", "--max-line-width", "96"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(json.GetProperty("snippet_truncated").GetBoolean());
            Assert.Contains("target", json.GetProperty("snippet").GetString());
            Assert.True(json.GetProperty("snippet").GetString()!.Length <= 96);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFindThenExcerpt_JsonKeepsMatchedTokenVisible()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_find_excerpt_flow");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var longLine = new string('a', 320) + "TARGET" + new string('b', 320);
            TestProjectHelper.InsertIndexedFile(dbPath, "dist/data.txt", "text", longLine);

            var (findExitCode, findStdout, findStderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["TARGET", "--db", dbPath, "--path", "dist/data.txt", "--json", "--exact", "--max-line-width", "96"],
                _jsonOptions));

            using var findDocument = ParseJsonOutput(findStdout);
            var findJson = findDocument.RootElement;
            var line = findJson.GetProperty("line").GetInt32();
            var column = findJson.GetProperty("column").GetInt32();

            var (excerptExitCode, excerptStdout, excerptStderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
                ["dist/data.txt", "--db", dbPath, "--start", line.ToString(), "--end", line.ToString(), "--json", "--max-line-width", "96", "--focus-column", column.ToString(), "--focus-length", "6"],
                _jsonOptions));

            using var excerptDocument = ParseJsonOutput(excerptStdout);
            var excerptJson = excerptDocument.RootElement;

            Assert.Equal(CommandExitCodes.Success, findExitCode);
            Assert.Equal(string.Empty, findStderr);
            Assert.Equal(CommandExitCodes.Success, excerptExitCode);
            Assert.Equal(string.Empty, excerptStderr);
            Assert.Contains("TARGET", excerptJson.GetProperty("content").GetString());
            Assert.True(excerptJson.GetProperty("content_truncated").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_JsonZeroResults_WithMissingGraphTable_ReturnsDegradedPayload()
    {
        var (projectRoot, readOnlyUri) = CreateReadOnlyMissingGraphTableDb("cdidx_references_zero_json_missing_graph");
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Run", "--db", readOnlyUri, "--json", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.False(json.GetProperty("exact_index_available").GetBoolean());
            Assert.Contains("symbol_references table missing", json.GetProperty("degraded_reason").GetString());
            Assert.Equal(0, json.GetProperty("references").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallers_JsonZeroResults_WithMissingGraphTable_ReturnsDegradedPayload()
    {
        var (projectRoot, readOnlyUri) = CreateReadOnlyMissingGraphTableDb("cdidx_callers_zero_json_missing_graph");
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallers(
                ["Run", "--db", readOnlyUri, "--json", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.False(json.GetProperty("exact_index_available").GetBoolean());
            Assert.Contains("symbol_references table missing", json.GetProperty("degraded_reason").GetString());
            Assert.Equal(0, json.GetProperty("callers").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallers_ExactJson_FindsTernaryContinuationCallSite()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_callers_csharp_ternary");
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(
                Path.Combine(projectRoot, "src", "dispatcher.cs"),
                """
                public class Dispatcher
                {
                    private string Select(bool isUpdate)
                        => isUpdate
                            ? RunUpdateMode()
                            : RunFullScan();

                    private string RunUpdateMode() => "update";
                    private string RunFullScan() => "full";
                }
                """);

            var (indexExitCode, _, indexStderr) = CaptureConsole(() => IndexCommandRunner.Run(
                [projectRoot, "--json"],
                _jsonOptions));

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallers(
                ["RunUpdateMode", "--db", Path.Combine(projectRoot, ".cdidx", "codeindex.db"), "--json", "--exact", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, indexExitCode);
            Assert.Equal(string.Empty, indexStderr);
            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("src/dispatcher.cs", json.GetProperty("path").GetString());
            Assert.Equal("class", json.GetProperty("caller_kind").GetString());
            Assert.Equal("Dispatcher", json.GetProperty("caller_name").GetString());
            Assert.Equal("RunUpdateMode", json.GetProperty("callee_name").GetString());
            Assert.Equal(5, json.GetProperty("first_line").GetInt32());
            Assert.Equal(1, json.GetProperty("reference_count").GetInt32());
            Assert.True(json.GetProperty("exact_index_available").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallees_JsonZeroResults_WithMissingGraphTable_ReturnsDegradedPayload()
    {
        var (projectRoot, readOnlyUri) = CreateReadOnlyMissingGraphTableDb("cdidx_callees_zero_json_missing_graph");
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallees(
                ["Run", "--db", readOnlyUri, "--json", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.False(json.GetProperty("exact_index_available").GetBoolean());
            Assert.Contains("symbol_references table missing", json.GetProperty("degraded_reason").GetString());
            Assert.Equal(0, json.GetProperty("callees").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonIncludesConfidenceBuckets()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var symbols = json.GetProperty("symbols");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(9, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("likely_unused_private").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("maybe_unused_nonpublic").GetInt32());
            Assert.Equal(6, json.GetProperty("returned_bucket_counts").GetProperty("public_or_exported_no_refs").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("reflection_or_config_suspect").GetInt32());
            Assert.Equal("Hidden", symbols[0].GetProperty("name").GetString());
            Assert.Equal("likely_unused_private", symbols[0].GetProperty("unused_bucket").GetString());
            Assert.Equal("medium", symbols[0].GetProperty("unused_confidence").GetString());
            Assert.Equal("PathResolver", symbols[2].GetProperty("name").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols[2].GetProperty("unused_bucket").GetString());
            Assert.Equal("ConnectionString", symbols[3].GetProperty("name").GetString());
            Assert.Equal("reflection_or_config_suspect", symbols[3].GetProperty("unused_bucket").GetString());
            Assert.Equal("ApplyConfiguration", symbols[7].GetProperty("name").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols[7].GetProperty("unused_bucket").GetString());
            Assert.Equal("UseIOptions", symbols[8].GetProperty("name").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols[8].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonUsesReturnedBucketCountsForCurrentPage()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--limit", "2"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2, json.GetProperty("count").GetInt32());
            Assert.True(json.TryGetProperty("returned_bucket_counts", out var returnedBucketCounts));
            Assert.False(json.TryGetProperty("bucket_counts", out _));
            Assert.Equal(1, returnedBucketCounts.GetProperty("likely_unused_private").GetInt32());
            Assert.Equal(1, returnedBucketCounts.GetProperty("maybe_unused_nonpublic").GetInt32());
            Assert.False(returnedBucketCounts.TryGetProperty("public_or_exported_no_refs", out _));
            Assert.False(returnedBucketCounts.TryGetProperty("reflection_or_config_suspect", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonDiversifiesBucketsBeforeLimit()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--limit", "4"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var symbols = json.GetProperty("symbols");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(["Hidden", "InternalOnly", "PathResolver", "ConnectionString"], symbols.EnumerateArray().Select(symbol => symbol.GetProperty("name").GetString()).ToArray());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("likely_unused_private").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("maybe_unused_nonpublic").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("public_or_exported_no_refs").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("reflection_or_config_suspect").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMarksReflectionAttributedPropertyAsSuspect()
    {
        var (projectRoot, dbPath) = CreateReflectionUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var symbols = json.GetProperty("symbols");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("UserDto", symbols[0].GetProperty("name").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols[0].GetProperty("unused_bucket").GetString());
            Assert.Equal("FullName", symbols[1].GetProperty("name").GetString());
            Assert.Equal("reflection_or_config_suspect", symbols[1].GetProperty("unused_bucket").GetString());
            Assert.Contains("attribute-driven reflection surface", symbols[1].GetProperty("unused_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMarksCommentSeparatedReflectionAttributeAsSuspect()
    {
        var (projectRoot, dbPath) = CreateReflectionCommentedUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var symbols = json.GetProperty("symbols");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("FullName", symbols[1].GetProperty("name").GetString());
            Assert.Equal("reflection_or_config_suspect", symbols[1].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMarksQualifiedAndSuffixedAttributesAsSuspect()
    {
        var (projectRoot, dbPath) = CreateQualifiedReflectionUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var symbols = document.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(symbol => symbol.GetProperty("name").GetString()!, StringComparer.Ordinal);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("reflection_or_config_suspect", symbols["QualifiedName"].GetProperty("unused_bucket").GetString());
            Assert.Equal("reflection_or_config_suspect", symbols["SuffixedName"].GetProperty("unused_bucket").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols["IgnoredName"].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonKeepsPlainCliOptionsPropertiesInPublicBucket()
    {
        var (projectRoot, dbPath) = CreatePlainCliOptionsUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var symbols = document.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(symbol => symbol.GetProperty("name").GetString()!, StringComparer.Ordinal);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("public_or_exported_no_refs", symbols["ShowHelp"].GetProperty("unused_bucket").GetString());
            Assert.Equal("public_or_exported_no_refs", symbols["ProjectPath"].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMarksBlockCommentSeparatedReflectionAttributeAsSuspect()
    {
        var (projectRoot, dbPath) = CreateBlockCommentReflectionUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var symbols = document.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(symbol => symbol.GetProperty("name").GetString()!, StringComparer.Ordinal);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("reflection_or_config_suspect", symbols["FullName"].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonDiversifiesReflectionSuspectBeforeLimit()
    {
        var (projectRoot, dbPath) = CreateReflectionDiversifiedUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--limit", "4"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var symbols = json.GetProperty("symbols");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(["Hidden", "InternalOnly", "UserDto", "FullName"], symbols.EnumerateArray().Select(symbol => symbol.GetProperty("name").GetString()).ToArray());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("likely_unused_private").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("maybe_unused_nonpublic").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("public_or_exported_no_refs").GetInt32());
            Assert.Equal(1, json.GetProperty("returned_bucket_counts").GetProperty("reflection_or_config_suspect").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonIncludesGraphSupportMetadataForUnsupportedLanguage()
    {
        var (projectRoot, dbPath) = CreateUnsupportedLanguageUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "shell"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.False(json.GetProperty("graph_supported").GetBoolean());
            Assert.Contains("not indexed", json.GetProperty("graph_support_reason").GetString());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("symbols").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_CountJsonUnsupportedLanguage_ReturnsZero()
    {
        var (projectRoot, dbPath) = CreateUnsupportedLanguageUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "shell", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.False(json.GetProperty("graph_supported").GetBoolean());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonZeroResults_UsesUnusedSchema()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--path", "does-not-exist"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.True(json.GetProperty("graph_supported").GetBoolean());
            Assert.Contains("indexed", json.GetProperty("graph_support_reason").GetString());
            Assert.True(json.TryGetProperty("symbols", out var symbols));
            Assert.Equal(0, symbols.GetArrayLength());
            Assert.True(json.TryGetProperty("returned_bucket_counts", out var bucketCounts));
            Assert.Empty(bucketCounts.EnumerateObject());
            Assert.False(json.TryGetProperty("unused", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonUnsupportedLanguageZeroResults_UsesUnusedSchema()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "markdown"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("graph_supported").GetBoolean());
            Assert.Contains("not indexed", json.GetProperty("graph_support_reason").GetString());
            Assert.Equal(0, json.GetProperty("symbols").GetArrayLength());
            Assert.Empty(json.GetProperty("returned_bucket_counts").EnumerateObject());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMissingGraphTable_UsesUnusedSchema()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_missing_graph_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.TryGetProperty("symbols", out var symbols));
            Assert.Equal(0, symbols.GetArrayLength());
            Assert.True(json.TryGetProperty("returned_bucket_counts", out var bucketCounts));
            Assert.Empty(bucketCounts.EnumerateObject());
            Assert.False(json.TryGetProperty("unused", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_CountJson_DoesNotNeedChunksForReflectionClassification()
    {
        var (projectRoot, dbPath) = CreateReflectionUnusedFixtureDb();
        try
        {
            using (var db = new DbContext(dbPath))
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE chunks;";
                cmd.ExecuteNonQuery();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("files").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonMissingChunks_DegradesReflectionClassificationWithoutCrashing()
    {
        var (projectRoot, dbPath) = CreateReflectionUnusedFixtureDb();
        try
        {
            using (var db = new DbContext(dbPath))
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE chunks;";
                cmd.ExecuteNonQuery();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var symbols = document.RootElement.GetProperty("symbols").EnumerateArray()
                .ToDictionary(symbol => symbol.GetProperty("name").GetString()!, StringComparer.Ordinal);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("public_or_exported_no_refs", symbols["FullName"].GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_CountHumanMissingGraphTable_WarnsDegradedZero()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_missing_graph_count_human");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--lang", "csharp", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.Contains("degraded", stderr);
            Assert.Contains("symbol_references table missing", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithInlineAttributedProperty_ClassifiesPropertyAsReflectionSuspect()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_inline_attr_property");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/user_dto.cs",
                "csharp",
                """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
                }
                """);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var symbols = document.RootElement.GetProperty("symbols").EnumerateArray().ToList();
            var fullName = Assert.Single(symbols, symbol => symbol.GetProperty("name").GetString() == "FullName");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("reflection_or_config_suspect", fullName.GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_ExactNameAliasMatchesBackwardCompatibleExact()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_symbols_exact_alias");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "public class App\n{\n    public void Run() { }\n    public void RunAsync() { }\n}\n");

            var exact = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["Run", "--db", dbPath, "--json", "--exact"],
                _jsonOptions));
            var alias = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["Run", "--db", dbPath, "--json", "--exact-name"],
                _jsonOptions));

            Assert.Equal(exact.Result, alias.Result);
            Assert.Equal(exact.Stdout, alias.Stdout);
            Assert.Equal(exact.Stderr, alias.Stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithPropertyTargetWhitespaceInlineAttribute_ClassifiesPropertyAsReflectionSuspect()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_property_target_inline_attr");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/user_dto.cs",
                "csharp",
                """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [property : JsonPropertyName("full_name")] public string FullName { get; set; } = string.Empty;
                }
                """);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var fullName = Assert.Single(
                document.RootElement.GetProperty("symbols").EnumerateArray(),
                symbol => symbol.GetProperty("name").GetString() == "FullName");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("reflection_or_config_suspect", fullName.GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithPropertyTargetWhitespaceMultilineAttribute_ClassifiesPropertyAsReflectionSuspect()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_property_target_multiline_attr");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/user_dto.cs",
                "csharp",
                """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [property : JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var fullName = Assert.Single(
                document.RootElement.GetProperty("symbols").EnumerateArray(),
                symbol => symbol.GetProperty("name").GetString() == "FullName");

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("reflection_or_config_suspect", fullName.GetProperty("unused_bucket").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_WithJsonLargePublicLimit_IsNotCappedAtBudget()
    {
        var (projectRoot, dbPath) = CreateLargePublicUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--json", "--lang", "csharp", "--limit", "3000"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2500, json.GetProperty("count").GetInt32());
            Assert.Equal(2500, json.GetProperty("symbols").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunUnused_HumanOutputGroupsByConfidenceBucket()
    {
        var (projectRoot, dbPath) = CreateUnusedFixtureDb();
        try
        {
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunUnused(
                ["--db", dbPath, "--lang", "csharp"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Likely unused private (1)", stdout);
            Assert.Contains("Maybe unused non-public (1)", stdout);
            Assert.Contains("Public/exported with no refs (6)", stdout);
            Assert.Contains("Reflection/config suspects (1)", stdout);
            Assert.Contains("confidence=medium", stdout);
            Assert.Contains("confidence=low", stdout);
            Assert.Contains("returned potentially unused symbols; returned buckets:", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_RejectsExactSubstringAlias()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_symbols_wrong_exact_alias");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["Run", "--db", dbPath, "--exact-substring"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--exact-name", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_WithJsonOutputsCompactSnippetMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "line 1\nline 2\nline 3\nTarget();\nline 5\nline 6");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["Target", "--db", dbPath, "--json", "--snippet-lines", "3"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("src/app.cs", json.GetProperty("path").GetString());
            Assert.Equal(1, json.GetProperty("chunk_start_line").GetInt32());
            Assert.Equal(6, json.GetProperty("chunk_end_line").GetInt32());
            Assert.Equal(3, json.GetProperty("snippet_start_line").GetInt32());
            Assert.Equal(5, json.GetProperty("snippet_end_line").GetInt32());
            Assert.Contains("Target();", json.GetProperty("snippet").GetString());
            Assert.Equal(4, json.GetProperty("match_lines")[0].GetInt32());
            Assert.Equal(1, json.GetProperty("highlights").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ExactSubstringAliasMatchesBackwardCompatibleExact()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_exact_alias");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "void Run() { }\nvoid RunAsync() { Run(); }\nvoid run() { }\n");

            var exact = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["Run();", "--db", dbPath, "--json", "--exact"],
                _jsonOptions));
            var alias = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["Run();", "--db", dbPath, "--json", "--exact-substring"],
                _jsonOptions));

            Assert.Equal(exact.Result, alias.Result);
            Assert.Equal(exact.Stdout, alias.Stdout);
            Assert.Equal(exact.Stderr, alias.Stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ExactSubstringHumanSnippetUsesCaseSensitiveFocusLine()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_exact_human_snippet");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "void run() { }\nvoid Run() { }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["Run()", "--db", dbPath, "--exact-substring", "--snippet-lines", "1"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("src/app.cs:", stdout);
            Assert.Contains("  void Run() { }", stdout);
            Assert.DoesNotContain("  void run() { }", stdout);
            Assert.Contains("(1 results in 1 files)", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_RejectsExactNameAlias()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_wrong_exact_alias");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["Run", "--db", dbPath, "--exact-name"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--exact-substring", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_MissingQueryUsageMentionsExactSubstringAlias()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch([], _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--exact|--exact-substring", stderr);
    }

    [Fact]
    public void RunDefinition_MissingQueryUsageMentionsExactNameAlias()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunDefinition([], _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--exact|--exact-name", stderr);
    }

    [Theory]
    [InlineData("search", "results")]
    [InlineData("files", "files")]
    public void ZeroResultJson_SymbolAndTextCommands_EmitEnvelopeAndFreshness(string command, string resultsKey)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_zero_json_{command}");
        try
        {
            var dbPath = CreateIndexedDbWithSingleFile(projectRoot);
            var (exitCode, stdout, stderr) = CaptureConsole(() => RunZeroResultCommand(command, dbPath));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertZeroResultPayload(json, resultsKey);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("references")]
    [InlineData("callers")]
    [InlineData("callees")]
    public void ZeroResultJson_GraphCommands_KeepAuthoritativeZeroStdoutSilent(string command)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_zero_json_{command}");
        try
        {
            var dbPath = CreateIndexedDbWithSingleFile(projectRoot, markGraphReady: true);
            var (exitCode, stdout, stderr) = CaptureConsole(() => RunZeroResultCommand(command, dbPath));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(string.Empty, stdout);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("deps", "edges")]
    [InlineData("hotspots", "hotspots")]
    public void ZeroResultJson_AggregateCommands_EmitEnvelopeAndFreshness(string command, string resultsKey)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_zero_json_{command}");
        try
        {
            var dbPath = CreateIndexedDbWithSingleFile(projectRoot, markGraphReady: true);
            var (exitCode, stdout, stderr) = CaptureConsole(() => RunZeroResultCommand(command, dbPath));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertZeroResultPayload(json, resultsKey);
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunHotspots_ZeroJson_ReportsDegradedHotspotFamilyTrust()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_hotspots_family_zero_json");
        try
        {
            var dbPath = CreateHotspotFamilyFixtureDb(projectRoot, markHotspotFamilyReady: false);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunHotspots(
                ["--db", dbPath, "--json", "--lang", "csharp", "--kind", "function"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("hotspot_family_ready").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.Contains("csharp", json.GetProperty("hotspot_family_degraded_reason").GetString());
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunHotspots_ZeroJson_ReportsLegacyNullFamilyKeysAsDegraded()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_hotspots_family_legacy_zero_json");
        try
        {
            var dbPath = CreateHotspotFamilyFixtureDb(projectRoot, markHotspotFamilyReady: true);
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = """
                    UPDATE symbols
                    SET family_key = NULL,
                        container_qualified_name = NULL
                    WHERE file_id IN (
                        SELECT id FROM files WHERE lang = 'csharp'
                    );
                    """;
                cmd.ExecuteNonQuery();

                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.GetHotspotFamilyVersionMetaKey("csharp"), null);
                writer.SetMeta(DbContext.GetHotspotFamilyMarkerFingerprintMetaKey("csharp"), null);
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunHotspots(
                ["--db", dbPath, "--json", "--lang", "csharp", "--kind", "function"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.False(json.GetProperty("hotspot_family_ready").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.Contains("csharp", json.GetProperty("hotspot_family_degraded_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunHotspots_ZeroJson_ReportsMissingMarkerFingerprintAsDegraded()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_hotspots_family_missing_fingerprint_zero_json");
        try
        {
            var dbPath = CreateHotspotFamilyFixtureDb(projectRoot, markHotspotFamilyReady: true);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.GetHotspotFamilyMarkerFingerprintMetaKey("csharp"), null);
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunHotspots(
                ["--db", dbPath, "--json", "--lang", "csharp", "--kind", "function"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("hotspot_family_ready").GetBoolean());
            Assert.True(json.GetProperty("degraded").GetBoolean());
            Assert.Contains("csharp", json.GetProperty("hotspot_family_degraded_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunHotspots_HumanOutput_WarnsWhenHotspotFamilyTrustIsDegraded()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_hotspots_family_zero_human");
        try
        {
            var dbPath = CreateHotspotFamilyFixtureDb(projectRoot, markHotspotFamilyReady: false);
            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunHotspots(
                ["--db", dbPath, "--lang", "csharp", "--kind", "function"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("cross-file hotspot family grouping", stderr);
            Assert.Contains("authoritative cross-file hotspot families", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ZeroJson_EmitsEnvelopeAndFreshness()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_zero_json_impact");
        try
        {
            var dbPath = CreateIndexedDbWithSingleFile(projectRoot, markGraphReady: true);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["DefinitelyMissingSymbol", "--db", dbPath, "--json", "--depth", "3"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertZeroResultPayload(json, "callers");
            Assert.Equal("DefinitelyMissingSymbol", json.GetProperty("query").GetString());
            Assert.Equal(3, json.GetProperty("max_depth").GetInt32());
            Assert.False(json.GetProperty("truncated").GetBoolean());
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroJson_OnEmptyIndex_EmitsNullIndexedAt()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_zero_json_empty_index");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["definitely-missing-path", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetArrayLength());
            Assert.Equal(0, json.GetProperty("indexed_file_count").GetInt64());
            Assert.True(json.GetProperty("freshness_available").GetBoolean());
            Assert.True(json.TryGetProperty("indexed_at", out var indexedAt));
            Assert.Equal(JsonValueKind.Null, indexedAt.ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroJson_OnLegacyReadOnlyDb_EmitsFreshnessDegradedSignal()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_zero_json_legacy_freshness");
        try
        {
            var dbPath = CreateLegacyDbWithoutIndexedAt(projectRoot);
            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["definitely-missing-path", "--db", readOnlyUri, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetArrayLength());
            Assert.Equal(1, json.GetProperty("indexed_file_count").GetInt64());
            Assert.False(json.GetProperty("freshness_available").GetBoolean());
            Assert.Contains("files.indexed_at column missing", json.GetProperty("freshness_degraded_reason").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("indexed_at").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ZeroResultJson_EmitsStructuredPayloadWithFreshnessHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_zero_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "class App { void Target() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["MissingTarget", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("results").GetArrayLength());
            Assert.Equal(1, json.GetProperty("indexed_file_count").GetInt32());
            Assert.True(json.TryGetProperty("indexed_at", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ZeroResultJson_EmptyIndexEmitsNullIndexedAt()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_zero_json_empty");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["MissingTarget", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("results").GetArrayLength());
            Assert.Equal(0, json.GetProperty("indexed_file_count").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("indexed_at").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ZeroResultJson_CountOnlyEmitsFreshnessHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_zero_json_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                "class App { void Target() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["MissingTarget", "--db", dbPath, "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetInt32());
            Assert.Equal(1, json.GetProperty("indexed_file_count").GetInt32());
            Assert.True(json.TryGetProperty("indexed_at", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_ZeroResultJson_CountOnlyEmptyIndexEmitsNullIndexedAt()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_search_zero_json_count_empty");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
                ["MissingTarget", "--db", dbPath, "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetInt32());
            Assert.Equal(0, json.GetProperty("indexed_file_count").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("indexed_at").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_UnsupportedLanguageWithoutMatches_PrintsGraphSupportHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_refs");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["MissingSymbol", "--db", dbPath, "--lang", "markdown"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("No references found.", stderr);
            Assert.Contains("call-graph queries are not indexed for 'markdown'", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("references", "MissingSymbol")]
    [InlineData("callers", "MissingSymbol")]
    [InlineData("callees", "MissingSymbol")]
    public void GraphCommands_SymbolKindArgumentWarnsAboutReferenceKindSemantics(string command, string query)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_{command}_kind_warning");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, _, stderr) = CaptureConsole(() => RunGraphCommand(
                command,
                [query, "--db", dbPath, "--kind", "class"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("symbol kind", stderr);
            Assert.Contains("filters by reference kind", stderr);
            Assert.Contains("call, instantiate, subscribe", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_CountJsonKeepsSubscribeRowsVisibleByDefault()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_references_subscribe_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Publisher.cs", "csharp",
                """
                using System;

                public class Publisher
                {
                    public event EventHandler? Changed;
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Subscriber.cs", "csharp",
                """
                using System;

                public class Subscriber
                {
                    public void Hook(Publisher publisher)
                    {
                        publisher.Changed += OnChanged;
                    }

                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Changed", "--db", dbPath, "--json", "--count", "--lang", "csharp", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("files").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallers_JsonKeepsSubscribeRowsVisibleByDefault()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_callers_subscribe_default");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Publisher.cs", "csharp",
                """
                using System;

                public class Publisher
                {
                    public event EventHandler? Changed;
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Subscriber.cs", "csharp",
                """
                using System;

                public class Subscriber
                {
                    public void Hook(Publisher publisher)
                    {
                        publisher.Changed += OnChanged;
                    }

                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallers(
                ["Changed", "--db", dbPath, "--json", "--lang", "csharp", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Hook", json.GetProperty("caller_name").GetString());
            Assert.Equal("Changed", json.GetProperty("callee_name").GetString());
            Assert.Equal(1, json.GetProperty("reference_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallees_JsonKeepsSubscribeRowsVisibleByDefault()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_callees_subscribe_default");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Publisher.cs", "csharp",
                """
                using System;

                public class Publisher
                {
                    public event EventHandler? Changed;
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Subscriber.cs", "csharp",
                """
                using System;

                public class Subscriber
                {
                    public void Hook(Publisher publisher)
                    {
                        publisher.Changed += OnChanged;
                    }

                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallees(
                ["Hook", "--db", dbPath, "--json", "--lang", "csharp", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Hook", json.GetProperty("caller_name").GetString());
            Assert.Equal("Changed", json.GetProperty("callee_name").GetString());
            Assert.Equal("subscribe", json.GetProperty("reference_kind").GetString());
            Assert.Equal(1, json.GetProperty("reference_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_JsonKeepsSubscribeReferencesVisibleInBundle()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_subscribe_bundle");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Publisher.cs", "csharp",
                """
                using System;

                public class Publisher
                {
                    public event EventHandler? Changed;
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Subscriber.cs", "csharp",
                """
                using System;

                public class Subscriber
                {
                    public void Hook(Publisher publisher)
                    {
                        publisher.Changed += OnChanged;
                    }

                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Changed", "--db", dbPath, "--json", "--lang", "csharp", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var reference = Assert.Single(json.GetProperty("references").EnumerateArray());
            var caller = Assert.Single(json.GetProperty("callers").EnumerateArray());

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("subscribe", reference.GetProperty("reference_kind").GetString());
            Assert.Equal("Hook", reference.GetProperty("container_name").GetString());
            Assert.Equal("Hook", caller.GetProperty("caller_name").GetString());
            Assert.Equal("Changed", caller.GetProperty("callee_name").GetString());
            Assert.Empty(json.GetProperty("callees").EnumerateArray());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_JsonKeepsSubscribeCalleesVisibleForCallerSymbols()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_subscribe_callee_bundle");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Publisher.cs", "csharp",
                """
                using System;

                public class Publisher
                {
                    public event EventHandler? Changed;
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Subscriber.cs", "csharp",
                """
                using System;

                public class Subscriber
                {
                    public void Hook(Publisher publisher)
                    {
                        publisher.Changed += OnChanged;
                    }

                    private void OnChanged(object? sender, EventArgs e) { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Hook", "--db", dbPath, "--json", "--lang", "csharp", "--exact"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;
            var callee = Assert.Single(json.GetProperty("callees").EnumerateArray());

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Hook", callee.GetProperty("caller_name").GetString());
            Assert.Equal("Changed", callee.GetProperty("callee_name").GetString());
            Assert.Equal("subscribe", callee.GetProperty("reference_kind").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ClassSymbolJsonReturnsHeuristicFileDependencyHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_class_fallback");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FolderDiffService.cs", "csharp",
                """
                public class FolderDiffService
                {
                    public void ExecuteFolderDiffAsync() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Run(FolderDiffService service)
                    {
                        service.ExecuteFolderDiffAsync();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FolderDiffService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("heuristic").GetBoolean());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("confirmed_count").GetInt32());
            Assert.Equal(0, json.GetProperty("confirmed_file_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_file_count").GetInt32());
            Assert.False(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.True(json.GetProperty("has_class_like_definitions").GetBoolean());
            Assert.Equal("src/App.cs", json.GetProperty("file_impacts")[0].GetProperty("source_path").GetString());
            Assert.Equal("src/FolderDiffService.cs", json.GetProperty("file_impacts")[0].GetProperty("target_path").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ClassAndNamespaceWithSameNameJsonStillReturnsHeuristicHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_namespace_sibling");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                namespace FooService;

                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("heuristic").GetBoolean());
            Assert.True(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal(2, json.GetProperty("definition_count").GetInt32());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("confirmed_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_HeuristicHintsCountOnlyJsonUsesVisibleResultCount()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_hint_count_only");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FolderDiffService.cs", "csharp",
                """
                public class FolderDiffService
                {
                    public void ExecuteFolderDiffAsync() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Run(FolderDiffService service)
                    {
                        service.ExecuteFolderDiffAsync();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FolderDiffService", "--db", dbPath, "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("file_count").GetInt32());
            Assert.Equal(0, json.GetProperty("confirmed_count").GetInt32());
            Assert.Equal(0, json.GetProperty("confirmed_file_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_FoldEquivalentClassDefinitionsJsonReportAmbiguity()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_fold_siblings");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FullwidthFooService.cs", "csharp",
                """
                public class ＦｏｏＳｅｒｖｉｃｅ
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.Equal(2, json.GetProperty("definition_count").GetInt32());
            Assert.Equal("multiple_definition_files", json.GetProperty("zero_result_reason").GetString());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_PartialClassJsonReturnsResolutionHintPayload()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_partial_hint");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Worker.Part1.cs", "csharp",
                """
                public partial class Worker
                {
                    public void Start() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Worker.Part2.cs", "csharp",
                """
                public partial class Worker
                {
                    public void Stop() { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["Worker", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.True(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.True(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal("multiple_definition_files", json.GetProperty("zero_result_reason").GetString());
            Assert.Contains("deps --path <definition-path> --reverse", json.GetProperty("suggestion").GetString());
            Assert.Equal(2, json.GetProperty("definition_file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ClassCollisionWithoutTypeEvidenceReturnsNoHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_collision");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/BarService.cs", "csharp",
                """
                public class BarService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(BarService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.False(json.GetProperty("heuristic").GetBoolean());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
            Assert.Equal(0, json.GetProperty("file_impacts").GetArrayLength());
            Assert.Equal("class_symbol_no_symbol_callers", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_CommentOnlyTypeMentionDoesNotProduceHeuristicHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_comment_only_type_name");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/OtherService.cs", "csharp",
                """
                public class OtherService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(OtherService service)
                    {
                        service.Run(); // TODO: maybe replace with FooService later
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
            Assert.Equal("class_symbol_no_symbol_callers", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_StringLiteralTypeMentionDoesNotProduceHeuristicHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_string_only_type_name");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Execute() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Worker.cs", "csharp",
                """
                public class Worker
                {
                    public void Execute() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(Worker worker)
                    {
                        var label = "FooService";
                        worker.Execute();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
            Assert.Equal("class_symbol_no_symbol_callers", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_NamespaceJsonDoesNotFallbackToFileDependencies()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_namespace");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Services.cs", "csharp",
                """
                namespace Acme;

                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                namespace Acme;

                public class App
                {
                    public void Boot(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["Acme", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal("non_callable_symbol_kind", json.GetProperty("zero_result_reason").GetString());
            Assert.Equal(0, json.GetProperty("file_impacts").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ImportOnlyQueryJsonReportsNonCallableSymbolKind()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_import_only");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.py", "python",
                """
                import requests
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["requests", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("definition_count").GetInt32());
            Assert.Equal("non_callable_symbol_kind", json.GetProperty("zero_result_reason").GetString());
            Assert.Contains("definition <symbol>", json.GetProperty("suggestion").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ZeroResultJsonPayloadRemainsStableAcrossRepeatedTempProjects()
    {
        for (var iteration = 0; iteration < 10; iteration++)
        {
            RunImpactPartialClassZeroResultIteration(iteration);
            RunImpactImportOnlyZeroResultIteration(iteration);
        }
    }

    [Fact]
    public void RunImpact_UnicodeTypeEvidenceStillReturnsHeuristicHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_unicode_type_evidence");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/ＦｏｏＳｅｒｖｉｃｅ.cs", "csharp",
                """
                public class ＦｏｏＳｅｒｖｉｃｅ
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(ＦｏｏＳｅｒｖｉｃｅ service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["ＦｏｏＳｅｒｖｉｃｅ", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
            Assert.Equal("src/App.cs", json.GetProperty("file_impacts")[0].GetProperty("source_path").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ExcludeTestsJsonIgnoresOutOfScopeDuplicateDefinitions()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_exclude_tests_duplicate");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "tests/FooServiceTests.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--exclude-tests", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("heuristic").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal(1, json.GetProperty("definition_file_count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal("src/FooService.cs", json.GetProperty("definitions")[0].GetProperty("path").GetString());
            Assert.Equal("src/App.cs", json.GetProperty("file_impacts")[0].GetProperty("source_path").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_UnsupportedLanguageDuplicateDoesNotTriggerMultipleDefinitions()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_unsupported_duplicate");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/tools.sh", "shell",
                """
                FooService() {
                  :
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("heuristic").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal(1, json.GetProperty("definition_file_count").GetInt32());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal("src/FooService.cs", json.GetProperty("definitions")[0].GetProperty("path").GetString());
            Assert.Equal("src/App.cs", json.GetProperty("file_impacts")[0].GetProperty("source_path").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_ExactDefinitionResolutionSkipsUnsupportedMatchesBeforeLimit()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_unsupported_overflow");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            for (int i = 0; i < 60; i++)
            {
                TestProjectHelper.InsertIndexedFile(dbPath, $"scripts/Foo{i:D2}.sh", "shell",
                    """
                    Foo() {
                      :
                    }
                    """);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Foo.cs", "csharp",
                """
                public class Foo
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(Foo service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["Foo", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("definition_count").GetInt32());
            Assert.Equal("src/Foo.cs", json.GetProperty("definitions")[0].GetProperty("path").GetString());
            Assert.Equal("src/App.cs", json.GetProperty("file_impacts")[0].GetProperty("source_path").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_SubstringTypeEvidenceDoesNotProduceHeuristicHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_substring_type_evidence");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Foo.cs", "csharp",
                """
                public class Foo
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FooService.cs", "csharp",
                """
                public class FooService
                {
                    public void Run() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Handle(FooService service)
                    {
                        service.Run();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["Foo", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
            Assert.Equal("class_symbol_no_symbol_callers", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_DuplicateDefinitionsInOneFileJsonReportsAmbiguity()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_same_file_duplicate");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Services.cs", "csharp",
                """
                namespace A
                {
                    public class FooService
                    {
                        public void Run() { }
                    }
                }

                namespace B
                {
                    public class FooService
                    {
                        public void Run() { }
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(2, json.GetProperty("definition_count").GetInt32());
            Assert.Equal(1, json.GetProperty("definition_file_count").GetInt32());
            Assert.True(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.False(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal("multiple_definitions", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_DuplicateDefinitionsInOneFileHumanOutputMentionsDefinitionAndFileCounts()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_same_file_duplicate_human");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Services.cs", "csharp",
                """
                namespace A
                {
                    public class FooService
                    {
                        public void Run() { }
                    }
                }

                namespace B
                {
                    public class FooService
                    {
                        public void Run() { }
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FooService", "--db", dbPath],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.DoesNotContain("file_dependency_hints", stdout);
            Assert.Contains("2 definition(s) across 1 file(s)", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_HeuristicHintsJsonSetTruncatedAndReturnSuccess()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_hint_truncated");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FolderDiffService.cs", "csharp",
                """
                public class FolderDiffService
                {
                    public void ExecuteFolderDiffAsync() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App1.cs", "csharp",
                """
                public class App1
                {
                    public void Boot(FolderDiffService service)
                    {
                        service.ExecuteFolderDiffAsync();
                    }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App2.cs", "csharp",
                """
                public class App2
                {
                    public void Boot(FolderDiffService service)
                    {
                        service.ExecuteFolderDiffAsync();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FolderDiffService", "--db", dbPath, "--limit", "1", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("truncated").GetBoolean());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("hint_count").GetInt32());
            Assert.Equal(1, json.GetProperty("file_impacts").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_HeuristicHintsJsonKeepActualReferenceCount()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_hint_refcount");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FolderDiffService.cs", "csharp",
                """
                public class FolderDiffService
                {
                    public void ExecuteFolderDiffAsync() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp",
                """
                public class App
                {
                    public void Boot(FolderDiffService service)
                    {
                        service.ExecuteFolderDiffAsync();
                        service.ExecuteFolderDiffAsync();
                        service.ExecuteFolderDiffAsync();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FolderDiffService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("file_dependency_hints", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal(3, json.GetProperty("file_impacts")[0].GetProperty("reference_count").GetInt32());
            Assert.Equal("ExecuteFolderDiffAsync", json.GetProperty("file_impacts")[0].GetProperty("symbols").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunImpact_UnresolvedExternalCallWithoutTypeEvidenceReturnsNoHints()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_impact_unresolved_external");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/FolderDiffService.cs", "csharp",
                """
                public class FolderDiffService
                {
                    public void ExecuteFolderDiffAsync() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/ExternalConsumer.cs", "csharp",
                """
                public class ExternalConsumer
                {
                    public void Boot()
                    {
                        ExecuteFolderDiffAsync();
                    }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["FolderDiffService", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(0, json.GetProperty("hint_count").GetInt32());
            Assert.Equal("class_symbol_no_symbol_callers", json.GetProperty("zero_result_reason").GetString());
            Assert.Equal(0, json.GetProperty("file_impacts").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void BuildExactZeroHint_NoRelaxedMatch_DoesNotRunSampleQuery()
    {
        var sampleInvoked = false;

        var result = QueryCommandRunner.BuildExactZeroHint(
            shouldProbe: true,
            anyRelaxedMatch: () => false,
            relaxedCountQuery: () => throw new InvalidOperationException("count should not run"),
            relaxedSampleQuery: () =>
            {
                sampleInvoked = true;
                return new List<string> { "should_not_run" };
            },
            nameSelector: name => name);

        Assert.Null(result);
        Assert.False(sampleInvoked);
    }

    [Fact]
    public void RunDefinition_ExactZeroJson_ReturnsEmptyStdout()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_definition_exact_zero");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void HandleRequest() { }
                    public void HandleRequestAsync() { HandleRequest(); }
                }
                """);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunDefinition(
                ["Handle", "--db", dbPath, "--json", "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunDefinition_ExactZeroJson_PreservesRelaxedCountAndCapsSamplesToFive()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_definition_exact_zero_cap");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void HandleRequest1() { }
                    public void HandleRequest2() { }
                    public void HandleRequest3() { }
                    public void HandleRequest4() { }
                    public void HandleRequest5() { }
                    public void HandleRequest6() { }
                    public void HandleRequest7() { }
                }
                """);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunDefinition(
                ["Handle", "--db", dbPath, "--json", "--count", "--exact", "--limit", "99"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(7, json.GetProperty("exact_zero_hint").GetProperty("relaxed_count").GetInt32());
            Assert.Equal(5, json.GetProperty("exact_zero_hint").GetProperty("sample_names").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunDefinition_ExactZeroJson_RespectsRequestedLimitForRelaxedCount()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_definition_exact_zero_limit");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void HandleRequest1() { }
                    public void HandleRequest2() { }
                    public void HandleRequest3() { }
                }
                """);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunDefinition(
                ["Handle", "--db", dbPath, "--json", "--count", "--exact", "--limit", "1"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(1, json.GetProperty("exact_zero_hint").GetProperty("relaxed_count").GetInt32());
            Assert.Equal(1, json.GetProperty("exact_zero_hint").GetProperty("sample_names").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("references")]
    [InlineData("callers")]
    [InlineData("callees")]
    public void GraphCommands_ExactZeroJson_RespectRequestedLimitAndCapSamples(string command)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_query_runner_{command}_exact_zero_limit");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            SeedGraphExactZeroFixture(dbPath, command);

            var (exitCode, stdout, stderr) = CaptureConsole(() => RunGraphCommand(command,
                GetExactZeroArgs(command, dbPath, limit: 6, queryOverride: null, countOnly: true),
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(6, json.GetProperty("exact_zero_hint").GetProperty("relaxed_count").GetInt32());
            Assert.Equal(5, json.GetProperty("exact_zero_hint").GetProperty("sample_names").GetArrayLength());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("references")]
    [InlineData("callers")]
    [InlineData("callees")]
    public void GraphCommands_ExactZeroJson_OmitHintWhenRelaxedQueryStillReturnsZero(string command)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_query_runner_{command}_exact_zero_miss");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            SeedGraphExactZeroFixture(dbPath, command);

            var (exitCode, stdout, stderr) = CaptureConsole(() => RunGraphCommand(command,
                GetExactZeroArgs(command, dbPath, limit: 6, queryOverride: "DefinitelyMissing", countOnly: true),
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.TryGetProperty("exact_zero_hint", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_MultiNameExactZeroJson_OmitsRelaxedCountButReturnsSamples()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_symbols_multi_exact_zero");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void AlphaWorker() { }
                    public void BetaWorker() { }
                }
                """);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["Alpha", "Beta", "--db", dbPath, "--json", "--count", "--exact", "--limit", "999"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.False(json.GetProperty("exact_zero_hint").TryGetProperty("relaxed_count", out _));
            Assert.Contains("AlphaWorker", json.GetProperty("exact_zero_hint").GetProperty("sample_names").EnumerateArray().Select(e => e.GetString()));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_ExactOnReadOnlyLegacyDb_WarnsAboutMissingIndex()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_exact_warn");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def login(user, password):\n    return Run(user)\n");
            DropGraphExactFallbackIndexes(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Run", "--db", readOnlyUri, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("src/session.py:2:12", stdout);
            Assert.Contains("WARN: --exact graph query ran without the supporting index", stderr);
            Assert.Contains("idx_symbol_refs_name_nocase", stderr);
            Assert.Contains("re-index with `cdidx index <projectPath>`", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunCallees_ExactJsonOnReadOnlyLegacyDb_IncludesExactIndexSignal()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_exact_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def login(user, password):\n    return Run(user)\n");
            DropGraphExactFallbackIndexes(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunCallees(
                ["login", "--db", readOnlyUri, "--exact", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Run", json.GetProperty("callee_name").GetString());
            Assert.False(json.GetProperty("exact_index_available").GetBoolean());
            Assert.Contains("idx_symbol_refs_container_nocase", json.GetProperty("degraded_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_ExactOnReadOnlyLegacyDb_WarnsAboutMissingIndex()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_symbol_exact_warn");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def Run(user):\n    return user\n\ndef login(user, password):\n    return Run(user)\n");
            DropSymbolExactFallbackIndex(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["Run", "--db", readOnlyUri, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Run", stdout);
            Assert.Contains("WARN: --exact symbol query ran without the supporting index", stderr);
            Assert.Contains("idx_symbols_name_nocase", stderr);
            Assert.Contains("re-index with `cdidx index <projectPath>`", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSymbols_ExactWithoutQuery_OnReadOnlyLegacyDb_OmitsExactSignalAndWarning()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_symbol_exact_no_query");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def Run(user):\n    return user\n");
            DropSymbolExactFallbackIndex(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunSymbols(
                ["--db", readOnlyUri, "--exact", "--json", "--limit", "1"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Run", json.GetProperty("name").GetString());
            Assert.False(json.TryGetProperty("exact_index_available", out _));
            Assert.False(json.TryGetProperty("degraded_reason", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunDefinition_ExactJsonOnReadOnlyLegacyDb_IncludesExactIndexSignal()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_definition_exact_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def Run(user):\n    return user\n\ndef login(user, password):\n    return Run(user)\n");
            DropSymbolExactFallbackIndex(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunDefinition(
                ["Run", "--db", readOnlyUri, "--exact", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Run", json.GetProperty("name").GetString());
            Assert.False(json.GetProperty("exact_index_available").GetBoolean());
            Assert.Contains("idx_symbols_name_nocase", json.GetProperty("degraded_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_NonExactJsonOnReadOnlyLegacyDb_OmitsExactDegradedFields()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_nonexact");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def login(user, password):\n    return Run(user)\n");
            DropGraphExactFallbackIndexes(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Run", "--db", readOnlyUri, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.False(json.TryGetProperty("exact_index_available", out _));
            Assert.False(json.TryGetProperty("degraded_reason", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_WithJsonIncludesWorkspaceMetadataForCustomDbUnderCdidx()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_custom_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_custom_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "shared.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["App", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunInspect_WithJsonUsesProjectLocalCdidxPathForExplicitProjectDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_project_local_explicit");
        var staleRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_project_local_stale");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");
            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, staleRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["App", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(staleRoot);
        }
    }

    [Fact]
    public void RunInspect_WithJsonIncludesWorkspaceMetadataForExplicitExternalCodeIndexDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_codeindex_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_codeindex_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "codeindex.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["App", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunInspect_ExactOnReadOnlyLegacyDb_WithMissingSymbolFallbackIndex_WarnsAtBundleLevel()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_symbol_exact_warn");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/session.py",
                "python",
                "def Run(user):\n    return user\n\ndef login(user, password):\n    return Run(user)\n");
            ForceLegacyExactFallbackMode(dbPath);
            DropSymbolExactFallbackIndex(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Run", "--db", readOnlyUri, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Exact Index          : DEGRADED", stdout);
            Assert.Contains("idx_symbols_name_nocase", stdout);
            Assert.Contains("WARN: --exact inspect bundle ran without all supporting indexes", stderr);
            Assert.Contains("idx_symbols_name_nocase", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_ExactOnReadOnlyLegacyDb_WithMissingSymbolIndexAndGraphTable_StillWarnsAboutIndex()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_symbol_and_table_missing");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/session.py",
                "python",
                "def Run(user):\n    return user\n\ndef login(user, password):\n    return Run(user)\n");
            ForceLegacyExactFallbackMode(dbPath);
            DropSymbolExactFallbackIndex(dbPath);
            DropGraphTable(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Run", "--db", readOnlyUri, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Graph Table          : MISSING", stdout);
            Assert.Contains("Exact Index          : DEGRADED", stdout);
            Assert.Contains("idx_symbols_name_nocase", stdout);
            Assert.Contains("WARN: --exact inspect bundle ran without all supporting indexes", stderr);
            Assert.Contains("idx_symbols_name_nocase", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_ExactOnReadOnlyLegacyDb_UnsupportedGraphLanguage_DoesNotReportFalseDegradedSignal()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_markdown_exact_ok");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "docs/guide.md",
                "markdown",
                "# Heading\n\nSee also `Run`.\n");
            ForceLegacyExactFallbackMode(dbPath);
            DropGraphExactFallbackIndexes(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Heading", "--db", readOnlyUri, "--exact", "--lang", "markdown", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.False(json.GetProperty("graph_supported").GetBoolean());
            Assert.True(json.GetProperty("exact_index_available").GetBoolean());
            Assert.False(json.TryGetProperty("degraded_reason", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_ExactOnReadOnlyLegacyDb_PathOnlyUnsupportedSlice_DoesNotReportFalseDegradedSignal()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_path_only_exact_ok");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "docs/guide.md",
                "markdown",
                "# Heading\n\nSee also `Run`.\n");
            ForceLegacyExactFallbackMode(dbPath);
            DropGraphExactFallbackIndexes(dbPath);

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["Run", "--db", readOnlyUri, "--exact", "--path", "docs/", "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(json.GetProperty("exact_index_available").GetBoolean());
            Assert.False(json.TryGetProperty("degraded_reason", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_PrefersExactDefinitionFileWhenSubstringDefinitionsOverlap()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_exact_anchor");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Services/ILoggerService.cs",
                "csharp",
                """
                public interface ILoggerService
                {
                    void Log(string message);
                }
                """);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Services/LoggerService.cs",
                "csharp",
                """
                public class LoggerService : ILoggerService
                {
                    public void Log(string message) { }
                    public void Execute() { }
                }
                """);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["loggerservice", "--db", dbPath, "--lang", "csharp", "--limit", "1"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("File : src/Services/LoggerService.cs", stdout);
            Assert.Contains("class      LoggerService", stdout);
            Assert.DoesNotContain("File : src/Services/ILoggerService.cs", stdout);
            Assert.DoesNotContain("interface  ILoggerService", stdout);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunInspect_ExactZeroHumanOutput_PrintsExactZeroHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_inspect_exact_zero");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void HandleRequest() { }
                    public void HandleRequestAsync() { HandleRequest(); }
                }
                """);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
            }

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
                ["HandleRe", "--db", dbPath, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("--exact found 0 matches, but substring matching would return 2", stderr);
            Assert.Contains("`HandleRequest`", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_ExactZeroHumanOutput_PrintsExactZeroHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_refs_exact_zero");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/app.cs",
                "csharp",
                """
                public class App
                {
                    public void HandleRequest() { }
                    public void HandleRequestAsync() { HandleRequest(); }
                }
                """);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                writer.MarkGraphReady();
            }

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Handle", "--db", dbPath, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("No references found.", stderr);
            Assert.Contains("--exact found 0 matches, but substring matching would return 1", stderr);
            Assert.Contains("`HandleRequest`", stderr);
            Assert.Contains("Drop --exact or use the exact indexed name.", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_ExactWithoutGraphTable_DoesNotClaimSlowButCorrect()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_missing_graph");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Run", "--db", dbPath, "--exact"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.DoesNotContain("Results are correct but may be slow", stderr);
            Assert.Contains("symbol_references table missing", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunReferences_ExactCountWithoutGraphTable_WarnsCountIsDegraded()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_missing_graph_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunReferences(
                ["Run", "--db", dbPath, "--exact", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("0", stdout.Trim());
            Assert.DoesNotContain("Results are correct but may be slow", stderr);
            Assert.Contains("count result is degraded, not authoritative", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_RequiresPathScope()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
            ["guard"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("requires at least one --path", stderr);
    }

    [Fact]
    public void RunFind_RejectsUnsupportedFlags()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
            ["guard", "--path", "src/Auth.cs", "--since", "2099-01-01"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("unsupported option for find: --since", stderr);
    }

    [Fact]
    public void RunFind_RejectsInvalidNumericOptions()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
            ["FindUsage", "--path", "src/CodeIndex/Cli/QueryCommandRunner.cs", "--before", "-1"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--before requires a non-negative integer", stderr);
    }

    [Fact]
    public void RunFind_RejectsInvalidLimit()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
            ["FindUsage", "--path", "src/CodeIndex/Cli/QueryCommandRunner.cs", "--limit", "nope"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--limit requires a positive integer", stderr);
    }

    [Fact]
    public void RunFind_InvalidSinceFailsClosedInsteadOfRunning()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
            ["guard", "--path", "src/Auth.cs", "--since", "not-a-date"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("unsupported option for find: --since", stderr);
    }

    [Fact]
    public void RunFind_AllowsDashedLiteralViaQueryFlag()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_query_flag");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "README.md",
                "markdown",
                "--json appears here\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["--query", "--json", "--db", dbPath, "--path", "README.md", "--count"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("1", stdout.Trim());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_AllowsDashedLiteralViaDoubleDash()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_double_dash");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "README.md",
                "markdown",
                "--path appears here\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["--db", dbPath, "--path", "README.md", "--count", "--", "--path"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("1", stdout.Trim());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunSearch_RejectsFindOnlyQueryOption()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunSearch(
            ["RunFind", "--query", "PrepareFindArgs", "--path", "src/CodeIndex/Cli/QueryCommandRunner.cs", "--count"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--query is only supported by 'find'", stderr);
    }

    [Fact]
    public void RunExcerpt_RejectsFindOnlyQueryOption()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
            ["src/CodeIndex/Cli/QueryCommandRunner.cs", "--start", "626", "--query", "src/CodeIndex/Cli/ConsoleUi.cs"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--query is only supported by 'find'", stderr);
    }

    [Fact]
    public void RunFind_ZeroResultHintDoesNotSuggestRemovingRequiredPath()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_zero_hint");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "README.md",
                "markdown",
                "hello world\n");

            var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["qqq__no_such_token__zzz", "--db", dbPath, "--path", "README.md"],
                _jsonOptions));
            var normalizedStderr = stderr.ToLowerInvariant();

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("No matches found.", stderr);
            Assert.Contains("broadening --path or adding another --path value", normalizedStderr);
            Assert.DoesNotContain("try removing --lang, --path", normalizedStderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_WithJsonOutputsLineColumnAndSnippet()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Auth.cs",
                "csharp",
                "class Auth\n{\n    void Guard() {}\n    void Next() {}\n}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["guard", "--db", dbPath, "--path", "src/Auth.cs", "--json", "--before", "1", "--after", "1"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("src/Auth.cs", json.GetProperty("path").GetString());
            Assert.Equal(3, json.GetProperty("line").GetInt32());
            Assert.Equal(10, json.GetProperty("column").GetInt32());
            Assert.Equal(2, json.GetProperty("start_line").GetInt32());
            Assert.Equal(4, json.GetProperty("end_line").GetInt32());
            Assert.Contains("void Guard()", json.GetProperty("snippet").GetString());
            Assert.Contains("void Next()", json.GetProperty("snippet").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_CountOnlyJsonIncludesVisibleMatchAndFileCounts()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Auth.cs",
                "csharp",
                "guard one\nline two\nguard three\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["guard", "--db", dbPath, "--path", "src/Auth.cs", "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("files").GetInt32());
            Assert.Equal(1, json.GetProperty("file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_CountOnlyJsonCountsEverySameLineOccurrence()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_multi_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Sample.cs",
                "csharp",
                "alpha alpha alpha\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["alpha", "--db", dbPath, "--path", "src/Sample.cs", "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(3, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("files").GetInt32());
            Assert.Equal(1, json.GetProperty("file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFind_CountOnlyJsonCountsOverlappingOccurrences()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_find_overlap_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Sample.cs",
                "csharp",
                "// banana\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFind(
                ["ana", "--db", dbPath, "--path", "src/Sample.cs", "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(2, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("files").GetInt32());
            Assert.Equal(1, json.GetProperty("file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunExcerpt_RequiresStartLine()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
            ["src/app.cs"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("Error: excerpt requires --start <line>", stderr);
    }

    [Fact]
    public void RunExcerpt_RejectsStartGreaterThanEnd()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunExcerpt(
            ["src/app.cs", "--start", "5", "--end", "3"],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("--start (5) must be less than or equal to --end (3)", stderr);
    }

    [Fact]
    public void RunInspect_BlankQueryReturnsUsageError()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunInspect(
            ["   "],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("Error: inspect requires a symbol query argument", stderr);
        Assert.Contains("Hint: Add the symbol you want to inspect", stderr);
        Assert.Contains($"Usage: {ConsoleUi.GetUsageLine("inspect")}", stderr);
    }

    [Fact]
    public void RunMap_WithJsonIncludesWorkspaceMetadataForProjectDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunMap(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunMap_WithJsonIncludesWorkspaceMetadataForCustomDbUnderCdidx()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map_custom_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map_custom_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "shared.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunMap(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunMap_NonexistentPathReturnsNotFound()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map_notfound");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunMap(
                ["--db", dbPath, "--path", "nonexistent/"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Contains("No files found", stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroResultJson_EmitsStructuredPayloadWithFreshnessHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_files_zero_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["missing-file-fragment", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetArrayLength());
            Assert.Equal(1, json.GetProperty("indexed_file_count").GetInt32());
            Assert.True(json.TryGetProperty("indexed_at", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroResultJson_EmptyIndexEmitsNullIndexedAt()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_files_zero_json_empty");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["missing-file-fragment", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("files").GetArrayLength());
            Assert.Equal(0, json.GetProperty("indexed_file_count").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("indexed_at").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroResultJson_CountOnlyEmitsFreshnessHint()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_files_zero_json_count");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["missing-file-fragment", "--db", dbPath, "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("indexed_file_count").GetInt32());
            Assert.True(json.TryGetProperty("indexed_at", out _));
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunFiles_ZeroResultJson_CountOnlyEmptyIndexEmitsNullIndexedAt()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_files_zero_json_count_empty");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunFiles(
                ["missing-file-fragment", "--db", dbPath, "--json", "--count"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(0, json.GetProperty("indexed_file_count").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("indexed_at").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunMap_NonexistentPathJsonReturnsNotFoundWithPayload()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map_notfound_json");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            var (exitCode, stdout, _) = CaptureConsole(() => QueryCommandRunner.RunMap(
                ["--db", dbPath, "--path", "nonexistent/", "--json"],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            using var document = ParseJsonOutput(stdout);
            Assert.Equal(0, document.RootElement.GetProperty("file_count").GetInt32());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunMap_EmptyDbWithoutFiltersReturnsSuccess()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_map_empty_ok");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            // No files inserted — empty but valid index / ファイル未挿入 — 空だが有効なインデックス

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunMap(
                ["--db", dbPath],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Files      : 0", stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunStatus_HumanReadableIncludesGitMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath],
                _jsonOptions));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains($"Git HEAD: {expectedHead}", stdout);
            Assert.Contains("Git Dirty: True", stdout);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunStatus_ReadOnlyUriForExplicitDb_UsesPersistedProjectRootMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_uri");
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_query_runner_status_{Guid.NewGuid():N}.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", readOnlyUri, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.True(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void RunStatus_CustomDbUnderCdidx_UsesPersistedProjectRootMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_custom_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_custom_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "shared.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.True(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunStatus_ExplicitProjectLocalDb_LeavesWorkspaceMetadataNullWhenMetadataIsMissing()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_project_local_explicit");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "DELETE FROM codeindex_meta WHERE key = @key";
                cmd.Parameters.AddWithValue("@key", DbContext.IndexedProjectRootMetaKey);
                cmd.ExecuteNonQuery();
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("project_root").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RunStatus_ExplicitProjectLocalReadOnlyUri_LeavesWorkspaceMetadataNullWhenMetadataIsMissing()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_project_local_uri");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "DELETE FROM codeindex_meta WHERE key = @key";
                cmd.Parameters.AddWithValue("@key", DbContext.IndexedProjectRootMetaKey);
                cmd.ExecuteNonQuery();
            }
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", readOnlyUri, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("project_root").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public void RunStatus_ExplicitExternalCodeIndexDb_UsesPersistedProjectRootMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "codeindex.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            var sourcePath = Path.Combine(projectRoot, "src", "app.cs");
            File.WriteAllText(sourcePath, "class App {}\n");
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", "class App {}\n");

            File.WriteAllText(sourcePath, "class App { void Run() {} }\n");

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.True(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunStatus_ExplicitExternalCodeIndexDb_IgnoresSingleSiblingPathCollision()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_collision_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_collision_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "codeindex.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            Directory.CreateDirectory(Path.Combine(dbContainerRoot, "src"));

            const string content = "class App {}\n";
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), content);
            File.WriteAllText(Path.Combine(dbContainerRoot, "src", "app.cs"), content);
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");

            var expectedHead = TestProjectHelper.RunGit(projectRoot, "rev-parse", "HEAD").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", content);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(projectRoot, json.GetProperty("project_root").GetString());
            Assert.Equal(expectedHead, json.GetProperty("git_head").GetString());
            Assert.False(json.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunStatus_ExplicitExternalCodeIndexDbWithoutMetadata_IgnoresSiblingPathCollisionAndLeavesWorkspaceMetadataNull()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_missing_meta_db");
        var dbContainerRoot = TestProjectHelper.CreateTempProject("cdidx_query_runner_status_codeindex_missing_meta_container");
        var dbPath = Path.Combine(dbContainerRoot, ".cdidx", "codeindex.db");
        try
        {
            TestProjectHelper.InitializeGitRepo(projectRoot);
            TestProjectHelper.InitializeGitRepo(dbContainerRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
            Directory.CreateDirectory(Path.Combine(dbContainerRoot, "src"));

            const string indexedContent = "class App {}\n";
            const string siblingContent = "class App { void Different() {} }\n";
            File.WriteAllText(Path.Combine(projectRoot, "src", "app.cs"), indexedContent);
            File.WriteAllText(Path.Combine(dbContainerRoot, "src", "app.cs"), siblingContent);
            TestProjectHelper.RunGit(projectRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(projectRoot, "commit", "-m", "initial");
            TestProjectHelper.RunGit(dbContainerRoot, "add", "src/app.cs");
            TestProjectHelper.RunGit(dbContainerRoot, "commit", "-m", "initial");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
            }
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", indexedContent);
            using (var db = new DbContext(dbPath))
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "DELETE FROM codeindex_meta WHERE key = @key";
                cmd.Parameters.AddWithValue("@key", DbContext.IndexedProjectRootMetaKey);
                cmd.ExecuteNonQuery();
            }

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
                ["--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("project_root").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, json.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(dbContainerRoot);
        }
    }

    [Fact]
    public void RunStatus_MissingDatabaseReturnsGuidance()
    {
        var missingDbPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.db");

        var (exitCode, _, stderr) = CaptureConsole(() => QueryCommandRunner.RunStatus(
            ["--db", missingDbPath],
            _jsonOptions));

        Assert.Equal(CommandExitCodes.DatabaseError, exitCode);
        Assert.Contains("Error: database not found at", stderr);
        // Verify full (absolute) path is shown, not just the basename / フルパス表示を検証
        Assert.Contains(Path.GetFullPath(missingDbPath), stderr);
        Assert.Contains("Hint: create or refresh the index with `cdidx index <projectPath>` (or `cdidx .`) and then rerun this command.", stderr);
    }

    private static (T Result, string Stdout, string Stderr) CaptureConsole<T>(Func<T> action)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                var result = action();
                return (result, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    private int RunCommandWithInvalidSince(string command)
    {
        return command switch
        {
            "search" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--since", "nope"], _jsonOptions),
            "definition" => QueryCommandRunner.RunDefinition(["QueryCommandRunner", "--since", "nope"], _jsonOptions),
            "symbols" => QueryCommandRunner.RunSymbols(["QueryCommandRunner", "--since", "nope"], _jsonOptions),
            "files" => QueryCommandRunner.RunFiles(["QueryCommandRunner", "--since", "nope"], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    private int RunCommandWithUnsupportedSince(string command, string sinceValue)
    {
        return command switch
        {
            "references" => QueryCommandRunner.RunReferences(["QueryCommandRunner", "--since", sinceValue, "--count"], _jsonOptions),
            "callers" => QueryCommandRunner.RunCallers(["QueryCommandRunner", "--since", sinceValue, "--count"], _jsonOptions),
            "callees" => QueryCommandRunner.RunCallees(["QueryCommandRunner", "--since", sinceValue, "--count"], _jsonOptions),
            "excerpt" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "--start", "1", "--since", sinceValue], _jsonOptions),
            "map" => QueryCommandRunner.RunMap(["--since", sinceValue], _jsonOptions),
            "inspect" => QueryCommandRunner.RunInspect(["QueryCommandRunner", "--since", sinceValue], _jsonOptions),
            "outline" => QueryCommandRunner.RunOutline(["src/CodeIndex/Program.cs", "--since", sinceValue], _jsonOptions),
            "status" => QueryCommandRunner.RunStatus(["--since", sinceValue], _jsonOptions),
            "impact" => QueryCommandRunner.RunImpact(["QueryCommandRunner", "--since", sinceValue, "--count"], _jsonOptions),
            "deps" => QueryCommandRunner.RunDeps(["--since", sinceValue], _jsonOptions),
            "hotspots" => QueryCommandRunner.RunHotspots(["--since", sinceValue, "--count"], _jsonOptions),
            "unused" => QueryCommandRunner.RunUnused(["--since", sinceValue, "--count"], _jsonOptions),
            "validate" => QueryCommandRunner.RunValidate(["--since", sinceValue], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    private int RunCommandWithUnsupportedOption(string command, string[] args)
    {
        return command switch
        {
            "search" => QueryCommandRunner.RunSearch(["QueryCommandRunner", .. args], _jsonOptions),
            "map" => QueryCommandRunner.RunMap(args, _jsonOptions),
            "inspect" => QueryCommandRunner.RunInspect(["QueryCommandRunner", .. args], _jsonOptions),
            "status" => QueryCommandRunner.RunStatus(args, _jsonOptions),
            "validate" => QueryCommandRunner.RunValidate(args, _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    private int RunCommandWithMissingOrSwallowedValue(string scenario)
    {
        return scenario switch
        {
            "search-limit-tail" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--limit"], _jsonOptions),
            "search-top-tail" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--top"], _jsonOptions),
            "search-db-tail" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--db"], _jsonOptions),
            "search-db-swallow" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--db", "--count"], _jsonOptions),
            "search-db-unknown-double-dash" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--db", "--mystery"], _jsonOptions),
            "search-db-recognized-double-dash" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--db", "--lang", "--count"], _jsonOptions),
            "search-lang-swallow" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--lang", "--count"], _jsonOptions),
            "search-lang-unknown-double-dash" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--lang", "--mystery", "--count"], _jsonOptions),
            "search-path-swallow" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--path", "--count"], _jsonOptions),
            "search-exclude-path-swallow" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--exclude-path", "--count"], _jsonOptions),
            "definition-kind-swallow" => QueryCommandRunner.RunDefinition(["QueryCommandRunner", "--kind", "--count"], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }

    private int RunCommandWithEmptyInlineStringValue(string scenario)
    {
        return scenario switch
        {
            "search-db-inline-empty" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--db="], _jsonOptions),
            "search-lang-inline-empty" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--lang="], _jsonOptions),
            "search-path-inline-empty" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--path="], _jsonOptions),
            "search-exclude-path-inline-empty" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--exclude-path="], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }

    private int RunCommandWithUnexpectedPositionals(string scenario)
    {
        return scenario switch
        {
            "search-extra" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "extra"], _jsonOptions),
            "excerpt-extra" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "extra", "--start", "1"], _jsonOptions),
            "map-extra" => QueryCommandRunner.RunMap(["stray"], _jsonOptions),
            "outline-extra" => QueryCommandRunner.RunOutline(["src/CodeIndex/Program.cs", "extra"], _jsonOptions),
            "status-extra" => QueryCommandRunner.RunStatus(["stray"], _jsonOptions),
            "validate-extra" => QueryCommandRunner.RunValidate(["stray"], _jsonOptions),
            "languages-extra" => QueryCommandRunner.RunLanguages(["stray"], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }

    private int RunCommandWithInvalidNumeric(string scenario)
    {
        return scenario switch
        {
            "search-limit" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--limit", "nope"], _jsonOptions),
            "search-top" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--top", "nope"], _jsonOptions),
            "search-snippet-lines" => QueryCommandRunner.RunSearch(["QueryCommandRunner", "--snippet-lines", "nope"], _jsonOptions),
            "impact-depth" => QueryCommandRunner.RunImpact(["QueryCommandRunner", "--depth", "nope", "--count"], _jsonOptions),
            "excerpt-start" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "--start", "nope"], _jsonOptions),
            "excerpt-end" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "--start", "1", "--end", "nope"], _jsonOptions),
            "excerpt-before" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "--start", "1", "--before", "nope"], _jsonOptions),
            "excerpt-after" => QueryCommandRunner.RunExcerpt(["src/CodeIndex/Program.cs", "--start", "1", "--after", "nope"], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
    }

    private static int RunGraphCommand(string command, string[] args, JsonSerializerOptions jsonOptions) => command switch
    {
        "references" => QueryCommandRunner.RunReferences(args, jsonOptions),
        "callers" => QueryCommandRunner.RunCallers(args, jsonOptions),
        "callees" => QueryCommandRunner.RunCallees(args, jsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported graph command"),
    };

    private static string[] GetExactZeroArgs(string command, string dbPath, int limit, string? queryOverride, bool countOnly = false)
    {
        var query = queryOverride ?? command switch
        {
            "references" => "Target",
            "callers" => "Target",
            "callees" => "Caller",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported graph command"),
        };

        return countOnly
            ? [query, "--db", dbPath, "--json", "--count", "--exact", "--limit", limit.ToString()]
            : [query, "--db", dbPath, "--json", "--exact", "--limit", limit.ToString()];
    }

    private static void SeedGraphExactZeroFixture(string dbPath, string command)
    {
        var content = command switch
        {
            "references" or "callers" => """
                public class App
                {
                    public void TargetWork1() { }
                    public void TargetWork2() { }
                    public void TargetWork3() { }
                    public void TargetWork4() { }
                    public void TargetWork5() { }
                    public void TargetWork6() { }
                    public void TargetWork7() { }

                    public void Caller1() { TargetWork1(); }
                    public void Caller2() { TargetWork2(); }
                    public void Caller3() { TargetWork3(); }
                    public void Caller4() { TargetWork4(); }
                    public void Caller5() { TargetWork5(); }
                    public void Caller6() { TargetWork6(); }
                    public void Caller7() { TargetWork7(); }
                }
                """,
            "callees" => """
                public class App
                {
                    public void Called1() { }
                    public void Called2() { }
                    public void Called3() { }
                    public void Called4() { }
                    public void Called5() { }
                    public void Called6() { }
                    public void Called7() { }

                    public void CallerWork1() { Called1(); }
                    public void CallerWork2() { Called2(); }
                    public void CallerWork3() { Called3(); }
                    public void CallerWork4() { Called4(); }
                    public void CallerWork5() { Called5(); }
                    public void CallerWork6() { Called6(); }
                    public void CallerWork7() { Called7(); }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported graph command"),
        };

        TestProjectHelper.InsertIndexedFile(dbPath, "src/app.cs", "csharp", content);
        using var db = new DbContext(dbPath);
        var writer = new DbWriter(db.Connection);
        writer.MarkGraphReady();
    }

    // --- TryParseIso8601Since tests / TryParseIso8601Sinceテスト ---

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-01-15T10:30")]              // minute precision / 分精度
    [InlineData("2024-01-15T10:30Z")]
    [InlineData("2024-01-15T10:30+09:00")]
    [InlineData("2024-01-15T10:30:00")]
    [InlineData("2024-01-15T10:30:00Z")]
    [InlineData("2024-01-15T10:30:00+09:00")]
    [InlineData("2024-01-15T10:30:00.000Z")]
    [InlineData("2024-01-15T10:30:00.123")]       // offsetless fractional / オフセットなし小数秒
    [InlineData("2024-01-15T10:30:00.1234567Z")]
    [InlineData("2024-01-15T10:30:00.1Z")]        // 1-digit fraction / 1桁小数
    public void TryParseIso8601Since_AcceptsValidIsoFormats(string input)
    {
        var ok = QueryCommandRunner.TryParseIso8601Since(input, out var result);
        Assert.True(ok, $"Expected '{input}' to be accepted as ISO 8601");
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Theory]
    [InlineData("01/02/2024")]        // ambiguous locale-dependent / ロケール依存の曖昧な形式
    [InlineData("1/2/2024")]
    [InlineData("02-Jan-2024")]
    [InlineData("Jan 15, 2024")]
    [InlineData("not-a-date")]
    [InlineData("yesterday")]
    [InlineData("")]
    public void TryParseIso8601Since_RejectsNonIsoFormats(string input)
    {
        var ok = QueryCommandRunner.TryParseIso8601Since(input, out _);
        Assert.False(ok, $"Expected '{input}' to be rejected as non-ISO 8601");
    }

    [Fact]
    public void TryParseIso8601Since_DateOnlyTreatedAsLocalTime()
    {
        // Offsetless dates are treated as local time, matching prior DateTime.TryParse behavior /
        // オフセットなしの日付はローカル時刻として扱う（従来のDateTime.TryParseの動作と一致）
        QueryCommandRunner.TryParseIso8601Since("2024-06-15", out var result);
        var expected = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 6, 15))).UtcDateTime;
        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void TryParseIso8601Since_ExplicitUtcTimestamp()
    {
        QueryCommandRunner.TryParseIso8601Since("2024-06-15T12:00:00Z", out var result);
        Assert.Equal(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void TryParseIso8601Since_ConvertsTimezoneOffsetToUtc()
    {
        QueryCommandRunner.TryParseIso8601Since("2024-06-15T12:00:00+09:00", out var result);
        Assert.Equal(new DateTime(2024, 6, 15, 3, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ParseArgs_RejectsSinceWithAmbiguousDate()
    {
        var options = QueryCommandRunner.ParseArgs(
            ["search", "foo", "--since", "01/02/2024"], jsonDefault: false);
        Assert.NotNull(options.ParseError);
        Assert.Contains("could not parse", options.ParseError);
    }

    [Fact]
    public void ParseArgs_AcceptsSinceWithIsoDate()
    {
        var options = QueryCommandRunner.ParseArgs(
            ["search", "foo", "--since", "2024-01-02"], jsonDefault: false);
        Assert.Null(options.ParseError);
        Assert.NotNull(options.Since);
        // Offsetless date → local midnight → UTC / オフセットなし → ローカル深夜 → UTC
        var expected = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 1, 2))).UtcDateTime;
        Assert.Equal(expected, options.Since.Value);
    }

    [Fact]
    public void ParseArgs_RejectsBareSinceWithNoValue()
    {
        var options = QueryCommandRunner.ParseArgs(
            ["search", "foo", "--since"], jsonDefault: false);
        Assert.NotNull(options.ParseError);
        Assert.Contains("--since requires a value", options.ParseError);
    }

    [Fact]
    public void ParseArgs_RejectsBareSinceForFiles()
    {
        var options = QueryCommandRunner.ParseArgs(
            ["files", "--since"], jsonDefault: false);
        Assert.NotNull(options.ParseError);
        Assert.Contains("--since requires a value", options.ParseError);
    }

    private static JsonDocument ParseJsonOutput(string stdout)
    {
        var jsonLine = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last();
        return JsonDocument.Parse(jsonLine);
    }

    private static (string ProjectRoot, string DbPath) CreateUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_confidence");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/config/unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 20,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "ExportedApi",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "InternalOnly",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "internal void InternalOnly() { }",
                Visibility = "internal",
                ContainerKind = "class",
                ContainerName = "ExportedApi",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "PathResolver",
                Line = 1,
                StartLine = 1,
                EndLine = 1,
                Signature = "public class PathResolver",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "AdoptionService",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "public class AdoptionService",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "TokenService",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public class TokenService",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "AppSettings",
                Line = 9,
                StartLine = 9,
                EndLine = 11,
                Signature = "public class AppSettings",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "ApplyConfiguration",
                Line = 12,
                StartLine = 12,
                EndLine = 12,
                Signature = "public void ApplyConfiguration()",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "UseIOptions",
                Line = 13,
                StartLine = 13,
                EndLine = 13,
                Signature = "public void UseIOptions()",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ConnectionString",
                Line = 10,
                StartLine = 10,
                EndLine = 10,
                Signature = "public string ConnectionString { get; set; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "AppSettings",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreatePlainCliOptionsUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_cli_options");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/cli_options_fixture.cs",
            Lang = "csharp",
            Size = 180,
            Lines = 6,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "CliOptions",
                Line = 1,
                StartLine = 1,
                EndLine = 4,
                Signature = "public sealed class CliOptions",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ShowHelp",
                Line = 3,
                StartLine = 3,
                EndLine = 3,
                Signature = "public bool ShowHelp { get; init; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "CliOptions",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "ProjectPath",
                Line = 4,
                StartLine = 4,
                EndLine = 4,
                Signature = "public string? ProjectPath { get; init; }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "CliOptions",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateReflectionUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_reflection");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 10,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 6,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateReflectionDiversifiedUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_reflection_diversified");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_diversified_unused_fixture.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 12,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    public string FullName { get; set; } = string.Empty;
                    public void Run() { Hidden(); }
                    private void Hidden() { }
                    internal void InternalOnly() { }
                }
                """,
            }
        ]);
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 8,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 5,
                StartLine = 5,
                EndLine = 5,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Run",
                Line = 6,
                StartLine = 6,
                EndLine = 6,
                Signature = "public void Run() { Hidden(); }",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "Hidden",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "private void Hidden() { }",
                Visibility = "private",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "InternalOnly",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "internal void InternalOnly() { }",
                Visibility = "internal",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateReflectionCommentedUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_reflection_commented");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_comment_fixture.cs",
            Lang = "csharp",
            Size = 220,
            Lines = 9,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 8,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    /// Bound from JSON payload.
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 7,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 7,
                StartLine = 7,
                EndLine = 7,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateQualifiedReflectionUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_reflection_qualified");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_qualified_fixture.cs",
            Lang = "csharp",
            Size = 360,
            Lines = 12,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 12,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [global::System.Text.Json.Serialization.JsonPropertyName("full_name")]
                    public string QualifiedName { get; set; } = string.Empty;
                    [JsonPropertyNameAttribute("display_name")]
                    public string SuffixedName { get; set; } = string.Empty;
                    [System.Text.Json.Serialization.JsonIgnoreAttribute]
                    public string IgnoredName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 10,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "QualifiedName",
                Line = 6,
                StartLine = 6,
                EndLine = 6,
                Signature = "public string QualifiedName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "SuffixedName",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public string SuffixedName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "IgnoredName",
                Line = 10,
                StartLine = 10,
                EndLine = 10,
                Signature = "public string IgnoredName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateBlockCommentReflectionUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_reflection_block_comment");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/reflection_block_comment_fixture.cs",
            Lang = "csharp",
            Size = 280,
            Lines = 10,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 10,
                Content = """
                using System.Text.Json.Serialization;

                public class UserDto
                {
                    [JsonPropertyName("full_name")]
                    /* bound from payload
                       via serializer */
                    public string FullName { get; set; } = string.Empty;
                }
                """,
            }
        ]);
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = "UserDto",
                Line = 3,
                StartLine = 3,
                EndLine = 8,
                Signature = "public class UserDto",
                Visibility = "public",
            },
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "property",
                Name = "FullName",
                Line = 8,
                StartLine = 8,
                EndLine = 8,
                Signature = "public string FullName { get; set; } = string.Empty;",
                Visibility = "public",
                ContainerKind = "class",
                ContainerName = "UserDto",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateUnsupportedLanguageUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_shell_json");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "script.sh",
            Lang = "shell",
            Size = 64,
            Lines = 6,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertSymbols(
        [
            new SymbolRecord
            {
                FileId = fileId,
                Kind = "function",
                Name = "helper",
                Line = 1,
                StartLine = 1,
                EndLine = 3,
                Signature = "helper() {",
            },
        ]);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static (string ProjectRoot, string DbPath) CreateLargePublicUnusedFixtureDb()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_unused_large_public");
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        using var db = new DbContext(dbPath);
        db.InitializeSchema();
        var writer = new DbWriter(db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/large_public_unused_fixture.cs",
            Lang = "csharp",
            Size = 16000,
            Lines = 2600,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks(
        [
            new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 1,
                Content = "public class PublicNoise0000 { }",
            }
        ]);

        var symbols = new List<SymbolRecord>();
        for (var i = 0; i < 2500; i++)
        {
            symbols.Add(new SymbolRecord
            {
                FileId = fileId,
                Kind = "class",
                Name = $"PublicNoise{i:D4}",
                Line = i + 1,
                StartLine = i + 1,
                EndLine = i + 1,
                Signature = $"public class PublicNoise{i:D4} {{ }}",
                Visibility = "public",
            });
        }
        writer.InsertSymbols(symbols);
        writer.MarkGraphReady();
        return (projectRoot, dbPath);
    }

    private static void MarkGraphAndFoldReady(string dbPath)
    {
        using var db = new DbContext(dbPath);
        var writer = new DbWriter(db.Connection);
        writer.MarkGraphReady();
        writer.MarkFoldReady();
    }

    private static string CreateIndexedDbWithSingleFile(string projectRoot, bool markGraphReady = false)
    {
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        TestProjectHelper.InsertIndexedFile(
            dbPath,
            "src/app.cs",
            "csharp",
            """
            public class App
            {
                public void HandleRequest() { }
            }
            """);

        if (markGraphReady)
        {
            using var db = new DbContext(dbPath);
            var writer = new DbWriter(db.Connection);
            writer.MarkGraphReady();
        }

        return dbPath;
    }

    private static string CreateHotspotFamilyFixtureDb(string projectRoot, bool markHotspotFamilyReady)
    {
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        TestProjectHelper.InsertIndexedFile(
            dbPath,
            "src/Api.Part1.cs",
            "csharp",
            """
            public partial class Api
            {
                public void Run() { }
            }
            """);
        TestProjectHelper.InsertIndexedFile(
            dbPath,
            "src/Api.Part2.cs",
            "csharp",
            """
            public partial class Api
            {
                public void Run(int value) { }
            }
            """);
        TestProjectHelper.InsertIndexedFile(
            dbPath,
            "src/Caller.cs",
            "csharp",
            """
            public class Caller
            {
                public void Call(Api api)
                {
                    api.Run();
                    api.Run(1);
                }
            }
            """);

        using var db = new DbContext(dbPath);
        var writer = new DbWriter(db.Connection);
        writer.MarkGraphReady();
        if (markHotspotFamilyReady)
            writer.MarkHotspotFamilyReady("csharp", "fixture-fingerprint");
        return dbPath;
    }

    private static string CreateLegacyDbWithoutIndexedAt(string projectRoot)
    {
        var dbPath = Path.Combine(projectRoot, "legacy.db");
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    lang TEXT,
                    size INTEGER,
                    lines INTEGER,
                    modified DATETIME
                );
                CREATE TABLE symbols (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_id INTEGER NOT NULL,
                    name TEXT NOT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO files (path, lang, size, lines, modified)
                VALUES ('src/legacy.cs', 'csharp', 42, 3, '2026-01-01T00:00:00Z');
                """;
            insert.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return dbPath;
    }

    private int RunZeroResultCommand(string command, string dbPath)
    {
        return command switch
        {
            "search" => QueryCommandRunner.RunSearch(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "files" => QueryCommandRunner.RunFiles(["definitely-missing-path", "--db", dbPath, "--json"], _jsonOptions),
            "symbols" => QueryCommandRunner.RunSymbols(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "definition" => QueryCommandRunner.RunDefinition(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "references" => QueryCommandRunner.RunReferences(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "callers" => QueryCommandRunner.RunCallers(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "callees" => QueryCommandRunner.RunCallees(["DefinitelyMissingSymbol", "--db", dbPath, "--json"], _jsonOptions),
            "deps" => QueryCommandRunner.RunDeps(["--db", dbPath, "--json"], _jsonOptions),
            "unused" => QueryCommandRunner.RunUnused(["--db", dbPath, "--json", "--kind", "delegate"], _jsonOptions),
            "hotspots" => QueryCommandRunner.RunHotspots(["--db", dbPath, "--json", "--kind", "delegate"], _jsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    private static void AssertZeroResultPayload(JsonElement json, string resultsKey)
    {
        Assert.Equal(0, json.GetProperty("count").GetInt32());
        Assert.True(json.TryGetProperty(resultsKey, out var results));
        Assert.Equal(JsonValueKind.Array, results.ValueKind);
        Assert.Equal(0, results.GetArrayLength());
        Assert.True(json.TryGetProperty("indexed_file_count", out var indexedFileCount));
        Assert.True(indexedFileCount.GetInt64() > 0);
        Assert.True(json.GetProperty("freshness_available").GetBoolean());
        Assert.True(json.TryGetProperty("indexed_at", out var indexedAt));
        Assert.Equal(JsonValueKind.String, indexedAt.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(indexedAt.GetString()));
    }

    private static void DropGraphExactFallbackIndexes(string dbPath)
    {
        using var db = new DbContext(dbPath);
        var writer = new DbWriter(db.Connection);
        writer.MarkGraphReady();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            DROP INDEX IF EXISTS idx_symbol_refs_name_nocase;
            DROP INDEX IF EXISTS idx_symbol_refs_container_nocase;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static (string ProjectRoot, string ReadOnlyUri) CreateReadOnlyMissingGraphTableDb(string projectName)
    {
        var projectRoot = TestProjectHelper.CreateTempProject(projectName);
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        TestProjectHelper.InsertIndexedFile(dbPath, "src/session.py", "python", "def login(user, password):\n    return Run(user)\n");

        using (var db = new DbContext(dbPath))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = """
                DROP TABLE symbol_references;
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return (projectRoot, new Uri(dbPath).AbsoluteUri + "?immutable=1");
    }

    private static void DropSymbolExactFallbackIndex(string dbPath)
    {
        using var db = new DbContext(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            DROP INDEX IF EXISTS idx_symbols_name_nocase;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static void ForceLegacyExactFallbackMode(string dbPath)
    {
        using var db = new DbContext(dbPath);
        db.ClearReadyFlags();
        var writer = new DbWriter(db.Connection);
        writer.MarkGraphReady();
        writer.MarkIssuesReady();
    }

    private static void DropGraphTable(string dbPath)
    {
        using var db = new DbContext(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS symbol_references;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        cmd.ExecuteNonQuery();
    }

    private void RunImpactPartialClassZeroResultIteration(int iteration)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_query_runner_impact_partial_stability_{iteration}");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Worker.Part1.cs", "csharp",
                """
                public partial class Worker
                {
                    public void Start() { }
                }
                """);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/Worker.Part2.cs", "csharp",
                """
                public partial class Worker
                {
                    public void Stop() { }
                }
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["Worker", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertZeroResultPayload(json, "callers");
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.True(json.GetProperty("has_multiple_definitions").GetBoolean());
            Assert.True(json.GetProperty("has_multiple_definition_files").GetBoolean());
            Assert.Equal("multiple_definition_files", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    private void RunImpactImportOnlyZeroResultIteration(int iteration)
    {
        var projectRoot = TestProjectHelper.CreateTempProject($"cdidx_query_runner_impact_import_stability_{iteration}");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/app.py", "python",
                """
                import requests
                """);
            MarkGraphAndFoldReady(dbPath);

            var (exitCode, stdout, stderr) = CaptureConsole(() => QueryCommandRunner.RunImpact(
                ["requests", "--db", dbPath, "--json"],
                _jsonOptions));

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            AssertZeroResultPayload(json, "callers");
            Assert.Equal("none", json.GetProperty("impact_mode").GetString());
            Assert.Equal(1, json.GetProperty("definition_count").GetInt32());
            Assert.Equal("non_callable_symbol_kind", json.GetProperty("zero_result_reason").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }
}
