using CodeIndex.Cli;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for GitHelper.ResolveGitCommonDir.
/// GitHelper.ResolveGitCommonDirのテスト。
/// </summary>
public class GitHelperTests : IDisposable
{
    private readonly string _tempDir;

    public GitHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cdidx_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void NormalRepo_ReturnsGitDirectory()
    {
        // Arrange: create a normal .git directory / 通常の.gitディレクトリを作成
        var gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(gitDir);

        // Act
        var result = GitHelper.ResolveGitCommonDir(_tempDir);

        // Assert
        Assert.Equal(gitDir, result);
    }

    [Fact]
    public void NoGitAtAll_ReturnsNull()
    {
        // No .git file or directory / .gitファイルもディレクトリもない
        var result = GitHelper.ResolveGitCommonDir(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void WorktreeWithAbsolutePath_ResolvesCommonDir()
    {
        // Arrange: simulate a worktree structure / worktree構造をシミュレート
        // Main repo .git dir
        var mainGitDir = Path.Combine(_tempDir, "main_repo", ".git");
        Directory.CreateDirectory(Path.Combine(mainGitDir, "info"));
        Directory.CreateDirectory(Path.Combine(mainGitDir, "worktrees", "my-worktree"));

        // commondir file inside the worktree git dir points to the main .git
        var worktreeGitDir = Path.Combine(mainGitDir, "worktrees", "my-worktree");
        File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

        // Worktree project directory with .git file
        var worktreeRoot = Path.Combine(_tempDir, "worktree_checkout");
        Directory.CreateDirectory(worktreeRoot);
        File.WriteAllText(Path.Combine(worktreeRoot, ".git"), $"gitdir: {worktreeGitDir}");

        // Act
        var result = GitHelper.ResolveGitCommonDir(worktreeRoot);

        // Assert: should resolve to the main .git directory
        Assert.Equal(Path.GetFullPath(mainGitDir), Path.GetFullPath(result!));
    }

    [Fact]
    public void WorktreeWithRelativePath_ResolvesCommonDir()
    {
        // Arrange: simulate worktree with relative gitdir path / 相対パスのgitdirでworktreeをシミュレート
        var mainGitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(Path.Combine(mainGitDir, "info"));
        Directory.CreateDirectory(Path.Combine(mainGitDir, "worktrees", "feat-branch"));

        var worktreeGitDir = Path.Combine(mainGitDir, "worktrees", "feat-branch");
        File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..");

        // Worktree is a sibling directory
        var worktreeRoot = Path.Combine(_tempDir, "worktree-feat");
        Directory.CreateDirectory(worktreeRoot);
        // Use relative path from worktree root to worktree git dir
        File.WriteAllText(Path.Combine(worktreeRoot, ".git"),
            $"gitdir: ../.git/worktrees/feat-branch");

        // Act
        var result = GitHelper.ResolveGitCommonDir(worktreeRoot);

        // Assert
        Assert.Equal(Path.GetFullPath(mainGitDir), Path.GetFullPath(result!));
    }

    [Fact]
    public void GitFileWithInvalidContent_ReturnsNull()
    {
        // .git file exists but doesn't start with "gitdir:" / .gitファイルがあるが"gitdir:"で始まらない
        File.WriteAllText(Path.Combine(_tempDir, ".git"), "some random content");

        var result = GitHelper.ResolveGitCommonDir(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void WorktreeWithoutCommonDir_FallsBackToWorktreeGitDir()
    {
        // Arrange: worktree git dir exists but has no commondir file / commondirファイルがない場合のフォールバック
        var worktreeGitDir = Path.Combine(_tempDir, "fake-git-dir");
        Directory.CreateDirectory(worktreeGitDir);

        var projectRoot = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, ".git"), $"gitdir: {worktreeGitDir}");

        // Act
        var result = GitHelper.ResolveGitCommonDir(projectRoot);

        // Assert: falls back to the worktree git dir itself
        Assert.Equal(Path.GetFullPath(worktreeGitDir), Path.GetFullPath(result!));
    }
}
