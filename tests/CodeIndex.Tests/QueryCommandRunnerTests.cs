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
            "--no-json",
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
            "--snippet-lines", "99",
        ], jsonDefault: true);

        Assert.Equal("/tmp/query.db", options.DbPath);
        Assert.False(options.Json);
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
        Assert.Equal(SearchSnippetFormatter.MaxSnippetLines, options.SnippetLines);
    }

    [Fact]
    public void ParseArgs_CountFlagParsed()
    {
        var options = QueryCommandRunner.ParseArgs(["myquery", "--count"], jsonDefault: false);
        Assert.True(options.CountOnly);
        Assert.Equal("myquery", options.Query);
    }

    [Fact]
    public void ParseArgs_InvalidNumbersAndUnknownOptionsFallbackAndReportErrors()
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
        Assert.Contains("Error: --limit requires a positive integer", stderr);
        Assert.Contains("Error: --start requires a positive integer", stderr);
        Assert.Contains("Error: --end requires a positive integer", stderr);
        Assert.Contains("Error: --before requires a non-negative integer", stderr);
        Assert.Contains("Error: --after requires a non-negative integer", stderr);
        Assert.Contains("Error: --snippet-lines requires a positive integer", stderr);
        Assert.Contains("Warning: unknown option '--mystery' (ignored)", stderr);
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
    public void BuildSymbolQueryList_FailsClosedOnlyWhenEveryInputIsEmptyString()
    {
        // --name "" is explicit input that normalizes to empty — must be flagged, not silently
        // broadened into an all-symbols dump.
        // --name "" は明示入力だが正規化で空になる。フラグを立てて全件ダンプを防ぐ。
        var rejected = QueryCommandRunner.ParseArgs(["--name", ""], jsonDefault: false);
        var (rejectedQueries, rejectedHadInput) = QueryCommandRunner.BuildSymbolQueryList(rejected);
        Assert.Null(rejectedQueries);
        Assert.True(rejectedHadInput);
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
            Assert.Contains("empty after normalization", stderr);
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
            Assert.Equal("helper", json.GetProperty("symbols")[0].GetProperty("name").GetString());
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

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
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

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
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

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
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

    [Fact]
    public void RunDefinition_ExactZeroJson_EmitsExactZeroHintPayload()
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

            using var document = ParseJsonOutput(stdout);
            var json = document.RootElement;

            Assert.Equal(CommandExitCodes.NotFound, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, json.GetProperty("count").GetInt32());
            Assert.Equal(2, json.GetProperty("exact_zero_hint").GetProperty("relaxed_count").GetInt32());
            Assert.Equal("HandleRequest", json.GetProperty("exact_zero_hint").GetProperty("sample_names")[0].GetString());
            Assert.Equal("drop --exact or use the exact indexed name", json.GetProperty("exact_zero_hint").GetProperty("suggestion").GetString());
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
        Assert.Contains("Run 'cdidx index <projectPath>' first to create the index.", stderr);
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
            Path = "src/unused_fixture.cs",
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
                    [System.Text.Json.Serialization.JsonPropertyName("full_name")]
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
}
