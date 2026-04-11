using CodeIndex.Cli;
using CodeIndex.Database;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for database corruption recovery and graceful degradation.
/// DB破損からの復旧とグレースフル劣化のテスト。
/// </summary>
public class DbRecoveryTests : IDisposable
{
    private readonly string _dbPath;

    public DbRecoveryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_recovery_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public void TryMigrateForRead_OnCorruptedDb_DoesNotCrash()
    {
        // Write garbage to simulate a corrupted database / ゴミデータを書き込んで破損DBをシミュレート
        File.WriteAllText(_dbPath, "NOT A SQLITE DATABASE - corrupted content here");

        // Opening a corrupted DB should throw at the SQLite level, not crash the process
        // 破損DBを開くとSQLiteレベルで例外が出るがプロセスはクラッシュしない
        Assert.ThrowsAny<Exception>(() =>
        {
            using var db = new DbContext(_dbPath);
            db.TryMigrateForRead();
        });
    }

    [Fact]
    public void RebuildAfterCorruption_CreatesCleanIndex()
    {
        // Create a valid DB first / まず有効なDBを作成
        using (var db = new DbContext(_dbPath))
        {
            db.InitializeSchema();
        }

        // Corrupt it by overwriting / 上書きして破損させる
        File.WriteAllBytes(_dbPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        // Delete and recreate — simulates user running --rebuild after corruption
        // 削除して再作成 — ユーザーが破損後に --rebuild を実行するシミュレート
        File.Delete(_dbPath);
        using var newDb = new DbContext(_dbPath);
        newDb.InitializeSchema();

        var writer = new DbWriter(newDb.Connection);
        var (files, _, _, _) = writer.GetCounts();
        Assert.Equal(0, files); // Clean slate / クリーンな状態
    }

    [Fact]
    public void QueryOnMissingDb_ReturnsProperExitCode()
    {
        // Query commands against non-existent DB should return DatabaseError (3)
        // 存在しないDBへのクエリはDatabaseError (3) を返すべき
        var nonExistentDb = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.db");
        lock (TestConsoleLock.Gate)
        {
            var originalErr = Console.Error;
            using var errWriter = new StringWriter();
            try
            {
                Console.SetError(errWriter);
                var exitCode = QueryCommandRunner.RunStatus(["--db", nonExistentDb], new System.Text.Json.JsonSerializerOptions());
                Assert.Equal(CommandExitCodes.DatabaseError, exitCode);
                Assert.Contains("database not found", errWriter.ToString().ToLowerInvariant());
            }
            finally
            {
                Console.SetError(originalErr);
            }
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
