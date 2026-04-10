using CodeIndex.Database;

namespace CodeIndex.Cli;

/// <summary>
/// Enrich repo/status responses with workspace-level freshness metadata.
/// ワークスペース単位の鮮度メタデータで repo/status レスポンスを補強する。
/// </summary>
public static class WorkspaceMetadataEnricher
{
    public static void Enrich(StatusResult status, string dbPath)
    {
        var projectRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath);
        status.ProjectRoot = projectRoot;
        status.GitHead = GitHelper.TryGetHeadCommit(projectRoot);
        status.GitIsDirty = GitHelper.TryIsWorktreeDirty(projectRoot);
    }

    public static void Enrich(RepoMapResult map, string dbPath)
    {
        var projectRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath);
        map.ProjectRoot = projectRoot;
        map.GitHead = GitHelper.TryGetHeadCommit(projectRoot);
        map.GitIsDirty = GitHelper.TryIsWorktreeDirty(projectRoot);
    }

    public static void Enrich(SymbolAnalysisResult analysis, string dbPath)
    {
        var projectRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath);
        analysis.ProjectRoot = projectRoot;
        analysis.GitHead = GitHelper.TryGetHeadCommit(projectRoot);
        analysis.GitIsDirty = GitHelper.TryIsWorktreeDirty(projectRoot);
    }
}
