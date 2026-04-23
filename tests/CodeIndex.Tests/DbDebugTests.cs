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
