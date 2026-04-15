using Microsoft.Data.Sqlite;

namespace CodeIndex.Cli;

/// <summary>
/// Resolves database paths for CLI commands.
/// CLIコマンドのデータベースパスを解決する。
/// </summary>
public static class DbPathResolver
{
    /// <summary>
    /// Resolve the DB path for indexing. When no explicit path is provided,
    /// store the DB under the indexed project's .cdidx directory.
    /// インデックス時のDBパスを解決する。明示指定がない場合は、
    /// 対象プロジェクトの .cdidx ディレクトリ配下に保存する。
    /// </summary>
    public static string ResolveForIndex(string projectPath, string? explicitDbPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitDbPath))
            return explicitDbPath;

        return Path.Combine(Path.GetFullPath(projectPath), ".cdidx", "codeindex.db");
    }

    /// <summary>
    /// Resolve the most likely project root for query commands from the DB path.
    /// クエリ系コマンドのDBパスから、もっとも可能性が高いプロジェクトルートを解決する。
    /// </summary>
    public static string? ResolveProjectRootForQuery(string dbPath, bool dbPathExplicit = false)
    {
        var fullDbPath = Path.GetFullPath(NormalizeDbPath(dbPath));
        var dbDir = Path.GetDirectoryName(fullDbPath);
        if (dbDir == null)
            return null;

        if (!dbPathExplicit
            && string.Equals(Path.GetFileName(dbDir), ".cdidx", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(fullDbPath), "codeindex.db", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(dbDir);

        var indexedProjectRoot = TryReadIndexedProjectRoot(dbPath);
        if (indexedProjectRoot == null && !string.Equals(dbPath, fullDbPath, StringComparison.Ordinal))
            indexedProjectRoot = TryReadIndexedProjectRoot(fullDbPath);
        if (!string.IsNullOrWhiteSpace(indexedProjectRoot))
            return Path.GetFullPath(indexedProjectRoot);

        return null;
    }

    /// <summary>
    /// Normalize a SQLite `file:///...?immutable=1`-style URI to a plain filesystem path.
    /// Plain paths pass through unchanged. Used by metadata resolution so CLI `--db` URIs
    /// (the read-only escape hatch) don't poison workspace / git lookups that depend on a
    /// real path — feeding `file:///…` into `Path.GetFullPath` otherwise produces paths
    /// like `/cwd/file:/abs/...` and breaks `project_root`, `git_head`, `git_is_dirty`.
    /// SQLite の file: URI をローカルパスに正規化する。平文パスは素通し。
    /// </summary>
    public static string NormalizeDbPath(string dbPath)
    {
        if (!dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return dbPath;
        try
        {
            // Strip query params (?immutable=1 etc.) before URI parsing so LocalPath is clean.
            var qIdx = dbPath.IndexOf('?');
            var trimmed = qIdx >= 0 ? dbPath[..qIdx] : dbPath;
            var uri = new Uri(trimmed);
            return uri.IsFile ? uri.LocalPath : dbPath;
        }
        catch
        {
            return dbPath;
        }
    }

    private static string? TryReadIndexedProjectRoot(string dbPath)
    {
        try
        {
            using var connection = OpenMetadataConnection(dbPath);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM codeindex_meta WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", CodeIndex.Database.DbContext.IndexedProjectRootMetaKey);
            var raw = cmd.ExecuteScalar();
            return raw is string value && !string.IsNullOrWhiteSpace(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static SqliteConnection OpenMetadataConnection(string dbPath)
    {
        if (dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && UriRequestsReadOnly(dbPath))
            return new SqliteConnection($"Data Source={dbPath}");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    private static bool UriRequestsReadOnly(string uriText)
    {
        var qIdx = uriText.IndexOf('?');
        if (qIdx < 0) return false;
        var query = uriText[(qIdx + 1)..];
        foreach (var raw in query.Split('&'))
        {
            var seg = raw.Trim();
            if (seg.Equals("immutable=1", StringComparison.OrdinalIgnoreCase)) return true;
            if (seg.Equals("mode=ro", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
