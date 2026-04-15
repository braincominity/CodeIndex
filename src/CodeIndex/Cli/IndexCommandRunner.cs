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
        var resolvedDbPath = Path.GetFullPath(DbPathResolver.NormalizeDbPath(dbPath));
        var stopwatch = Stopwatch.StartNew();
        var isUpdateMode = options.Commits.Count > 0 || options.UpdateFiles.Count > 0;
        var mode = options.Rebuild ? "rebuild" : isUpdateMode ? "update" : "incremental";

        if (!options.Json)
        {
            ConsoleUi.PrintBanner();
            Console.WriteLine();
            Console.WriteLine($"  Project : {Path.GetFullPath(options.ProjectPath)}");
            Console.WriteLine($"  Output  : {resolvedDbPath}");
            Console.WriteLine($"  Mode    : {mode}");
            Console.WriteLine();
        }

        if (!Directory.Exists(options.ProjectPath))
        {
            if (options.Json)
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = $"directory not found: {options.ProjectPath}",
                    hint = "Check the project path and rerun `cdidx index <projectPath>` with an existing directory."
                }, jsonOptions));
            else
            {
                Console.Error.WriteLine($"Error: directory not found: {options.ProjectPath}");
                Console.Error.WriteLine("Hint: check the project path and rerun `cdidx index <projectPath>` with an existing directory.");
            }
            return CommandExitCodes.NotFound;
        }

        if (options.Rebuild && isUpdateMode)
        {
            if (options.Json)
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = "--rebuild cannot be used with --commits or --files (rebuild requires a full rescan)",
                    hint = "Drop `--rebuild` for partial updates, or rerun `cdidx index <projectPath> --rebuild` for a full rescan."
                }, jsonOptions));
            else
            {
                Console.Error.WriteLine("Error: --rebuild cannot be used with --commits or --files (rebuild requires a full rescan)");
                Console.Error.WriteLine("Hint: drop `--rebuild` for `--commits`/`--files`, or rerun `cdidx index <projectPath> --rebuild` for a full rescan.");
            }
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
        // touches a subset of files, so trust bits the DB did NOT previously carry must not
        // be fabricated after a partial pass. But bits the DB DID carry should survive —
        // independently, not as a single all-or-nothing gate. Codex #86 review flagged that
        // gating all three bits on `user_version == CurrentSchemaVersion` regressed pre-#86
        // DBs (user_version=3): a `--files` refresh on such a DB would silently drop Graph/
        // Issues trust too, breaking references/callers/callees/impact for the whole repo.
        // update モードは元々立っていた readiness bit のみを個別に復元する。pre-#86 DB
        // (user_version=3) でも Graph/Issues を巻き込んで落とさないように、単一フラグではなく
        // 事前 bit をそのまま保持する。Codex #86 第 2 pass レビュー対応。
        var priorReadiness = db.GetUserVersion();
        // Also snapshot the stored fold-key version BEFORE ClearReadyFlags wipes trust. When
        // a future `NameFold.Version` bump lands, a partial update must NOT restamp
        // FoldReady on a DB whose untouched rows still carry the old-version fold keys — we
        // can't re-fold those rows without re-reading them, so the only safe state is to leave
        // fold degraded until `--rebuild`. Snapshot both version and runtime fingerprint so
        // partial update does not restamp FoldReady across either algorithm drift or runtime
        // casing-table drift. Issue #97.
        // fold metadata を事前 snapshot する。version だけでなく fingerprint のズレでも
        // partial update で FoldReady を restamp しない。
        var priorFoldVersion = db.GetMetaString("fold_key_version");
        var priorFoldFingerprint = db.GetMetaString("fold_key_fingerprint");

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

        return isUpdateMode
            ? RunUpdateMode(writer, indexer, projectRoot, resolvedDbPath, options, stopwatch, spinnerFrames, jsonOptions, priorReadiness, priorFoldVersion, priorFoldFingerprint)
            : RunFullScan(writer, indexer, projectRoot, resolvedDbPath, options, stopwatch, spinnerFrames, jsonOptions, priorFoldVersion, priorFoldFingerprint);
    }

    public static int RunBackfillFold(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseBackfillFoldArgs(cmdArgs);
        if (options.ShowHelp)
        {
            ConsoleUi.PrintUsage();
            return CommandExitCodes.Success;
        }

        if (options.ParseError != null)
            return WriteCommandError(
                options.Json,
                jsonOptions,
                options.ParseError,
                CommandExitCodes.UsageError,
                "Run `cdidx backfill-fold --help` to see the supported command shape.");

        if (!DbContext.TryValidateExistingCodeIndexDb(options.DbPath, out var validationMessage, out var isNotFound))
            return WriteCommandError(
                options.Json,
                jsonOptions,
                validationMessage,
                isNotFound ? CommandExitCodes.NotFound : CommandExitCodes.DatabaseError,
                isNotFound
                    ? "Point `--db` at an existing `codeindex.db`, or run `cdidx index <projectPath>` first to create one."
                    : "Point `--db` at an existing CodeIndex database created by `cdidx index`, then retry `cdidx backfill-fold`.");

        try
        {
            using var db = new DbContext(options.DbPath);
            db.InitializeSchema();
            var writer = new DbWriter(db.Connection);

            var userVersionBefore = db.GetUserVersion();
            var currentFoldVersion = NameFold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var currentFoldFingerprint = NameFold.Fingerprint();
            var storedFoldVersion = db.GetMetaString("fold_key_version");
            var storedFoldFingerprint = db.GetMetaString("fold_key_fingerprint");
            // Missing or mismatched fold metadata means persisted keys may have been generated
            // by a different fold algorithm/runtime, so refresh every row from source names.
            // fold metadata 未記録 / 不一致時は全行再計算して version/runtime skew を解消する。
            var rewriteAll = storedFoldVersion != currentFoldVersion
                || storedFoldFingerprint != currentFoldFingerprint;

            var (symbols, symbolReferences) = writer.BackfillFoldedColumns(rewriteAll);
            var verified = writer.AllFoldedColumnsBackfilled();
            if (!verified)
            {
                return WriteCommandError(
                    options.Json,
                    jsonOptions,
                    "folded-name backfill verification failed: some rows still have NULL folded values",
                    CommandExitCodes.DatabaseError,
                    "Retry `cdidx backfill-fold`. If the DB still does not verify, rebuild it with `cdidx index <projectPath> --rebuild`.");
            }

            writer.MarkFoldReady();
            var userVersionAfter = db.GetUserVersion();

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    symbols,
                    symbol_references = symbolReferences,
                    rewrite_all = rewriteAll,
                    verified,
                    user_version_before = userVersionBefore,
                    user_version_after = userVersionAfter,
                    fold_ready = true,
                }, jsonOptions));
            }
            else
            {
                Console.WriteLine("Backfilling folded-name columns ...");
                Console.WriteLine($"  symbols:            {symbols:N0} row(s) rewritten");
                Console.WriteLine($"  symbol_references:  {symbolReferences:N0} row(s) rewritten");
                if (rewriteAll)
                    Console.WriteLine("  mode:               full folded-key refresh (fold metadata missing or mismatched)");
                Console.WriteLine($"  verified:           {(verified ? "yes" : "no")}");
                Console.WriteLine($"  stamp:              FoldReady bit set (user_version: {userVersionBefore} -> {userVersionAfter})");
            }

            return CommandExitCodes.Success;
        }
        catch (Exception ex)
        {
            if (JsonOutputFailure.TryHandle(ex, out var exitCode))
                return exitCode;

            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"failed to backfill folded-name columns: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Retry `cdidx backfill-fold`. If this persists, rebuild the index with `cdidx index <projectPath> --rebuild`.");
        }
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

    private static BackfillFoldCommandOptions ParseBackfillFoldArgs(string[] args)
    {
        var dbPath = Path.Combine(".cdidx", "codeindex.db");
        var json = false;

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
                case "--help" or "-h":
                    return new BackfillFoldCommandOptions { ShowHelp = true, DbPath = dbPath, Json = json };
                default:
                    if (args[i].StartsWith('-'))
                        Console.Error.WriteLine($"Warning: unknown option '{args[i]}' (ignored) / 不明なオプション '{args[i]}'（無視されます）");
                    else
                        return new BackfillFoldCommandOptions
                        {
                            DbPath = dbPath,
                            Json = json,
                            ParseError = $"backfill-fold does not accept positional arguments: '{args[i]}'"
                        };
                    break;
            }
        }

        return new BackfillFoldCommandOptions
        {
            DbPath = dbPath,
            Json = json,
        };
    }

    private static int RunUpdateMode(
        DbWriter writer,
        FileIndexer indexer,
        string projectRoot,
        string resolvedDbPath,
        IndexCommandOptions options,
        Stopwatch stopwatch,
        string[] spinnerFrames,
        JsonSerializerOptions jsonOptions,
        int priorReadiness,
        string? priorFoldVersion,
        string? priorFoldFingerprint)
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
                    CommandExitCodes.UsageError,
                    "Check the commit IDs and rerun `cdidx index <projectPath> --commits <id> [id ...]`.");
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
                        Console.Error.WriteLine($"  [WARN] Skipping file outside project root: {f}. Use a path under the indexed project root or run `cdidx index` from the correct workspace.");
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
        var graphTableAvailableAfter = false;
        var issuesTableAvailableAfter = false;
        var foldReadyAfter = false;
        if (errors == 0)
        {
            // Restore each readiness bit independently based on what the DB carried BEFORE
            // ClearReadyFlags wiped them. A pre-#86 DB (user_version=3, i.e. Graph+Issues but
            // no Fold) must keep Graph+Issues after a successful partial update, even though
            // FoldReady can't be restamped. Codex #86 second-pass review: the old single-flag
            // `wasFullyReady` gate silently dropped Graph/Issues for the whole workspace on
            // such DBs, breaking references/callers/callees/impact.
            // Fold is the only bit that needs the runtime verify: the other two only require
            // that the DB previously reached end-of-run for those subsystems. Fold also
            // requires name_folded to be populated for every row, but the invariant holds
            // when the prior bit was set AND this update rewrote its touched rows with
            // name_folded populated, so no extra scan is needed here.
            // update mode は事前 bit を個別に復元。Graph/Issues は prior bit があれば復元、
            // Fold も prior bit があれば invariant を信じて restamp（codex 2nd review 対応）。
            if ((priorReadiness & DbContext.GraphReadyFlag) != 0)
            {
                writer.MarkGraphReady();
                graphTableAvailableAfter = true;
            }
            if ((priorReadiness & DbContext.IssuesReadyFlag) != 0)
            {
                writer.MarkIssuesReady();
                issuesTableAvailableAfter = true;
            }
            // FoldReady restamp requires both the prior stored version and fingerprint to
            // match the current binary/runtime. Otherwise untouched rows still carry keys
            // from an older fold implementation or runtime table set, and advertising
            // FoldReady would silently mismatch on --exact. Only full rebuild can re-fold all rows.
            // fold は version / fingerprint の両一致時のみ restamp。ズレた DB は rebuild まで
            // fold_ready=false のまま残す。
            var currentFoldVersion = NameFold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var currentFoldFingerprint = NameFold.Fingerprint();
            if ((priorReadiness & DbContext.FoldReadyFlag) != 0
                && priorFoldVersion == currentFoldVersion
                && priorFoldFingerprint == currentFoldFingerprint)
            {
                writer.MarkFoldReady();
                foldReadyAfter = true;
            }
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
                graph_table_available = graphTableAvailableAfter,
                issues_table_available = issuesTableAvailableAfter,
                // #86 codex review: expose fold-readiness so AI clients can decide whether
                // `--exact` will use the Unicode fold path or fall back to ASCII NOCASE.
                // #86 codex: AI クライアントが --exact の経路を判断できるよう fold_ready を返す。
                fold_ready = foldReadyAfter,
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
            Console.WriteLine($"  Graph   : {(graphTableAvailableAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Issues  : {(issuesTableAvailableAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Fold    : {(foldReadyAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine();
            if (errors > 0)
                ConsoleUi.PrintWarning($"Some files failed to update. Fix the reported files or permissions, then rerun `cdidx index \"{projectRoot}\"` to restore a fully ready index.");
            if (!graphTableAvailableAfter || !issuesTableAvailableAfter || !foldReadyAfter)
                ConsoleUi.PrintWarning(GetIndexReadinessWarning(graphTableAvailableAfter, issuesTableAvailableAfter, foldReadyAfter, resolvedDbPath));
        }

        return CommandExitCodes.Success;
    }

    private static bool IsOutsideProjectRoot(string relativePath) =>
        relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal);

    private static int WriteCommandError(bool json, JsonSerializerOptions jsonOptions, string message, int exitCode, string? hint = null)
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new { status = "error", message, hint }, jsonOptions));
        else
        {
            Console.Error.WriteLine($"Error: {message}");
            if (hint != null)
                Console.Error.WriteLine($"Hint: {hint}");
        }
        return exitCode;
    }

    private static int RunFullScan(
        DbWriter writer,
        FileIndexer indexer,
        string projectRoot,
        string resolvedDbPath,
        IndexCommandOptions options,
        Stopwatch stopwatch,
        string[] spinnerFrames,
        JsonSerializerOptions jsonOptions,
        string? priorFoldVersion,
        string? priorFoldFingerprint)
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
        var graphTableAvailableAfter = false;
        var issuesTableAvailableAfter = false;
        var foldReadyAfter = false;
        if (errors == 0)
        {
            // Full-scan covers the whole repo, so it may always stamp Graph / Issues on
            // success regardless of what the DB carried before. Fold still gates on the
            // backfill verification below because incremental-by-default full scans skip
            // unchanged legacy files whose folded columns remain NULL.
            // full-scan は全repo をカバーするため、Graph / Issues は常に stamp。Fold のみ条件付き。
            writer.MarkGraphReady();
            writer.MarkIssuesReady();
            graphTableAvailableAfter = true;
            issuesTableAvailableAfter = true;
            // FoldReady must reflect reality (#86). Full-scan is INCREMENTAL by default — it
            // skips unchanged files via GetUnchangedFileId, so a legacy DB's pre-#86 rows
            // keep NULL name_folded / *_folded values. Stamping FoldReady anyway would flip
            // readers onto the folded-equality path and silently miss those rows. Verify
            // every existing row has its folded column populated before stamping, and tell
            // the user how to upgrade when not (only --rebuild / a truly-fresh index can
            // guarantee 100% backfill on a legacy DB).
            // fold は実検証が通ったときだけ stamp。legacy DB で skip された行は NULL のため、
            // 黙って stamp すると reader が fold 経路で legacy 行を見逃す。codex #86 レビュー。
            var backfillReady = writer.AllFoldedColumnsBackfilled();
            var currentFoldVersion = NameFold.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var currentFoldFingerprint = NameFold.Fingerprint();
            var foldVersionMatchesCurrent = priorFoldVersion == currentFoldVersion;
            var foldFingerprintMatchesCurrent = priorFoldFingerprint == currentFoldFingerprint;
            var canRestampExistingFoldTrust = foldVersionMatchesCurrent
                && foldFingerprintMatchesCurrent;
            // A normal `index .` run still skips unchanged files. If the prior fold metadata
            // is stale, those skipped rows keep the old physical folded keys, so stamping the
            // NEW metadata for the whole DB would silently misadvertise trust. Only stamp when
            // every row was regenerated this run (skipped==0) or when the carried metadata is
            // already known-good for the current runtime, even if user_version was cleared by
            // an interrupted refresh before MarkFoldReady ran. Issue #97 codex review.
            // 通常の `index .` は unchanged 行を skip するため、事前 metadata が stale なら
            // skipped 行は旧 key のまま残る。全件再生成済み（skipped==0）か、事前 metadata が
            // current と一致しているときだけ FoldReady を stamp する。途中中断で
            // user_version だけ落ちた current DB もここで回復させる。
            if (backfillReady && (skipped == 0 || canRestampExistingFoldTrust))
            {
                writer.MarkFoldReady();
                foldReadyAfter = true;
            }
            else if (!options.Json)
            {
                ConsoleUi.PrintWarning(GetFoldNotReadyWarning(backfillReady, foldVersionMatchesCurrent, foldFingerprintMatchesCurrent));
            }
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
                graph_table_available = graphTableAvailableAfter,
                issues_table_available = issuesTableAvailableAfter,
                // #86 codex review: expose fold-readiness so AI clients can decide whether
                // `--exact` will use the Unicode fold path or fall back to ASCII NOCASE.
                // #86 codex: AI クライアントが --exact の経路を判断できるよう fold_ready を返す。
                fold_ready = foldReadyAfter,
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
            Console.WriteLine($"  Graph   : {(graphTableAvailableAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Issues  : {(issuesTableAvailableAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Fold    : {(foldReadyAfter ? "ready" : "degraded")}");
            Console.WriteLine($"  Elapsed : {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine();
            if (errors > 0)
                ConsoleUi.PrintWarning($"Some files failed to index. Fix the reported files or permissions, then rerun `cdidx index \"{projectRoot}\"` to restore a fully ready index.");
            if (!graphTableAvailableAfter || !issuesTableAvailableAfter || !foldReadyAfter)
                ConsoleUi.PrintWarning(GetIndexReadinessWarning(graphTableAvailableAfter, issuesTableAvailableAfter, foldReadyAfter, resolvedDbPath));
        }

        return CommandExitCodes.Success;
    }

    private static string GetFoldNotReadyWarning(bool backfillReady, bool foldVersionMatchesCurrent, bool foldFingerprintMatchesCurrent)
    {
        if (!backfillReady)
        {
            return "--exact Unicode fold path not stamped: legacy rows without name_folded remain. Run `cdidx backfill-fold` to upgrade without reparsing files, or use `cdidx index . --rebuild` to regenerate the whole DB.";
        }

        if (!foldVersionMatchesCurrent)
        {
            return "--exact Unicode fold path not stamped: unchanged rows still carry an older fold-key version. Rewrite or purge those stale rows and rerun `cdidx index .`, run `cdidx backfill-fold`, or use `cdidx index . --rebuild` to regenerate the whole DB.";
        }

        if (!foldFingerprintMatchesCurrent)
        {
            return "--exact Unicode fold path not stamped: unchanged rows still carry folded keys generated under an older runtime fingerprint. Rewrite or purge those stale rows and rerun `cdidx index .`, run `cdidx backfill-fold`, or use `cdidx index . --rebuild` to regenerate the whole DB.";
        }

        return "--exact Unicode fold path not stamped: some folded keys were not regenerated under the current runtime. Run `cdidx backfill-fold` to rewrite folded keys in place, or use `cdidx index . --rebuild` to regenerate the whole DB.";
    }

    private static string GetIndexReadinessWarning(bool graphTableAvailable, bool issuesTableAvailable, bool foldReady, string resolvedDbPath)
    {
        var degradedParts = new List<string>();
        if (!graphTableAvailable)
            degradedParts.Add("graph_table_available=false");
        if (!issuesTableAvailable)
            degradedParts.Add("issues_table_available=false");
        if (!foldReady)
            degradedParts.Add("fold_ready=false");

        return $"Index completed with degraded readiness ({string.Join(", ", degradedParts)}). Run `cdidx status --db \"{resolvedDbPath}\" --json` to inspect the current DB state.";
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

public sealed class BackfillFoldCommandOptions
{
    public bool ShowHelp { get; init; }
    public string DbPath { get; init; } = Path.Combine(".cdidx", "codeindex.db");
    public bool Json { get; init; }
    public string? ParseError { get; init; }
}
