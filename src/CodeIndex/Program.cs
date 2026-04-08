using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Indexer;

// On Windows the console defaults to the OEM code page, causing Unicode
// characters (box-drawing, block elements, etc.) to appear as '?'.
// Windows のコンソールは既定で OEM コードページを使用するため、Unicode 文字が文字化けします。
Console.OutputEncoding = Encoding.UTF8;

// Load version from version.json / version.jsonからバージョンを読み込み
var appVersion = ConsoleUi.LoadVersion();

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
    ConsoleUi.PrintUsage();
    return args.Length == 0 ? ExitUsageError : ExitSuccess;
}

if (args[0] is "--version" or "-V")
{
    Console.WriteLine($"cdidx v{appVersion}");
    return ExitSuccess;
}

// Easter eggs — standalone only (no command/path given)
// イースターエッグ — 単体実行時のみ（コマンド/パスなし）
var easterEgg = args.FirstOrDefault(a => a is "--sushi" or "--coffee" or "--ramen" or "--wine" or "--beer" or "--matcha" or "--whisky");
if (easterEgg != null && !args.Any(a => !a.StartsWith('-')))
{
    ConsoleUi.PrintEasterEggMessage(easterEgg);
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
    var (dbPath, json, limit, lang, _, query) = ParseQueryArgs(cmdArgs, jsonDefault: false);
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
    var (dbPath, json, limit, lang, kind, query) = ParseQueryArgs(cmdArgs, jsonDefault: false);

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
    var (dbPath, json, limit, lang, _, query) = ParseQueryArgs(cmdArgs, jsonDefault: false);

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
    var (projectPath, dbPath, rebuild, verbose, jsonOutput, commits, updateFiles, easterEgg) = ParseIndexArgs(indexArgs);
    var spinnerFrames = ConsoleUi.GetSpinnerFrames(easterEgg);

    if (projectPath == null)
    {
        ConsoleUi.PrintUsage();
        return ExitUsageError;
    }

    var stopwatch = Stopwatch.StartNew();
    var isUpdateMode = commits.Count > 0 || updateFiles.Count > 0;
    var mode = rebuild ? "rebuild" : isUpdateMode ? "update" : "incremental";

    if (!jsonOutput)
    {
        ConsoleUi.PrintBanner();
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
        return RunUpdateMode(writer, indexer, projectRoot, dbPath, verbose, jsonOutput, commits, updateFiles, stopwatch, spinnerFrames);
    }
    else
    {
        return RunFullScan(writer, indexer, projectRoot, dbPath, verbose, jsonOutput, stopwatch, spinnerFrames);
    }
}

// --- Update mode / 更新モード ---
int RunUpdateMode(DbWriter writer, FileIndexer indexer, string projectRoot, string dbPath,
    bool verbose, bool jsonOutput, List<string> commits, List<string> updateFiles, Stopwatch stopwatch, string[] spinnerFrames)
{
    var targetPaths = new HashSet<string>(StringComparer.Ordinal);

    // Resolve files from commit IDs / コミットIDから変更ファイルを解決
    if (commits.Count > 0)
    {
        CancellationTokenSource? spinnerCts = null;
        if (!jsonOutput)
            spinnerCts = ConsoleUi.StartSpinner("Resolving changed files...", spinnerFrames);
        foreach (var commit in commits)
        {
            var changedFiles = GitHelper.GetChangedFilesFromCommit(projectRoot, commit);
            foreach (var f in changedFiles)
                targetPaths.Add(f);
        }
        ConsoleUi.StopSpinner(spinnerCts);
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
                        Console.WriteLine($"  [DEL ] {relPath}");
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

            // Wrap clean + upsert + insert in a transaction for atomicity
            // clean + upsert + insert をトランザクションでアトミックに実行
            using var txn = writer.BeginTransaction();
            try
            {
                var fileId = writer.UpsertFile(record);

                var chunks = ChunkSplitter.Split(fileId, content);
                writer.InsertChunks(chunks);

                var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
                writer.InsertSymbols(symbols);
                txn.Commit();

                updated++;
                if (verbose && !jsonOutput)
                    Console.WriteLine($"  [OK  ] {relPath} ({chunks.Count} chunks, {symbols.Count} symbols)");
            }
            finally
            {
                writer.EndTransaction();
            }
        }
        catch (Exception ex)
        {
            errors++;
            errorList.Add(new { file = relPath, message = ex.Message });
            if (!jsonOutput)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [ERR ] {relPath}: {ex.Message}\n{ex.StackTrace}");
                else
                    Console.Error.WriteLine($"  [ERR ] {relPath}: {ex.Message}");
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
    bool verbose, bool jsonOutput, Stopwatch stopwatch, string[] spinnerFrames)
{
    CancellationTokenSource? spinnerCts = null;
    if (!jsonOutput)
        spinnerCts = ConsoleUi.StartSpinner("Scanning...", spinnerFrames);
    var files = indexer.ScanFiles();
    ConsoleUi.StopSpinner(spinnerCts);
    if (!jsonOutput)
    {
        Console.WriteLine($"  Found {files.Count:N0} files");
        Console.WriteLine();
    }

    CancellationTokenSource? purgeCts = null;
    if (!jsonOutput)
        purgeCts = ConsoleUi.StartSpinner("Cleaning up stale entries...", spinnerFrames);
    var purged = writer.PurgeStaleFiles(projectRoot);
    ConsoleUi.StopSpinner(purgeCts);
    if (purged > 0 && !jsonOutput)
        Console.WriteLine($"  Purged {purged:N0} stale files (no longer on disk)");

    CancellationTokenSource? indexCts = null;
    if (!jsonOutput)
        indexCts = ConsoleUi.StartSpinner("Indexing...", spinnerFrames);
    int processed = 0, skipped = 0, errors = 0;
    var errorList = new List<object>();
    // Stop the indexing spinner before the first progress update / 最初のプログレス更新前にスピナーを停止
    bool indexSpinnerStopped = false;

    foreach (var filePath in files)
    {
        if (!indexSpinnerStopped)
        {
            ConsoleUi.StopSpinner(indexCts);
            indexSpinnerStopped = true;
            if (!jsonOutput) Console.WriteLine("Indexing...");
        }
        try
        {
            var (record, content) = indexer.BuildRecord(filePath);

            var existingId = writer.GetUnchangedFileId(record.Path, record.Modified, record.Checksum);
            if (existingId != null)
            {
                skipped++;
                processed++;
                if (verbose && !jsonOutput)
                    Console.WriteLine($"  [SKIP] {record.Path}");
                if (!jsonOutput) ConsoleUi.PrintProgress(processed, files.Count);
                continue;
            }

            // Wrap clean + upsert + insert in a transaction for atomicity
            // clean + upsert + insert をトランザクションでアトミックに実行
            using var txn = writer.BeginTransaction();
            try
            {
                var fileId = writer.UpsertFile(record);

                var chunks = ChunkSplitter.Split(fileId, content);
                writer.InsertChunks(chunks);

                var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
                writer.InsertSymbols(symbols);
                txn.Commit();

                if (verbose && !jsonOutput)
                    Console.WriteLine($"  [OK  ] {record.Path} ({chunks.Count} chunks, {symbols.Count} symbols)");
            }
            finally
            {
                writer.EndTransaction();
            }
        }
        catch (Exception ex)
        {
            errors++;
            errorList.Add(new { file = filePath, message = ex.Message });
            if (!jsonOutput)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [ERR ] {filePath}: {ex.Message}\n{ex.StackTrace}");
                else
                    Console.Error.WriteLine($"  [ERR ] {filePath}: {ex.Message}");
            }
        }

        processed++;
        if (!jsonOutput) ConsoleUi.PrintProgress(processed, files.Count);
    }

    // If no files to process, stop the indexing spinner / ファイルがない場合はスピナーを停止
    if (!indexSpinnerStopped)
    {
        ConsoleUi.StopSpinner(indexCts);
        if (!jsonOutput) Console.WriteLine("Indexing...");
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
                if (!int.TryParse(args[++i], out limit) || limit <= 0)
                {
                    Console.Error.WriteLine($"Error: --limit requires a positive integer, got '{args[i]}'");
                    limit = 20; // Reset to default / デフォルトにリセット
                }
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

    // Default: human-readable for all commands; use --json for machine output
    // デフォルト: 全コマンド人間向け出力、--jsonで機械向け出力
    return (dbPath, json ?? jsonDefault, limit, lang, kind, query);
}

// Parse index subcommand arguments / インデックスサブコマンドの引数を解析
static (string? projectPath, string dbPath, bool rebuild, bool verbose, bool json, List<string> commits, List<string> updateFiles, string? easterEgg) ParseIndexArgs(string[] args)
{
    string? projectPath = null;
    string dbPath = "codeindex.db";
    bool rebuild = false;
    bool verbose = false;
    bool json = false;
    string? easterEgg = null;
    int spinnerFlagCount = 0;
    bool randomSpinner = false;
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
                return (null, dbPath, rebuild, verbose, json, commits, updateFiles, null);
            case "--sushi" or "--coffee" or "--ramen" or "--wine" or "--beer" or "--matcha" or "--whisky":
                easterEgg = args[i];
                spinnerFlagCount++;
                break;
            case "--random-spinner":
                randomSpinner = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    projectPath = args[i];
                break;
        }
    }

    // Multiple spinner flags detected — fall back to matcha with a warning
    // 複数のスピナーフラグ検出 — 警告を出して抹茶にフォールバック
    if (spinnerFlagCount > 1)
    {
        Console.Error.WriteLine("\U0001f375 Simultaneous intake of beer and coffee is not recommended. How about some matcha instead?");
        Console.Error.WriteLine("   \u30d3\u30fc\u30eb\u3068\u30b3\u30fc\u30d2\u30fc\u306e\u540c\u6642\u6442\u53d6\u306f\u304a\u3059\u3059\u3081\u3057\u307e\u305b\u3093\u3002\u62b9\u8336\u306f\u3044\u304b\u304c\uff1f");
        easterEgg = "--matcha";
    }

    // --random-spinner picks a random theme / --random-spinnerでランダムテーマ選択
    if (randomSpinner && easterEgg == null)
    {
        var themes = new[] { "--sushi", "--coffee", "--ramen", "--wine", "--beer", "--matcha", "--whisky" };
        easterEgg = themes[Random.Shared.Next(themes.Length)];
    }

    return (projectPath, dbPath, rebuild, verbose, json, commits, updateFiles, easterEgg);
}
