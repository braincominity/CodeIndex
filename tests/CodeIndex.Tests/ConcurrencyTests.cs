using CodeIndex.Database;
using CodeIndex.Models;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for concurrent database access patterns.
/// 並行データベースアクセスパターンのテスト。
/// </summary>
[Collection("SQLite pool sensitive")]
public class ConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;

    public ConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_concurrency_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
    }

    [Fact]
    public async Task ConcurrentReads_DoNotBlock()
    {
        // Seed data / テストデータ投入
        var writer = new DbWriter(_db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/app.cs", Lang = "csharp", Size = 100, Lines = 10,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = "abc",
        });
        writer.InsertChunks([new ChunkRecord { FileId = fileId, ChunkIndex = 0, StartLine = 1, EndLine = 10, Content = "public class App { }" }]);

        // Open multiple concurrent readers — WAL mode should allow this
        // 複数の同時読み取りを開く — WALモードなら可能
        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
        {
            using var readDb = new DbContext(_dbPath);
            readDb.TryMigrateForRead();
            var reader = new DbReader(readDb.Connection);
            var status = reader.GetStatus();
            Assert.True(status.Files > 0);
            return status.Files;
        })).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r > 0));
    }

    [Fact]
    public async Task ConcurrentReadDuringWrite_Succeeds()
    {
        // Writer inserts while readers query — WAL allows this
        // 書き込み中に読み取り — WALモードなら可能
        var writer = new DbWriter(_db.Connection);

        // Pre-seed a file so reads have something to find
        writer.UpsertFile(new FileRecord
        {
            Path = "src/seed.cs", Lang = "csharp", Size = 50, Lines = 5,
            Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Checksum = "seed",
        });

        var writeTask = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                using var writeDb = new DbContext(_dbPath);
                writeDb.TryMigrateForRead();
                var w = new DbWriter(writeDb.Connection);
                w.UpsertFile(new FileRecord
                {
                    Path = $"src/file{i}.cs", Lang = "csharp", Size = 100, Lines = 10,
                    Modified = DateTime.UtcNow,
                    Checksum = $"hash{i}",
                });
            }
        });

        var readTask = Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                using var readDb = new DbContext(_dbPath);
                readDb.TryMigrateForRead();
                var reader = new DbReader(readDb.Connection);
                // Should not throw even during concurrent writes
                // 同時書き込み中でも例外を投げないこと
                var status = reader.GetStatus();
                Assert.True(status.Files >= 1); // at least the seed file
            }
        });

        await Task.WhenAll(writeTask, readTask);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
