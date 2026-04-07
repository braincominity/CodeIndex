using System.Diagnostics;
using CodeIndex.Database;
using CodeIndex.Indexer;

// Parse command-line arguments / コマンドライン引数を解析
var (projectPath, dbPath, rebuild, verbose, commits, updateFiles) = ParseArgs(args);

if (projectPath == null)
{
    PrintUsage();
    return 1;
}

var stopwatch = Stopwatch.StartNew();
var isUpdateMode = commits.Count > 0 || updateFiles.Count > 0;
var mode = rebuild ? "rebuild" : isUpdateMode ? "update" : "incremental";

Console.WriteLine("CodeIndex v1.0.0");
Console.WriteLine("================");
Console.WriteLine($"Project : {Path.GetFullPath(projectPath)}");
Console.WriteLine($"Output  : {dbPath}");
Console.WriteLine($"Mode    : {mode}");
Console.WriteLine();

if (!Directory.Exists(projectPath))
{
    Console.Error.WriteLine($"Error: directory not found: {projectPath}");
    return 1;
}

if (rebuild && isUpdateMode)
{
    Console.Error.WriteLine("Error: --rebuild cannot be used with --commits or --files");
    return 1;
}

// Delete DB if rebuild mode / rebuildモードならDB削除
if (rebuild && File.Exists(dbPath))
    File.Delete(dbPath);

using var db = new DbContext(dbPath);

if (rebuild)
    db.DropAll();

db.InitializeSchema();

var writer = new DbWriter(db.Connection);
var indexer = new FileIndexer(projectPath);
var projectRoot = Path.GetFullPath(projectPath);

if (isUpdateMode)
{
    // Update mode: process only specified files
    // 更新モード: 指定ファイルのみ処理
    var targetPaths = new HashSet<string>(StringComparer.Ordinal);

    // Resolve files from commit IDs / コミットIDから変更ファイルを解決
    if (commits.Count > 0)
    {
        Console.WriteLine($"Resolving changed files from {commits.Count} commit(s)...");
        foreach (var commit in commits)
        {
            var changedFiles = GetChangedFilesFromCommit(projectRoot, commit);
            foreach (var f in changedFiles)
                targetPaths.Add(f);
        }
        Console.WriteLine($"  Found {targetPaths.Count} changed file(s) from git");
    }

    // Resolve explicitly specified files / 明示的に指定されたファイルを解決
    if (updateFiles.Count > 0)
    {
        foreach (var f in updateFiles)
        {
            var absPath = Path.IsPathRooted(f) ? f : Path.GetFullPath(Path.Combine(projectRoot, f));
            var relPath = Path.GetRelativePath(projectRoot, absPath).Replace('\\', '/');
            targetPaths.Add(relPath);
        }
    }

    Console.WriteLine($"Updating {targetPaths.Count} file(s)...");
    int updated = 0, removed = 0, skipped = 0, errors = 0;

    foreach (var relPath in targetPaths)
    {
        var absPath = Path.Combine(projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!File.Exists(absPath))
            {
                // File deleted: remove from DB / ファイル削除済み: DBから除去
                if (writer.DeleteFileByPath(relPath))
                {
                    removed++;
                    if (verbose)
                        Console.WriteLine($"  [DEL]  {relPath}");
                }
                else
                {
                    skipped++;
                    if (verbose)
                        Console.WriteLine($"  [SKIP] {relPath} (not in DB)");
                }
                continue;
            }

            // Check if file type is supported / サポート対象のファイル種別か確認
            if (FileIndexer.DetectLanguage(absPath) == null)
            {
                skipped++;
                if (verbose)
                    Console.WriteLine($"  [SKIP] {relPath} (unsupported type)");
                continue;
            }

            var (record, content) = indexer.BuildRecord(absPath);
            var fileId = writer.UpsertFile(record);

            // Delete old chunks/symbols before re-indexing
            // 再インデックス前に古いチャンク・シンボルを削除
            writer.DeleteFileData(fileId);

            // Split into chunks / チャンクに分割
            var chunks = ChunkSplitter.Split(fileId, content);
            writer.InsertChunks(chunks);

            // Extract symbols / シンボルを抽出
            var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
            writer.InsertSymbols(symbols);

            updated++;
            if (verbose)
                Console.WriteLine($"  [OK]   {relPath} ({chunks.Count} chunks, {symbols.Count} symbols)");
        }
        catch (Exception ex)
        {
            errors++;
            if (verbose)
                Console.Error.WriteLine($"  [ERR]  {relPath}: {ex.Message}\n{ex.StackTrace}");
            else
                Console.Error.WriteLine($"  [ERR]  {relPath}: {ex.Message}");
        }
    }

    Console.WriteLine();

    // Summary / サマリー
    stopwatch.Stop();
    var (totalFiles, totalChunks, totalSymbols) = writer.GetCounts();

    Console.WriteLine();
    Console.WriteLine("Done.");
    Console.WriteLine($"  Files   : {totalFiles:N0} (total in DB)");
    Console.WriteLine($"  Chunks  : {totalChunks:N0}");
    Console.WriteLine($"  Symbols : {totalSymbols:N0}");
    Console.WriteLine($"  Updated : {updated:N0}");
    if (removed > 0)
        Console.WriteLine($"  Removed : {removed:N0}");
    if (skipped > 0)
        Console.WriteLine($"  Skipped : {skipped:N0}");
    if (errors > 0)
        Console.WriteLine($"  Errors  : {errors:N0}");
    Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
}
else
{
    // Full scan mode (existing behavior) / フルスキャンモード（既存の動作）

    // Phase 1: Scan files / フェーズ1: ファイル走査
    Console.WriteLine("Scanning...");
    var files = indexer.ScanFiles();
    Console.WriteLine($"  Found {files.Count:N0} files");
    Console.WriteLine();

    // Phase 1.5: Purge stale files (handles branch switches)
    // フェーズ1.5: 古いファイルを削除（ブランチ切り替え対応）
    var purged = writer.PurgeStaleFiles(projectRoot);
    if (purged > 0)
        Console.WriteLine($"  Purged {purged:N0} stale files (no longer on disk)");

    // Phase 2: Index files / フェーズ2: ファイルインデックス
    Console.WriteLine("Indexing...");
    int processed = 0;
    int skipped = 0;
    int errors = 0;

    foreach (var filePath in files)
    {
        try
        {
            var (record, content) = indexer.BuildRecord(filePath);

            // Incremental: skip unchanged files / インクリメンタル: 未変更ファイルをスキップ
            var existingId = writer.GetUnchangedFileId(record.Path, record.Modified);
            if (existingId != null)
            {
                skipped++;
                processed++;
                if (verbose)
                    Console.WriteLine($"  [SKIP] {record.Path}");
                PrintProgress(processed, files.Count);
                continue;
            }

            // Upsert file record / ファイルレコードをUPSERT
            var fileId = writer.UpsertFile(record);

            // Delete old chunks/symbols before re-indexing
            // 再インデックス前に古いチャンク・シンボルを削除
            writer.DeleteFileData(fileId);

            // Split into chunks / チャンクに分割
            var chunks = ChunkSplitter.Split(fileId, content);
            writer.InsertChunks(chunks);

            // Extract symbols / シンボルを抽出
            var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
            writer.InsertSymbols(symbols);

            if (verbose)
                Console.WriteLine($"  [OK]   {record.Path} ({chunks.Count} chunks, {symbols.Count} symbols)");
        }
        catch (Exception ex)
        {
            // Always count errors; show details in verbose mode
            // エラーは常にカウント、詳細はverboseモードで表示
            errors++;
            if (verbose)
                Console.Error.WriteLine($"  [ERR]  {filePath}: {ex.Message}\n{ex.StackTrace}");
            else
                Console.Error.WriteLine($"  [ERR]  {filePath}: {ex.Message}");
        }

        processed++;
        PrintProgress(processed, files.Count);
    }

    Console.WriteLine();

    // Summary / サマリー
    stopwatch.Stop();
    var (totalFiles, totalChunks, totalSymbols) = writer.GetCounts();

    Console.WriteLine();
    Console.WriteLine("Done.");
    Console.WriteLine($"  Files   : {totalFiles:N0}");
    Console.WriteLine($"  Chunks  : {totalChunks:N0}");
    Console.WriteLine($"  Symbols : {totalSymbols:N0}");
    if (skipped > 0)
        Console.WriteLine($"  Skipped : {skipped:N0} (unchanged)");
    if (errors > 0)
        Console.WriteLine($"  Errors  : {errors:N0}");
    Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
}

return 0;

// --- Helper methods / ヘルパーメソッド ---

// Print progress at every 500 files / 500ファイルごとに進捗を表示
static void PrintProgress(int current, int total)
{
    if (current % 500 == 0 || current == total)
    {
        var pct = (double)current / total * 100;
        Console.WriteLine($"  [{current,5}/{total}] {pct,5:F1}%");
    }
}

// Get changed files from a git commit / gitコミットから変更ファイルを取得
static List<string> GetChangedFilesFromCommit(string projectRoot, string commitId)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        Arguments = $"diff-tree --no-commit-id -r --name-only {commitId}",
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

// Parse CLI arguments / CLI引数を解析
static (string? projectPath, string dbPath, bool rebuild, bool verbose, List<string> commits, List<string> updateFiles) ParseArgs(string[] args)
{
    string? projectPath = null;
    string dbPath = "codeindex.db";
    bool rebuild = false;
    bool verbose = false;
    var commits = new List<string>();
    var updateFiles = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            case "--rebuild":
                rebuild = true;
                break;
            case "--verbose":
                verbose = true;
                break;
            case "--commits":
                // Consume subsequent non-flag arguments as commit IDs
                // 後続のフラグ以外の引数をコミットIDとして取得
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    commits.Add(args[++i]);
                break;
            case "--files":
                // Consume subsequent non-flag arguments as file paths
                // 後続のフラグ以外の引数をファイルパスとして取得
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    updateFiles.Add(args[++i]);
                break;
            case "--help" or "-h":
                return (null, dbPath, rebuild, verbose, commits, updateFiles);
            default:
                if (!args[i].StartsWith('-'))
                    projectPath = args[i];
                break;
        }
    }

    return (projectPath, dbPath, rebuild, verbose, commits, updateFiles);
}

// Print usage information / 使い方を表示
static void PrintUsage()
{
    Console.WriteLine("CodeIndex v1.0.0");
    Console.WriteLine("================");
    Console.WriteLine("Usage: codeindex <projectPath> [options]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  projectPath                Path to the project to index");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --db <path>                Output database file path (default: codeindex.db)");
    Console.WriteLine("  --rebuild                  Delete existing DB and rebuild from scratch");
    Console.WriteLine("  --verbose                  Show verbose output");
    Console.WriteLine("  --commits <id> [id ...]    Update only files changed in the specified git commits");
    Console.WriteLine("  --files <path> [path ...]  Update only the specified files (relative or absolute)");
    Console.WriteLine("  --help, -h                 Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  codeindex ./myproject                           Full incremental index");
    Console.WriteLine("  codeindex ./myproject --commits abc123 def456   Update files from commits");
    Console.WriteLine("  codeindex ./myproject --files src/app.cs        Update specific files");
}
