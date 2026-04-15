using CodeIndex.Cli;
using CodeIndex.Database;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for default DB path resolution.
/// 既定DBパス解決のテスト。
/// </summary>
public class DbPathResolverTests
{
    [Fact]
    public void ResolveForIndex_UsesProjectLocalCdidxByDefault()
    {
        var projectPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "sample-project");
        var expectedProjectPath = Path.GetFullPath(projectPath);

        var dbPath = DbPathResolver.ResolveForIndex(projectPath, null);

        Assert.Equal(
            Path.Combine(expectedProjectPath, ".cdidx", "codeindex.db"),
            dbPath);
    }

    [Fact]
    public void ResolveForIndex_PrefersExplicitPath()
    {
        var projectPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "sample-project");
        var explicitPath = Path.Combine("custom", "index.db");

        var dbPath = DbPathResolver.ResolveForIndex(projectPath, explicitPath);

        Assert.Equal(explicitPath, dbPath);
    }

    [Fact]
    public void ResolveProjectRootForQuery_UsesParentOfCdidxDirectory()
    {
        var projectPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "sample-project");
        var dbPath = Path.Combine(projectPath, ".cdidx", "codeindex.db");

        var resolved = DbPathResolver.ResolveProjectRootForQuery(dbPath);

        Assert.Equal(Path.GetFullPath(projectPath), resolved);
    }

    [Fact]
    public void ResolveProjectRootForQuery_PrefersStoredIndexedProjectRootMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_db_path_resolver_meta_root");
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_db_path_resolver_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }

            var resolved = DbPathResolver.ResolveProjectRootForQuery(dbPath);

            Assert.Equal(projectRoot, resolved);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void ResolveProjectRootForQuery_ReadOnlyUri_PrefersStoredIndexedProjectRootMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_db_path_resolver_meta_uri");
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_db_path_resolver_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, projectRoot);
            }

            var readOnlyUri = new Uri(dbPath).AbsoluteUri + "?immutable=1";
            var resolved = DbPathResolver.ResolveProjectRootForQuery(readOnlyUri);

            Assert.Equal(projectRoot, resolved);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void ResolveProjectRootForQuery_ProjectLocalDbPrefersCdidxSiblingOverStoredMetadata()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_db_path_resolver_local");
        var staleRoot = TestProjectHelper.CreateTempProject("cdidx_db_path_resolver_stale");
        var dbPath = Path.Combine(projectRoot, ".cdidx", "codeindex.db");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
                var writer = new DbWriter(db.Connection);
                writer.SetMeta(DbContext.IndexedProjectRootMetaKey, staleRoot);
            }

            var resolved = DbPathResolver.ResolveProjectRootForQuery(dbPath);

            Assert.Equal(projectRoot, resolved);
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
            TestProjectHelper.DeleteDirectory(staleRoot);
        }
    }

    [Fact]
    public void ResolveProjectRootForQuery_ReturnsNullForExplicitDbWithoutMetadata()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_db_path_resolver_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DbContext(dbPath))
            {
                db.InitializeSchema();
            }

            var resolved = DbPathResolver.ResolveProjectRootForQuery(dbPath);

            Assert.Null(resolved);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
