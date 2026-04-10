using CodeIndex.Database;

namespace CodeIndex.Cli;

/// <summary>
/// Enrich repo/status responses with workspace-level freshness metadata.
/// ワークスペース単位の鮮度メタデータで repo/status レスポンスを補強する。
/// </summary>
public static class WorkspaceMetadataEnricher
{
    public static void Enrich(StatusResult status, string dbPath) =>
        Apply(dbPath, (root, head, dirty) => { status.ProjectRoot = root; status.GitHead = head; status.GitIsDirty = dirty; });

    public static void Enrich(RepoMapResult map, string dbPath) =>
        Apply(dbPath, (root, head, dirty) => { map.ProjectRoot = root; map.GitHead = head; map.GitIsDirty = dirty; });

    public static void Enrich(SymbolAnalysisResult analysis, string dbPath) =>
        Apply(dbPath, (root, head, dirty) => { analysis.ProjectRoot = root; analysis.GitHead = head; analysis.GitIsDirty = dirty; });

    /// <summary>
    /// Resolve workspace metadata once and apply it via callback.
    /// ワークスペースメタデータを一度解決し、コールバックで適用する。
    /// </summary>
    private static void Apply(string dbPath, Action<string?, string?, bool?> setter)
    {
        var projectRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath);
        setter(projectRoot, GitHelper.TryGetHeadCommit(projectRoot), GitHelper.TryIsWorktreeDirty(projectRoot));
    }
}
