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

        var indexedProjectRoot = TryReadIndexedProjectRoot(dbPath);
        if (indexedProjectRoot == null && !string.Equals(dbPath, fullDbPath, StringComparison.Ordinal))
            indexedProjectRoot = TryReadIndexedProjectRoot(fullDbPath);

        var projectLocalRoot = TryResolveProjectLocalRoot(fullDbPath, dbPath, dbPathExplicit, indexedProjectRoot);
        if (projectLocalRoot != null)
            return projectLocalRoot;

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

    private static string? TryResolveProjectLocalRoot(string fullDbPath, string dbPath, bool dbPathExplicit, string? indexedProjectRoot)
    {
        var dbDir = Path.GetDirectoryName(fullDbPath);
        if (dbDir == null)
            return null;

        if (!string.Equals(Path.GetFileName(dbDir), ".cdidx", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(fullDbPath), "codeindex.db", StringComparison.OrdinalIgnoreCase))
            return null;

        var siblingRoot = Path.GetDirectoryName(dbDir);
        if (string.IsNullOrWhiteSpace(siblingRoot))
            return null;

        // Implicit default queries always trust the sibling `.cdidx/codeindex.db` layout.
        // 明示指定なしのデフォルト query は `.cdidx/codeindex.db` の sibling layout を常に信頼する。
        if (!dbPathExplicit)
            return siblingRoot;

        // Explicit `--db .../.cdidx/codeindex.db` is ambiguous: it may be a genuine
        // project-local DB, or an external/shared DB whose container happens to use the
        // same filename. Prefer the sibling path only when the indexed contents still line
        // up with that sibling root; otherwise fall back to persisted metadata.
        // 明示指定の `--db .../.cdidx/codeindex.db` は曖昧なので、DB内容と sibling root が
        // 実際に整合しているときだけ sibling を採用し、それ以外は persisted metadata を使う。
        if (SiblingRootMatchesIndexedContents(dbPath, fullDbPath, siblingRoot, indexedProjectRoot))
            return siblingRoot;

        return null;
    }

    private static bool SiblingRootMatchesIndexedContents(string dbPath, string fullDbPath, string siblingRoot, string? indexedProjectRoot)
    {
        if (!string.IsNullOrWhiteSpace(indexedProjectRoot))
        {
            var storedDbPath = Path.Combine(Path.GetFullPath(indexedProjectRoot), ".cdidx", "codeindex.db");
            if (PathsEqual(storedDbPath, fullDbPath))
                return true;
        }

        try
        {
            using var connection = OpenMetadataConnection(dbPath);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT path FROM files ORDER BY id LIMIT 5";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                    continue;

                var relativePath = reader.GetString(0);
                var absolutePath = Path.Combine(siblingRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                    return true;
            }
        }
        catch
        {
            // Fall back to persisted metadata / 永続化 metadata 側へフォールバック
        }

        return false;
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

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }
}
