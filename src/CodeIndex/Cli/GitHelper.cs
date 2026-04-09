using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodeIndex.Cli;

/// <summary>
/// Git integration helpers.
/// Git連携ヘルパー。
/// </summary>
public static class GitHelper
{
    /// <summary>
    /// Resolve the common git directory for a project root, handling both normal repos and worktrees.
    /// プロジェクトルートの共通gitディレクトリを解決する。通常リポジトリとworktreeの両方に対応。
    /// In a normal repo, .git is a directory and is returned directly.
    /// In a worktree, .git is a file containing "gitdir: path/to/.git/worktrees/name".
    /// The common dir is resolved via the "commondir" file inside the worktree git dir.
    /// </summary>
    public static string? ResolveGitCommonDir(string projectRoot)
    {
        var dotGit = Path.Combine(projectRoot, ".git");

        // Normal repository: .git is a directory / 通常リポジトリ: .gitがディレクトリ
        if (Directory.Exists(dotGit)) return dotGit;

        // Worktree: .git is a file containing "gitdir: <path>" / worktree: .gitがファイルで "gitdir: <path>" を含む
        if (!File.Exists(dotGit)) return null;

        var gitFileContent = File.ReadAllText(dotGit).Trim();
        if (!gitFileContent.StartsWith("gitdir:")) return null;

        var worktreeGitDir = gitFileContent.Substring("gitdir:".Length).Trim();
        if (!Path.IsPathRooted(worktreeGitDir))
            worktreeGitDir = Path.GetFullPath(Path.Combine(projectRoot, worktreeGitDir));

        // Read commondir to find the shared .git directory / commondirを読んで共有.gitディレクトリを見つける
        var commonDirFile = Path.Combine(worktreeGitDir, "commondir");
        if (File.Exists(commonDirFile))
        {
            var commonDirRelative = File.ReadAllText(commonDirFile).Trim();
            return Path.GetFullPath(Path.Combine(worktreeGitDir, commonDirRelative));
        }

        // Fallback: use worktreeGitDir itself (e.g. submodules) / フォールバック: worktreeGitDir自体を使用
        return worktreeGitDir;
    }

    /// <summary>
    /// Get changed files from a git commit.
    /// gitコミットから変更ファイルを取得する。
    /// </summary>
    public static List<string> GetChangedFilesFromCommit(string projectRoot, string commitId)
    {
        // Validate commit ID to prevent argument injection (only hex + common ref chars allowed)
        // Reject values starting with "-" to prevent git option injection even without "--" separator
        // コミットIDをバリデーションし引数インジェクションを防止（16進数+一般的な参照文字のみ許可）
        // "-"で始まる値も拒否し、"--"セパレータなしでもgitオプション注入を防止
        if (commitId.StartsWith('-') || !Regex.IsMatch(commitId, @"^[a-zA-Z0-9_./^~\-]+$"))
            throw new ArgumentException($"Invalid commit ID: {commitId}");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            // Use "--" to terminate options, preventing commitId from being parsed as a flag
            // "--"でオプション終了を明示し、commitIdがフラグとして解釈されるのを防止
            Arguments = $"diff-tree --no-commit-id -r --name-only -- {commitId}",
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process / gitプロセスの起動に失敗");
        // Read stderr asynchronously to avoid deadlock when stderr buffer fills
        // before stdout is fully consumed. See: MS docs on Process.StandardOutput.
        // stderrバッファが満杯になった時のデッドロックを防ぐため非同期で読む。
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        var error = errorTask.GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff-tree failed for commit {commitId}: {error.Trim()}");

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }
}
