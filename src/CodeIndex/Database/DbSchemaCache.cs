using Microsoft.Data.Sqlite;

namespace CodeIndex.Database;

/// <summary>
/// Connection-scoped cache for SQLite schema discovery (`PRAGMA table_info`,
/// `PRAGMA index_list`, and `sqlite_master` table existence checks).
///
/// `DbReader` discovered these on every construction, so MCP sessions that
/// reuse a `DbContext` across tool calls were re-running the same PRAGMAs per
/// invocation (issue #1565). Caching them at the `DbContext` level pays the
/// scan once per session and serves subsequent `DbReader` instances from
/// memory.
///
/// In-process migrations (`InitializeSchema`, `TryMigrateForRead`, `DropAll`)
/// call <see cref="Refresh"/> directly. To also catch DDL run through a
/// different connection — for example, a long-running MCP server while
/// `cdidx index` rebuilds the same DB out of band — every lookup first
/// consults SQLite's `PRAGMA schema_version`, which is bumped by SQLite on
/// every schema mutation regardless of which connection issued it. A change
/// auto-clears the cache before the lookup is served, so the next reader
/// observes the new shape without restart.
/// </summary>
public sealed class DbSchemaCache
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _tableExists = new(StringComparer.OrdinalIgnoreCase);
    private long? _lastSchemaVersion;

    public DbSchemaCache(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public HashSet<string> GetColumns(string tableName)
    {
        lock (_lock)
        {
            EnsureFreshUnlocked();
            if (_columns.TryGetValue(tableName, out var cached))
                return cached;
            var fresh = LoadColumns(_connection, tableName);
            _columns[tableName] = fresh;
            return fresh;
        }
    }

    public HashSet<string> GetIndexes(string tableName)
    {
        lock (_lock)
        {
            EnsureFreshUnlocked();
            if (_indexes.TryGetValue(tableName, out var cached))
                return cached;
            var fresh = LoadIndexes(_connection, tableName, HasTableUnlocked(tableName));
            _indexes[tableName] = fresh;
            return fresh;
        }
    }

    public bool HasTable(string tableName)
    {
        lock (_lock)
        {
            EnsureFreshUnlocked();
            return HasTableUnlocked(tableName);
        }
    }

    /// <summary>
    /// Drop all cached schema state. Call after DDL that adds or removes
    /// tables, columns, or indexes so subsequent reads observe the new shape.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            ClearUnlocked();
        }
    }

    private void ClearUnlocked()
    {
        _columns.Clear();
        _indexes.Clear();
        _tableExists.Clear();
        _lastSchemaVersion = null;
    }

    /// <summary>
    /// Detect schema mutations performed through any connection to the same
    /// database file by comparing `PRAGMA schema_version`. SQLite bumps this
    /// counter on every CREATE / DROP / ALTER, so a long-lived MCP `DbContext`
    /// that out-of-process tooling has touched will reload cached entries on
    /// the next lookup instead of serving stale results until restart.
    /// </summary>
    private void EnsureFreshUnlocked()
    {
        long current;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA schema_version";
            var raw = cmd.ExecuteScalar();
            current = raw switch
            {
                long l => l,
                int i => i,
                _ => 0L,
            };
        }
        catch (SqliteException)
        {
            // Could not read schema_version (e.g. transient lock during DDL on
            // another connection). Treat as a forced refresh: drop cache so the
            // caller's PRAGMA actually re-reads fresh, rather than silently
            // serving potentially-stale entries.
            // PRAGMA schema_version 取得に失敗した場合は安全側に倒してキャッシュを破棄する。
            ClearUnlocked();
            return;
        }

        if (_lastSchemaVersion is null)
        {
            _lastSchemaVersion = current;
            return;
        }
        if (_lastSchemaVersion.Value == current)
            return;

        _columns.Clear();
        _indexes.Clear();
        _tableExists.Clear();
        _lastSchemaVersion = current;
    }

    private bool HasTableUnlocked(string tableName)
    {
        if (_tableExists.TryGetValue(tableName, out var cached))
            return cached;
        var exists = QueryHasTable(_connection, tableName);
        _tableExists[tableName] = exists;
        return exists;
    }

    internal static HashSet<string> LoadColumns(SqliteConnection conn, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
            columns.Add(reader.GetString(1));
        return columns;
    }

    internal static HashSet<string> LoadIndexes(SqliteConnection conn, string tableName, bool tableExists)
    {
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!tableExists)
            return indexes;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA index_list('{tableName.Replace("'", "''")}')";
        using var reader = cmd.ExecuteTrackedReader();
        while (reader.TrackedRead())
        {
            if (!reader.IsDBNull(1))
                indexes.Add(reader.GetString(1));
        }
        return indexes;
    }

    internal static bool QueryHasTable(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }
}
