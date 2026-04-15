using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CodeIndex.Cli;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class ProgramRunnerTests
{
    [Fact]
    public void Run_StatusJsonTrimFailure_ReturnsFeatureUnavailableInsteadOfDatabaseError()
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

            Assert.Equal(CommandExitCodes.FeatureUnavailable, exitCode);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("--json is not available on this trimmed build", stderr);
            Assert.Contains("use `cdidx mcp` for structured output", stderr);
            Assert.DoesNotContain("database error", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_IndexJsonTrimFailure_ReturnsFeatureUnavailable()
    {
        var missingProject = Path.Combine(Path.GetTempPath(), $"program_runner_missing_{Guid.NewGuid():N}");
        var options = CreateTrimmedFailureJsonOptions();

        var (exitCode, stdout, stderr) = CaptureConsole(() => ProgramRunner.Run(
            [missingProject, "--json"],
            options,
            "1.10.0"));

        Assert.Equal(CommandExitCodes.FeatureUnavailable, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("--json is not available on this trimmed build", stderr);
        Assert.DoesNotContain("directory not found", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTrimmedJsonUnavailable_RecognizesReflectionDisabledMessage()
    {
        var ex = new InvalidOperationException(JsonOutputFailure.ReflectionDisabledMessage);

        Assert.True(JsonOutputFailure.IsTrimmedJsonUnavailable(ex));
    }

    private static JsonSerializerOptions CreateTrimmedFailureJsonOptions() => new()
    {
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
