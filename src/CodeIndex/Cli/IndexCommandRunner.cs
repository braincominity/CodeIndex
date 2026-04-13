using System.Diagnostics;
using System.Text.Json;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Cli;

/// <summary>
/// Runs indexing CLI commands.
/// インデックス系CLIコマンドを実行する。
/// </summary>
public static class IndexCommandRunner
{
    public static int Run(string[] indexArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(indexArgs);

        if (options.ShowHelp)
        {
            ConsoleUi.PrintUsage();
            return CommandExitCodes.Success;
        }

        var spinnerFrames = ConsoleUi.GetSpinnerFrames(options.EasterEgg);
        ConsoleUi.SetProgressTheme(options.EasterEgg);

        if (options.ProjectPath == null)
        {
            ConsoleUi.PrintUsage(showBanner: false);
            return CommandExitCodes.UsageError;
        }

        var dbPath = DbPathResolver.ResolveForIndex(options.ProjectPath, options.DbPath);
        var stopwatch = Stopwatch.StartNew();
        var isUpdateMode = options.Commits.Count > 0 || options.UpdateFiles.Count > 0;
        var mode = options.Rebuild ? "rebuild" : isUpdateMode ? "update" : "incremental";

        if (!options.Json)
        {
            ConsoleUi.PrintBanner();
            Console.WriteLine();
            Console.WriteLine($"  Project : {Path.GetFullPath(options.ProjectPath)}");
            Console.WriteLine($"  Output  : {Path.GetFullPath(dbPath)}");
            Console.WriteLine($"  Mode    : {mode}");
            Console.WriteLine();
        }

        if (!Directory.Exists(options.ProjectPath))
        {
            if (options.Json)
                Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message = $"directory not found: {options.ProjectPath}" }, jsonOptions));
            else
                Console.Error.WriteLine($"Error: directory not found: {options.ProjectPath}");
            return CommandExitCodes.NotFound;
        }

        if (options.Rebuild && isUpdateMode)
        {
            if (options.Json)
                Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message = "--rebuild cannot be used with --commits or --files (rebuild requires a full rescan)" }, jsonOptions));
            else
                Console.Error.WriteLine("Error: --rebuild cannot be used with --commits or --files (rebuild requires a full rescan)");
            return CommandExitCodes.UsageError;
        }

        // --dry-run: scan files but do not write to database / --dry-run: ファイルスキャンのみでDBに書き込まない
        if (options.DryRun)
        {
            var dryIndexer = new FileIndexer(options.ProjectPath);
            IReadOnlyList<string> dryFiles;

            if (options.UpdateFiles.Count > 0)
            {
                // --files: only the specified files / --files: 指定ファイルのみ
                dryFiles = options.UpdateFiles
                    .Select(f => Path.IsPathRooted(f) ? f : Path.Combine(options.ProjectPath, f))
                    .Where(File.Exists)
                    .ToList();
            }
            else if (options.Commits.Count > 0)
            {
                // --commits: files changed in the specified commits / --commits: 指定コミットの変更ファイル
                var changedFiles = new List<string>();
                foreach (var commit in options.Commits)
                {
                    try
                    {
                        var changed = GitHelper.GetChangedFilesFromCommit(options.ProjectPath, commit);
                        changedFiles.AddRange(changed.Select(f => Path.Combine(options.ProjectPath, f)).Where(File.Exists));
                    }
                    catch { /* ignore git errors in dry-run */ }
                }
                dryFiles = changedFiles.Distinct().ToList();
            }
            else
            {
                dryFiles = dryIndexer.ScanFiles();
            }

            var langCounts = new Dictionary<string, int>();
            foreach (var f in dryFiles)
            {
                var lang = FileIndexer.DetectLanguage(f) ?? "unknown";
                langCounts[lang] = langCounts.GetValueOrDefault(lang) + 1;
            }
            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "dry_run", files_total = dryFiles.Count, languages = langCounts }, jsonOptions));
            }
            else
            {
                Console.WriteLine($"Dry run: {dryFiles.Count} files would be indexed");
                foreach (var (lang, count) in langCounts.OrderByDescending(kv => kv.Value))
                    Console.WriteLine($"  {lang,-12} {count,6}");
            }
            return CommandExitCodes.Success;
        }

        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);

        using var db = new DbContext(dbPath);

        // Capture prior readiness BEFORE we clear it. Update mode (--commits / --files) only
        // touches a subset of files, so stamping readiness after a partial pass on a
        // previously-degraded DB would falsely bless untouched files as authoritative. Only
        // a DB that was already fully ready may be restamped after a partial update.
        // update モードは一部ファイルしか再インデックスしないため、元々縮退状態だった DB を
        // partial pass の後に trusted 扱いに昇格させてはいけない。元の readiness を捕獲しておく。
        var wasFullyReady = db.GetUserVersion() == DbContext.CurrentSchemaVersion;

        // Don't demote readiness yet. A transient usage error in update-mode preflight
        // (bad --commits hash, git unavailable, etc.) would permanently downgrade a healthy
        // DB even though no data was touched. Clearing happens just before the first
        // destructive / schema-changing operation, inside the mode-specific runner.
        // まだ clear しない。update モードの preflight が失敗しただけで healthy な DB を
        // 縮退状態に落とさないよう、clear は実際に書き込み直前で行う。

        if (options.Rebuild)
        {
            db.ClearReadyFlags();
            db.DropAll();
        }

        db.InitializeSchema();
        AddToGitExclude(options.ProjectPath, dbPath);

        var writer = new DbWriter(db.Connection);
        var indexer = new FileIndexer(options.ProjectPath);
        var projectRoot = Path.GetFullPath(options.ProjectPath);

        // Full-scan covers the whole repo, so it may always stamp on success. Update mode
        // only stamps when the DB was already fully ready, preventing a partial pass from
        // promoting a legacy / degraded DB to trusted.
        // full-scan は常に stamp 可。update は元々 trusted だった場合のみ再 stamp 可。
        var canStampReadiness = !isUpdateMode || wasFullyReady;

        return isUpdateMode
            ? RunUpdateMode(writer, indexer, projectRoot, options, stopwatch, spinnerFrames, jsonOptions, canStampReadiness)
            : RunFullScan(writer, indexer, projectRoot, options, stopwatch, spinnerFrames, jsonOptions, canStampReadiness);
    }


    public static IndexCommandOptions ParseArgs(string[] args)
    {
        string? projectPath = null;
        string? dbPath = null;
        bool rebuild = false;
        bool verbose = false;
        bool json = false;
        bool dryRun = false;
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
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--commits":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        commits.Add(args[++i]);
                    if (commits.Count == 0)
                        Console.Error.WriteLine("Warning: --commits specified but no commit IDs provided / --commits が指定されましたがコミットIDがありません");
                    break;
                case "--files":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        updateFiles.Add(args[++i]);
                    if (updateFiles.Count == 0)
                        Console.Error.WriteLine("Warning: --files specified but no file paths provided / --files が指定されましたがファイルパスがありません");
                    break;
                case "--help" or "-h":
                    return new IndexCommandOptions { ShowHelp = true };
                case "--sushi" or "--coffee" or "--ramen" or "--wine" or "--beer" or "--matcha" or "--whisky":
                    easterEgg = args[i];
                    spinnerFlagCount++;
                    break;
                case "--random-spinner":
                    randomSpinner = true;
                    break;
                default:
                    if (args[i].StartsWith('-'))
                        Console.Error.WriteLine($"Warning: unknown option '{args[i]}' (ignored) / 不明なオプション '{args[i]}'（無視されます）");
                    else
                        projectPath = args[i];
                    break;
            }
        }

        if (spinnerFlagCount > 1)
        {
            Console.Error.WriteLine("\U0001f375 Simultaneous intake of beer and coffee is not recommended. How about some matcha instead?");
            Console.Error.WriteLine("   \u30d3\u30fc\u30eb\u3068\u30b3\u30fc\u30d2\u30fc\u306e\u540c\u6642\u6442\u53d6\u306f\u304a\u3059\u3059\u3081\u3057\u307e\u305b\u3093\u3002\u62b9\u8336\u306f\u3044\u304b\u304c\uff1f");
            easterEgg = "--matcha";
        }

        if (randomSpinner && easterEgg == null)
        {
            var themes = new[] { "--sushi", "--coffee", "--ramen", "--wine", "--beer", "--matcha", "--whisky" };
            easterEgg = themes[Random.Shared.Next(themes.Length)];
        }

        return new IndexCommandOptions
        {
            ProjectPath = projectPath,
            DbPath = dbPath,
            Rebuild = rebuild,
            Verbose = verbose,
            Json = json,
            Commits = commits,
            UpdateFiles = updateFiles,
            EasterEgg = easterEgg,
            DryRun = dryRun,
        };
    }

    private static int RunUpdateMode(
        DbWriter writer,
        FileIndexer indexer,
        string projectRoot,
        IndexCommandOptions options,
        Stopwatch stopwatch,
        string[] spinnerFrames,
        JsonSerializerOptions jsonOptions,
        bool canStampReadiness)
    {
        var targetPaths = new HashSet<string>(StringComparer.Ordinal);

        if (options.Commits.Count > 0)
        {
            CancellationTokenSource? spinnerCts = null;
            try
            {
                if (!options.Json)
                    spinnerCts = ConsoleUi.StartSpinner("Resolving changed files...", spinnerFrames);
                foreach (var commit in options.Commits)
                {
                    var changedFiles = GitHelper.GetChangedFilesFromCommit(projectRoot, commit);
                    foreach (var f in changedFiles)
                        targetPaths.Add(f);
                }
            }
            catch (Exception ex)
            {
                return WriteCommandError(
                    options.Json,
                    jsonOptions,
                    $"failed to resolve changed files from git commits: {ex.Message}",
                    CommandExitCodes.UsageError);
            }
            finally
            {
                ConsoleUi.StopSpinner(spinnerCts);
            }
            if (!options.Json)
            {
                Console.WriteLine($"  Found {targetPaths.Count} changed file(s) from git");
                Console.WriteLine("  Note    : After reset/rebase/amend/switch/merge, prefer `cdidx .` over `--commits` for a full sync / 履歴改変やcheckout変更後は `--commits` より `cdidx .` を推奨");
            }
        }

        if (options.UpdateFiles.Count > 0)
        {
            foreach (var f in options.UpdateFiles)
            {
                var absPath = Path.IsPathRooted(f) ? f : Path.GetFullPath(Path.Combine(projectRoot, f));
                var relPath = Path.GetRelativePath(projectRoot, absPath).Replace('\\', '/');
                if (IsOutsideProjectRoot(relPath))
                {
                    if (!options.Json)
                        Console.Error.WriteLine($"  [WARN] Skipping file outside project root: {f}");
                    continue;
                }
                targetPaths.Add(relPath);
            }
        }

        // Now that preflight (commit / file resolution) has succeeded and we're committed to
        // mutating the DB, demote readiness. Doing this earlier — before commit resolution —
        // would turn a transient git failure or a typo in --commits into a permanent
        // downgrade of a previously-healthy index.
        // preflight 成功後、実書き込み直前で readiness をクリア。途中で失敗した場合は healthy index を壊さない。
        writer.ClearReadyFlags();

        // Purge references for languages no longer graph-supported / グラフ非対応になった言語の参照をパージ
        var purgedRefs = writer.PurgeUnsupportedReferences(ReferenceExtractor.GetSupportedLanguages());
        if (purgedRefs > 0 && !options.Json)
            Console.WriteLine($"  Purged {purgedRefs:N0} stale references (unsupported language)");

        if (!options.Json)
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
                        if (options.Verbose && !options.Json)
                            Console.WriteLine($"  [DEL ] {relPath}");
                    }
                    else
                    {
                        skipped++;
                        if (options.Verbose && !options.Json)
                            Console.WriteLine($"  [SKIP] {relPath} (not in DB)");
                    }
                    continue;
                }

                if (FileIndexer.DetectLanguage(absPath) == null)
                {
                    skipped++;
                    if (options.Verbose && !options.Json)
                        Console.WriteLine($"  [SKIP] {relPath} (unsupported type)");
                    continue;
                }

                var (record, content, rawBytes, warning) = indexer.BuildRecordWithRawBytes(absPath);

                if (warning != null && !options.Json)
                    ConsoleUi.PrintWarning(warning);

                var existingId = writer.GetUnchangedFileId(record.Path, record.Modified, record.Checksum);
                if (existingId != null)
                {
                    skipped++;
                    if (options.Verbose && !options.Json)
                        Console.WriteLine($"  [SKIP] {relPath} (unchanged)");
                    continue;
                }

                using var txn = writer.BeginTransaction();
                var fileId = writer.UpsertFile(record);
                var chunks = ChunkSplitter.Split(fileId, content);
                writer.InsertChunks(chunks);
                var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
                writer.InsertSymbols(symbols);
                var references = ReferenceExtractor.Extract(fileId, record.Lang, content, symbols);
                writer.InsertReferences(references);
                // Validate content for encoding issues / エンコーディング問題を検証
                var issues = FileIndexer.ValidateContent(record.Path, rawBytes, content);
                writer.InsertIssues(fileId, issues);
                txn.Commit();

                updated++;
                if (options.Verbose && !options.Json)
                    Console.WriteLine($"  [OK  ] {relPath} ({chunks.Count} chunks, {symbols.Count} symbols, {references.Count} refs)");
            }
            catch (Exception ex)
            {
                errors++;
                errorList.Add(new { file = relPath, message = ex.Message });
                if (!options.Json)
                {
                    if (options.Verbose)
                        Console.Error.WriteLine($"  [ERR ] {relPath}: {ex.Message}\n{ex.StackTrace}");
                    else
                        Console.Error.WriteLine($"  [ERR ] {relPath}: {ex.Message}");
                }
            }
        }

        writer.OptimizeFts();
        // Only stamp readiness on a fully successful run (errors == 0). A partial / error
        // run leaves the DB unstamped so readers correctly treat graph / issues data as
        // degraded rather than authoritative. Interrupted runs also stay unstamped because
        // ClearReadyFlags() ran at the start.
        // errors==0 の成功 run のみマーカーを打つ。途中失敗は未 stamp のままで縮退扱い。
        if (errors == 0 && canStampReadiness)
        {
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            // Update mode does NOT stamp FoldReady (#86): only the files in this partial
            // update have name_folded populated, so legacy rows outside the update scope
            // stay NULL. Reader must keep using the COLLATE NOCASE fallback until a full
            // scan upgrades the whole DB.
            // update mode は fold stamp しない。範囲外の legacy 行が未埋めで残るため。
        }
        stopwatch.Stop();
        var (totalFiles, totalChunks, totalSymbols, totalReferences) = writer.GetCounts();

        if (options.Json)
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
                    references_total = totalReferences,
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
            Console.WriteLine();
            Console.WriteLine($"  Files   : {totalFiles:N0} (total in DB)");
            Console.WriteLine($"  Chunks  : {totalChunks:N0}");
            Console.WriteLine($"  Symbols : {totalSymbols:N0}");
            Console.WriteLine($"  Refs    : {totalReferences:N0}");
            Console.WriteLine($"  Updated : {updated:N0}");
            if (removed > 0) Console.WriteLine($"  Removed : {removed:N0}");
            if (skipped > 0) Console.WriteLine($"  Skipped : {skipped:N0}");
            if (errors > 0) Console.WriteLine($"  Errors  : {errors:N0}");
            Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine();
        }

        return CommandExitCodes.Success;
    }

    private static bool IsOutsideProjectRoot(string relativePath) =>
        relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal);

    private static int WriteCommandError(bool json, JsonSerializerOptions jsonOptions, string message, int exitCode)
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message }, jsonOptions));
        else
            Console.Error.WriteLine($"Error: {message}");
        return exitCode;
    }

    private static int RunFullScan(
        DbWriter writer,
        FileIndexer indexer,
        string projectRoot,
        IndexCommandOptions options,
        Stopwatch stopwatch,
        string[] spinnerFrames,
        JsonSerializerOptions jsonOptions,
        bool canStampReadiness)
    {
        CancellationTokenSource? spinnerCts = null;
        if (!options.Json)
            spinnerCts = ConsoleUi.StartSpinner("Scanning...", spinnerFrames);
        var files = indexer.ScanFiles();
        ConsoleUi.StopSpinner(spinnerCts);
        if (!options.Json)
        {
            Console.WriteLine($"  Found {files.Count:N0} files");
            Console.WriteLine();
        }

        // Full-scan commits to mutating the DB from here on. Demote readiness just before
        // the first write (PurgeStaleFiles). This is equivalent to clearing earlier in terms
        // of the --rebuild crash-window guard (the --rebuild path already cleared before
        // DropAll in RunIndex), but avoids downgrading a healthy DB if full-scan itself
        // never reaches this point for any reason.
        // 実書き込み直前で readiness をクリア。--rebuild 経路は RunIndex で既に clear 済み。
        writer.ClearReadyFlags();

        CancellationTokenSource? purgeCts = null;
        if (!options.Json)
            purgeCts = ConsoleUi.StartSpinner("Cleaning up stale entries...", spinnerFrames);
        var purged = writer.PurgeStaleFiles(projectRoot);
        ConsoleUi.StopSpinner(purgeCts);
        if (purged > 0 && !options.Json)
            Console.WriteLine($"  Purged {purged:N0} stale files (no longer on disk)");

        // Purge references for languages no longer graph-supported / グラフ非対応になった言語の参照をパージ
        var purgedRefs = writer.PurgeUnsupportedReferences(ReferenceExtractor.GetSupportedLanguages());
        if (purgedRefs > 0 && !options.Json)
            Console.WriteLine($"  Purged {purgedRefs:N0} stale references (unsupported language)");

        CancellationTokenSource? indexCts = null;
        if (!options.Json)
            indexCts = ConsoleUi.StartSpinner("Indexing...", spinnerFrames);
        int processed = 0, skipped = 0, errors = 0;
        var errorList = new List<object>();
        bool indexSpinnerStopped = false;

        foreach (var filePath in files)
        {
            if (!indexSpinnerStopped)
            {
                ConsoleUi.StopSpinner(indexCts);
                indexSpinnerStopped = true;
                if (!options.Json) Console.WriteLine("Indexing...");
            }
            try
            {
                var (record, content, rawBytes, warning) = indexer.BuildRecordWithRawBytes(filePath);

                if (warning != null && !options.Json)
                    ConsoleUi.PrintWarning(warning);

                var existingId = writer.GetUnchangedFileId(record.Path, record.Modified, record.Checksum);
                if (existingId != null)
                {
                    skipped++;
                    processed++;
                    if (options.Verbose && !options.Json)
                    {
                        ConsoleUi.ClearProgressLine();
                        Console.WriteLine($"  [SKIP] {record.Path}");
                    }
                    if (!options.Json) ConsoleUi.PrintProgress(processed, files.Count);
                    continue;
                }

                using var txn = writer.BeginTransaction();
                var fileId = writer.UpsertFile(record);
                var chunks = ChunkSplitter.Split(fileId, content);
                writer.InsertChunks(chunks);
                var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
                writer.InsertSymbols(symbols);
                var references = ReferenceExtractor.Extract(fileId, record.Lang, content, symbols);
                writer.InsertReferences(references);
                // Validate content for encoding issues / エンコーディング問題を検証
                var issues = FileIndexer.ValidateContent(record.Path, rawBytes, content);
                writer.InsertIssues(fileId, issues);
                txn.Commit();

                if (options.Verbose && !options.Json)
                {
                    ConsoleUi.ClearProgressLine();
                    Console.WriteLine($"  [OK  ] {record.Path} ({chunks.Count} chunks, {symbols.Count} symbols, {references.Count} refs)");
                }
            }
            catch (Exception ex)
            {
                errors++;
                errorList.Add(new { file = filePath, message = ex.Message });
                if (!options.Json)
                {
                    ConsoleUi.ClearProgressLine();
                    if (options.Verbose)
                        Console.Error.WriteLine($"  [ERR ] {filePath}: {ex.Message}\n{ex.StackTrace}");
                    else
                        Console.Error.WriteLine($"  [ERR ] {filePath}: {ex.Message}");
                }
            }

            processed++;
            if (!options.Json) ConsoleUi.PrintProgress(processed, files.Count);
        }

        if (!indexSpinnerStopped)
        {
            ConsoleUi.StopSpinner(indexCts);
            if (!options.Json) Console.WriteLine("Indexing...");
        }

        writer.OptimizeFts();
        // Only stamp readiness on a fully successful run (errors == 0). A partial / error
        // run leaves the DB unstamped so readers correctly treat graph / issues data as
        // degraded rather than authoritative. Interrupted runs also stay unstamped because
        // ClearReadyFlags() ran at the start.
        // errors==0 の成功 run のみマーカーを打つ。途中失敗は未 stamp のままで縮退扱い。
        if (errors == 0 && canStampReadiness)
        {
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            // Full-scan always stamps FoldReady (#86): every symbols / symbol_references
            // row this run inserted has name_folded populated, so the reader's Unicode
            // fold path is safe to trust for `--exact` queries.
            // full-scan は fold も stamp。全行に folded が入っているので reader の unicode 経路 OK。
            writer.MarkFoldReady();
        }
        stopwatch.Stop();
        var (totalFiles, totalChunks, totalSymbols, totalReferences) = writer.GetCounts();

        if (options.Json)
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
                    references_total = totalReferences,
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
            Console.WriteLine();
            Console.WriteLine($"  Files   : {totalFiles:N0}");
            Console.WriteLine($"  Chunks  : {totalChunks:N0}");
            Console.WriteLine($"  Symbols : {totalSymbols:N0}");
            Console.WriteLine($"  Refs    : {totalReferences:N0}");
            if (skipped > 0) Console.WriteLine($"  Skipped : {skipped:N0} (unchanged)");
            if (errors > 0) Console.WriteLine($"  Errors  : {errors:N0}");
            Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine();
        }

        return CommandExitCodes.Success;
    }

    private static void AddToGitExclude(string projectPath, string dbPath)
    {
        try
        {
            var projectRoot = Path.GetFullPath(projectPath);
            var gitDir = GitHelper.ResolveGitCommonDir(projectRoot);
            if (gitDir == null) return;

            var excludeFile = Path.Combine(gitDir, "info", "exclude");
            var dbAbsolutePath = Path.IsPathRooted(dbPath)
                ? Path.GetFullPath(dbPath)
                : Path.GetFullPath(Path.Combine(projectRoot, dbPath));
            var dbDirAbsolute = Path.GetDirectoryName(dbAbsolutePath);
            if (string.IsNullOrEmpty(dbDirAbsolute)) return;

            var dbDirRelative = Path.GetRelativePath(projectRoot, dbDirAbsolute).Replace('\\', '/');
            if (IsOutsideProjectRoot(dbDirRelative)) return;

            string[] patterns;
            if (dbDirRelative == ".")
            {
                var dbFileName = Path.GetFileName(dbAbsolutePath);
                patterns = [dbFileName, $"{dbFileName}-*"];
            }
            else
            {
                patterns = [$"{dbDirRelative.TrimEnd('/')}/"];
            }

            var existingContent = File.Exists(excludeFile) ? File.ReadAllText(excludeFile) : "";
            var existingLines = existingContent.Split('\n').Select(l => l.TrimEnd('\r')).ToHashSet();

            var missing = patterns.Where(p => !existingLines.Contains(p)).ToList();
            if (missing.Count == 0) return;

            Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);

            using var sw = File.AppendText(excludeFile);
            if (existingContent.Length > 0 && !existingContent.EndsWith('\n'))
                sw.WriteLine();
            sw.WriteLine("# cdidx (CodeIndex) — auto-generated");
            foreach (var pattern in missing)
                sw.WriteLine(pattern);
        }
        catch
        {
        }
    }
}

public sealed class IndexCommandOptions
{
    public bool ShowHelp { get; init; }
    public string? ProjectPath { get; init; }
    public string? DbPath { get; init; }
    public bool Rebuild { get; init; }
    public bool Verbose { get; init; }
    public bool Json { get; init; }
    public List<string> Commits { get; init; } = [];
    public List<string> UpdateFiles { get; init; } = [];
    public string? EasterEgg { get; init; }
    public bool DryRun { get; init; }
}
