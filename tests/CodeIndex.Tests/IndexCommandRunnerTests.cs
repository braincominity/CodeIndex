using System.Text.Json;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Models;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for indexing command argument handling.
/// インデックスコマンドの引数処理テスト。
/// </summary>
[Collection("SQLite pool sensitive")]
public class IndexCommandRunnerTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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
        int exitCode;
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                exitCode = IndexCommandRunner.Run(["--help"], new JsonSerializerOptions());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        Assert.Equal(CommandExitCodes.Success, exitCode);
    }

    [Fact]
    public void Run_MissingDirectory_PrintsActionableHint()
    {
        var missingProject = Path.Combine(Path.GetTempPath(), $"cdidx_missing_project_{Guid.NewGuid():N}");
        var (exitCode, _, stderr) = RunAndCaptureStreams([missingProject]);

        Assert.Equal(CommandExitCodes.NotFound, exitCode);
        Assert.Contains("Error: directory not found", stderr);
        Assert.Contains("rerun `cdidx index <projectPath>` with an existing directory", stderr);
    }

    [Fact]
    public void Run_MissingDirectory_JsonIncludesHint()
    {
        var missingProject = Path.Combine(Path.GetTempPath(), $"cdidx_missing_project_{Guid.NewGuid():N}");

        var (exitCode, json) = RunAndCaptureJson([missingProject, "--json"]);

        Assert.Equal(CommandExitCodes.NotFound, exitCode);
        Assert.Equal("error", json.GetProperty("status").GetString());
        Assert.Contains("directory not found", json.GetProperty("message").GetString());
        Assert.Contains("rerun `cdidx index <projectPath>`", json.GetProperty("hint").GetString());
    }

    [Fact]
    public void Run_RebuildWithCommits_PrintsActionableHint()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var (exitCode, _, stderr) = RunAndCaptureStreams([projectRoot, "--rebuild", "--commits", "HEAD"]);

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Contains("--rebuild cannot be used with --commits or --files", stderr);
            Assert.Contains("drop `--rebuild`", stderr);
            Assert.Contains("cdidx index <projectPath> --rebuild", stderr);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_RebuildWithCommits_JsonIncludesHint()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--rebuild", "--commits", "HEAD", "--json"]);

            Assert.Equal(CommandExitCodes.UsageError, exitCode);
            Assert.Equal("error", json.GetProperty("status").GetString());
            Assert.Contains("--rebuild cannot be used with --commits or --files", json.GetProperty("message").GetString());
            Assert.Contains("cdidx index <projectPath> --rebuild", json.GetProperty("hint").GetString());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_ExplicitDb_PersistsIndexedProjectRootMetadata()
    {
        var projectRoot = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_explicit_root_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.py"), "print('hello')\n");

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--db", dbPath, "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());

            using var db = new DbContext(dbPath);
            Assert.Equal(Path.GetFullPath(projectRoot), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
        }
        finally
        {
            DeleteDirectory(projectRoot);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_NoOpAgainstSharedExplicitDb_DoesNotRewriteIndexedProjectRoot()
    {
        var projectRootA = CreateTempProject();
        var projectRootB = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_shared_root_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(Path.Combine(projectRootA, "app.py"), "print('from a')\n");
            var initialExitCode = IndexCommandRunner.Run([projectRootA, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            Directory.CreateDirectory(Path.Combine(projectRootB, "docs"));
            File.WriteAllText(Path.Combine(projectRootB, "docs", "readme.txt"), "not indexable\n");

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRootB, "--db", dbPath, "--files", "docs/readme.txt", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("skipped").GetInt32());
            Assert.True(updateJson.GetProperty("graph_table_available").GetBoolean());
            Assert.True(updateJson.GetProperty("issues_table_available").GetBoolean());
            Assert.True(updateJson.GetProperty("fold_ready").GetBoolean());

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRootA), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var stdout = new StringWriter();
                try
                {
                    Console.SetOut(stdout);
                    var statusExitCode = QueryCommandRunner.RunStatus(["--db", dbPath, "--json"], _jsonOptions);
                    Assert.Equal(CommandExitCodes.Success, statusExitCode);
                    using var document = JsonDocument.Parse(stdout.ToString());
                    Assert.Equal(Path.GetFullPath(projectRootA), document.RootElement.GetProperty("project_root").GetString());
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }
        }
        finally
        {
            DeleteDirectory(projectRootA);
            DeleteDirectory(projectRootB);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_NoOpAgainstSharedExplicitDb_PurgesUnsupportedReferencesWithoutRewritingIndexedProjectRoot()
    {
        var projectRootA = CreateTempProject();
        var projectRootB = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_shared_stale_refs_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(Path.Combine(projectRootA, "app.py"), "print('from a')\n");
            var initialExitCode = IndexCommandRunner.Run([projectRootA, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "docs/readme.md",
                    Lang = "markdown",
                    Size = 12,
                    Lines = 1,
                    Modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Checksum = "stale-edge",
                });
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "LegacyLink",
                        ReferenceKind = "call",
                        Line = 1,
                        Column = 1,
                        Context = "LegacyLink",
                    },
                ]);
            }

            long CountReferences()
            {
                using var db = new DbContext(dbPath);
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM symbol_references";
                return (long)cmd.ExecuteScalar()!;
            }

            Assert.Equal(1, CountReferences());

            Directory.CreateDirectory(Path.Combine(projectRootB, "docs"));
            File.WriteAllText(Path.Combine(projectRootB, "docs", "readme.txt"), "not indexable\n");

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRootB, "--db", dbPath, "--files", "docs/readme.txt", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("skipped").GetInt32());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("references_total").GetInt32());
            Assert.True(updateJson.GetProperty("graph_table_available").GetBoolean());
            Assert.True(updateJson.GetProperty("issues_table_available").GetBoolean());
            Assert.True(updateJson.GetProperty("fold_ready").GetBoolean());

            Assert.Equal(0, CountReferences());

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRootA), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var stdout = new StringWriter();
                try
                {
                    Console.SetOut(stdout);
                    var statusExitCode = QueryCommandRunner.RunStatus(["--db", dbPath, "--json"], _jsonOptions);
                    Assert.Equal(CommandExitCodes.Success, statusExitCode);
                    using var document = JsonDocument.Parse(stdout.ToString());
                    Assert.Equal(Path.GetFullPath(projectRootA), document.RootElement.GetProperty("project_root").GetString());
                    Assert.Equal(0, document.RootElement.GetProperty("references").GetInt32());
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }
        }
        finally
        {
            DeleteDirectory(projectRootA);
            DeleteDirectory(projectRootB);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_ExplicitDb_RealMutationRewritesIndexedProjectRootMetadata()
    {
        var projectRootA = CreateTempProject();
        var projectRootB = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_shared_rewrite_root_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRootA, "init");
            File.WriteAllText(Path.Combine(projectRootA, "app.cs"), "public class App { public void Run() { } }\n");
            RunGit(projectRootA, "add", ".");
            RunGit(projectRootA, "commit", "-m", "init-a");
            var headA = RunGitCaptureStdOut(projectRootA, "rev-parse", "HEAD").Trim();

            RunGit(projectRootB, "init");
            var sourcePathB = Path.Combine(projectRootB, "app.cs");
            File.WriteAllText(sourcePathB, "public class App { public void Run() { } public void Extra() { } }\n");
            RunGit(projectRootB, "add", ".");
            RunGit(projectRootB, "commit", "-m", "init-b");
            var headB = RunGitCaptureStdOut(projectRootB, "rev-parse", "HEAD").Trim();
            File.SetLastWriteTimeUtc(sourcePathB, DateTime.UtcNow.AddSeconds(2));

            var initialExitCode = IndexCommandRunner.Run([projectRootA, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRootA), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRootB, "--db", dbPath, "--files", "app.cs", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRootB), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Equal(Path.GetFullPath(projectRootB), statusJson.GetProperty("project_root").GetString());
            Assert.Equal(headB, statusJson.GetProperty("git_head").GetString());
            Assert.NotEqual(headA, statusJson.GetProperty("git_head").GetString());
            Assert.False(statusJson.GetProperty("git_is_dirty").GetBoolean());
        }
        finally
        {
            DeleteDirectory(projectRootA);
            DeleteDirectory(projectRootB);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_LegacySharedExplicitDb_NoOpDoesNotHijackMissingIndexedProjectRootMetadata()
    {
        var projectRootA = CreateTempProject();
        var projectRootB = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_legacy_explicit_noop_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRootA, "init");
            File.WriteAllText(Path.Combine(projectRootA, "app.py"), "print('hello')\n");
            RunGit(projectRootA, "add", ".");
            RunGit(projectRootA, "commit", "-m", "init");

            var initialExitCode = IndexCommandRunner.Run([projectRootA, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            DeleteIndexedProjectRootMetadata(dbPath);

            Directory.CreateDirectory(Path.Combine(projectRootB, "docs"));
            File.WriteAllText(Path.Combine(projectRootB, "docs", "readme.txt"), "not indexable\n");

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRootB, "--db", dbPath, "--files", "docs/readme.txt", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("skipped").GetInt32());

            using (var db = new DbContext(dbPath))
            {
                Assert.Null(db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Null(statusJson.GetProperty("project_root").GetString());
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            DeleteDirectory(projectRootA);
            DeleteDirectory(projectRootB);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_LegacyExplicitDb_SuccessfulFileUpdateBackfillsIndexedProjectRootMetadata()
    {
        var projectRoot = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_legacy_explicit_update_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRoot, "init");
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            DeleteIndexedProjectRootMetadata(dbPath);
            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRoot, "--db", dbPath, "--files", "app.cs", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRoot), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Equal(Path.GetFullPath(projectRoot), statusJson.GetProperty("project_root").GetString());
            Assert.False(string.IsNullOrWhiteSpace(statusJson.GetProperty("git_head").GetString()));
        }
        finally
        {
            DeleteDirectory(projectRoot);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_LegacyExplicitDb_PurgeOnlyNoOpDoesNotBackfillIndexedProjectRootMetadata()
    {
        var projectRoot = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_legacy_explicit_purge_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRoot, "init");
            File.WriteAllText(Path.Combine(projectRoot, "app.py"), "print('hello')\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            DeleteIndexedProjectRootMetadata(dbPath);
            using (var db = new DbContext(dbPath))
            {
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "docs/readme.md",
                    Lang = "markdown",
                    Size = 12,
                    Lines = 1,
                    Modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Checksum = "stale-edge",
                });
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "LegacyLink",
                        ReferenceKind = "call",
                        Line = 1,
                        Column = 1,
                        Context = "LegacyLink",
                    },
                ]);
            }

            Directory.CreateDirectory(Path.Combine(projectRoot, "docs"));
            File.WriteAllText(Path.Combine(projectRoot, "docs", "readme.txt"), "not indexable\n");

            var (updateExitCode, updateJson) = RunAndCaptureJson([projectRoot, "--db", dbPath, "--files", "docs/readme.txt", "--json"]);
            Assert.Equal(CommandExitCodes.Success, updateExitCode);
            Assert.Equal("success", updateJson.GetProperty("status").GetString());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(1, updateJson.GetProperty("summary").GetProperty("skipped").GetInt32());
            Assert.Equal(0, updateJson.GetProperty("summary").GetProperty("references_total").GetInt32());

            using (var db = new DbContext(dbPath))
            {
                Assert.Null(db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Null(statusJson.GetProperty("project_root").GetString());
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            DeleteDirectory(projectRoot);
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_LegacyExplicitDb_RollbackedFirstMutationDoesNotBackfillIndexedProjectRootMetadata()
    {
        var projectRoot = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_legacy_explicit_rollback_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRoot, "init");
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            DeleteIndexedProjectRootMetadata(dbPath);
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TRIGGER fail_update
                    BEFORE UPDATE ON files
                    BEGIN
                        SELECT RAISE(FAIL, 'boom');
                    END;
                    """;
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--db", dbPath, "--files", "app.cs", "--json"]);
            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("partial", json.GetProperty("status").GetString());

            using (var db = new DbContext(dbPath))
            {
                Assert.Null(db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Null(statusJson.GetProperty("project_root").GetString());
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_head").ValueKind);
            Assert.Equal(JsonValueKind.Null, statusJson.GetProperty("git_is_dirty").ValueKind);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateMode_FailedFirstMutation_DemotesReadinessBeforeRollback()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TRIGGER fail_update
                    BEFORE UPDATE ON files
                    BEGIN
                        SELECT RAISE(FAIL, 'boom');
                    END;
                    """;
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "app.cs", "--json"]);
            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("partial", json.GetProperty("status").GetString());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.False(json.GetProperty("issues_table_available").GetBoolean());
            Assert.False(json.GetProperty("fold_ready").GetBoolean());

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.False(statusJson.GetProperty("graph_table_available").GetBoolean());
            Assert.False(statusJson.GetProperty("issues_table_available").GetBoolean());
            Assert.False(statusJson.GetProperty("fold_ready").GetBoolean());

            using var verify = OpenNonPoolingConnection(dbPath);
            verify.Open();
            using var verifyCmd = verify.CreateCommand();
            verifyCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)verifyCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.GraphReadyFlag);
            Assert.Equal(0, userVersion & DbContext.IssuesReadyFlag);
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScanExplicitDb_FailedFirstMutation_DoesNotRewriteIndexedProjectRootMetadata()
    {
        var projectRootA = CreateTempProject();
        var projectRootB = CreateTempProject();
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_fullscan_explicit_rollback_{Guid.NewGuid():N}.db");
        try
        {
            RunGit(projectRootA, "init");
            var sourcePathA = Path.Combine(projectRootA, "app.cs");
            File.WriteAllText(sourcePathA, "public class AppA { public void Run() { } }\n");
            RunGit(projectRootA, "add", ".");
            RunGit(projectRootA, "commit", "-m", "init-a");
            var headA = RunGitCaptureStdOut(projectRootA, "rev-parse", "HEAD").Trim();

            RunGit(projectRootB, "init");
            var sourcePathB = Path.Combine(projectRootB, "app.cs");
            File.WriteAllText(sourcePathB, "public class AppB { public void Run() { } public void Extra() { } }\n");
            RunGit(projectRootB, "add", ".");
            RunGit(projectRootB, "commit", "-m", "init-b");
            File.SetLastWriteTimeUtc(sourcePathB, DateTime.UtcNow.AddSeconds(2));

            var initialExitCode = IndexCommandRunner.Run([projectRootA, "--db", dbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TRIGGER fail_update
                    BEFORE UPDATE ON files
                    BEGIN
                        SELECT RAISE(FAIL, 'boom');
                    END;
                    """;
                cmd.ExecuteNonQuery();
            }

            var (exitCode, json) = RunAndCaptureJson([projectRootB, "--db", dbPath, "--json"]);
            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("partial", json.GetProperty("status").GetString());

            using (var db = new DbContext(dbPath))
            {
                Assert.Equal(Path.GetFullPath(projectRootA), db.GetMetaString(DbContext.IndexedProjectRootMetaKey));
            }

            var (_, statusJson) = RunStatusAndCaptureJson(["--db", dbPath, "--json"]);
            Assert.Equal(Path.GetFullPath(projectRootA), statusJson.GetProperty("project_root").GetString());
            Assert.Equal(headA, statusJson.GetProperty("git_head").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRootA);
            DeleteDirectory(projectRootB);
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void RunBackfillFold_MissingDb_PrintsActionableHint()
    {
        var missingDb = Path.Combine(Path.GetTempPath(), $"cdidx_missing_db_{Guid.NewGuid():N}.db");
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
                var exitCode = IndexCommandRunner.RunBackfillFold(["--db", missingDb], _jsonOptions);

                Assert.Equal(CommandExitCodes.NotFound, exitCode);
                Assert.Contains("database not found", stderr.ToString());
                Assert.Contains("Point `--db` at an existing `codeindex.db`", stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    [Fact]
    public void RunBackfillFold_MissingDb_JsonIncludesHint()
    {
        var missingDb = Path.Combine(Path.GetTempPath(), $"cdidx_missing_db_{Guid.NewGuid():N}.db");
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var stdout = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                var exitCode = IndexCommandRunner.RunBackfillFold(["--db", missingDb, "--json"], _jsonOptions);
                using var document = JsonDocument.Parse(stdout.ToString());
                var json = document.RootElement;

                Assert.Equal(CommandExitCodes.NotFound, exitCode);
                Assert.Equal("error", json.GetProperty("status").GetString());
                Assert.Contains("database not found", json.GetProperty("message").GetString());
                Assert.Contains("Point `--db` at an existing `codeindex.db`", json.GetProperty("hint").GetString());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    [Fact]
    public void RunBackfillFold_BackfillsLegacyRowsAndStampsFoldReady()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_backfill_fold_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "src/app.py",
                    Lang = "python",
                    Size = 64,
                    Lines = 2,
                    Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                });
                writer.InsertSymbols([
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "café_init", Line = 1, StartLine = 1, EndLine = 1 },
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "bootstrap", Line = 2, StartLine = 2, EndLine = 2 },
                ]);
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "CAFÉ_INIT",
                        ReferenceKind = "call",
                        Line = 2,
                        Column = 5,
                        Context = "CAFÉ_INIT()",
                        ContainerKind = "function",
                        ContainerName = "bootstrap",
                    },
                ]);
                writer.MarkGraphReady();
                writer.MarkIssuesReady();
            }

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE symbols SET name_folded = NULL; UPDATE symbol_references SET symbol_name_folded = NULL, container_name_folded = NULL; PRAGMA user_version = 3";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            JsonElement json;
            int exitCode;
            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var writer = new StringWriter();
                try
                {
                    Console.SetOut(writer);
                    exitCode = IndexCommandRunner.RunBackfillFold(["--db", dbPath, "--json"], _jsonOptions);
                    using var document = JsonDocument.Parse(writer.ToString());
                    json = document.RootElement.Clone();
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(2, json.GetProperty("symbols").GetInt32());
            Assert.Equal(1, json.GetProperty("symbol_references").GetInt32());
            Assert.True(json.GetProperty("rewrite_all").GetBoolean());
            Assert.True(json.GetProperty("verified").GetBoolean());
            Assert.Equal(3, json.GetProperty("user_version_before").GetInt32());
            Assert.Equal(7, json.GetProperty("user_version_after").GetInt32());
            Assert.True(json.GetProperty("fold_ready").GetBoolean());

            using var verifyDb = new DbContext(dbPath);
            verifyDb.TryMigrateForRead();
            var reader = new DbReader(verifyDb.Connection);
            Assert.True(reader._foldReady);
            Assert.Single(reader.SearchSymbols(["ＣＡＦÉ_ＩＮＩＴ"], limit: 10, exact: true));
            Assert.Single(reader.GetCallers("ＣＡＦÉ_ＩＮＩＴ", limit: 10, exact: true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void RunBackfillFold_RewritesAllWhenOnlyFingerprintDrifted()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_backfill_fold_fp_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "src/app.py",
                    Lang = "python",
                    Size = 64,
                    Lines = 2,
                    Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                });
                writer.InsertSymbols([
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "café_init", Line = 1, StartLine = 1, EndLine = 1 },
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "bootstrap", Line = 2, StartLine = 2, EndLine = 2 },
                ]);
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "CAFÉ_INIT",
                        ReferenceKind = "call",
                        Line = 2,
                        Column = 5,
                        Context = "CAFÉ_INIT()",
                        ContainerKind = "function",
                        ContainerName = "bootstrap",
                    },
                ]);
                writer.MarkGraphReady();
                writer.MarkIssuesReady();
                writer.MarkFoldReady();
                writer.SetMeta("fold_key_fingerprint", "DEADBEEFDEADBEEF");
            }

            JsonElement json;
            int exitCode;
            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var writer = new StringWriter();
                try
                {
                    Console.SetOut(writer);
                    exitCode = IndexCommandRunner.RunBackfillFold(["--db", dbPath, "--json"], _jsonOptions);
                    using var document = JsonDocument.Parse(writer.ToString());
                    json = document.RootElement.Clone();
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(2, json.GetProperty("symbols").GetInt32());
            Assert.Equal(1, json.GetProperty("symbol_references").GetInt32());
            Assert.True(json.GetProperty("rewrite_all").GetBoolean());
            Assert.True(json.GetProperty("verified").GetBoolean());
            Assert.True(json.GetProperty("fold_ready").GetBoolean());

            using var verifyDb = new DbContext(dbPath);
            verifyDb.TryMigrateForRead();
            Assert.Equal(NameFold.Fingerprint(), verifyDb.GetMetaString("fold_key_fingerprint"));
            var reader = new DbReader(verifyDb.Connection);
            Assert.True(reader._foldReady);
            Assert.Single(reader.SearchSymbols(["ＣＡＦÉ_ＩＮＩＴ"], limit: 10, exact: true));
            Assert.Single(reader.GetCallers("ＣＡＦÉ_ＩＮＩＴ", limit: 10, exact: true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void RunBackfillFold_BlankFile_ReturnsDatabaseError()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_backfill_blank_{Guid.NewGuid():N}.db");
        File.WriteAllText(dbPath, string.Empty);

        try
        {
            JsonElement json;
            int exitCode;
            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var writer = new StringWriter();
                try
                {
                    Console.SetOut(writer);
                    exitCode = IndexCommandRunner.RunBackfillFold(["--db", dbPath, "--json"], _jsonOptions);
                    using var document = JsonDocument.Parse(writer.ToString());
                    json = document.RootElement.Clone();
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            Assert.Equal(CommandExitCodes.DatabaseError, exitCode);
            Assert.Equal("error", json.GetProperty("status").GetString());
            Assert.Contains("not an existing CodeIndex DB", json.GetProperty("message").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void RunBackfillFold_NonexistentFileUri_ReturnsNotFound()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_backfill_missing_{Guid.NewGuid():N}.db");
        var dbUri = new Uri(dbPath).AbsoluteUri;

        JsonElement json;
        int exitCode;
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                exitCode = IndexCommandRunner.RunBackfillFold(["--db", dbUri, "--json"], _jsonOptions);
                using var document = JsonDocument.Parse(writer.ToString());
                json = document.RootElement.Clone();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        Assert.Equal(CommandExitCodes.NotFound, exitCode);
        Assert.Equal("error", json.GetProperty("status").GetString());
        Assert.Contains("database not found", json.GetProperty("message").GetString());
    }

    [Fact]
    public void RunBackfillFold_LegacyDbWithoutCodeIndexMeta_Succeeds()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_backfill_legacy_no_meta_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                var fileId = writer.UpsertFile(new FileRecord
                {
                    Path = "src/app.py",
                    Lang = "python",
                    Size = 64,
                    Lines = 2,
                    Modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                });
                writer.InsertSymbols([
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "café_init", Line = 1, StartLine = 1, EndLine = 1 },
                    new SymbolRecord { FileId = fileId, Kind = "function", Name = "bootstrap", Line = 2, StartLine = 2, EndLine = 2 },
                ]);
                writer.InsertReferences([
                    new ReferenceRecord
                    {
                        FileId = fileId,
                        SymbolName = "CAFÉ_INIT",
                        ReferenceKind = "call",
                        Line = 2,
                        Column = 5,
                        Context = "CAFÉ_INIT()",
                        ContainerKind = "function",
                        ContainerName = "bootstrap",
                    },
                ]);
                using var dropMeta = db.Connection.CreateCommand();
                dropMeta.CommandText = "DROP TABLE codeindex_meta; UPDATE symbols SET name_folded = NULL; UPDATE symbol_references SET symbol_name_folded = NULL, container_name_folded = NULL; PRAGMA user_version = 3";
                dropMeta.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            JsonElement json;
            int exitCode;
            lock (TestConsoleLock.Gate)
            {
                var originalOut = Console.Out;
                using var writer = new StringWriter();
                try
                {
                    Console.SetOut(writer);
                    exitCode = IndexCommandRunner.RunBackfillFold(["--db", dbPath, "--json"], _jsonOptions);
                    using var document = JsonDocument.Parse(writer.ToString());
                    json = document.RootElement.Clone();
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal(2, json.GetProperty("symbols").GetInt32());
            Assert.Equal(1, json.GetProperty("symbol_references").GetInt32());
            Assert.True(json.GetProperty("fold_ready").GetBoolean());

            using var verifyDb = new DbContext(dbPath);
            verifyDb.TryMigrateForRead();
            Assert.Equal(NameFold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), verifyDb.GetMetaString("fold_key_version"));
            Assert.Equal(NameFold.Fingerprint(), verifyDb.GetMetaString("fold_key_fingerprint"));
            var reader = new DbReader(verifyDb.Connection);
            Assert.True(reader._foldReady);
            Assert.Single(reader.SearchSymbols(["ＣＡＦÉ_ＩＮＩＴ"], limit: 10, exact: true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void Run_UpdateFiles_AllowsProjectRelativePathsStartingWithDotDotName()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var hiddenDir = Path.Combine(projectRoot, "..hidden");
            Directory.CreateDirectory(hiddenDir);
            File.WriteAllText(Path.Combine(hiddenDir, "sample.cs"), "class Sample {}\n");

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "..hidden/sample.cs", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.Equal(1, json.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(0, json.GetProperty("summary").GetProperty("errors").GetInt32());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateFiles_SkipsPathsOutsideProjectRoot()
    {
        var projectRoot = CreateTempProject();
        var outsideFile = Path.Combine(Directory.GetParent(projectRoot)!.FullName, $"outside_{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(outsideFile, "class Outside {}\n");

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", $"../{Path.GetFileName(outsideFile)}", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.Equal(0, json.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.Equal(0, json.GetProperty("summary").GetProperty("errors").GetInt32());
        }
        finally
        {
            if (File.Exists(outsideFile))
                File.Delete(outsideFile);
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_WithIndexingErrors_PrintsRecoveryWarning()
    {
        var projectRoot = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { public void Run() { } }\n");
            File.WriteAllBytes(Path.Combine(projectRoot, "huge.py"), new byte[10 * 1024 * 1024 + 1]);

            var (exitCode, _, stderr) = RunCliInSubprocess([projectRoot], projectRoot);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Some files failed to index", stderr);
            Assert.Contains("rerun `cdidx index", stderr);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_WithIndexingErrors_PrintsRecoveryWarning()
    {
        var projectRoot = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            File.WriteAllBytes(Path.Combine(projectRoot, "huge.py"), new byte[10 * 1024 * 1024 + 1]);

            var (exitCode, _, stderr) = RunCliInSubprocess([projectRoot, "--files", "huge.py"], projectRoot);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Some files failed to update", stderr);
            Assert.Contains("rerun `cdidx index", stderr);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_JsonKeepsGraphAndIssuesReadyAfterHealthyScopedRefresh()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "app.cs", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.Equal(1, json.GetProperty("summary").GetProperty("updated").GetInt32());
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("issues_table_available").GetBoolean());
            Assert.True(json.GetProperty("fold_ready").GetBoolean());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_WithFiles_DoesNotPurgeOldRenamePathUnlessExplicitlyListed()
    {
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            var srcDir = Path.Combine(projectRoot, "src");
            Directory.CreateDirectory(srcDir);
            var oldPath = Path.Combine(srcDir, "OldName.cs");
            var newPath = Path.Combine(srcDir, "NewName.cs");

            File.WriteAllText(oldPath, "public class OldName { }\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "initial");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            File.Move(oldPath, newPath);
            File.WriteAllText(newPath, "public class NewName { }\n");

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "src/NewName.cs", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());

            var indexedPaths = ReadIndexedPaths(Path.Combine(projectRoot, ".cdidx", "codeindex.db"));
            Assert.Contains("src/OldName.cs", indexedPaths);
            Assert.Contains("src/NewName.cs", indexedPaths);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_WithCommits_PurgesOldRenamePath()
    {
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            var srcDir = Path.Combine(projectRoot, "src");
            Directory.CreateDirectory(srcDir);
            var oldPath = Path.Combine(srcDir, "OldName.cs");
            var newPath = Path.Combine(srcDir, "NewName.cs");

            File.WriteAllText(oldPath, "public class OldName { }\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "initial");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            File.Move(oldPath, newPath);
            File.WriteAllText(newPath, "public class NewName { }\n");
            RunGit(projectRoot, "add", "-A");
            RunGit(projectRoot, "commit", "-m", "rename");

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--commits", "HEAD", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());

            var indexedPaths = ReadIndexedPaths(Path.Combine(projectRoot, ".cdidx", "codeindex.db"));
            Assert.DoesNotContain("src/OldName.cs", indexedPaths);
            Assert.Contains("src/NewName.cs", indexedPaths);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_JsonReportsDegradedReadinessWhenBitsStayDown()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 0";
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "app.cs", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.False(json.GetProperty("graph_table_available").GetBoolean());
            Assert.False(json.GetProperty("issues_table_available").GetBoolean());
            Assert.False(json.GetProperty("fold_ready").GetBoolean());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_HumanOutputShowsDegradedReadinessWhenBitsStayDown()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 0";
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, output) = RunAndCaptureOutput([projectRoot, "--files", "app.cs"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Graph   : degraded", output);
            Assert.Contains("Issues  : degraded", output);
            Assert.Contains("Fold    : degraded", output);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_JsonPreservesGraphAndIssuesWhenOnlyFoldIsMissing()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE symbols SET name_folded = NULL;
                    UPDATE symbol_references SET symbol_name_folded = NULL, container_name_folded = NULL;
                    PRAGMA user_version = 3
                    """;
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, json) = RunAndCaptureJson([projectRoot, "--files", "app.cs", "--json"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("issues_table_available").GetBoolean());
            Assert.False(json.GetProperty("fold_ready").GetBoolean());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_DegradedWarningUsesResolvedProjectDbPathWhenCwdDiffers()
    {
        var projectRoot = CreateTempProject();
        var otherCwd = Path.Combine(Path.GetTempPath(), $"cdidx_other_cwd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherCwd);
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 0";
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, _, errorOutput) = RunCliInSubprocess([projectRoot, "--files", "app.cs"], otherCwd);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Index completed with degraded readiness", errorOutput);
            Assert.Contains("graph_table_available=false", errorOutput);
            Assert.Contains("issues_table_available=false", errorOutput);
            Assert.Contains("fold_ready=false", errorOutput);
            Assert.Contains($"cdidx status --db \"{dbPath}\" --json", errorOutput);
        }
        finally
        {
            if (Directory.Exists(otherCwd))
                DeleteDirectory(otherCwd);
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_DegradedWarningUsesExplicitDbPath()
    {
        var projectRoot = CreateTempProject();
        var customDbDir = Path.Combine(Path.GetTempPath(), $"cdidx_custom_db_{Guid.NewGuid():N}");
        Directory.CreateDirectory(customDbDir);
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");
            var customDbPath = Path.Combine(customDbDir, "custom-index.db");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--db", customDbPath, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            using (var conn = OpenNonPoolingConnection(customDbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 0";
                cmd.ExecuteNonQuery();
            }

            File.WriteAllText(sourcePath, "public class App { public void Run() { } public void Extra() { } }\n");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(2));

            var (exitCode, _, errorOutput) = RunCliInSubprocess([projectRoot, "--db", customDbPath, "--files", "app.cs"], projectRoot);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Index completed with degraded readiness", errorOutput);
            Assert.Contains("graph_table_available=false", errorOutput);
            Assert.Contains("issues_table_available=false", errorOutput);
            Assert.Contains("fold_ready=false", errorOutput);
            Assert.Contains($"cdidx status --db \"{customDbPath}\" --json", errorOutput);
        }
        finally
        {
            if (Directory.Exists(customDbDir))
                DeleteDirectory(customDbDir);
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_OutputReportsReadinessInJsonAndHumanModes()
    {
        var projectRoot = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { public void Run() { } }\n");

            var (jsonExitCode, json) = RunAndCaptureJson([projectRoot, "--json"]);
            Assert.Equal(CommandExitCodes.Success, jsonExitCode);
            Assert.Equal("success", json.GetProperty("status").GetString());
            Assert.True(json.GetProperty("graph_table_available").GetBoolean());
            Assert.True(json.GetProperty("issues_table_available").GetBoolean());
            Assert.True(json.GetProperty("fold_ready").GetBoolean());

            var (humanExitCode, output) = RunAndCaptureOutput([projectRoot]);
            Assert.Equal(CommandExitCodes.Success, humanExitCode);
            Assert.Contains("Graph   : ready", output);
            Assert.Contains("Issues  : ready", output);
            Assert.Contains("Fold    : ready", output);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_DegradedWarningSummarizesRemainingFoldGap()
    {
        var projectRoot = CreateTempProject();
        try
        {
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "public class App { public void Run() { } }\n");

            var initialExitCode = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, initialExitCode);

            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
            using (var conn = OpenNonPoolingConnection(dbPath))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE codeindex_meta SET value = '0' WHERE key = 'fold_key_version'";
                cmd.ExecuteNonQuery();
            }

            var (exitCode, _, errorOutput) = RunCliInSubprocess([projectRoot], projectRoot);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("Index completed with degraded readiness", errorOutput);
            Assert.Contains("fold_ready=false", errorOutput);
            Assert.DoesNotContain("graph_table_available=false", errorOutput);
            Assert.DoesNotContain("issues_table_available=false", errorOutput);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_WithAbsoluteDbPathInsideProject_WritesRepoRelativePatternToGitExclude()
    {
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            var exitCode = IndexCommandRunner.Run([projectRoot, "--db", dbPath, "--json"], _jsonOptions);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            var excludePath = Path.Combine(projectRoot, ".git", "info", "exclude");
            var excludeContent = File.ReadAllText(excludePath);
            Assert.Contains(".cdidx/", excludeContent);
            Assert.DoesNotContain(dbPath.Replace('\\', '/'), excludeContent);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_WithAbsoluteDbPathOutsideProject_DoesNotWriteAbsolutePathToGitExclude()
    {
        var projectRoot = CreateTempProject();
        var outsideDir = Path.Combine(Path.GetTempPath(), $"cdidx_external_db_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outsideDir);
            RunGit(projectRoot, "init");
            var dbPath = Path.Combine(outsideDir, "external.db");

            var exitCode = IndexCommandRunner.Run([projectRoot, "--db", dbPath, "--json"], _jsonOptions);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            var excludePath = Path.Combine(projectRoot, ".git", "info", "exclude");
            var excludeContent = File.ReadAllText(excludePath);
            Assert.DoesNotContain(dbPath.Replace('\\', '/'), excludeContent);
            Assert.DoesNotContain("/external.db", excludeContent);
        }
        finally
        {
            if (Directory.Exists(outsideDir))
                DeleteDirectory(outsideDir);
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_WithCommits_PrintsFullSyncGuidanceForHistoryRewrites()
    {
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            File.WriteAllText(Path.Combine(projectRoot, "tracked.cs"), "class Sample {}\n");
            RunGit(projectRoot, "add", "tracked.cs");
            RunGit(projectRoot, "commit", "-m", "initial");

            var (exitCode, output) = RunAndCaptureOutput([projectRoot, "--commits", "HEAD"]);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            Assert.Contains("prefer `cdidx .` over `--commits`", output);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_InWorktreeWithAbsoluteDbPathInsideProject_WritesRelativePatternToSharedExclude()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cdidx_worktree_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var mainGitDir = Path.Combine(tempRoot, "main", ".git");
        var worktreeRoot = Path.Combine(tempRoot, "wt");
        try
        {
            Directory.CreateDirectory(Path.Combine(mainGitDir, "info"));
            var worktreeGitDir = Path.Combine(mainGitDir, "worktrees", "wt");
            Directory.CreateDirectory(worktreeGitDir);
            File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

            Directory.CreateDirectory(worktreeRoot);
            File.WriteAllText(Path.Combine(worktreeRoot, ".git"), $"gitdir: {worktreeGitDir}");

            var dbPath = Path.Combine(worktreeRoot, ".cdidx", "codeindex.db");
            var exitCode = IndexCommandRunner.Run([worktreeRoot, "--db", dbPath, "--json"], _jsonOptions);

            Assert.Equal(CommandExitCodes.Success, exitCode);
            var sharedExcludePath = Path.Combine(mainGitDir, "info", "exclude");
            var excludeContent = File.ReadAllText(sharedExcludePath);
            Assert.Contains(".cdidx/", excludeContent);
            Assert.DoesNotContain(dbPath.Replace('\\', '/'), excludeContent);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Run_RebuildFlag_DropsAndRebuildsIndex()
    {
        var projectRoot = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");

            // First index / 初回インデックス
            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);

            // Add another file / ファイル追加
            File.WriteAllText(Path.Combine(projectRoot, "extra.cs"), "public class Extra { }");

            // Rebuild: should drop and re-scan all files / rebuild: 全削除して全ファイル再スキャン
            var (exitCode2, json) = RunAndCaptureJson([projectRoot, "--rebuild", "--json"]);
            Assert.Equal(CommandExitCodes.Success, exitCode2);
            // After rebuild, all files should be scanned (not skipped)
            // rebuild 後、全ファイルがスキャンされるべき（スキップなし）
            Assert.True(json.GetProperty("summary").GetProperty("files_total").GetInt32() >= 2);
            Assert.Equal(0, json.GetProperty("summary").GetProperty("files_skipped").GetInt32());
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_DoesNotStampFoldReadyWhenLegacyRowsRemain()
    {
        // Codex #86 review regression: on a legacy DB (pre-#86) opened by a new binary, the
        // incremental default of `cdidx index .` skips unchanged files via GetUnchangedFileId.
        // Their old rows stay NULL in name_folded. Stamping FoldReady would flip readers onto
        // the folded-equality path and silently miss those rows. Verify the stamp is withheld.
        // Legacy 行が残っているときに FoldReady が stamp されないことを確認する回帰テスト。
        var projectRoot = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");

            // Initial index — writes every row with name_folded populated, stamps FoldReady.
            // 初回 index: 全行 folded 付き、FoldReady stamp される。
            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            // Simulate pre-#86 legacy state: wipe folded columns + FoldReady bit on the existing
            // row to model an upgrade from a binary that did not populate name_folded yet.
            // pre-#86 を模擬: folded 列を NULL に戻し、FoldReady bit も落とす。
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE symbols SET name_folded = NULL; UPDATE symbol_references SET symbol_name_folded = NULL, container_name_folded = NULL; PRAGMA user_version = 3";
                cmd.ExecuteNonQuery();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Incremental re-run skips the unchanged file — legacy rows with NULL folded columns
            // still exist, so FoldReady MUST NOT be restamped.
            // 再 index は unchanged file を skip するため legacy 行が残る → FoldReady は立てない。
            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var verifyCmd = verify.CreateCommand();
            verifyCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)verifyCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_PreservesGraphAndIssuesOnPre86Db_WithoutStampingFold()
    {
        // Codex #86 second-pass regression: pre-#86 DB has user_version=3 (Graph|Issues).
        // Before this fix, `wasFullyReady = user_version == CurrentSchemaVersion (=7)` returned
        // false, so update mode cleared all 3 bits and restamped none — silently breaking
        // references/callers/callees/impact for the whole workspace even though only the
        // Fold bit was missing. After the fix, Graph/Issues must survive a partial update on
        // a pre-#86 DB; only Fold stays off (needs full rebuild).
        // pre-#86 DB (user_version=3) に対する partial update で Graph/Issues が落ちず、
        // Fold だけが未 stamp のまま残ることを確認する回帰テスト。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            // Initial full scan stamps user_version = CurrentSchemaVersion (7 = Graph|Issues|Fold).
            // 初回 full scan で user_version = 7（全 bit stamp）。
            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            // Simulate a pre-#86 DB by stripping the Fold bit (and wiping name_folded rows to
            // reflect a pre-#86 writer that did not populate them). User_version = 3.
            // pre-#86 DB を模擬: Fold bit を落とし、name_folded も NULL に戻す。
            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE symbols SET name_folded = NULL; UPDATE symbol_references SET symbol_name_folded = NULL, container_name_folded = NULL; PRAGMA user_version = 3";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Partial update via --files. Must NOT strip Graph/Issues trust just because Fold
            // was missing. After run: Graph+Issues still stamped, Fold stays off.
            // --files で partial update。Graph/Issues は維持、Fold は未 stamp のまま。
            var targetFile = Path.Combine(projectRoot, "app.cs");
            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--files", targetFile, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var verifyCmd = verify.CreateCommand();
            verifyCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)verifyCmd.ExecuteScalar()!;
            Assert.NotEqual(0, userVersion & DbContext.GraphReadyFlag);
            Assert.NotEqual(0, userVersion & DbContext.IssuesReadyFlag);
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_DoesNotRestampFoldReadyWhenFoldKeyVersionMismatches()
    {
        // Codex #86 fourth-pass regression: when a future NameFold.Version bump ships, the
        // stored fold_key_version on existing DBs becomes stale. A partial --files / --commits
        // update can only re-fold touched rows with the new version; untouched rows keep the
        // OLD folded keys. Restamping FoldReady + overwriting fold_key_version to the new
        // version would let the reader advertise full Unicode-exact readiness while silently
        // mismatching on untouched rows. The correct behavior is to leave FoldReady off until
        // a full --rebuild regenerates every row at the current version.
        // Simulate by writing an older fold_key_version into codeindex_meta before the update.
        // 将来の version bump 後の partial update で FoldReady を restamp しないことを確認する。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            // Initial index stamps the current fold-key version.
            // 初回 index で現在の fold-key version が stamp される。
            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            // Simulate a future version bump: the DB was stamped by a binary that wrote
            // fold_key_version=0 (pretend old). The current binary expects the latest
            // NameFold.Version
            // so the reader sees a mismatch and falls back to NOCASE. A partial update must
            // preserve that state, not silently restamp the current version on mixed-state rows.
            // version 不一致を模擬: codeindex_meta の fold_key_version を 0 に書き換え。
            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE codeindex_meta SET value = '0' WHERE key = 'fold_key_version'";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Run a partial update. FoldReady bit AND version must NOT advance to the new state
            // because untouched rows still carry the old version's fold keys.
            // partial update 実行。FoldReady bit も version も新状態に進めてはいけない。
            var targetFile = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(targetFile, "public class App { public void Run() { } }\n");
            File.SetLastWriteTimeUtc(targetFile, DateTime.UtcNow.AddSeconds(2));
            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--files", targetFile, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var userVerCmd = verify.CreateCommand();
            userVerCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)userVerCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);

            using var versionCmd = verify.CreateCommand();
            versionCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_version'";
            var storedVersion = versionCmd.ExecuteScalar() as string;
            // Stored version may stay at "0" (what we wrote) or be unset; critically it must
            // NOT have advanced to the current NameFold.Version because that would let the
            // reader treat mixed-state rows as fully fold-ready.
            // version は "0" のままで OK。現在の NameFold.Version に昇格してはいけない。
            Assert.NotEqual(NameFold.Version.ToString(), storedVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_DoesNotRestampFoldReadyWhenFoldKeyVersionMismatches()
    {
        // Normal non-rebuild `cdidx index .` is still incremental: unchanged rows are skipped.
        // If an existing DB carries old-version fold keys, a full scan must not advertise the
        // new version unless every row is rewritten (that requires --rebuild).
        // 通常の full scan も skip を使うため、旧 version key が残る DB では FoldReady を
        // restamp してはいけない。安全に昇格できるのは --rebuild のみ。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "intl.py"), "def Straße():\n    pass\n");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE symbols SET name_folded = 'straße' WHERE name = 'Straße';
                    UPDATE codeindex_meta SET value = '0' WHERE key = 'fold_key_version';
                    """;
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Add a new file so the next non-rebuild scan mixes freshly-written v2 rows with
            // untouched v1-style rows. The run must leave FoldReady off.
            // 新規ファイルを追加して mixed-state を作る。FoldReady は off のままであるべき。
            File.WriteAllText(Path.Combine(projectRoot, "new.cs"), "public class NewFile { }");

            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var userVerCmd = verify.CreateCommand();
            userVerCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)userVerCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);

            using var versionCmd = verify.CreateCommand();
            versionCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_version'";
            var storedVersion = versionCmd.ExecuteScalar() as string;
            Assert.NotEqual(NameFold.Version.ToString(), storedVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_UpdateMode_DoesNotRestampFoldReadyWhenFoldFingerprintMismatches()
    {
        // #97: partial update must not restamp FoldReady when the stored runtime canary
        // fingerprint differs from the current binary/runtime, even if NameFold.Version is
        // unchanged. Untouched rows still carry keys generated under the old runtime tables.
        // #97: version が同じでも fingerprint がズレた DB は partial update で restamp しない。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE codeindex_meta SET value = 'DEADBEEFDEADBEEF' WHERE key = 'fold_key_fingerprint'";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var targetFile = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(targetFile, "public class App { public void Run() { } }\n");
            File.SetLastWriteTimeUtc(targetFile, DateTime.UtcNow.AddSeconds(2));
            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--files", targetFile, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var userVerCmd = verify.CreateCommand();
            userVerCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)userVerCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);

            using var fingerprintCmd = verify.CreateCommand();
            fingerprintCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_fingerprint'";
            var storedFingerprint = fingerprintCmd.ExecuteScalar() as string;
            Assert.NotEqual(NameFold.Fingerprint(), storedFingerprint);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_DoesNotRestampFoldReadyWhenFoldFingerprintMismatchesAndFilesAreSkipped()
    {
        // #97 codex review: a normal `index .` run still skips unchanged files, so a stale
        // fold_key_fingerprint must not be overwritten with the current runtime fingerprint
        // unless every row was regenerated. Otherwise skipped rows keep old physical keys.
        // #97: 通常の `index .` で unchanged 行が skip される場合、stale fingerprint を
        // current 値へ再 stamp してはいけない。全件再生成できたときだけ trusted に戻せる。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE codeindex_meta SET value = 'DEADBEEFDEADBEEF' WHERE key = 'fold_key_fingerprint'";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var userVerCmd = verify.CreateCommand();
            userVerCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)userVerCmd.ExecuteScalar()!;
            Assert.Equal(0, userVersion & DbContext.FoldReadyFlag);

            using var fingerprintCmd = verify.CreateCommand();
            fingerprintCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_fingerprint'";
            var storedFingerprint = fingerprintCmd.ExecuteScalar() as string;
            Assert.Equal("DEADBEEFDEADBEEF", storedFingerprint);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_FullScan_RestampsFoldReadyWhenUserVersionWasClearedButFoldMetadataStillMatches()
    {
        // #97 codex review: if a previous refresh cleared user_version before restamping
        // FoldReady, a normal unchanged full scan should recover trust when the stored fold
        // version/fingerprint still match the current runtime and every folded column is
        // already backfilled.
        // #97: 途中中断で user_version だけ落ちた current DB は、fold metadata が current と
        // 一致していれば通常の unchanged full scan で FoldReady を回復できる必要がある。
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            RunGit(projectRoot, "config", "user.email", "test@example.com");
            RunGit(projectRoot, "config", "user.name", "Test");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "public class App { }");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            var exitCode1 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode1);
            var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version = 0";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var exitCode2 = IndexCommandRunner.Run([projectRoot, "--json"], _jsonOptions);
            Assert.Equal(CommandExitCodes.Success, exitCode2);

            using var verify = new SqliteConnection($"Data Source={dbPath}");
            verify.Open();
            using var userVerCmd = verify.CreateCommand();
            userVerCmd.CommandText = "PRAGMA user_version";
            var userVersion = (long)userVerCmd.ExecuteScalar()!;
            Assert.NotEqual(0, userVersion & DbContext.FoldReadyFlag);

            using var versionCmd = verify.CreateCommand();
            versionCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_version'";
            var storedVersion = versionCmd.ExecuteScalar() as string;
            Assert.Equal(NameFold.Version.ToString(), storedVersion);

            using var fingerprintCmd = verify.CreateCommand();
            fingerprintCmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = 'fold_key_fingerprint'";
            var storedFingerprint = fingerprintCmd.ExecuteScalar() as string;
            Assert.Equal(NameFold.Fingerprint(), storedFingerprint);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void Run_RebuildWithCommits_ReturnsUsageError()
    {
        var projectRoot = CreateTempProject();
        try
        {
            RunGit(projectRoot, "init");
            File.WriteAllText(Path.Combine(projectRoot, "app.cs"), "class A {}");
            RunGit(projectRoot, "add", ".");
            RunGit(projectRoot, "commit", "-m", "init");

            // --rebuild + --commits should conflict / --rebuild + --commits は矛盾
            var (exitCode, output) = RunAndCaptureOutput([projectRoot, "--rebuild", "--commits", "HEAD"]);
            Assert.Equal(CommandExitCodes.UsageError, exitCode);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    private (int ExitCode, JsonElement Json) RunAndCaptureJson(string[] args)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);
                var exitCode = IndexCommandRunner.Run(args, _jsonOptions);
                using var document = JsonDocument.Parse(writer.ToString());
                return (exitCode, document.RootElement.Clone());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    private static (int ExitCode, string Output) RunAndCaptureOutput(string[] args)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);
                var exitCode = IndexCommandRunner.Run(args, new JsonSerializerOptions());
                return (exitCode, writer.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private (int ExitCode, JsonElement Json) RunStatusAndCaptureJson(string[] args)
    {
        lock (TestConsoleLock.Gate)
        {
            var originalOut = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);
                var exitCode = QueryCommandRunner.RunStatus(args, _jsonOptions);
                using var document = JsonDocument.Parse(writer.ToString());
                return (exitCode, document.RootElement.Clone());
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunAndCaptureStreams(string[] args)
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
                var exitCode = IndexCommandRunner.Run(args, new JsonSerializerOptions());
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCliInSubprocess(string[] args, string workingDirectory)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(GetBuiltCliDllPath());
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cdidx subprocess / cdidx サブプロセスの起動に失敗");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    private static string GetBuiltCliDllPath()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        var fallbackConfigurations = new[] { configuration, "Debug", "Release" }
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var candidateConfiguration in fallbackConfigurations)
            {
                var candidate = Path.Combine(dir.FullName, "src", "CodeIndex", "bin", candidateConfiguration!, tfm, "cdidx.dll");
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate built cdidx.dll from test output path / テスト出力パスから cdidx.dll を特定できませんでした");
    }

    private static SqliteConnection OpenNonPoolingConnection(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false,
        };
        return new SqliteConnection(builder.ToString());
    }

    private static HashSet<string> ReadIndexedPaths(string dbPath)
    {
        using var db = new DbContext(dbPath);
        db.TryMigrateForRead();
        var reader = new DbReader(db.Connection, db.IsReadOnly);
        return reader.ListFiles(limit: 1000)
            .Select(file => file.Path)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void DeleteIndexedProjectRootMetadata(string dbPath)
    {
        using var conn = OpenNonPoolingConnection(dbPath);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM codeindex_meta WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", DbContext.IndexedProjectRootMetaKey);
        cmd.ExecuteNonQuery();
    }

    private static string CreateTempProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"cdidx_index_runner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        return projectRoot;
    }

    private static void RunGit(string workDir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process / gitプロセスの起動に失敗");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.Trim()}");

        if (args.Length == 1 && args[0] == "init")
        {
            RunGit(workDir, "config", "user.name", "CodeIndex Tests");
            RunGit(workDir, "config", "user.email", "tests@codeindex.local");
        }
    }

    private static string RunGitCaptureStdOut(string workDir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process / gitプロセスの起動に失敗");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.Trim()}");

        return stdout;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        ClearAttributes(path);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(100);
                ClearAttributes(path);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                System.Threading.Thread.Sleep(100);
                ClearAttributes(path);
            }
        }
    }

    private static void ClearAttributes(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(dir, FileAttributes.Normal);

        File.SetAttributes(path, FileAttributes.Normal);
    }
}
