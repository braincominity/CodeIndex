using System.Diagnostics;
using CodeIndex.Database;
using CodeIndex.Models;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Performance smoke tests for large datasets (10K+ files).
/// 大規模データ（10K+ファイル）のパフォーマンススモークテスト。
/// </summary>
[Collection("SQLite pool sensitive")]
public class PerformanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;

    public PerformanceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_perf_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
    }

    [Fact(Skip = "Performance test — run manually with: dotnet test --filter Insert10KFiles")]
    public void Insert10KFiles_CompletesInReasonableTime()
    {
        var writer = new DbWriter(_db.Connection);
        var sw = Stopwatch.StartNew();

        // Insert 10,000 files with minimal content / 10,000ファイルを最小限の内容で挿入
        using var tx = writer.BeginTransaction();
        for (int i = 0; i < 10_000; i++)
        {
            writer.UpsertFile(new FileRecord
            {
                Path = $"src/module{i / 100}/file{i}.cs",
                Lang = "csharp",
                Size = 100 + i,
                Lines = 10,
                Modified = DateTime.UtcNow,
                Checksum = $"hash{i:X8}",
            });
        }
        tx.Commit();
        sw.Stop();

        // Should complete in under 10 seconds (typically < 2s on modern hardware)
        // 10秒以内に完了すべき（通常は現代のハードウェアで2秒未満）
        Assert.True(sw.Elapsed.TotalSeconds < 10, $"Insert 10K files took {sw.Elapsed.TotalSeconds:F1}s");

        var (files, _, _, _) = writer.GetCounts();
        Assert.Equal(10_000, files);
    }

    [Fact(Skip = "Performance test — run manually with: dotnet test --filter Search10KFileIndex")]
    public void Search10KFileIndex_ReturnsInReasonableTime()
    {
        var writer = new DbWriter(_db.Connection);

        // Seed 1000 files with searchable content / 1000ファイルに検索可能な内容を投入
        using var tx = writer.BeginTransaction();
        for (int i = 0; i < 1_000; i++)
        {
            var fileId = writer.UpsertFile(new FileRecord
            {
                Path = $"src/mod{i / 50}/service{i}.cs",
                Lang = "csharp",
                Size = 500,
                Lines = 20,
                Modified = DateTime.UtcNow,
                Checksum = $"hash{i:X8}",
            });
            writer.InsertChunks([new ChunkRecord
            {
                FileId = fileId,
                ChunkIndex = 0,
                StartLine = 1,
                EndLine = 20,
                Content = $"public class Service{i} {{ public void Execute() {{ }} }}",
            }]);
        }
        tx.Commit();

        var reader = new DbReader(_db.Connection);
        var sw = Stopwatch.StartNew();
        var results = reader.Search("Execute", limit: 20);
        sw.Stop();

        // FTS5 search should be fast even with many files / FTS5検索は多数ファイルでも高速であるべき
        Assert.True(sw.Elapsed.TotalMilliseconds < 500, $"Search took {sw.Elapsed.TotalMilliseconds:F0}ms");
        Assert.True(results.Count > 0);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
