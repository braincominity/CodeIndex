using System.Text.Json;
using CodeIndex.Indexer;
using Microsoft.Data.Sqlite;

namespace CodeIndex.Cli;

public static class DiffCommandRunner
{
    private const int DefaultDiffLimit = 20;
    private const int DriftExitCode = 1;
    private const int SchemaMismatchExitCode = 2;
    private const int UnreadableExitCode = 3;

    public static int Run(string[] args, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(args);
        if (options.ShowHelp)
        {
            ConsoleUi.PrintUsage();
            return CommandExitCodes.Success;
        }

        if (options.ParseError is not null)
            return WriteCommandError(
                options.Json || options.SummaryOnly,
                jsonOptions,
                options.ParseError,
                CommandExitCodes.UsageError,
                "Run `cdidx diff <db1> <db2> --help` to see the supported command shape.",
                CommandErrorCodes.UsageError);

        try
        {
            var left = ReadSnapshot(options.LeftDb!);
            var right = ReadSnapshot(options.RightDb!);
            var result = BuildDiff(left, right, options);

            if (options.SummaryOnly)
                WriteSummaryJson(result, jsonOptions);
            else if (options.Json)
                WriteJson(result, jsonOptions);
            else
                WriteText(result, options);

            if (!result.Summary.SchemaVersionsEqual)
                return SchemaMismatchExitCode;
            return result.Identical ? CommandExitCodes.Success : DriftExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            return WriteCommandError(
                options.Json || options.SummaryOnly,
                jsonOptions,
                $"failed to compare databases: {ex.Message}",
                UnreadableExitCode,
                "Pass two readable CodeIndex SQLite database paths.",
                CommandErrorCodes.DbError);
        }
    }

    internal static DiffCommandOptions ParseArgs(string[] args)
    {
        var dbs = new List<string>(2);
        var json = false;
        var detailed = false;
        var summaryOnly = false;
        var limit = DefaultDiffLimit;
        string? parseError = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    return new DiffCommandOptions { ShowHelp = true };
                case "--json":
                    json = true;
                    break;
                case "--detailed":
                    detailed = true;
                    break;
                case "--summary-only":
                    summaryOnly = true;
                    break;
                case "--limit" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out limit) || limit < 0)
                        parseError = "--limit requires a non-negative integer";
                    break;
                case "--limit":
                    parseError = "--limit requires a value";
                    break;
                default:
                    if (arg.StartsWith('-'))
                        parseError = $"diff does not support option: '{arg}'";
                    else if (dbs.Count >= 2)
                        parseError = $"diff accepts exactly two database paths; unexpected argument: '{arg}'";
                    else
                        dbs.Add(arg);
                    break;
            }

            if (parseError is not null)
                break;
        }

        if (parseError is null && dbs.Count != 2)
            parseError = "diff requires exactly two database paths";

        return new DiffCommandOptions
        {
            LeftDb = dbs.Count > 0 ? dbs[0] : null,
            RightDb = dbs.Count > 1 ? dbs[1] : null,
            Json = json,
            Detailed = detailed,
            SummaryOnly = summaryOnly,
            Limit = limit,
            ParseError = parseError,
        };
    }

    private static DiffDbSnapshot ReadSnapshot(string dbPath)
    {
        var isUri = dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && !File.Exists(LongPath.EnsureWindowsPrefix(dbPath)))
            throw new IOException($"database not found: {dbPath}");

        var connectionString = isUri
            ? $"Data Source={dbPath}"
            : new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ConnectionString;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        return new DiffDbSnapshot(
            Path.GetFullPath(isUri ? dbPath : dbPath),
            ExecuteLong(connection, "PRAGMA user_version"),
            ExecuteLong(connection, "SELECT COUNT(*) FROM files"),
            ExecuteLong(connection, "SELECT COUNT(*) FROM symbols"),
            ExecuteLong(connection, "SELECT COUNT(*) FROM symbol_references"),
            ReadStrings(connection, "SELECT path FROM files ORDER BY path"),
            ReadStrings(connection, "SELECT COALESCE((SELECT path FROM files WHERE files.id = symbols.file_id), '') || ':' || COALESCE(line, -1) || ' ' || COALESCE(kind, '') || ' ' || COALESCE(name, '') FROM symbols ORDER BY 1"));
    }

    private static DiffJsonResult BuildDiff(DiffDbSnapshot left, DiffDbSnapshot right, DiffCommandOptions options)
    {
        var filesOnlyInLeft = TakeDiff(left.Files, right.Files, options.Limit);
        var filesOnlyInRight = TakeDiff(right.Files, left.Files, options.Limit);
        var symbolsOnlyInLeft = options.Detailed ? TakeDiff(left.SymbolKeys, right.SymbolKeys, options.Limit) : [];
        var symbolsOnlyInRight = options.Detailed ? TakeDiff(right.SymbolKeys, left.SymbolKeys, options.Limit) : [];

        var summary = new DiffSummaryJsonResult(
            left.FileCount,
            right.FileCount,
            right.FileCount - left.FileCount,
            left.SymbolCount,
            right.SymbolCount,
            right.SymbolCount - left.SymbolCount,
            left.ReferenceCount,
            right.ReferenceCount,
            right.ReferenceCount - left.ReferenceCount,
            left.SchemaVersion,
            right.SchemaVersion,
            left.SchemaVersion == right.SchemaVersion);

        var identical =
            summary.SchemaVersionsEqual &&
            summary.FileCountDelta == 0 &&
            summary.SymbolCountDelta == 0 &&
            summary.ReferenceCountDelta == 0 &&
            left.Files.SetEquals(right.Files) &&
            (!options.Detailed || left.SymbolKeys.SetEquals(right.SymbolKeys));

        return new DiffJsonResult(
            identical ? "identical" : "different",
            identical,
            left.Path,
            right.Path,
            summary,
            filesOnlyInLeft,
            filesOnlyInRight,
            options.Detailed ? symbolsOnlyInLeft : null,
            options.Detailed ? symbolsOnlyInRight : null,
            options.Limit,
            options.Detailed);
    }

    private static List<string> TakeDiff(HashSet<string> source, HashSet<string> other, int limit)
    {
        if (limit == 0)
            return [];

        var result = new List<string>(Math.Min(source.Count, limit));
        foreach (var item in source.Order(StringComparer.Ordinal))
        {
            if (other.Contains(item))
                continue;
            result.Add(item);
            if (result.Count >= limit)
                break;
        }
        return result;
    }

    private static long ExecuteLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HashSet<string> ReadStrings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        return values;
    }

    private static void WriteJson(DiffJsonResult result, JsonSerializerOptions jsonOptions)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            CliJsonSerializerContextFactory.Create(jsonOptions).DiffJsonResult));
    }

    private static void WriteSummaryJson(DiffJsonResult result, JsonSerializerOptions jsonOptions)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            new DiffSummaryOnlyJsonResult(result.Status, result.Identical, result.LeftDb, result.RightDb, result.Summary),
            CliJsonSerializerContextFactory.Create(jsonOptions).DiffSummaryOnlyJsonResult));
    }

    private static void WriteText(DiffJsonResult result, DiffCommandOptions options)
    {
        Console.WriteLine("Index database diff");
        Console.WriteLine($"  left   : {result.LeftDb}");
        Console.WriteLine($"  right  : {result.RightDb}");
        Console.WriteLine($"  status : {result.Status}");
        Console.WriteLine($"  schema : {result.Summary.LeftSchemaVersion} -> {result.Summary.RightSchemaVersion}");
        Console.WriteLine($"  files  : {result.Summary.LeftFileCount} -> {result.Summary.RightFileCount} ({FormatDelta(result.Summary.FileCountDelta)})");
        Console.WriteLine($"  symbols: {result.Summary.LeftSymbolCount} -> {result.Summary.RightSymbolCount} ({FormatDelta(result.Summary.SymbolCountDelta)})");
        Console.WriteLine($"  refs   : {result.Summary.LeftReferenceCount} -> {result.Summary.RightReferenceCount} ({FormatDelta(result.Summary.ReferenceCountDelta)})");

        WriteList("files only in left", result.FilesOnlyInLeft);
        WriteList("files only in right", result.FilesOnlyInRight);
        if (options.Detailed)
        {
            WriteList("symbols only in left", result.SymbolsOnlyInLeft ?? []);
            WriteList("symbols only in right", result.SymbolsOnlyInRight ?? []);
        }
    }

    private static void WriteList(string label, List<string> values)
    {
        if (values.Count == 0)
            return;
        Console.WriteLine($"  {label}:");
        foreach (var value in values)
            Console.WriteLine($"    - {value}");
    }

    private static string FormatDelta(long delta) => delta >= 0 ? $"+{delta}" : delta.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int WriteCommandError(bool json, JsonSerializerOptions jsonOptions, string message, int exitCode, string? hint = null, string? errorCode = null)
    {
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(
                new CommandErrorJsonResult("error", message, hint, errorCode),
                CliJsonSerializerContextFactory.Create(jsonOptions).CommandErrorJsonResult));
        else
        {
            Console.Error.WriteLine($"Error [{errorCode ?? CommandErrorCodes.UsageError}]: {message}");
            if (!string.IsNullOrWhiteSpace(hint))
                Console.Error.WriteLine($"Hint: {hint}");
        }
        return exitCode;
    }

    private sealed record DiffDbSnapshot(
        string Path,
        long SchemaVersion,
        long FileCount,
        long SymbolCount,
        long ReferenceCount,
        HashSet<string> Files,
        HashSet<string> SymbolKeys);
}

internal sealed class DiffCommandOptions
{
    public string? LeftDb { get; init; }
    public string? RightDb { get; init; }
    public bool Json { get; init; }
    public bool Detailed { get; init; }
    public bool SummaryOnly { get; init; }
    public bool ShowHelp { get; init; }
    public int Limit { get; init; } = 20;
    public string? ParseError { get; init; }
}
