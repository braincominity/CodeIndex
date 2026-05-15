using CodeIndex.Database;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class DbDebugTests
{
    private static string CaptureStderr(Action action)
    {
        lock (TestConsoleLock.Gate)
        {
            var err = new StringWriter();
            var prev = Console.Error;
            try
            {
                Console.SetError(err);
                action();
            }
            finally { Console.SetError(prev); }
            return err.ToString();
        }
    }

    [Fact]
    public void DumpToStderr_NoOp_WhenDisabled()
    {
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
        DbDebug.ResetContext();
        var output = CaptureStderr(() => DbDebug.DumpToStderr(new InvalidOperationException("boom")));
        Assert.Empty(output);
    }

    [Fact]
    public void DumpToStderr_RedactsTextByDefault()
    {
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", "1");
        try
        {
            DbDebug.ResetContext();
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var init = conn.CreateCommand())
            {
                init.CommandText = "CREATE TABLE t (id INTEGER, content TEXT); INSERT INTO t VALUES (1, 'SECRET_SOURCE_CODE_TOKEN'), (2, NULL);";
                init.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, content FROM t WHERE id >= @min ORDER BY id";
            cmd.Parameters.AddWithValue("@min", 1);

            Exception? caught = null;
            try
            {
                using var reader = cmd.ExecuteTrackedReader();
                while (reader.TrackedRead())
                    _ = reader.GetString(1);
            }
            catch (Exception ex) { caught = ex; }

            Assert.NotNull(caught);
            var output = CaptureStderr(() => DbDebug.DumpToStderr(caught!));
            Assert.Contains("CDIDX_DEBUG", output);
            Assert.Contains("redacted", output);
            Assert.Contains("SELECT id, content FROM t", output);
            Assert.Contains("@min", output);
            Assert.Contains("[content] = <NULL>", output);
            // Row 1's string content must NOT leak verbatim in redacted mode.
            Assert.DoesNotContain("SECRET_SOURCE_CODE_TOKEN", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
            DbDebug.ResetContext();
        }
    }

    [Fact]
    public void DumpToStderr_UnsafeMode_IncludesRawContent()
    {
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", "unsafe");
        DbDebug.EnableUnsafeForProcess();
        try
        {
            DbDebug.ResetContext();
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var init = conn.CreateCommand())
            {
                init.CommandText = "CREATE TABLE t (id INTEGER, content TEXT); INSERT INTO t VALUES (1, 'RAW_TOKEN');";
                init.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, content FROM t";
            using (var reader = cmd.ExecuteTrackedReader())
            {
                while (reader.TrackedRead()) { }
            }
            var output = CaptureStderr(() => DbDebug.DumpToStderr(new InvalidOperationException("boom")));
            Assert.Contains("unsafe", output);
            Assert.Contains("RAW_TOKEN", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
            DbDebug.ResetContext();
            DbDebug.ResetForTesting();
        }
    }

    [Fact]
    public void DumpToStderr_UnsafeEnvAlone_DowngradesToRedactedAndWarns()
    {
        // Issue #1530: a stale `CDIDX_DEBUG=unsafe` in a shell profile or CI env
        // must not silently expose indexed text. Without an explicit per-process
        // opt-in (`--debug-unsafe` on the command line) the helper falls back to
        // redacted mode and emits a one-shot stderr warning. The capture has to
        // wrap the tracking calls too because the downgrade warning fires the
        // first time ResolveMode runs (here: during ExecuteTrackedReader).
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", "unsafe");
        DbDebug.ResetForTesting();
        try
        {
            DbDebug.ResetContext();
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var init = conn.CreateCommand())
            {
                init.CommandText = "CREATE TABLE t (id INTEGER, content TEXT); INSERT INTO t VALUES (1, 'SECRET_LITERAL');";
                init.ExecuteNonQuery();
            }

            var output = CaptureStderr(() =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, content FROM t";
                using (var reader = cmd.ExecuteTrackedReader())
                {
                    while (reader.TrackedRead()) { }
                }
                DbDebug.DumpToStderr(new InvalidOperationException("boom"));
            });

            Assert.Contains("CDIDX_DEBUG=unsafe was ignored", output);
            Assert.Contains("--debug-unsafe", output);
            Assert.Contains("Mode: redacted", output);
            // Raw text must not leak when only the env var is set.
            Assert.DoesNotContain("SECRET_LITERAL", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
            DbDebug.ResetContext();
            DbDebug.ResetForTesting();
        }
    }

    [Fact]
    public void DumpToStderr_UnsafeDowngradeWarning_EmittedOnlyOnce()
    {
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", "unsafe");
        DbDebug.ResetForTesting();
        try
        {
            DbDebug.ResetContext();
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var init = conn.CreateCommand())
            {
                init.CommandText = "CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1);";
                init.ExecuteNonQuery();
            }

            string Run()
            {
                return CaptureStderr(() =>
                {
                    DbDebug.ResetContext();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT id FROM t";
                    using (var reader = cmd.ExecuteTrackedReader())
                    {
                        while (reader.TrackedRead()) { }
                    }
                    DbDebug.DumpToStderr(new InvalidOperationException("boom"));
                });
            }

            var first = Run();
            var second = Run();

            Assert.Contains("CDIDX_DEBUG=unsafe was ignored", first);
            Assert.DoesNotContain("CDIDX_DEBUG=unsafe was ignored", second);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
            DbDebug.ResetContext();
            DbDebug.ResetForTesting();
        }
    }

    [Fact]
    public void DumpToStderr_DoesNotDumpStaleStateAfterReset()
    {
        Environment.SetEnvironmentVariable("CDIDX_DEBUG", "1");
        try
        {
            DbDebug.ResetContext();
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var init = conn.CreateCommand())
            {
                init.CommandText = "CREATE TABLE prev (id INTEGER); INSERT INTO prev VALUES (42);";
                init.ExecuteNonQuery();
            }
            // Request A: populate tracked state / リクエスト A で状態を埋める
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM prev";
                using var reader = cmd.ExecuteTrackedReader();
                while (reader.TrackedRead()) { _ = reader.GetInt32(0); }
            }

            // Request boundary / リクエスト境界
            DbDebug.ResetContext();

            // Request B: unrelated non-reader exception / リクエスト B で無関係な例外
            var output = CaptureStderr(() => DbDebug.DumpToStderr(new IOException("disk unplugged")));
            Assert.Empty(output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDIDX_DEBUG", null);
            DbDebug.ResetContext();
        }
    }
}
