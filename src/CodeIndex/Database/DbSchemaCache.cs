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
/// Migrations (`InitializeSchema`, `TryMigrateForRead`, `DropAll`) call
/// <see cref="Refresh"/> so cached entries cannot outlive a structural change.
/// </summary>
public sealed class DbSchemaCache
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _tableExists = new(StringComparer.OrdinalIgnoreCase);

    public DbSchemaCache(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public HashSet<string> GetColumns(string tableName)
    {
        lock (_lock)
        {
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
            _columns.Clear();
            _indexes.Clear();
            _tableExists.Clear();
        }
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
