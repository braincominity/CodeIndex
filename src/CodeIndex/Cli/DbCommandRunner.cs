using System.Text.Json;
using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Cli;

/// <summary>
/// Runs `db` subcommands that operate directly on the SQLite file (integrity check, schema, prune).
/// SQLite ファイル本体に対する `db` サブコマンド（整合性チェック、schema、prune）を実行する。
/// </summary>
public static class DbCommandRunner
{
    public static int Run(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs);
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
                "Run `cdidx db --integrity-check --help` to see the supported command shape.",
                CommandErrorCodes.UsageError);

        if (!options.IntegrityCheck && !options.Schema && !options.Prune)
            return WriteCommandError(
                options.Json,
                jsonOptions,
                "db requires a mode flag",
                CommandExitCodes.UsageError,
                "Pass `--integrity-check`, `schema`, or `prune --dry-run|--apply`.",
                CommandErrorCodes.UsageError);

        if ((options.IntegrityCheck ? 1 : 0) + (options.Schema ? 1 : 0) + (options.Prune ? 1 : 0) > 1)
            return WriteCommandError(
                options.Json,
                jsonOptions,
                "db accepts exactly one mode",
                CommandExitCodes.UsageError,
                "Run one of `cdidx db --integrity-check`, `cdidx db schema`, or `cdidx db prune --dry-run|--apply`.",
                CommandErrorCodes.UsageError);

        var dbPath = options.DbPath;
        var isUri = dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && !File.Exists(LongPath.EnsureWindowsPrefix(dbPath)))
            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"database not found: {dbPath}",
                CommandExitCodes.NotFound,
                "Point `--db` at an existing `codeindex.db`, or run `cdidx index <projectPath>` first to create one.",
                CommandErrorCodes.DbNotFound);

        if (options.Schema)
            return RunSchema(options, jsonOptions, dbPath, isUri);

        if (options.Prune)
            return RunPrune(options, jsonOptions, dbPath, isUri);

        return RunIntegrityCheck(options, jsonOptions, dbPath, isUri);
    }

    public static int RunIntegrityCheck(string[] cmdArgs, JsonSerializerOptions jsonOptions) => Run(cmdArgs, jsonOptions);

    private static int RunIntegrityCheck(DbCommandOptions options, JsonSerializerOptions jsonOptions, string dbPath, bool isUri)
    {
        try
        {
            var issues = RunIntegrityCheckPragma(dbPath);
            var ok = issues.Count == 1 && string.Equals(issues[0], "ok", StringComparison.Ordinal);
            var jsonContext = CliJsonSerializerContextFactory.Create(jsonOptions);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new DbIntegrityCheckJsonResult(
                        Path.GetFullPath(isUri ? dbPath : dbPath),
                        ok,
                        ok ? new List<string>() : issues),
                    jsonContext.DbIntegrityCheckJsonResult));
            }
            else
            {
                Console.WriteLine("Integrity check");
                Console.WriteLine($"  database: {Path.GetFullPath(isUri ? dbPath : dbPath)}");
                Console.WriteLine($"  result  : {(ok ? "ok" : "corrupted")}");
                if (!ok)
                {
                    Console.WriteLine($"  issues  : {ConsoleUi.Counted(issues.Count, "row")}");
                    foreach (var line in issues)
                        Console.WriteLine($"    - {line}");
                    Console.WriteLine();
                    Console.Error.WriteLine($"Error [{CommandErrorCodes.DbIntegrityFailed}]: SQLite reported integrity_check failures.");
                    Console.Error.WriteLine("Hint: rebuild with `cdidx index <projectPath> --rebuild` to discard the corrupted DB and start fresh.");
                }
            }

            return ok ? CommandExitCodes.Success : CommandExitCodes.DatabaseError;
        }
        catch (Exception ex)
        {
            if (JsonOutputFailure.TryHandle(ex, out var exitCode))
                return exitCode;

            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"failed to run integrity check: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Retry `cdidx db --integrity-check`. If this persists, the DB may be unreadable; rebuild with `cdidx index <projectPath> --rebuild`.",
                CommandErrorCodes.DbError);
        }
    }

    private static int RunSchema(DbCommandOptions options, JsonSerializerOptions jsonOptions, string dbPath, bool isUri)
    {
        try
        {
            var schema = ReadSchema(dbPath);
            var fullPath = Path.GetFullPath(isUri ? dbPath : dbPath);
            if (options.Json)
            {
                var jsonContext = CliJsonSerializerContextFactory.Create(jsonOptions);
                Console.WriteLine(JsonSerializer.Serialize(
                    new DbSchemaJsonResult(fullPath, schema.UserVersion, schema.Entries),
                    jsonContext.DbSchemaJsonResult));
            }
            else
            {
                Console.WriteLine("Database schema");
                Console.WriteLine($"  database    : {fullPath}");
                Console.WriteLine($"  user_version: {schema.UserVersion}");
                foreach (var entry in schema.Entries)
                {
                    Console.WriteLine();
                    Console.WriteLine($"-- {entry.Type}: {entry.Name}");
                    if (!string.IsNullOrWhiteSpace(entry.Sql))
                        Console.WriteLine(entry.Sql);
                }
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
                $"failed to read schema: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Retry `cdidx db schema`. If this persists, rebuild with `cdidx index <projectPath> --rebuild`.",
                CommandErrorCodes.DbError);
        }
    }

    private static int RunPrune(DbCommandOptions options, JsonSerializerOptions jsonOptions, string dbPath, bool isUri)
    {
        if (!options.PruneApply && !options.PruneDryRun)
            return WriteCommandError(
                options.Json,
                jsonOptions,
                "db prune requires --dry-run or --apply",
                CommandExitCodes.UsageError,
                "Use `cdidx db prune --dry-run` to inspect stale rows, then `cdidx db prune --apply` to delete them.",
                CommandErrorCodes.UsageError);

        if (options.PruneApply && options.PruneDryRun)
            return WriteCommandError(
                options.Json,
                jsonOptions,
                "db prune accepts only one of --dry-run or --apply",
                CommandExitCodes.UsageError,
                "Choose `--dry-run` or `--apply`.",
                CommandErrorCodes.UsageError);

        if (isUri && DbPathResolver.UriRequestsReadOnly(dbPath))
            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"database must be writable for prune: {dbPath}",
                CommandExitCodes.DatabaseError,
                "Point `--db` at a writable filesystem path, or omit read-only URI parameters such as `immutable=1` / `mode=ro`.",
                CommandErrorCodes.DbNotWritable);

        try
        {
            var result = PruneOrphans(dbPath, apply: options.PruneApply);
            var fullPath = Path.GetFullPath(isUri ? dbPath : dbPath);
            if (options.Json)
            {
                var jsonContext = CliJsonSerializerContextFactory.Create(jsonOptions);
                Console.WriteLine(JsonSerializer.Serialize(
                    new DbPruneJsonResult(
                        "success",
                        fullPath,
                        options.PruneDryRun,
                        result.OrphanSymbolReferences,
                        result.OrphanReferenceLines,
                        result.OrphanSymbols,
                        result.Total),
                    jsonContext.DbPruneJsonResult));
            }
            else
            {
                Console.WriteLine(options.PruneApply ? "Pruned database stale rows." : "Database prune dry run.");
                Console.WriteLine($"  database                 : {fullPath}");
                Console.WriteLine($"  orphan symbol_references : {result.OrphanSymbolReferences:N0}");
                Console.WriteLine($"  orphan reference_lines   : {result.OrphanReferenceLines:N0}");
                Console.WriteLine($"  orphan symbols           : {result.OrphanSymbols:N0}");
                Console.WriteLine($"  total                    : {result.Total:N0}");
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
                $"failed to prune database: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Ensure no other writer is holding the database lock, then retry `cdidx db prune --dry-run`.",
                CommandErrorCodes.DbError);
        }
    }

    // PRAGMA integrity_check returns a single row `"ok"` when the file passes every consistency
    // probe, otherwise it returns up to N rows of corruption findings. The pragma itself only
    // reads the database, so a read-only connection is sufficient and avoids the WAL-mode
    // pragma side effects of the normal DbContext open path.
    // PRAGMA integrity_check は問題が無ければ 1 行の `"ok"` を、破損があれば最大 N 行の検出結果を返す。
    // 読み取りのみのため read-only 接続で十分で、DbContext の WAL モード設定副作用を避けられる。
    private static List<string> RunIntegrityCheckPragma(string dbPath)
    {
        var connectionString = dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? $"Data Source={dbPath}"
            : new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        using var reader = cmd.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var raw = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            rows.Add(raw);
        }
        return rows.Count > 0 ? rows : new List<string> { "ok" };
    }

    private static (int UserVersion, List<DbSchemaEntryJsonResult> Entries) ReadSchema(string dbPath)
    {
        using var connection = OpenConnection(dbPath, writable: false);
        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version";
        var rawVersion = versionCmd.ExecuteScalar();
        var userVersion = rawVersion is long l ? (int)l : (rawVersion is int i ? i : 0);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT type, name, tbl_name, sql
            FROM sqlite_master
            WHERE type IN ('table', 'index', 'trigger', 'view')
            ORDER BY type, name";
        using var reader = cmd.ExecuteReader();
        var entries = new List<DbSchemaEntryJsonResult>();
        while (reader.Read())
        {
            entries.Add(new DbSchemaEntryJsonResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return (userVersion, entries);
    }

    private static (int OrphanSymbolReferences, int OrphanReferenceLines, int OrphanSymbols, int Total) PruneOrphans(string dbPath, bool apply)
    {
        using var connection = OpenConnection(dbPath, writable: apply);
        using var transaction = apply ? connection.BeginTransaction() : null;

        var orphanSymbolReferences = Count(connection, transaction, @"
            SELECT COUNT(*)
            FROM symbol_references sr
            LEFT JOIN files f ON f.id = sr.file_id
            LEFT JOIN reference_lines rl ON rl.id = sr.reference_line_id
            LEFT JOIN files rlf ON rlf.id = rl.file_id
            WHERE f.id IS NULL
               OR (sr.reference_line_id IS NOT NULL AND (rl.id IS NULL OR rlf.id IS NULL))");
        var orphanReferenceLines = Count(connection, transaction, @"
            SELECT COUNT(*)
            FROM reference_lines rl
            LEFT JOIN files f ON f.id = rl.file_id
            WHERE f.id IS NULL");
        var orphanSymbols = Count(connection, transaction, @"
            SELECT COUNT(*)
            FROM symbols s
            LEFT JOIN files f ON f.id = s.file_id
            WHERE f.id IS NULL");

        if (apply)
        {
            Execute(connection, transaction, @"
                DELETE FROM symbol_references
                WHERE file_id NOT IN (SELECT id FROM files)
                   OR (reference_line_id IS NOT NULL AND reference_line_id NOT IN (
                       SELECT rl.id
                       FROM reference_lines rl
                       INNER JOIN files f ON f.id = rl.file_id
                   ))");
            Execute(connection, transaction, "DELETE FROM reference_lines WHERE file_id NOT IN (SELECT id FROM files)");
            Execute(connection, transaction, "DELETE FROM symbols WHERE file_id NOT IN (SELECT id FROM files)");
            transaction!.Commit();
            Execute(connection, null, "PRAGMA optimize");
        }

        var total = orphanSymbolReferences + orphanReferenceLines + orphanSymbols;
        return (orphanSymbolReferences, orphanReferenceLines, orphanSymbols, total);
    }

    private static SqliteConnection OpenConnection(string dbPath, bool writable)
    {
        var connectionString = dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? $"Data Source={dbPath}"
            : new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = writable ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
            }.ConnectionString;
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static int Count(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal static DbCommandOptions ParseArgs(string[] args)
    {
        var dbPath = Path.Combine(".cdidx", "codeindex.db");
        var json = false;
        var integrityCheck = false;
        var schema = false;
        var prune = false;
        var pruneDryRun = false;
        var pruneApply = false;
        string? parseError = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db" when i + 1 < args.Length:
                    dbPath = args[++i];
                    break;
                case "--db":
                    parseError = "--db requires a value";
                    break;
                case "--json":
                    json = true;
                    break;
                case "--integrity-check":
                    integrityCheck = true;
                    break;
                case "schema":
                    schema = true;
                    break;
                case "prune":
                    prune = true;
                    break;
                case "--dry-run":
                    pruneDryRun = true;
                    break;
                case "--apply":
                    pruneApply = true;
                    break;
                case "--help" or "-h":
                    return new DbCommandOptions { ShowHelp = true, DbPath = dbPath, Json = json };
                default:
                    if (args[i].StartsWith('-'))
                        parseError = $"db does not support option: '{args[i]}'";
                    else
                        parseError = $"db does not accept positional arguments: '{args[i]}'";
                    break;
            }

            if (parseError != null)
                break;
        }

        return new DbCommandOptions
        {
            DbPath = dbPath,
            Json = json,
            IntegrityCheck = integrityCheck,
            Schema = schema,
            Prune = prune,
            PruneDryRun = pruneDryRun,
            PruneApply = pruneApply,
            ParseError = parseError,
        };
    }

    private static int WriteCommandError(bool json, JsonSerializerOptions jsonOptions, string message, int exitCode, string? hint = null, string? errorCode = null)
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(
                new CommandErrorJsonResult("error", message, hint, errorCode),
                CliJsonSerializerContextFactory.Create(jsonOptions).CommandErrorJsonResult));
        else
        {
            var prefix = errorCode is null ? "Error" : $"Error [{errorCode}]";
            Console.Error.WriteLine($"{prefix}: {message}");
            if (hint != null)
                Console.Error.WriteLine($"Hint: {hint}");
        }
        return exitCode;
    }
}

internal sealed class DbCommandOptions
{
    public string DbPath { get; init; } = string.Empty;
    public bool Json { get; init; }
    public bool ShowHelp { get; init; }
    public bool IntegrityCheck { get; init; }
    public bool Schema { get; init; }
    public bool Prune { get; init; }
    public bool PruneDryRun { get; init; }
    public bool PruneApply { get; init; }
    public string? ParseError { get; init; }
}
