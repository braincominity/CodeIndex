using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodeIndex.Cli;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class ProgramRunnerTests
{
    [Fact]
    public void Run_ForcedGlobalToolLogging_WritesLifecycleAndMirrorsStderr()
    {
        var logDir = Path.Combine(Path.GetTempPath(), $"cdidx_global_tool_log_{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDir);
        var originalForce = Environment.GetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG");
        var originalDisable = Environment.GetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG");
        var originalLogDir = Environment.GetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR");

        try
        {
            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", "1");
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", null);
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", logDir);

            var (exitCode, stdout, stderr) = CaptureConsole(() => ProgramRunner.Run(
                ["definitely-not-a-command"],
                appVersion: "1.10.0"));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("Unknown command: definitely-not-a-command", stderr);

            var logPath = Directory.GetFiles(logDir, "stderr-*.log", SearchOption.TopDirectoryOnly).Single();
            var log = File.ReadAllText(logPath);
            Assert.Contains("session_start", log);
            Assert.Contains("args=definitely-not-a-command", log);
            Assert.Contains("Unknown command: definitely-not-a-command", log);
            Assert.Contains("command_complete exit_code=1", log);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", originalForce);
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", originalDisable);
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", originalLogDir);
            TestProjectHelper.DeleteDirectory(logDir);
        }
    }

    [Fact]
    public void Run_ForcedGlobalToolLogging_PrunesToThirtyDailyFiles()
    {
        var logDir = Path.Combine(Path.GetTempPath(), $"cdidx_global_tool_log_prune_{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDir);
        var originalForce = Environment.GetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG");
        var originalDisable = Environment.GetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG");
        var originalLogDir = Environment.GetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR");

        try
        {
            for (var i = 0; i < 35; i++)
            {
                var date = new DateTime(2024, 1, 1).AddDays(i);
                File.WriteAllText(Path.Combine(logDir, $"stderr-{date:yyyyMMdd}.log"), $"old {i}");
            }

            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", "1");
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", null);
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", logDir);

            var (exitCode, _, stderr) = CaptureConsole(() => ProgramRunner.Run(
                ["definitely-not-a-command"],
                appVersion: "1.10.0"));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("Unknown command: definitely-not-a-command", stderr);

            var logs = Directory.GetFiles(logDir, "stderr-*.log", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(30, logs.Count);
            Assert.DoesNotContain("stderr-20240101.log", logs);
            Assert.DoesNotContain("stderr-20240105.log", logs);
            Assert.Contains($"stderr-{DateTime.UtcNow:yyyyMMdd}.log", logs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", originalForce);
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", originalDisable);
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", originalLogDir);
            TestProjectHelper.DeleteDirectory(logDir);
        }
    }

    [Fact]
    public void Run_ForcedGlobalToolLogging_CanBeDisabledExplicitly()
    {
        var logDir = Path.Combine(Path.GetTempPath(), $"cdidx_global_tool_log_disabled_{Guid.NewGuid():N}");
        Directory.CreateDirectory(logDir);
        var originalForce = Environment.GetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG");
        var originalDisable = Environment.GetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG");
        var originalLogDir = Environment.GetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR");

        try
        {
            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", "1");
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", "1");
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", logDir);

            var (exitCode, stdout, stderr) = CaptureConsole(() => ProgramRunner.Run(
                ["definitely-not-a-command"],
                appVersion: "1.10.0"));

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("Unknown command: definitely-not-a-command", stderr);
            Assert.Empty(Directory.GetFiles(logDir, "stderr-*.log", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_FORCE_GLOBAL_TOOL_LOG", originalForce);
            Environment.SetEnvironmentVariable("CDIDX_DISABLE_PERSISTENT_LOG", originalDisable);
            Environment.SetEnvironmentVariable("CDIDX_GLOBAL_TOOL_LOG_DIR", originalLogDir);
            TestProjectHelper.DeleteDirectory(logDir);
        }
    }

    [Fact]
    public void Run_StatusJson_UsesSourceGeneratedSerializerWhenReflectionResolverFails()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("program_runner_json_status");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            TestProjectHelper.InsertIndexedFile(dbPath, "src/App.cs", "csharp", "class App {}\n");

            var options = CreateTrimmedFailureJsonOptions();
            var (exitCode, stdout, stderr) = CaptureConsole(() => ProgramRunner.Run(
                ["status", "--db", dbPath, "--json"],
                options,
                "1.10.0"));

            Assert.Equal(CommandExitCodes.Success, exitCode);
            using var document = JsonDocument.Parse(stdout);
            Assert.Equal(1, document.RootElement.GetProperty("files").GetInt32());
            Assert.Equal("1.10.0", document.RootElement.GetProperty("version").GetString());
            Assert.Equal(string.Empty, stderr);
            Assert.DoesNotContain("database error", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_IndexJson_UsesSourceGeneratedSerializerWhenReflectionResolverFails()
    {
        var missingProject = Path.Combine(Path.GetTempPath(), $"program_runner_missing_{Guid.NewGuid():N}");
        var options = CreateTrimmedFailureJsonOptions();

        var (exitCode, stdout, stderr) = CaptureConsole(() => ProgramRunner.Run(
            [missingProject, "--json"],
            options,
            "1.10.0"));

        Assert.Equal(CommandExitCodes.NotFound, exitCode);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("directory not found", document.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stderr);
        Assert.DoesNotContain("directory not found", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(new[] { "--color=always", "status" }, ColorMode.Always, new[] { "status" })]
    [InlineData(new[] { "status", "--color", "never" }, ColorMode.Never, new[] { "status" })]
    [InlineData(new[] { "search", "--color=auto", "foo" }, ColorMode.Auto, new[] { "search", "foo" })]
    [InlineData(new[] { "status" }, ColorMode.Auto, new[] { "status" })]
    public void TryConsumeColorFlag_StripsFlagAndSetsMode(string[] input, ColorMode expectedMode, string[] expectedKept)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalMode = ConsoleUi.GetColorMode();
            try
            {
                var args = input;
                Assert.True(ProgramRunner.TryConsumeColorFlag(ref args, out var error));
                Assert.Empty(error);
                Assert.Equal(expectedMode, ConsoleUi.GetColorMode());
                Assert.Equal(expectedKept, args);
            }
            finally
            {
                ConsoleUi.SetColorMode(originalMode);
            }
        }
    }

    [Fact]
    public void TryConsumeColorFlag_InvalidValue_ReturnsError()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalMode = ConsoleUi.GetColorMode();
            try
            {
                var args = new[] { "search", "--color=sparkly" };
                Assert.False(ProgramRunner.TryConsumeColorFlag(ref args, out var error));
                Assert.Contains("sparkly", error);
            }
            finally
            {
                ConsoleUi.SetColorMode(originalMode);
            }
        }
    }

    [Fact]
    public void TryConsumeColorFlag_MissingValue_ReturnsError()
    {
        lock (TestConsoleLock.Gate)
        {
            var originalMode = ConsoleUi.GetColorMode();
            try
            {
                var args = new[] { "search", "--color" };
                Assert.False(ProgramRunner.TryConsumeColorFlag(ref args, out var error));
                Assert.Contains("requires a value", error);
            }
            finally
            {
                ConsoleUi.SetColorMode(originalMode);
            }
        }
    }

    [Fact]
    public void TryConsumeColorFlag_AfterDoubleDash_PreservesQueryEscape()
    {
        // `--` is the query-escape sentinel; anything after it must be left in
        // place so subcommands like `cdidx search -- --color=auto` can treat
        // `--color=auto` as a literal query argument rather than the global flag.
        lock (TestConsoleLock.Gate)
        {
            var originalMode = ConsoleUi.GetColorMode();
            try
            {
                var args = new[] { "search", "--", "--color=auto" };
                Assert.True(ProgramRunner.TryConsumeColorFlag(ref args, out var error));
                Assert.Empty(error);
                Assert.Equal(ColorMode.Auto, ConsoleUi.GetColorMode());
                Assert.Equal(new[] { "search", "--", "--color=auto" }, args);
            }
            finally
            {
                ConsoleUi.SetColorMode(originalMode);
            }
        }
    }

    [Fact]
    public void TryConsumeColorFlag_FlagBeforeDoubleDash_StillConsumed()
    {
        // The global flag must still be consumed when it appears before `--`,
        // even if the same string appears again afterward as a literal query.
        lock (TestConsoleLock.Gate)
        {
            var originalMode = ConsoleUi.GetColorMode();
            try
            {
                var args = new[] { "search", "--color=always", "--", "--color=auto" };
                Assert.True(ProgramRunner.TryConsumeColorFlag(ref args, out var error));
                Assert.Empty(error);
                Assert.Equal(ColorMode.Always, ConsoleUi.GetColorMode());
                Assert.Equal(new[] { "search", "--", "--color=auto" }, args);
            }
            finally
            {
                ConsoleUi.SetColorMode(originalMode);
            }
        }
    }

    [Fact]
    public void Run_InvalidColorValue_ReturnsUsageError()
    {
        var (exitCode, _, stderr) = CaptureConsole(() => ProgramRunner.Run(
            ["--color=sparkly", "status"],
            appVersion: "1.10.0"));

        Assert.Equal(CommandExitCodes.UsageError, exitCode);
        Assert.Contains("invalid --color value `sparkly`", stderr);
        Assert.Contains("Hint:", stderr);
    }

    [Fact]
    public void IsTrimmedJsonUnavailable_RecognizesReflectionDisabledMessage()
    {
        var ex = new InvalidOperationException(JsonOutputFailure.ReflectionDisabledMessage);

        Assert.True(JsonOutputFailure.IsTrimmedJsonUnavailable(ex));
    }

    private static JsonSerializerOptions CreateTrimmedFailureJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new ThrowingResolver(),
    };

    private static (int ExitCode, string Stdout, string Stderr) CaptureConsole(Func<int> action)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                return (action(), stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private sealed class ThrowingResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            throw new InvalidOperationException(JsonOutputFailure.ReflectionDisabledMessage);
    }
}
