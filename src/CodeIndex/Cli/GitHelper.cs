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
