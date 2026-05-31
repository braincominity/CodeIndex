using System.Text.Json;
using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Cli;

/// <summary>
/// Runs `db` subcommands that operate directly on the SQLite file (integrity check, etc.).
/// SQLite ファイル本体に対する `db` サブコマンド（整合性チェックなど）を実行する。
/// </summary>
public static class DbCommandRunner
{
    private const string CheckpointsDirectorySuffix = ".checkpoints";
    private const string AutoCheckpointPrefix = "auto-";
    private static readonly char[] InvalidCheckpointNameChars = Path.GetInvalidFileNameChars();

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
                "Run `cdidx db --help` to see the supported command shape.",
                CommandErrorCodes.UsageError);

        return options.Mode switch
        {
            DbCommandMode.IntegrityCheck => RunIntegrityCheck(options, jsonOptions),
            DbCommandMode.Checkpoint => RunCheckpoint(options, jsonOptions),
            DbCommandMode.ListCheckpoints => RunListCheckpoints(options, jsonOptions),
            DbCommandMode.Restore => RunRestore(options, jsonOptions),
            _ => WriteCommandError(
                options.Json,
                jsonOptions,
                "db requires a mode",
                CommandExitCodes.UsageError,
                "Use `--integrity-check`, `checkpoint`, `checkpoints --list`, or `restore <name>`.",
                CommandErrorCodes.UsageError),
        };
    }

    public static int RunIntegrityCheck(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs);
        return options.ParseError == null && options.Mode == DbCommandMode.IntegrityCheck
            ? RunIntegrityCheck(options, jsonOptions)
            : Run(cmdArgs, jsonOptions);
    }

    internal static string CreateAutomaticCheckpoint(string dbPath)
    {
        var fullDbPath = Path.GetFullPath(DbPathResolver.NormalizeDbPath(dbPath));
        var name = AutoCheckpointPrefix + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        return CreateCheckpoint(fullDbPath, name).CheckpointPath;
    }

    private static int RunIntegrityCheck(DbCommandOptions options, JsonSerializerOptions jsonOptions)
    {
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

    private static int RunCheckpoint(DbCommandOptions options, JsonSerializerOptions jsonOptions)
    {
        if (!ValidateWritableFileDb(options, jsonOptions, "checkpoint", out var fullDbPath, out var validationExitCode))
            return validationExitCode;

        try
        {
            var result = CreateCheckpoint(fullDbPath, options.Name ?? MakeTimestampCheckpointName());
            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new DbCheckpointJsonResult("success", fullDbPath, result.Name, result.CheckpointPath, result.Files),
                    CliJsonSerializerContextFactory.Create(jsonOptions).DbCheckpointJsonResult));
            }
            else
            {
                Console.WriteLine("Created database checkpoint.");
                Console.WriteLine($"  database  : {fullDbPath}");
                Console.WriteLine($"  name      : {result.Name}");
                Console.WriteLine($"  checkpoint: {result.CheckpointPath}");
                Console.WriteLine($"  files     : {ConsoleUi.Counted(result.Files.Count, "file")}");
            }

            return CommandExitCodes.Success;
        }
        catch (Exception ex)
        {
            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"failed to create database checkpoint: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Ensure the database and checkpoint directory are writable, then retry `cdidx db checkpoint`.",
                CommandErrorCodes.DbError);
        }
    }

    private static int RunListCheckpoints(DbCommandOptions options, JsonSerializerOptions jsonOptions)
    {
        if (!TryResolveFileDb(options.DbPath, out var fullDbPath, out var error))
            return WriteCommandError(options.Json, jsonOptions, error, CommandExitCodes.DatabaseError, "Use a filesystem database path, not a SQLite URI.", CommandErrorCodes.DbError);

        var entries = ListCheckpoints(fullDbPath);
        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new DbCheckpointListJsonResult(fullDbPath, entries),
                CliJsonSerializerContextFactory.Create(jsonOptions).DbCheckpointListJsonResult));
        }
        else
        {
            Console.WriteLine("Database checkpoints");
            Console.WriteLine($"  database: {fullDbPath}");
            if (entries.Count == 0)
            {
                Console.WriteLine("  checkpoints: none");
            }
            else
            {
                foreach (var entry in entries)
                    Console.WriteLine($"  {entry.Name}  {entry.CreatedAtUtc}  {entry.Bytes:N0} bytes");
            }
        }

        return CommandExitCodes.Success;
    }

    private static int RunRestore(DbCommandOptions options, JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
            return WriteCommandError(options.Json, jsonOptions, "restore requires a checkpoint name", CommandExitCodes.UsageError, "Use `cdidx db restore <name> --db <path>`.", CommandErrorCodes.UsageError);
        if (!ValidateWritableFileDb(options, jsonOptions, "restore", out var fullDbPath, out var validationExitCode))
            return validationExitCode;

        try
        {
            var checkpointPath = GetCheckpointPath(fullDbPath, options.Name);
            if (!Directory.Exists(checkpointPath))
                return WriteCommandError(options.Json, jsonOptions, $"checkpoint not found: {options.Name}", CommandExitCodes.NotFound, "Run `cdidx db checkpoints --list` to see available checkpoints.", CommandErrorCodes.DbNotFound);

            var backupPath = RestoreCheckpoint(fullDbPath, options.Name, checkpointPath);
            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new DbRestoreJsonResult("success", fullDbPath, options.Name, checkpointPath, backupPath),
                    CliJsonSerializerContextFactory.Create(jsonOptions).DbRestoreJsonResult));
            }
            else
            {
                Console.WriteLine("Restored database checkpoint.");
                Console.WriteLine($"  database  : {fullDbPath}");
                Console.WriteLine($"  checkpoint: {options.Name}");
                Console.WriteLine($"  backup    : {backupPath}");
            }

            return CommandExitCodes.Success;
        }
        catch (Exception ex)
        {
            return WriteCommandError(
                options.Json,
                jsonOptions,
                $"failed to restore database checkpoint: {ex.Message}",
                CommandExitCodes.DatabaseError,
                "Ensure no cdidx writer is running, then retry `cdidx db restore <name>`.",
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

    internal static DbCommandOptions ParseArgs(string[] args)
    {
        var dbPath = Path.Combine(".cdidx", "codeindex.db");
        var json = false;
        var mode = DbCommandMode.None;
        string? name = null;
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
                    mode = mode == DbCommandMode.None ? DbCommandMode.IntegrityCheck : mode;
                    if (mode != DbCommandMode.IntegrityCheck)
                        parseError = "db accepts only one mode";
                    break;
                case "checkpoint":
                    mode = mode == DbCommandMode.None ? DbCommandMode.Checkpoint : mode;
                    if (mode != DbCommandMode.Checkpoint)
                    {
                        parseError = "db accepts only one mode";
                        break;
                    }
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        name = args[++i];
                    break;
                case "checkpoints":
                    mode = mode == DbCommandMode.None ? DbCommandMode.ListCheckpoints : mode;
                    if (mode != DbCommandMode.ListCheckpoints)
                        parseError = "db accepts only one mode";
                    break;
                case "restore":
                    mode = mode == DbCommandMode.None ? DbCommandMode.Restore : mode;
                    if (mode != DbCommandMode.Restore)
                    {
                        parseError = "db accepts only one mode";
                        break;
                    }
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        name = args[++i];
                    else
                        parseError = "restore requires a checkpoint name";
                    break;
                case "--list":
                    if (mode == DbCommandMode.ListCheckpoints)
                        break;
                    parseError = "--list is only valid with `cdidx db checkpoints --list`";
                    break;
                case "--help" or "-h":
                    return new DbCommandOptions { ShowHelp = true, DbPath = dbPath, Json = json };
                default:
                    if (args[i].StartsWith('-'))
                        parseError = $"db does not support option: '{args[i]}'";
                    else
                        parseError = $"unknown db command or argument: '{args[i]}'";
                    break;
            }

            if (parseError != null)
                break;
        }

        return new DbCommandOptions
        {
            DbPath = dbPath,
            Json = json,
            Mode = mode,
            Name = name,
            ParseError = parseError,
        };
    }

    private static bool ValidateWritableFileDb(DbCommandOptions options, JsonSerializerOptions jsonOptions, string command, out string fullDbPath, out int exitCode)
    {
        exitCode = CommandExitCodes.Success;
        if (!TryResolveFileDb(options.DbPath, out fullDbPath, out var error))
        {
            WriteCommandError(options.Json, jsonOptions, error, CommandExitCodes.DatabaseError, "Use a filesystem database path, not a SQLite URI.", CommandErrorCodes.DbError);
            exitCode = CommandExitCodes.DatabaseError;
            return false;
        }

        if (!File.Exists(LongPath.EnsureWindowsPrefix(fullDbPath)))
        {
            WriteCommandError(
                options.Json,
                jsonOptions,
                $"database not found: {fullDbPath}",
                CommandExitCodes.NotFound,
                "Point `--db` at an existing `codeindex.db`, or run `cdidx index <projectPath>` first to create one.",
                CommandErrorCodes.DbNotFound);
            exitCode = CommandExitCodes.NotFound;
            return false;
        }

        if (DbPathResolver.UriRequestsReadOnly(options.DbPath))
        {
            WriteCommandError(
                options.Json,
                jsonOptions,
                $"database must be writable for {command}: {options.DbPath}",
                CommandExitCodes.DatabaseError,
                "Point `--db` at a writable filesystem path.",
                CommandErrorCodes.DbNotWritable);
            exitCode = CommandExitCodes.DatabaseError;
            return false;
        }

        return true;
    }

    private static bool TryResolveFileDb(string dbPath, out string fullDbPath, out string error)
    {
        fullDbPath = string.Empty;
        error = string.Empty;
        if (dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            error = $"database command requires a filesystem path: {dbPath}";
            return false;
        }

        fullDbPath = Path.GetFullPath(DbPathResolver.NormalizeDbPath(dbPath));
        return true;
    }

    private static DbCheckpointOperationResult CreateCheckpoint(string fullDbPath, string name)
    {
        ValidateCheckpointName(name);
        var checkpointPath = GetCheckpointPath(fullDbPath, name);
        if (Directory.Exists(checkpointPath))
            throw new InvalidOperationException($"checkpoint already exists: {name}");

        Directory.CreateDirectory(checkpointPath);
        CopyIfExists(fullDbPath, Path.Combine(checkpointPath, Path.GetFileName(fullDbPath)));
        CopyIfExists(fullDbPath + "-wal", Path.Combine(checkpointPath, Path.GetFileName(fullDbPath) + "-wal"));
        CopyIfExists(fullDbPath + "-shm", Path.Combine(checkpointPath, Path.GetFileName(fullDbPath) + "-shm"));
        File.WriteAllText(Path.Combine(checkpointPath, "manifest.txt"), $"name={name}{Environment.NewLine}created_at_utc={DateTimeOffset.UtcNow:O}{Environment.NewLine}db={fullDbPath}{Environment.NewLine}");

        var files = Directory.GetFiles(checkpointPath).Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).OrderBy(f => f, StringComparer.Ordinal).ToList();
        return new DbCheckpointOperationResult(name, checkpointPath, files);
    }

    private static List<DbCheckpointListEntryJsonResult> ListCheckpoints(string fullDbPath)
    {
        var root = GetCheckpointRoot(fullDbPath);
        if (!Directory.Exists(root))
            return [];

        return Directory.GetDirectories(root)
            .Select(path =>
            {
                var info = new DirectoryInfo(path);
                var bytes = Directory.GetFiles(path).Sum(file => new FileInfo(file).Length);
                return new DbCheckpointListEntryJsonResult(info.Name, path, info.CreationTimeUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), bytes);
            })
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string RestoreCheckpoint(string fullDbPath, string name, string checkpointPath)
    {
        ValidateCheckpointName(name);
        SqliteConnection.ClearAllPools();
        var backupPath = fullDbPath + ".restore-backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(backupPath);
        MoveIfExists(fullDbPath, Path.Combine(backupPath, Path.GetFileName(fullDbPath)));
        MoveIfExists(fullDbPath + "-wal", Path.Combine(backupPath, Path.GetFileName(fullDbPath) + "-wal"));
        MoveIfExists(fullDbPath + "-shm", Path.Combine(backupPath, Path.GetFileName(fullDbPath) + "-shm"));

        CopyIfExists(Path.Combine(checkpointPath, Path.GetFileName(fullDbPath)), fullDbPath);
        CopyIfExists(Path.Combine(checkpointPath, Path.GetFileName(fullDbPath) + "-wal"), fullDbPath + "-wal");
        CopyIfExists(Path.Combine(checkpointPath, Path.GetFileName(fullDbPath) + "-shm"), fullDbPath + "-shm");
        return backupPath;
    }

    private static void ValidateCheckpointName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(InvalidCheckpointNameChars) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || (Path.AltDirectorySeparatorChar != '\0' && name.Contains(Path.AltDirectorySeparatorChar)))
            throw new ArgumentException($"invalid checkpoint name: {name}");
    }

    private static string MakeTimestampCheckpointName()
        => DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);

    private static string GetCheckpointRoot(string fullDbPath)
        => fullDbPath + CheckpointsDirectorySuffix;

    private static string GetCheckpointPath(string fullDbPath, string name)
    {
        ValidateCheckpointName(name);
        return Path.Combine(GetCheckpointRoot(fullDbPath), name);
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(LongPath.EnsureWindowsPrefix(source)))
            File.Copy(LongPath.EnsureWindowsPrefix(source), LongPath.EnsureWindowsPrefix(destination), overwrite: false);
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (File.Exists(LongPath.EnsureWindowsPrefix(source)))
            File.Move(LongPath.EnsureWindowsPrefix(source), LongPath.EnsureWindowsPrefix(destination));
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
    public DbCommandMode Mode { get; init; }
    public string? Name { get; init; }
    public string? ParseError { get; init; }

    public bool IntegrityCheck => Mode == DbCommandMode.IntegrityCheck;
}

internal enum DbCommandMode
{
    None,
    IntegrityCheck,
    Checkpoint,
    ListCheckpoints,
    Restore,
}

internal sealed record DbCheckpointOperationResult(string Name, string CheckpointPath, List<string> Files);
