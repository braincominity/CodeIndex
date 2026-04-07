using System.Diagnostics;
using CodeIndex.Database;
using CodeIndex.Indexer;

// Parse command-line arguments / コマンドライン引数を解析
var (projectPath, dbPath, rebuild, verbose) = ParseArgs(args);

if (projectPath == null)
{
    PrintUsage();
    return 1;
}

var stopwatch = Stopwatch.StartNew();
var mode = rebuild ? "rebuild" : "incremental";

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

// Delete DB if rebuild mode / rebuildモードならDB削除
if (rebuild && File.Exists(dbPath))
    File.Delete(dbPath);

using var db = new DbContext(dbPath);

if (rebuild)
    db.DropAll();

db.InitializeSchema();

var writer = new DbWriter(db.Connection);
var indexer = new FileIndexer(projectPath);

// Phase 1: Scan files / フェーズ1: ファイル走査
Console.WriteLine("Scanning...");
var files = indexer.ScanFiles();
Console.WriteLine($"  Found {files.Count:N0} files");
Console.WriteLine();

// Phase 1.5: Purge stale files (handles branch switches)
// フェーズ1.5: 古いファイルを削除（ブランチ切り替え対応）
var purged = writer.PurgeStaleFiles(Path.GetFullPath(projectPath));
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

// Parse CLI arguments / CLI引数を解析
static (string? projectPath, string dbPath, bool rebuild, bool verbose) ParseArgs(string[] args)
{
    string? projectPath = null;
    string dbPath = "codeindex.db";
    bool rebuild = false;
    bool verbose = false;

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
            case "--help" or "-h":
                return (null, dbPath, rebuild, verbose);
            default:
                if (!args[i].StartsWith('-'))
                    projectPath = args[i];
                break;
        }
    }

    return (projectPath, dbPath, rebuild, verbose);
}

// Print usage information / 使い方を表示
static void PrintUsage()
{
    Console.WriteLine("CodeIndex v1.0.0");
    Console.WriteLine("================");
    Console.WriteLine("Usage: codeindex <projectPath> [options]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  projectPath    Path to the project to index");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --db <path>    Output database file path (default: codeindex.db)");
    Console.WriteLine("  --rebuild      Delete existing DB and rebuild from scratch");
    Console.WriteLine("  --verbose      Show verbose output");
    Console.WriteLine("  --help, -h     Show this help message");
}
