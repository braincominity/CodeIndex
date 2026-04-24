using System.Collections.Concurrent;
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

    [Fact]
    public async Task GetStatus_ReferencesAndFilesStaySnapshotConsistent_UnderConcurrentWriter()
    {
        // Issue #180 regression: GetStatus must expose a single consistent WAL snapshot
        // across its many COUNT(*) / freshness / readiness queries. The test seeds a known
        // ratio (refs == files * refsPerFile) that every committed writer step preserves,
        // spawns a background writer that keeps committing new files with exactly that
        // many refs, and spins a reader that repeatedly calls GetStatus. Without the
        // snapshot-isolation wrap in DbReader.GetStatus, a commit landing between the
        // `files` COUNT(*) and the `references` COUNT(*) breaks the invariant; with the
        // wrap, every observation must match.
        // Issue #180 の回帰テスト: GetStatus の複数 SELECT が 1 つの WAL snapshot に揃うこと
        // を検証する。seed で `refs == files * refsPerFile` という不変条件を作り、writer が
        // 同じ比率で file+refs を commit し続ける間に reader が GetStatus を回す。snapshot
        // 隔離が無いと files と refs の COUNT(*) の間に writer が commit した結果として
        // 比率が破れる。
        const int seedFileCount = 5;
        const int refsPerFile = 20;

        var writer = new DbWriter(_db.Connection);
        for (var seedIndex = 0; seedIndex < seedFileCount; seedIndex++)
        {
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = $"src/seed{seedIndex}.cs", Lang = "csharp", Size = 100, Lines = 10,
                Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Checksum = $"seed{seedIndex}",
            });
            writer.InsertReferences(BuildReferenceBatch(fileId, $"seed{seedIndex}", refsPerFile));
        }
        // DbReader gates `_hasReferencesTable` on the GraphReadyFlag bit in user_version, so
        // without this stamp every call to GetStatus returns `references: 0` regardless of
        // table contents and the snapshot-isolation assertion becomes vacuous.
        // DbReader は `_hasReferencesTable` を user_version の GraphReadyFlag で決めるため、
        // MarkGraphReady() を呼ばないと GetStatus は常に refs=0 を返し、検証が成立しない。
        writer.MarkGraphReady();

        using var cts = new CancellationTokenSource();
        var violations = new ConcurrentBag<(long files, long references)>();
        long readerIterations = 0;
        long writerIterations = 0;

        var writeTask = Task.Run(() =>
        {
            using var writeDb = new DbContext(_dbPath);
            writeDb.TryMigrateForRead();
            var w = new DbWriter(writeDb.Connection);
            var extra = 0;
            while (!cts.IsCancellationRequested)
            {
                using var txn = w.BeginTransaction();
                var fileId = w.UpsertFile(new FileRecord
                {
                    Path = $"src/added{extra}.cs", Lang = "csharp", Size = 100, Lines = 10,
                    Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Checksum = $"added{extra}",
                });
                w.InsertReferences(BuildReferenceBatch(fileId, $"added{extra}", refsPerFile));
                txn.Commit();
                Interlocked.Increment(ref writerIterations);
                extra++;
            }
        });

        var readTask = Task.Run(() =>
        {
            using var readDb = new DbContext(_dbPath);
            readDb.TryMigrateForRead();
            var reader = new DbReader(readDb.Connection);
            while (!cts.IsCancellationRequested)
            {
                var status = reader.GetStatus();
                if (status.References != status.Files * refsPerFile)
                    violations.Add((status.Files, status.References));
                Interlocked.Increment(ref readerIterations);
            }
        });

        // Run long enough for the two threads to interleave commits and reads many times.
        // 十分な時間動かし、コミットと読み出しの交錯機会を多数確保する。
        await Task.Delay(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Task.WhenAll(writeTask, readTask);

        Assert.True(
            violations.IsEmpty,
            $"GetStatus returned inconsistent files/refs snapshots {violations.Count} times out of " +
            $"{readerIterations} reads while the writer committed {writerIterations} times. " +
            $"Sample: {string.Join(", ", violations.Take(3).Select(v => $"files={v.files} refs={v.references}"))}");
    }

    private static List<ReferenceRecord> BuildReferenceBatch(long fileId, string label, int count)
    {
        var refs = new List<ReferenceRecord>(count);
        for (var refIndex = 0; refIndex < count; refIndex++)
        {
            refs.Add(new ReferenceRecord
            {
                FileId = fileId,
                SymbolName = $"{label}_sym{refIndex}",
                ReferenceKind = "call",
                Line = refIndex + 1,
                Column = 1,
                Context = $"// ref {refIndex} in {label}",
            });
        }
        return refs;
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
