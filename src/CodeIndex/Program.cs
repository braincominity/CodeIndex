using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIndex.Database;
using CodeIndex.Indexer;

// On Windows the console defaults to the OEM code page, causing Unicode
// characters (box-drawing, block elements, etc.) to appear as '?'.
// Windows のコンソールは既定で OEM コードページを使用するため、Unicode 文字が文字化けします。
Console.OutputEncoding = Encoding.UTF8;

// Exit codes / 終了コード
// 0 = success, 1 = usage error, 2 = not found, 3 = database error
const int ExitSuccess = 0;
const int ExitUsageError = 1;
const int ExitNotFound = 2;
const int ExitDbError = 3;

// JSON serializer options for structured output / JSON出力用のシリアライザ設定
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
};

// Route to subcommand / サブコマンドにルーティング
if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return args.Length == 0 ? ExitUsageError : ExitSuccess;
}

// Easter eggs / イースターエッグ
if (args.Any(a => a == "--sushi"))
{
    Console.WriteLine("\U0001f363 Indexing is like making sushi \u2014 patience yields perfection.");
    Console.WriteLine("   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u5bff\u53f8\u4f5c\u308a\u306e\u3088\u3046\u306b \u2014 \u5fcd\u8010\u304c\u5b8c\u74a7\u3092\u751f\u3080\u3002");
    return ExitSuccess;
}
if (args.Any(a => a == "--coffee"))
{
    Console.WriteLine("\u2615 Leave the indexing to me and go grab a coffee!");
    Console.WriteLine("   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u4efb\u305b\u3066\u3001\u30b3\u30fc\u30d2\u30fc\u3067\u3082\u98f2\u3093\u3067\u304d\u3066\uff01");
    return ExitSuccess;
}
if (args.Any(a => a == "--ramen"))
{
    Console.WriteLine("\U0001f35c Indexing in progress... perfect time for a bowl of ramen!");
    Console.WriteLine("   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u4e2d\u2026\u30e9\u30fc\u30e1\u30f3\u4e00\u676f\u3044\u304b\u304c\uff1f");
    return ExitSuccess;
}
if (args.Any(a => a == "--wine"))
{
    Console.WriteLine("\U0001f377 Crushing... Aging... Pouring... Sant\u00e9!");
    Console.WriteLine("   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u30ef\u30a4\u30f3\u306e\u3088\u3046\u306b\u2014\u719f\u6210\u3092\u5f85\u3064\u4fa1\u5024\u304c\u3042\u308b\u3002");
    return ExitSuccess;
}
if (args.Any(a => a == "--beer"))
{
    Console.WriteLine("\U0001f37a Tapping... Pouring... Foaming... Cheers!");
    Console.WriteLine("   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u5b8c\u4e86\u307e\u3067\u3001\u4e7e\u676f\uff01");
    return ExitSuccess;
}
if (args.Any(a => a == "--matcha"))
{
    Console.WriteLine("\U0001f375 Sifting... Pouring... Whisking... \u3069\u3046\u305e\uff01");
    Console.WriteLine("   \u4e00\u670d\u306e\u62b9\u8336\u3067\u3082\u3044\u304b\u304c\u3067\u3059\u304b\uff1f");
    return ExitSuccess;
}

return args[0] switch
{
    "search" => RunSearch(args[1..]),
    "symbols" => RunSymbols(args[1..]),
    "files" => RunFiles(args[1..]),
    "status" => RunStatus(args[1..]),
    "index" => RunIndex(args[1..]),
    // Backwards compatibility: if first arg looks like a path, treat as index
    // 後方互換性: 最初の引数がパスっぽければindexとして扱う
    _ when !args[0].StartsWith('-') && (Directory.Exists(args[0]) || args[0].Contains('/') || args[0].Contains('\\') || args[0] == ".") => RunIndex(args),
    _ => ShowError($"Unknown command: {args[0]}"),
};

// --- Search subcommand / 検索サブコマンド ---
int RunSearch(string[] cmdArgs)
{
    var (dbPath, json, limit, lang, _, query) = ParseQueryArgs(cmdArgs, jsonDefault: true);
    if (query == null)
    {
        Console.Error.WriteLine("Error: search requires a query argument");
        Console.Error.WriteLine("Usage: cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>]");
        return ExitUsageError;
    }

    return WithDb(dbPath, reader =>
    {
        var results = reader.Search(query, limit, lang);
        if (results.Count == 0)
        {
            if (!json)
                Console.Error.WriteLine("No results found.");
            return ExitNotFound;
        }

        if (json)
        {
            foreach (var r in results)
                Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
        }
        else
        {
            foreach (var r in results)
            {
                Console.WriteLine($"{r.Path}:{r.StartLine}-{r.EndLine}");
                // Indent content lines for readability / 可読性のためコンテンツ行をインデント
                foreach (var line in r.Content.Split('\n').Take(5))
                    Console.WriteLine($"  {line}");
                if (r.Content.Split('\n').Length > 5)
                    Console.WriteLine("  ...");
                Console.WriteLine();
            }
            Console.Error.WriteLine($"({results.Count} results)");
        }
        return ExitSuccess;
    });
}

// --- Symbols subcommand / シンボルサブコマンド ---
int RunSymbols(string[] cmdArgs)
{
    var (dbPath, json, limit, lang, kind, query) = ParseQueryArgs(cmdArgs, jsonDefault: true);

    return WithDb(dbPath, reader =>
    {
        var results = reader.SearchSymbols(query, limit, kind, lang);
        if (results.Count == 0)
        {
            if (!json)
                Console.Error.WriteLine("No symbols found.");
            return ExitNotFound;
        }

        if (json)
        {
            foreach (var r in results)
                Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
        }
        else
        {
            foreach (var r in results)
                Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.Path}:{r.Line}");
            Console.Error.WriteLine($"({results.Count} symbols)");
        }
        return ExitSuccess;
    });
}

// --- Files subcommand / ファイルサブコマンド ---
int RunFiles(string[] cmdArgs)
{
    var (dbPath, json, limit, lang, _, query) = ParseQueryArgs(cmdArgs, jsonDefault: true);

    return WithDb(dbPath, reader =>
    {
        var results = reader.ListFiles(query, limit, lang);
        if (results.Count == 0)
        {
            if (!json)
                Console.Error.WriteLine("No files found.");
            return ExitNotFound;
        }

        if (json)
        {
            foreach (var r in results)
                Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
        }
        else
        {
            foreach (var r in results)
                Console.WriteLine($"{r.Lang ?? "?",-12} {r.Lines,6} lines  {r.Path}");
            Console.Error.WriteLine($"({results.Count} files)");
        }
        return ExitSuccess;
    });
}

// --- Status subcommand / ステータスサブコマンド ---
int RunStatus(string[] cmdArgs)
{
    var (dbPath, json, _, _, _, _) = ParseQueryArgs(cmdArgs, jsonDefault: false);

    return WithDb(dbPath, reader =>
    {
        var status = reader.GetStatus();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(status, jsonOptions));
        }
        else
        {
            Console.WriteLine($"Files   : {status.Files:N0}");
            Console.WriteLine($"Chunks  : {status.Chunks:N0}");
            Console.WriteLine($"Symbols : {status.Symbols:N0}");
            if (status.Languages.Count > 0)
            {
                Console.WriteLine("Languages:");
                foreach (var (lang, count) in status.Languages)
                    Console.WriteLine($"  {lang,-12} {count,6}");
            }
        }
        return ExitSuccess;
    });
}

// --- Index subcommand (existing behavior) / インデックスサブコマンド（既存の動作） ---
int RunIndex(string[] indexArgs)
{
    var (projectPath, dbPath, rebuild, verbose, jsonOutput, commits, updateFiles) = ParseIndexArgs(indexArgs);

    if (projectPath == null)
    {
        PrintUsage();
        return ExitUsageError;
    }

    var stopwatch = Stopwatch.StartNew();
    var isUpdateMode = commits.Count > 0 || updateFiles.Count > 0;
    var mode = rebuild ? "rebuild" : isUpdateMode ? "update" : "incremental";

    if (!jsonOutput)
    {
        PrintBanner();
        Console.WriteLine($"  Project : {Path.GetFullPath(projectPath)}");
        Console.WriteLine($"  Output  : {dbPath}");
        Console.WriteLine($"  Mode    : {mode}");
        Console.WriteLine();
    }

    if (!Directory.Exists(projectPath))
    {
        if (jsonOutput)
            Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message = $"directory not found: {projectPath}" }, jsonOptions));
        else
            Console.Error.WriteLine($"Error: directory not found: {projectPath}");
        return ExitNotFound;
    }

    if (rebuild && isUpdateMode)
    {
        if (jsonOutput)
            Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message = "--rebuild cannot be used with --commits or --files" }, jsonOptions));
        else
            Console.Error.WriteLine("Error: --rebuild cannot be used with --commits or --files");
        return ExitUsageError;
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
        return RunUpdateMode(writer, indexer, projectRoot, dbPath, verbose, jsonOutput, commits, updateFiles, stopwatch);
    }
    else
    {
        return RunFullScan(writer, indexer, projectRoot, dbPath, verbose, jsonOutput, stopwatch);
    }
}

// --- Update mode / 更新モード ---
int RunUpdateMode(DbWriter writer, FileIndexer indexer, string projectRoot, string dbPath,
    bool verbose, bool jsonOutput, List<string> commits, List<string> updateFiles, Stopwatch stopwatch)
{
    var targetPaths = new HashSet<string>(StringComparer.Ordinal);

    // Resolve files from commit IDs / コミットIDから変更ファイルを解決
    if (commits.Count > 0)
    {
        if (!jsonOutput)
            Console.WriteLine($"Resolving changed files from {commits.Count} commit(s)...");
        foreach (var commit in commits)
        {
            var changedFiles = GetChangedFilesFromCommit(projectRoot, commit);
            foreach (var f in changedFiles)
                targetPaths.Add(f);
        }
        if (!jsonOutput)
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

    if (!jsonOutput)
        Console.WriteLine($"Updating {targetPaths.Count} file(s)...");
    int updated = 0, removed = 0, skipped = 0, errors = 0;
    var errorList = new List<object>();

    foreach (var relPath in targetPaths)
    {
        var absPath = Path.Combine(projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!File.Exists(absPath))
            {
                if (writer.DeleteFileByPath(relPath))
                {
                    removed++;
                    if (verbose && !jsonOutput)
                        Console.WriteLine($"  [DEL]  {relPath}");
                }
                else
                {
                    skipped++;
                    if (verbose && !jsonOutput)
                        Console.WriteLine($"  [SKIP] {relPath} (not in DB)");
                }
                continue;
            }

            if (FileIndexer.DetectLanguage(absPath) == null)
            {
                skipped++;
                if (verbose && !jsonOutput)
                    Console.WriteLine($"  [SKIP] {relPath} (unsupported type)");
                continue;
            }

            var (record, content) = indexer.BuildRecord(absPath);
            var fileId = writer.UpsertFile(record);
            writer.DeleteFileData(fileId);

            var chunks = ChunkSplitter.Split(fileId, content);
            writer.InsertChunks(chunks);

            var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
            writer.InsertSymbols(symbols);

            updated++;
            if (verbose && !jsonOutput)
                Console.WriteLine($"  [OK]   {relPath} ({chunks.Count} chunks, {symbols.Count} symbols)");
        }
        catch (Exception ex)
        {
            errors++;
            errorList.Add(new { file = relPath, message = ex.Message });
            if (!jsonOutput)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [ERR]  {relPath}: {ex.Message}\n{ex.StackTrace}");
                else
                    Console.Error.WriteLine($"  [ERR]  {relPath}: {ex.Message}");
            }
        }
    }

    stopwatch.Stop();
    var (totalFiles, totalChunks, totalSymbols) = writer.GetCounts();

    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = errors > 0 ? "partial" : "success",
            mode = "update",
            summary = new
            {
                files_total = totalFiles,
                chunks_total = totalChunks,
                symbols_total = totalSymbols,
                updated,
                removed,
                skipped,
                errors,
            },
            errors = errorList.Count > 0 ? errorList : null,
            elapsed_ms = stopwatch.ElapsedMilliseconds,
        }, jsonOptions));
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Done.");
        Console.WriteLine($"  Files   : {totalFiles:N0} (total in DB)");
        Console.WriteLine($"  Chunks  : {totalChunks:N0}");
        Console.WriteLine($"  Symbols : {totalSymbols:N0}");
        Console.WriteLine($"  Updated : {updated:N0}");
        if (removed > 0) Console.WriteLine($"  Removed : {removed:N0}");
        if (skipped > 0) Console.WriteLine($"  Skipped : {skipped:N0}");
        if (errors > 0) Console.WriteLine($"  Errors  : {errors:N0}");
        Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
    }

    return ExitSuccess;
}

// --- Full scan mode / フルスキャンモード ---
int RunFullScan(DbWriter writer, FileIndexer indexer, string projectRoot, string dbPath,
    bool verbose, bool jsonOutput, Stopwatch stopwatch)
{
    if (!jsonOutput) Console.WriteLine("Scanning...");
    var files = indexer.ScanFiles();
    if (!jsonOutput)
    {
        Console.WriteLine($"  Found {files.Count:N0} files");
        Console.WriteLine();
    }

    var purged = writer.PurgeStaleFiles(projectRoot);
    if (purged > 0 && !jsonOutput)
        Console.WriteLine($"  Purged {purged:N0} stale files (no longer on disk)");

    if (!jsonOutput) Console.WriteLine("Indexing...");
    int processed = 0, skipped = 0, errors = 0;
    var errorList = new List<object>();

    foreach (var filePath in files)
    {
        try
        {
            var (record, content) = indexer.BuildRecord(filePath);

            var existingId = writer.GetUnchangedFileId(record.Path, record.Modified);
            if (existingId != null)
            {
                skipped++;
                processed++;
                if (verbose && !jsonOutput)
                    Console.WriteLine($"  [SKIP] {record.Path}");
                if (!jsonOutput) PrintProgress(processed, files.Count);
                continue;
            }

            var fileId = writer.UpsertFile(record);
            writer.DeleteFileData(fileId);

            var chunks = ChunkSplitter.Split(fileId, content);
            writer.InsertChunks(chunks);

            var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
            writer.InsertSymbols(symbols);

            if (verbose && !jsonOutput)
                Console.WriteLine($"  [OK]   {record.Path} ({chunks.Count} chunks, {symbols.Count} symbols)");
        }
        catch (Exception ex)
        {
            errors++;
            errorList.Add(new { file = filePath, message = ex.Message });
            if (!jsonOutput)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [ERR]  {filePath}: {ex.Message}\n{ex.StackTrace}");
                else
                    Console.Error.WriteLine($"  [ERR]  {filePath}: {ex.Message}");
            }
        }

        processed++;
        if (!jsonOutput) PrintProgress(processed, files.Count);
    }

    stopwatch.Stop();
    var (totalFiles, totalChunks, totalSymbols) = writer.GetCounts();

    if (jsonOutput)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = errors > 0 ? "partial" : "success",
            mode = "incremental",
            summary = new
            {
                files_total = totalFiles,
                chunks_total = totalChunks,
                symbols_total = totalSymbols,
                files_scanned = files.Count,
                files_skipped = skipped,
                files_purged = purged,
                errors,
            },
            errors = errorList.Count > 0 ? errorList : null,
            elapsed_ms = stopwatch.ElapsedMilliseconds,
        }, jsonOptions));
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Done.");
        Console.WriteLine($"  Files   : {totalFiles:N0}");
        Console.WriteLine($"  Chunks  : {totalChunks:N0}");
        Console.WriteLine($"  Symbols : {totalSymbols:N0}");
        if (skipped > 0) Console.WriteLine($"  Skipped : {skipped:N0} (unchanged)");
        if (errors > 0) Console.WriteLine($"  Errors  : {errors:N0}");
        Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
    }

    return ExitSuccess;
}

// --- Helper: open DB and run query / ヘルパー: DBを開いてクエリ実行 ---
int WithDb(string dbPath, Func<DbReader, int> action)
{
    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: database not found: {dbPath}");
        Console.Error.WriteLine("Run 'cdidx index <projectPath>' first to create the index.");
        return ExitDbError;
    }

    try
    {
        using var db = new DbContext(dbPath);
        var reader = new DbReader(db.Connection);
        return action(reader);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: database error: {ex.Message}");
        return ExitDbError;
    }
}

int ShowError(string message)
{
    Console.Error.WriteLine($"Error: {message}");
    Console.Error.WriteLine("Run 'cdidx --help' for usage information.");
    return ExitUsageError;
}

// --- Argument parsers / 引数パーサー ---

// Parse query subcommand arguments / クエリサブコマンドの引数を解析
static (string dbPath, bool json, int limit, string? lang, string? kind, string? query) ParseQueryArgs(string[] args, bool jsonDefault)
{
    string dbPath = "codeindex.db";
    bool? json = null;
    int limit = 20;
    string? lang = null;
    string? kind = null;
    string? query = null;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--db" when i + 1 < args.Length:
                dbPath = args[++i];
                break;
            case "--json":
                json = true;
                break;
            case "--no-json":
                json = false;
                break;
            case "--limit" when i + 1 < args.Length:
                limit = int.Parse(args[++i]);
                break;
            case "--lang" when i + 1 < args.Length:
                lang = args[++i];
                break;
            case "--kind" when i + 1 < args.Length:
                kind = args[++i];
                break;
            default:
                if (!args[i].StartsWith('-') && query == null)
                    query = args[i];
                break;
        }
    }

    // Default: JSON for search/symbols/files, human-readable for status
    // デフォルト: search/symbols/filesはJSON、statusは人間向け
    return (dbPath, json ?? jsonDefault, limit, lang, kind, query);
}

// Parse index subcommand arguments / インデックスサブコマンドの引数を解析
static (string? projectPath, string dbPath, bool rebuild, bool verbose, bool json, List<string> commits, List<string> updateFiles) ParseIndexArgs(string[] args)
{
    string? projectPath = null;
    string dbPath = "codeindex.db";
    bool rebuild = false;
    bool verbose = false;
    bool json = false;
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
            case "--json":
                json = true;
                break;
            case "--commits":
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    commits.Add(args[++i]);
                break;
            case "--files":
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    updateFiles.Add(args[++i]);
                break;
            case "--help" or "-h":
                return (null, dbPath, rebuild, verbose, json, commits, updateFiles);
            default:
                if (!args[i].StartsWith('-'))
                    projectPath = args[i];
                break;
        }
    }

    return (projectPath, dbPath, rebuild, verbose, json, commits, updateFiles);
}

// Print inline progress bar / インライン進捗バーを表示
static void PrintProgress(int current, int total)
{
    // Update every 50 files or at completion / 50ファイルごと、または完了時に更新
    if (current % 50 != 0 && current != total)
        return;

    const int barWidth = 32;
    var pct = (double)current / total;
    int filled = (int)Math.Round(pct * barWidth);
    if (filled > barWidth) filled = barWidth;

    var bar = new string('\u2588', filled) + new string('\u2591', barWidth - filled);
    var line = $"  {bar} {pct * 100,5:F1}%  [{current:N0}/{total:N0}]";

    if (!Console.IsOutputRedirected)
    {
        Console.Write($"\r{line}");
        Console.Out.Flush();
        if (current == total)
            Console.WriteLine();
    }
    else
    {
        // Fallback for redirected output / リダイレクト時はフォールバック
        Console.WriteLine(line.TrimStart());
    }
}

// Print ASCII-art banner / ASCIIアートバナーを表示
static void PrintBanner()
{
    const string banner = """

         ██████╗ ██████╗ ██████╗ ███████╗██╗███╗   ██╗██████╗ ███████╗██╗  ██╗
        ██╔════╝██╔═══██╗██╔══██╗██╔════╝██║████╗  ██║██╔══██╗██╔════╝╚██╗██╔╝
        ██║     ██║   ██║██║  ██║█████╗  ██║██╔██╗ ██║██║  ██║█████╗   ╚███╔╝
        ██║     ██║   ██║██║  ██║██╔══╝  ██║██║╚██╗██║██║  ██║██╔══╝   ██╔██╗
        ╚██████╗╚██████╔╝██████╔╝███████╗██║██║ ╚████║██████╔╝███████╗██╔╝ ██╗
         ╚═════╝ ╚═════╝ ╚═════╝ ╚══════╝╚═╝╚═╝  ╚═══╝╚═════╝ ╚══════╝╚═╝  ╚═╝
                                                                        v1.1.0
        """;
    Console.WriteLine(banner);
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

// Print usage information / 使い方を表示
static void PrintUsage()
{
    PrintBanner();
    Console.WriteLine("Usage: cdidx <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  index <projectPath>        Index a project (default if path given)");
    Console.WriteLine("  search <query>             Full-text search across indexed chunks");
    Console.WriteLine("  symbols [query]            Search symbols (functions, classes, imports)");
    Console.WriteLine("  files [query]              List indexed files");
    Console.WriteLine("  status                     Show database statistics");
    Console.WriteLine();
    Console.WriteLine("Index options:");
    Console.WriteLine("  --db <path>                Database file path (default: codeindex.db)");
    Console.WriteLine("  --rebuild                  Delete existing DB and rebuild from scratch");
    Console.WriteLine("  --verbose                  Show verbose output");
    Console.WriteLine("  --json                     Output results as JSON (for AI/machine use)");
    Console.WriteLine("  --commits <id> [id ...]    Update only files changed in the specified git commits");
    Console.WriteLine("  --files <path> [path ...]  Update only the specified files (relative or absolute)");
    Console.WriteLine("  --help, -h                 Show this help message");
    Console.WriteLine();
    Console.WriteLine("Query options:");
    Console.WriteLine("  --db <path>                Database file path (default: codeindex.db)");
    Console.WriteLine("  --json                     Output as JSON lines (default for search/symbols/files)");
    Console.WriteLine("  --no-json                  Force human-readable output");
    Console.WriteLine("  --limit <n>                Max results to return (default: 20)");
    Console.WriteLine("  --lang <lang>              Filter by language");
    Console.WriteLine("  --kind <kind>              Filter symbols by kind (function/class/import)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  cdidx ./myproject                             Index a project");
    Console.WriteLine("  cdidx search \"authenticate\" --db codeindex.db  Full-text search");
    Console.WriteLine("  cdidx symbols UserService --kind class         Find class definitions");
    Console.WriteLine("  cdidx files --lang python                      List Python files");
    Console.WriteLine("  cdidx status --json                            DB stats as JSON");
    Console.WriteLine();
    Console.WriteLine("  cdidx ./myproject --commits abc123 def456      Update files from commits");
    Console.WriteLine("  cdidx ./myproject --files src/app.cs           Update specific files");
    Console.WriteLine();
    Console.WriteLine("Easter eggs:");
    Console.WriteLine("  --sushi                    \U0001f363");
    Console.WriteLine("  --coffee                   \u2615");
    Console.WriteLine("  --ramen                    \U0001f35c");
    Console.WriteLine("  --wine                     \U0001f377");
    Console.WriteLine("  --beer                     \U0001f37a");
    Console.WriteLine("  --matcha                   \U0001f375");
}
