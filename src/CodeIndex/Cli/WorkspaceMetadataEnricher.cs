using CodeIndex.Database;

namespace CodeIndex.Cli;

/// <summary>
/// Enrich repo/status responses with workspace-level freshness metadata.
/// ワークスペース単位の鮮度メタデータで repo/status レスポンスを補強する。
/// </summary>
public static class WorkspaceMetadataEnricher
{
    public static void Enrich(StatusResult status, string dbPath, bool dbPathExplicit = false) =>
        Apply(dbPath, dbPathExplicit, (root, head, dirty, indexedHead, headChanged) =>
        {
            status.ProjectRoot = root;
            status.GitHead = head;
            status.GitIsDirty = dirty;
            status.IndexedHeadCommit = indexedHead;
            status.WorktreeHeadChanged = headChanged;
            // #1509: compare the current HEAD against the SHA stamped at index time. Only
            // makes sense when both sides are known; otherwise leave the field null so the
            // CLI/MCP consumer can render "indexed at <sha>" without a misleading 0/N hint.
            // Note this reads `status.IndexedHeadSha` which was populated by the DbReader
            // (#1509 keys, stamped on every successful index — distinct from `indexedHead`
            // above which is #1508's full-scan-only `indexed_head_commit`).
            // #1509: index 時 HEAD と現 HEAD を比較し、両方判明している時のみ N を載せる。
            if (root != null && !string.IsNullOrWhiteSpace(status.IndexedHeadSha))
                status.CommitsAheadOfIndexedHead = GitHelper.TryCountCommitsAhead(root, status.IndexedHeadSha);
        });

    public static void Enrich(RepoMapResult map, string dbPath, bool dbPathExplicit = false) =>
        Apply(dbPath, dbPathExplicit, (root, head, dirty, indexedHead, headChanged) =>
        {
            map.ProjectRoot = root;
            map.GitHead = head;
            map.GitIsDirty = dirty;
            map.IndexedHeadCommit = indexedHead;
            map.WorktreeHeadChanged = headChanged;
        });

    public static void Enrich(SymbolAnalysisResult analysis, string dbPath, bool dbPathExplicit = false) =>
        Apply(dbPath, dbPathExplicit, (root, head, dirty, indexedHead, headChanged) =>
        {
            analysis.ProjectRoot = root;
            analysis.GitHead = head;
            analysis.GitIsDirty = dirty;
            analysis.IndexedHeadCommit = indexedHead;
            analysis.WorktreeHeadChanged = headChanged;
        });

    /// <summary>
    /// Resolve workspace metadata once and apply it via callback.
    /// ワークスペースメタデータを一度解決し、コールバックで適用する。
    /// </summary>
    private static void Apply(string dbPath, bool dbPathExplicit, Action<string?, string?, bool?, string?, bool?> setter)
    {
        var projectRoot = DbPathResolver.ResolveProjectRootForQuery(dbPath, dbPathExplicit);
        if (projectRoot == null)
        {
            setter(null, null, null, null, null);
            return;
        }

        var runtimeHead = GitHelper.TryGetHeadCommit(projectRoot);
        var runtimeBranch = GitHelper.TryGetHeadBranch(projectRoot);
        var dirty = GitHelper.TryIsWorktreeDirty(projectRoot);
        var indexedHead = DbPathResolver.TryReadIndexedHeadCommit(dbPath);
        var indexedBranch = DbPathResolver.TryReadIndexedHeadBranch(dbPath);
        var hasIndexedBranchStamp = DbPathResolver.TryHasIndexedHeadBranchStamp(dbPath);
        // Detect a per-worktree branch / HEAD switch by comparing the runtime HEAD against
        // the HEAD captured at index time. Also compare the branch stamp when a HEAD stamp is
        // present so branch <-> detached transitions at the same commit are still visible.
        // Only meaningful when enough metadata exists; legacy DBs or projects indexed outside
        // git report null and must not trigger a false-positive switch warning. Issues #1512
        // and #2094.
        // worktree 内の branch / HEAD 切替検出。index 時点と現在で HEAD を突き合わせる。
        // 同一 commit の branch/detached 遷移も branch stamp で検出する。
        var commitChanged = indexedHead != null && runtimeHead != null
            ? !string.Equals(indexedHead, runtimeHead, StringComparison.OrdinalIgnoreCase)
            : (bool?)null;
        var branchChanged = indexedHead != null
            && runtimeHead != null
            && hasIndexedBranchStamp
            && !string.Equals(indexedBranch, runtimeBranch, StringComparison.Ordinal)
            ? true
            : (bool?)null;
        bool? headChanged = commitChanged == true || branchChanged == true
            ? true
            : commitChanged;

        setter(projectRoot, runtimeHead, dirty, indexedHead, headChanged);
    }
}
