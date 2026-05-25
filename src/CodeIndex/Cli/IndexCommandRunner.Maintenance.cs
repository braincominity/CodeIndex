using System.Diagnostics;
using System.Text.Json;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Cli;

public static partial class IndexCommandRunner
{
    public static int RunBackfillFold(string[] cmdArgs, JsonSerializerOptions jsonOptions) =>
        RunBackfillFold(cmdArgs, jsonOptions, cancellationForTesting: null);

    public static int RunOptimizeFts(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseOptimizeFtsArgs(cmdArgs);
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
                "Run `cdidx optimize --help` to see the supported command shape.",
                CommandErrorCodes.UsageError);

        if (DbPathResolver.UriRequestsReadOnly(options.DbPath))
            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"database must be writable for optimize: {options.DbPath}",
                CommandExitCodes.DatabaseError,
                "Point `--db` at a writable filesystem path, or omit read-only URI parameters such as `immutable=1` / `mode=ro`.",
                CommandErrorCodes.DbNotWritable);

        return RunOptimizeFtsForDb(Path.GetFullPath(DbPathResolver.NormalizeDbPath(options.DbPath)), options.Json, jsonOptions, projectPath: null);
    }

    private static int RunOptimizeFtsForDb(string dbPath, bool json, JsonSerializerOptions jsonOptions, string? projectPath)
    {
        if (!DbContext.TryValidateExistingCodeIndexDb(dbPath, out var validationMessage, out var isNotFound))
            return WriteCommandError(
                json,
                jsonOptions,
                validationMessage,
                isNotFound ? CommandExitCodes.NotFound : CommandExitCodes.DatabaseError,
                isNotFound
                    ? "Point `--db` at an existing `codeindex.db`, or run `cdidx index <projectPath>` first to create one."
                    : "Point `--db` at an existing CodeIndex database created by `cdidx index`, then retry `cdidx optimize`.",
                isNotFound ? CommandErrorCodes.DbNotFound : CommandErrorCodes.DbError);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var lockPath = IndexLock.GetLockPath(dbPath);
            using var indexLock = IndexLock.Acquire(lockPath, projectPath ?? Path.GetDirectoryName(dbPath) ?? Environment.CurrentDirectory);
            using var db = new DbContext(dbPath);
            db.InitializeSchema();
            var writer = new DbWriter(db);
            var before = writer.GetFtsIncrementalWritesSinceOptimize();
            writer.OptimizeFts();
            stopwatch.Stop();
            var after = writer.GetFtsIncrementalWritesSinceOptimize();

            if (json)
            {
                var jsonContext = CliJsonSerializerContextFactory.Create(jsonOptions);
                Console.WriteLine(JsonSerializer.Serialize(
                    new OptimizeFtsJsonResult("success", dbPath, before, after, stopwatch.ElapsedMilliseconds),
                    jsonContext.OptimizeFtsJsonResult));
            }
            else
            {
                Console.WriteLine("Optimized FTS5 index.");
                Console.WriteLine(ConsoleUi.FormatSummaryLine("DB", dbPath, indent: "  "));
                Console.WriteLine(ConsoleUi.FormatSummaryLine("Writes before", before.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), indent: "  "));
                Console.WriteLine(ConsoleUi.FormatSummaryLine("Writes after", after.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), indent: "  "));
                Console.WriteLine(ConsoleUi.FormatSummaryLine("Elapsed", ConsoleUi.FormatDuration(stopwatch.Elapsed), indent: "  "));
            }

            return CommandExitCodes.Success;
        }
        catch (IndexLockConflictException ex)
        {
            var holderDescription = DescribeLockHolder(ex.Holder);
            var message = string.IsNullOrEmpty(holderDescription)
                ? "another cdidx index is already running on this database"
                : $"another cdidx index is already running on this database ({holderDescription})";
            return WriteCommandError(
                json,
                jsonOptions,
                message,
                CommandExitCodes.DatabaseError,
                "Wait for the running index to finish, then retry `cdidx optimize`.",
                CommandErrorCodes.DbLocked);
        }
        catch (Exception ex)
        {
            if (JsonOutputFailure.TryHandle(ex, out var exitCode))
                return exitCode;

            return WriteCommandError(
                json,
                jsonOptions,
                $"failed to optimize FTS5 index: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Ensure no other writer is holding the database lock, then retry `cdidx optimize`.",
                CommandErrorCodes.DbError);
        }
    }

    internal static int RunBackfillFold(
        string[] cmdArgs,
        JsonSerializerOptions jsonOptions,
        CancellationTokenSource? cancellationForTesting)
    {
        var options = ParseBackfillFoldArgs(cmdArgs);
        var jsonContext = CliJsonSerializerContextFactory.Create(jsonOptions);
        using var ownedCancellation = cancellationForTesting == null ? new CancellationTokenSource() : null;
        var backfillCancellation = cancellationForTesting ?? ownedCancellation!;
        using var cancelKeyPressRegistration = cancellationForTesting == null
            ? RegisterIndexCancelKeyPress(backfillCancellation)
            : NullDisposable.Instance;
        using var terminateSignalRegistration = cancellationForTesting == null
            ? RegisterIndexTerminateSignal(backfillCancellation)
            : NullDisposable.Instance;

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
                "Run `cdidx backfill-fold --help` to see the supported command shape.",
                CommandErrorCodes.UsageError);

        if (!DbContext.TryValidateExistingCodeIndexDb(options.DbPath, out var validationMessage, out var isNotFound))
            return WriteCommandError(
                options.Json,
                jsonOptions,
                validationMessage,
                isNotFound ? CommandExitCodes.NotFound : CommandExitCodes.DatabaseError,
                isNotFound
                    ? "Point `--db` at an existing `codeindex.db`, or run `cdidx index <projectPath>` first to create one."
                    : "Point `--db` at an existing CodeIndex database created by `cdidx index`, then retry `cdidx backfill-fold`.",
                isNotFound ? CommandErrorCodes.DbNotFound : CommandErrorCodes.DbError);

        try
        {
            using var db = new DbContext(options.DbPath);
            db.InitializeSchema();
            var writer = new DbWriter(db);

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

            var (symbols, symbolReferences) = writer.BackfillFoldedColumns(
                rewriteAll,
                backfillCancellation.Token);
            // MarkFoldReady re-verifies inside a BEGIN IMMEDIATE so a concurrent writer cannot
            // insert NULL-folded rows between the verify and the stamp. Issue #1535.
            // MarkFoldReady は BEGIN IMMEDIATE 内で再検証するため、concurrent writer による
            // NULL 行差し込みで fold_ready が嘘になるのを防ぐ。Issue #1535。
            var verified = writer.MarkFoldReady();
            if (!verified)
            {
                return WriteCommandError(
                    options.Json,
                    jsonOptions,
                    "folded-name backfill verification failed: some rows still have NULL folded values",
                    CommandExitCodes.DatabaseError,
                    "Retry `cdidx backfill-fold`. If the DB still does not verify, rebuild it with `cdidx index <projectPath> --rebuild`.",
                    CommandErrorCodes.DbError);
            }

            var userVersionAfter = db.GetUserVersion();

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new BackfillFoldJsonResult(
                    symbols,
                    symbolReferences,
                    rewriteAll,
                    verified,
                    userVersionBefore,
                    userVersionAfter,
                    true), jsonContext.BackfillFoldJsonResult));
            }
            else
            {
                Console.WriteLine("Backfilling folded-name columns ...");
                Console.WriteLine($"  symbols:            {ConsoleUi.Counted(symbols, "row", format: "N0")} rewritten");
                Console.WriteLine($"  symbol_references:  {ConsoleUi.Counted(symbolReferences, "row", format: "N0")} rewritten");
                if (rewriteAll)
                    Console.WriteLine("  mode:               full folded-key refresh (fold metadata missing or mismatched)");
                Console.WriteLine($"  verified:           {(verified ? "yes" : "no")}");
                Console.WriteLine($"  stamp:              FoldReady bit set (user_version: {userVersionBefore} -> {userVersionAfter})");
            }

            return CommandExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            return WriteCommandError(
                options.Json,
                jsonOptions,
                "folded-name backfill cancelled before it could complete.",
                CommandExitCodes.CancelledBySignal,
                "Rerun `cdidx backfill-fold` when you are ready to resume; the cancelled transaction was rolled back.",
                CommandErrorCodes.Interrupted);
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
                "Retry `cdidx backfill-fold`. If this persists, rebuild the index with `cdidx index <projectPath> --rebuild`.",
                CommandErrorCodes.DbError);
        }
    }
}
