using System.Diagnostics;
using System.Text.RegularExpressions;
using CodeIndex.Indexer;

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

        var worktreeGitDir = gitFileContent["gitdir:".Length..].Trim();
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
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("diff-tree");
        psi.ArgumentList.Add("--no-commit-id");
        psi.ArgumentList.Add("--root");
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add("--name-only");
        psi.ArgumentList.Add(commitId);

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
            .Select(FileIndexer.NormalizePathSeparators)
            .ToList();
    }

    /// <summary>
    /// Try to resolve the current HEAD commit for the repository that contains the project root.
    /// projectRoot を含むリポジトリの現在の HEAD コミットを安全に取得する。
    /// </summary>
    public static string? TryGetHeadCommit(string projectRoot)
    {
        var output = TryRunGit(projectRoot, "rev-parse", "HEAD");
        var value = output?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Try to resolve the repository root that contains the project path.
    /// projectPath を含むリポジトリのルートを安全に取得する。
    /// </summary>
    public static string? TryGetRepositoryRoot(string projectPath)
        => TryGetRepositoryRoot(projectPath, gitEnvironmentOverrides: null);

    /// <summary>
    /// Resolve whether ignore matching should be case-insensitive for this workspace.
    /// git 管理下なら core.ignorecase を優先し、そうでなければファイルシステム特性を推定する。
    /// </summary>
    public static bool ResolveIgnoreCase(string projectRoot)
        => ResolveIgnoreCase(projectRoot, gitEnvironmentOverrides: null);

    internal static bool ResolveIgnoreCase(string projectRoot, IReadOnlyDictionary<string, string?>? gitEnvironmentOverrides)
    {
        var repoRoot = TryGetRepositoryRoot(projectRoot, gitEnvironmentOverrides);
        if (repoRoot == null)
            return ProbeFileSystemIgnoreCase(projectRoot);

        var configured = TryRunGit(repoRoot, gitEnvironmentOverrides, "config", "--bool", "--get", "core.ignorecase")?.Trim();
        if (bool.TryParse(configured, out var ignoreCase))
            return ignoreCase;

        return ProbeFileSystemIgnoreCase(projectRoot);
    }

    /// <summary>
    /// Try to determine whether the worktree has uncommitted changes.
    /// worktree に未コミット変更があるか安全に判定する。
    /// </summary>
    public static bool? TryIsWorktreeDirty(string projectRoot)
    {
        var output = TryRunGit(projectRoot, "status", "--porcelain");
        return output == null ? null : output.Length > 0;
    }

    internal static string? TryGetRepositoryRoot(string projectPath, IReadOnlyDictionary<string, string?>? gitEnvironmentOverrides)
    {
        var cdup = TryRunGit(projectPath, gitEnvironmentOverrides, "rev-parse", "--show-cdup");
        if (cdup == null)
            return null;

        var value = cdup.Trim();
        return string.IsNullOrEmpty(value)
            ? Path.GetFullPath(projectPath)
            : Path.GetFullPath(Path.Combine(projectPath, value));
    }

    private static string? TryRunGit(string projectRoot, params string[] args)
        => TryRunGit(projectRoot, gitEnvironmentOverrides: null, args);

    private static string? TryRunGit(string projectRoot, IReadOnlyDictionary<string, string?>? gitEnvironmentOverrides, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            if (gitEnvironmentOverrides != null)
            {
                foreach (var (key, value) in gitEnvironmentOverrides)
                {
                    if (value == null)
                        psi.Environment.Remove(key);
                    else
                        psi.Environment[key] = value;
                }
            }

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            var error = errorTask.GetAwaiter().GetResult();
            process.WaitForExit();

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ProbeFileSystemIgnoreCase(string projectRoot)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(projectRoot);
            if (TryProbeExistingDirectoryPath(normalizedRoot, out var ignoreCase))
                return ignoreCase;

            var probePath = Path.Combine(normalizedRoot, $".cdidx_case_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            try
            {
                if (TryCreateCaseVariant(probePath, out var variant))
                    return File.Exists(variant);
            }
            finally
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
        }
        catch
        {
            // Best-effort fallback only / best-effort のフォールバックのみ
        }

        return OperatingSystem.IsWindows();
    }

    private static bool TryProbeExistingDirectoryPath(string path, out bool ignoreCase)
    {
        ignoreCase = false;
        if (!TryCreateCaseVariant(path, out var variant))
            return false;

        ignoreCase = Directory.Exists(variant);
        return true;
    }

    private static bool TryCreateCaseVariant(string path, out string variant)
    {
        var chars = path.ToCharArray();
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            var ch = chars[i];
            if (!char.IsLetter(ch))
                continue;

            chars[i] = char.IsUpper(ch)
                ? char.ToLowerInvariant(ch)
                : char.ToUpperInvariant(ch);
            variant = new string(chars);
            return true;
        }

        variant = path;
        return false;
    }
}
