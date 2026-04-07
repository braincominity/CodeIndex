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

// Load version from version.json / version.jsonからバージョンを読み込み
var appVersion = LoadVersion();

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
    PrintEasterEggMessage(easterEgg);
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
    var spinnerFrames = GetSpinnerFrames(easterEgg);

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
            spinnerCts = StartSpinner("Resolving changed files...", spinnerFrames);
        foreach (var commit in commits)
        {
            var changedFiles = GetChangedFilesFromCommit(projectRoot, commit);
            foreach (var f in changedFiles)
                targetPaths.Add(f);
        }
        StopSpinner(spinnerCts);
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
            var fileId = writer.UpsertFile(record);
            writer.DeleteFileData(fileId);

            var chunks = ChunkSplitter.Split(fileId, content);
            writer.InsertChunks(chunks);

            var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
            writer.InsertSymbols(symbols);

            updated++;
            if (verbose && !jsonOutput)
                Console.WriteLine($"  [OK  ] {relPath} ({chunks.Count} chunks, {symbols.Count} symbols)");
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
        spinnerCts = StartSpinner("Scanning...", spinnerFrames);
    var files = indexer.ScanFiles();
    StopSpinner(spinnerCts);
    if (!jsonOutput)
    {
        Console.WriteLine($"  Found {files.Count:N0} files");
        Console.WriteLine();
    }

    CancellationTokenSource? purgeCts = null;
    if (!jsonOutput)
        purgeCts = StartSpinner("Cleaning up stale entries...", spinnerFrames);
    var purged = writer.PurgeStaleFiles(projectRoot);
    StopSpinner(purgeCts);
    if (purged > 0 && !jsonOutput)
        Console.WriteLine($"  Purged {purged:N0} stale files (no longer on disk)");

    CancellationTokenSource? indexCts = null;
    if (!jsonOutput)
        indexCts = StartSpinner("Indexing...", spinnerFrames);
    int processed = 0, skipped = 0, errors = 0;
    var errorList = new List<object>();
    // Stop the indexing spinner before the first progress update / 最初のプログレス更新前にスピナーを停止
    bool indexSpinnerStopped = false;

    foreach (var filePath in files)
    {
        if (!indexSpinnerStopped)
        {
            StopSpinner(indexCts);
            indexSpinnerStopped = true;
            if (!jsonOutput) Console.WriteLine("Indexing...");
        }
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
                Console.WriteLine($"  [OK  ] {record.Path} ({chunks.Count} chunks, {symbols.Count} symbols)");
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
        if (!jsonOutput) PrintProgress(processed, files.Count);
    }

    // If no files to process, stop the indexing spinner / ファイルがない場合はスピナーを停止
    if (!indexSpinnerStopped)
    {
        StopSpinner(indexCts);
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

// Load version from version.json / version.jsonからバージョンを読み込み
static string LoadVersion()
{
    var exeDir = AppContext.BaseDirectory;
    var path = Path.Combine(exeDir, "version.json");
    if (!File.Exists(path))
    {
        // Fallback: look relative to current directory / カレントディレクトリからの相対パスでフォールバック
        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.json");
    }
    if (File.Exists(path))
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("version", out var ver))
            return ver.GetString() ?? "0.0.0";
    }
    return "0.0.0";
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
    Console.WriteLine("  --verbose                  Show per-file status ([OK  ]/[SKIP]/[DEL ]/[ERR ])");
    Console.WriteLine("  --json                     Output results as JSON (for AI/machine use)");
    Console.WriteLine("  --commits <id> [id ...]    Update only files changed in the specified git commits");
    Console.WriteLine("  --files <path> [path ...]  Update only the specified files (relative or absolute)");
    Console.WriteLine("  --help, -h                 Show this help message");
    Console.WriteLine("  --version, -V              Show version information");
    Console.WriteLine();
    Console.WriteLine("Query options:");
    Console.WriteLine("  --db <path>                Database file path (default: codeindex.db)");
    Console.WriteLine("  --json                     Output as JSON lines (for AI/machine use)");
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
    Console.WriteLine("Easter eggs (themed spinner when combined with index):");
    Console.WriteLine("  --sushi                    \U0001f363 Slicing... Shaping... Itadakimasu!");
    Console.WriteLine("  --coffee                   \u2615 Grinding... Heating... Brewing...");
    Console.WriteLine("  --ramen                    \U0001f35c Boiling... Steaming... Itadakimasu!");
    Console.WriteLine("  --wine                     \U0001f377 Crushing... Aging... Sant\u00e9!");
    Console.WriteLine("  --beer                     \U0001f37a Tapping... Pouring... Cheers!");
    Console.WriteLine("  --matcha                   \U0001f375 Sifting... Whisking... Douzo!");
    Console.WriteLine("  --whisky                   \U0001f943 Mashing... Distilling... Slainte!");
    Console.WriteLine("  --random-spinner           \U0001f3b2 Pick a random theme");
}

// --- Spinner / スピナー ---

// Start spinner on a background thread, returns CancellationTokenSource to stop it
// バックグラウンドスレッドでスピナーを開始。停止用のCancellationTokenSourceを返す
// When themed frames are used (easter egg), the frame itself contains the full display text
// テーマフレーム使用時（イースターエッグ）はフレーム自体が表示テキストを含む
static CancellationTokenSource? StartSpinner(string message, string[] frames)
{
    // Braille frames are single-char; themed frames are longer strings containing the display text
    // ブレイルフレームは1文字、テーマフレームは表示テキストを含む長い文字列
    bool isThemed = frames.Length > 0 && frames[0].Length > 2;

    if (Console.IsOutputRedirected)
    {
        Console.WriteLine(message);
        return null;
    }

    var cts = new CancellationTokenSource();
    var ct = cts.Token;
    Task.Run(() =>
    {
        int i = 0;
        while (!ct.IsCancellationRequested)
        {
            var frame = frames[i % frames.Length];
            var line = isThemed ? $"\r{frame}" : $"\r{frame} {message}";
            Console.Write(line);
            Console.Out.Flush();
            i++;
            try { Task.Delay(100, ct).Wait(ct); } catch (OperationCanceledException) { break; }
        }
    }, ct);
    return cts;
}

// Stop spinner and clear the line / スピナーを停止して行をクリア
static void StopSpinner(CancellationTokenSource? cts)
{
    if (cts == null) return;
    cts.Cancel();
    // Small delay to let the spinner task exit / スピナータスク終了のための短い待機
    Thread.Sleep(20);
    if (!Console.IsOutputRedirected)
    {
        Console.Write($"\r{new string(' ', Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 80)}\r");
        Console.Out.Flush();
    }
}

// Get spinner frames based on easter egg flag / イースターエッグフラグに基づくスピナーフレームを取得
static string[] GetSpinnerFrames(string? easterEgg) => easterEgg switch
{
    "--sushi" =>
    [
        "\U0001f363 Slicing       ", "\U0001f363 Slicing.      ", "\U0001f363 Slicing..     ", "\U0001f363 Slicing...    ",
        "\U0001f363 Shaping       ", "\U0001f363 Shaping.      ", "\U0001f363 Shaping..     ", "\U0001f363 Shaping...    ",
        "\U0001f363 Pressing      ", "\U0001f363 Pressing.     ", "\U0001f363 Pressing..    ", "\U0001f363 Pressing...   ",
        "\U0001f363 Itadakimasu!  ",
    ],
    "--coffee" =>
    [
        "\u2615 Grinding      ", "\u2615 Grinding.     ", "\u2615 Grinding..    ", "\u2615 Grinding...   ",
        "\u2615 Heating       ", "\u2615 Heating.      ", "\u2615 Heating..     ", "\u2615 Heating...    ",
        "\u2615 Brewing       ", "\u2615 Brewing.      ", "\u2615 Brewing..     ", "\u2615 Brewing...    ",
    ],
    "--ramen" =>
    [
        "\U0001f35c Boiling       ", "\U0001f35c Boiling.      ", "\U0001f35c Boiling..     ", "\U0001f35c Boiling...    ",
        "\U0001f35c Steaming      ", "\U0001f35c Steaming.     ", "\U0001f35c Steaming..    ", "\U0001f35c Steaming...   ",
        "\U0001f35c Slurping      ", "\U0001f35c Slurping.     ", "\U0001f35c Slurping..    ", "\U0001f35c Slurping...   ",
        "\U0001f35c Itadakimasu!  ",
    ],
    "--wine" =>
    [
        "\U0001f377 Crushing      ", "\U0001f377 Crushing.     ", "\U0001f377 Crushing..    ", "\U0001f377 Crushing...   ",
        "\U0001f377 Aging         ", "\U0001f377 Aging.        ", "\U0001f377 Aging..       ", "\U0001f377 Aging...      ",
        "\U0001f377 Pouring       ", "\U0001f377 Pouring.      ", "\U0001f377 Pouring..     ", "\U0001f377 Pouring...    ",
        "\U0001f377 Sant\u00e9!        ",
    ],
    "--beer" =>
    [
        "\U0001f37a Tapping       ", "\U0001f37a Tapping.      ", "\U0001f37a Tapping..     ", "\U0001f37a Tapping...    ",
        "\U0001f37a Pouring       ", "\U0001f37a Pouring.      ", "\U0001f37a Pouring..     ", "\U0001f37a Pouring...    ",
        "\U0001f37a Foaming       ", "\U0001f37a Foaming.      ", "\U0001f37a Foaming..     ", "\U0001f37a Foaming...    ",
        "\U0001f37a Cheers!       ",
    ],
    "--matcha" =>
    [
        "\U0001f375 Sifting       ", "\U0001f375 Sifting.      ", "\U0001f375 Sifting..     ", "\U0001f375 Sifting...    ",
        "\U0001f375 Pouring       ", "\U0001f375 Pouring.      ", "\U0001f375 Pouring..     ", "\U0001f375 Pouring...    ",
        "\U0001f375 Whisking      ", "\U0001f375 Whisking.     ", "\U0001f375 Whisking..    ", "\U0001f375 Whisking...   ",
        "\U0001f375 Douzo!        ",
    ],
    "--whisky" =>
    [
        "\U0001f943 Mashing       ", "\U0001f943 Mashing.      ", "\U0001f943 Mashing..     ", "\U0001f943 Mashing...    ",
        "\U0001f943 Distilling    ", "\U0001f943 Distilling.   ", "\U0001f943 Distilling..  ", "\U0001f943 Distilling... ",
        "\U0001f943 Aging         ", "\U0001f943 Aging.        ", "\U0001f943 Aging..       ", "\U0001f943 Aging...      ",
        "\U0001f943 Slainte!      ",
    ],
    // Default: Braille spinner / デフォルト: ブレイルスピナー
    _ => ["\u2807", "\u280b", "\u280f", "\u2838", "\u280f", "\u2839"],
};

// Print easter egg message (standalone mode) / イースターエッグメッセージを表示（単体実行時）
static void PrintEasterEggMessage(string flag)
{
    var (en, ja) = flag switch
    {
        "--sushi"  => ("\U0001f363 Indexing is like making sushi \u2014 patience yields perfection.",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u5bff\u53f8\u4f5c\u308a\u306e\u3088\u3046\u306b \u2014 \u5fcd\u8010\u304c\u5b8c\u74a7\u3092\u751f\u3080\u3002"),
        "--coffee" => ("\u2615 Leave the indexing to me and go grab a coffee!",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u4efb\u305b\u3066\u3001\u30b3\u30fc\u30d2\u30fc\u3067\u3082\u98f2\u3093\u3067\u304d\u3066\uff01"),
        "--ramen"  => ("\U0001f35c Indexing in progress... perfect time for a bowl of ramen!",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u4e2d\u2026\u30e9\u30fc\u30e1\u30f3\u4e00\u676f\u3044\u304b\u304c\uff1f"),
        "--wine"   => ("\U0001f377 Crushing... Aging... Pouring... Sant\u00e9!",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u30ef\u30a4\u30f3\u306e\u3088\u3046\u306b\u2014\u719f\u6210\u3092\u5f85\u3064\u4fa1\u5024\u304c\u3042\u308b\u3002"),
        "--beer"   => ("\U0001f37a Tapping... Pouring... Foaming... Cheers!",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u5b8c\u4e86\u307e\u3067\u3001\u4e7e\u676f\uff01"),
        "--matcha" => ("\U0001f375 Sifting... Pouring... Whisking... \u3069\u3046\u305e\uff01",
                       "   \u4e00\u670d\u306e\u62b9\u8336\u3067\u3082\u3044\u304b\u304c\u3067\u3059\u304b\uff1f"),
        "--whisky" => ("\U0001f943 Mashing... Distilling... Aging... Slainte!",
                       "   \u30a4\u30f3\u30c7\u30c3\u30af\u30b9\u306f\u30a6\u30a4\u30b9\u30ad\u30fc\u306e\u3088\u3046\u306b\u2014\u719f\u6210\u304c\u5927\u4e8b\u3002"),
        _ => ("", ""),
    };
    Console.WriteLine(en);
    Console.WriteLine(ja);
}
