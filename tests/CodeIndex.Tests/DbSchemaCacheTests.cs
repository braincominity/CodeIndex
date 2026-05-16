using CodeIndex.Database;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for the connection-scoped schema cache that backs DbReader's
/// PRAGMA table_info / PRAGMA index_list / sqlite_master lookups (issue #1565).
/// </summary>
[Collection("SQLite pool sensitive")]
public sealed class DbSchemaCacheTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;

    public DbSchemaCacheTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"codeindex_schema_cache_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
    }

    [Fact]
    public void DbContext_SchemaCache_IsLazySingleton()
    {
        var first = _db.SchemaCache;
        var second = _db.SchemaCache;

        Assert.Same(first, second);
    }

    [Fact]
    public void GetColumns_ReturnsSameInstanceForRepeatedLookup()
    {
        var first = _db.SchemaCache.GetColumns("files");
        var second = _db.SchemaCache.GetColumns("files");

        Assert.Same(first, second);
        Assert.Contains("path", first);
        Assert.Contains("checksum", first);
    }

    [Fact]
    public void GetIndexes_ReturnsSameInstanceForRepeatedLookup()
    {
        var first = _db.SchemaCache.GetIndexes("symbols");
        var second = _db.SchemaCache.GetIndexes("symbols");

        Assert.Same(first, second);
        Assert.Contains("idx_symbols_name_nocase", first);
    }

    [Fact]
    public void GetIndexes_ReturnsEmptyForMissingTable()
    {
        var indexes = _db.SchemaCache.GetIndexes("nonexistent_table_for_test");

        Assert.Empty(indexes);
    }

    [Fact]
    public void HasTable_CachesPositiveAndNegativeResults()
    {
        Assert.True(_db.SchemaCache.HasTable("symbols"));
        Assert.False(_db.SchemaCache.HasTable("nonexistent_table_for_test"));

        // Repeated calls must not error and must match.
        Assert.True(_db.SchemaCache.HasTable("symbols"));
        Assert.False(_db.SchemaCache.HasTable("nonexistent_table_for_test"));
    }

    [Fact]
    public void Refresh_ObservesNewColumnAddedOutOfBand()
    {
        // Prime the cache.
        var beforeColumns = _db.SchemaCache.GetColumns("files");
        Assert.DoesNotContain("synthetic_test_column", beforeColumns);

        // Add a column via raw SQL — the cache has no chance to invalidate
        // automatically because the DDL went directly through the connection.
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "ALTER TABLE files ADD COLUMN synthetic_test_column TEXT";
            cmd.ExecuteNonQuery();
        }

        // Without Refresh the cache still serves the old set.
        Assert.DoesNotContain("synthetic_test_column", _db.SchemaCache.GetColumns("files"));

        _db.RefreshSchemaCache();
        var afterColumns = _db.SchemaCache.GetColumns("files");
        Assert.Contains("synthetic_test_column", afterColumns);
        Assert.NotSame(beforeColumns, afterColumns);
    }

    [Fact]
    public void TryMigrateForRead_AutoRefreshesCache()
    {
        // Prime the cache with the freshly-initialized schema.
        var beforeColumns = _db.SchemaCache.GetColumns("files");

        // Drop a column-bearing table and recreate it with a different shape so
        // a follow-up TryMigrateForRead has migration steps to run. We do this
        // by deleting the codeindex_meta table (one TryMigrateForRead step
        // re-creates it) and confirming the cache sees the post-migration shape.
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE codeindex_meta";
            cmd.ExecuteNonQuery();
        }

        // Sanity: the cache served before the drop; this call would resolve from
        // the connection now and capture the missing state.
        Assert.False(_db.SchemaCache.HasTable("codeindex_meta"));

        _db.TryMigrateForRead();

        Assert.True(_db.SchemaCache.HasTable("codeindex_meta"));
        // The files columns may legitimately remain identical, but the instance
        // must be a fresh one (cache cleared by TryMigrateForRead).
        var afterColumns = _db.SchemaCache.GetColumns("files");
        Assert.NotSame(beforeColumns, afterColumns);
        Assert.Equal(beforeColumns, afterColumns);
    }

    [Fact]
    public void DbReader_FromDbContext_ReusesSharedSchemaCacheInstances()
    {
        // Multiple DbReader constructions on the same DbContext should share
        // the same underlying schema HashSet instances rather than each one
        // performing its own PRAGMA scan.
        _ = new DbReader(_db);
        var fromCacheBefore = _db.SchemaCache.GetColumns("files");
        _ = new DbReader(_db);
        var fromCacheAfter = _db.SchemaCache.GetColumns("files");

        Assert.Same(fromCacheBefore, fromCacheAfter);
    }

    [Fact]
    public void DbReader_LegacyConstructor_StillWorksWithoutCache()
    {
        // The (SqliteConnection, bool) overload is the legacy path used by
        // write-side signal readers; it must keep functioning without a cache.
        var reader = new DbReader(_db.Connection, _db.IsReadOnly);

        Assert.NotNull(reader);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
        }
    }
}
