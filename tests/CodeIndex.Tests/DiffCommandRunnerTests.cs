using System.Text.Json;
using CodeIndex.Cli;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class DiffCommandRunnerTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Run_JsonDiffReportsSyntheticDatabaseDrift_Issue1724()
    {
        var leftRoot = TestProjectHelper.CreateTempProject("cdidx_diff_left");
        var rightRoot = TestProjectHelper.CreateTempProject("cdidx_diff_right");
        try
        {
            var leftDb = SeedDb(leftRoot, includeExtraFile: false);
            var rightDb = SeedDb(rightRoot, includeExtraFile: true);

            var (exitCode, output) = RunWithCapturedOut([leftDb, rightDb, "--json", "--limit", "5"]);

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            Assert.Equal("different", root.GetProperty("status").GetString());
            Assert.False(root.GetProperty("identical").GetBoolean());
            Assert.Equal(50, root.GetProperty("summary").GetProperty("left_file_count").GetInt64());
            Assert.Equal(51, root.GetProperty("summary").GetProperty("right_file_count").GetInt64());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("file_count_delta").GetInt64());
            Assert.Contains(
                root.GetProperty("files_only_in_right").EnumerateArray(),
                item => item.GetString() == "src/Extra.cs");
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(leftRoot);
            TestProjectHelper.DeleteDirectory(rightRoot);
        }
    }

    [Fact]
    public void Run_ReturnsSchemaMismatchExitCodeBeforeDriftExitCode_Issue1724()
    {
        var leftRoot = TestProjectHelper.CreateTempProject("cdidx_diff_schema_left");
        var rightRoot = TestProjectHelper.CreateTempProject("cdidx_diff_schema_right");
        try
        {
            var leftDb = SeedDb(leftRoot, includeExtraFile: false);
            var rightDb = SeedDb(rightRoot, includeExtraFile: false);
            SetUserVersion(rightDb, 999);

            var (exitCode, output) = RunWithCapturedOut([leftDb, rightDb, "--summary-only"]);

            Assert.Equal(2, exitCode);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("summary").GetProperty("schema_versions_equal").GetBoolean());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(leftRoot);
            TestProjectHelper.DeleteDirectory(rightRoot);
        }
    }

    [Fact]
    public void Run_DetectsSameCountSymbolDriftWithoutDetailedMode_Issue1724()
    {
        var leftRoot = TestProjectHelper.CreateTempProject("cdidx_diff_symbol_left");
        var rightRoot = TestProjectHelper.CreateTempProject("cdidx_diff_symbol_right");
        try
        {
            var leftDb = TestProjectHelper.CreateProjectDb(leftRoot);
            var rightDb = TestProjectHelper.CreateProjectDb(rightRoot);
            TestProjectHelper.InsertIndexedFile(leftDb, "src/Same.cs", "csharp", "public class LeftOnly { public void Run() { } }");
            TestProjectHelper.InsertIndexedFile(rightDb, "src/Same.cs", "csharp", "public class RightOnly { public void Run() { } }");

            var (exitCode, output) = RunWithCapturedOut([leftDb, rightDb, "--summary-only"]);

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("different", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("file_count_delta").GetInt64());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("symbol_count_delta").GetInt64());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(leftRoot);
            TestProjectHelper.DeleteDirectory(rightRoot);
        }
    }

    [Fact]
    public void Run_DetectsSameCountReferenceDriftWithoutDetailedMode_Issue1724()
    {
        var leftRoot = TestProjectHelper.CreateTempProject("cdidx_diff_ref_left");
        var rightRoot = TestProjectHelper.CreateTempProject("cdidx_diff_ref_right");
        try
        {
            var leftDb = TestProjectHelper.CreateProjectDb(leftRoot);
            var rightDb = TestProjectHelper.CreateProjectDb(rightRoot);
            TestProjectHelper.InsertIndexedFile(leftDb, "src/Same.cs", "csharp", "public class Same { public void Run() { Foo(); } }");
            TestProjectHelper.InsertIndexedFile(rightDb, "src/Same.cs", "csharp", "public class Same { public void Run() { Bar(); } }");

            var (exitCode, output) = RunWithCapturedOut([leftDb, rightDb, "--summary-only"]);

            Assert.Equal(1, exitCode);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("different", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("file_count_delta").GetInt64());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("reference_count_delta").GetInt64());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(leftRoot);
            TestProjectHelper.DeleteDirectory(rightRoot);
        }
    }

    [Fact]
    public void Run_ReturnsUnreadableExitCodeForMissingDatabase_Issue1724()
    {
        var root = TestProjectHelper.CreateTempProject("cdidx_diff_missing");
        try
        {
            var db = SeedDb(root, includeExtraFile: false);
            var missing = Path.Combine(root, "missing.db");

            var (exitCode, output) = RunWithCapturedOut([db, missing, "--summary-only"]);

            Assert.Equal(3, exitCode);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("error", document.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(root);
        }
    }

    private static string SeedDb(string projectRoot, bool includeExtraFile)
    {
        var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
        for (var i = 0; i < 50; i++)
        {
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                $"src/File{i:00}.cs",
                "csharp",
                $$"""
                public static class File{{i:00}}
                {
                    public static void Run() { }
                }
                """);
        }

        if (includeExtraFile)
        {
            TestProjectHelper.InsertIndexedFile(
                dbPath,
                "src/Extra.cs",
                "csharp",
                """
                public static class Extra
                {
                    public static void Run() { }
                }
                """);
        }

        return dbPath;
    }

    private static void SetUserVersion(string dbPath, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
        }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version}";
        command.ExecuteNonQuery();
    }

    private (int ExitCode, string Output) RunWithCapturedOut(string[] args)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        lock (TestConsoleLock.Gate)
        {
            try
            {
                Console.SetOut(writer);
                var exitCode = DiffCommandRunner.Run(args, _jsonOptions);
                return (exitCode, writer.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
