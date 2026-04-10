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
    public static string ResolveProjectRootForQuery(string dbPath)
    {
        var fullDbPath = Path.GetFullPath(dbPath);
        var dbDir = Path.GetDirectoryName(fullDbPath);
        if (dbDir == null)
            return Path.GetFullPath(".");

        if (string.Equals(Path.GetFileName(dbDir), ".cdidx", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(dbDir) ?? Path.GetFullPath(".");

        return Path.GetFullPath(".");
    }
}
