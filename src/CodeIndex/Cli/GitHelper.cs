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
    /// Get changed files from a git commit.
    /// gitコミットから変更ファイルを取得する。
    /// </summary>
    public static List<string> GetChangedFilesFromCommit(string projectRoot, string commitId)
    {
        // Validate commit ID to prevent argument injection (only hex + common ref chars allowed)
        // コミットIDをバリデーションし引数インジェクションを防止（16進数+一般的な参照文字のみ許可）
        if (!Regex.IsMatch(commitId, @"^[a-zA-Z0-9_./^~\-]+$"))
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
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff-tree failed for commit {commitId}: {error.Trim()}");

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }
}
