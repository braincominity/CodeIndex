using System.Text.Json;
using CodeIndex.Database;

namespace CodeIndex.Cli;

/// <summary>
/// Runs query-style CLI commands.
/// クエリ系CLIコマンドを実行する。
/// </summary>
public static class QueryCommandRunner
{
    public static int RunSearch(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (options.Query == null)
        {
            Console.Error.WriteLine("Error: search requires a query argument");
            Console.Error.WriteLine("Usage: cdidx search <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--fts]");
            return CommandExitCodes.UsageError;
        }

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.Search(options.Query, options.Limit, options.Lang, options.RawFts, options.PathPattern, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (!options.Json)
                    Console.Error.WriteLine("No results found.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                foreach (var r in results)
                    Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
            }
            else
            {
                foreach (var r in results)
                {
                    Console.WriteLine($"{r.Path}:{r.StartLine}-{r.EndLine}");
                    var snippetLines = SearchSnippetFormatter.Format(r.Content, options.Query);
                    foreach (var line in snippetLines)
                        Console.WriteLine($"  {line}");
                    Console.WriteLine();
                }
                Console.Error.WriteLine($"({results.Count} results)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunDefinition(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (options.Query == null)
        {
            Console.Error.WriteLine("Error: definition requires a symbol query argument");
            Console.Error.WriteLine("Usage: cdidx definition <query> [--db <path>] [--json] [--limit <n>] [--lang <lang>] [--kind <kind>] [--body]");
            return CommandExitCodes.UsageError;
        }

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.GetDefinitions(options.Query, options.Limit, options.Kind, options.Lang, options.IncludeBody, options.PathPattern, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (!options.Json)
                    Console.Error.WriteLine("No definitions found.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                foreach (var r in results)
                    Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
            }
            else
            {
                foreach (var r in results)
                {
                    Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.Path}:{r.StartLine}-{r.EndLine}");
                    WriteNumberedExcerpt(r.StartLine, r.Content);
                    if (options.IncludeBody)
                    {
                        if (r.BodyContent != null && r.BodyStartLine != null)
                        {
                            Console.WriteLine();
                            Console.WriteLine("  Body:");
                            WriteNumberedExcerpt(r.BodyStartLine.Value, r.BodyContent);
                        }
                        else
                        {
                            Console.WriteLine("  Body: unavailable");
                        }
                    }
                    Console.WriteLine();
                }
                Console.Error.WriteLine($"({results.Count} definitions)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunSymbols(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.SearchSymbols(options.Query, options.Limit, options.Kind, options.Lang, options.PathPattern, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (!options.Json)
                    Console.Error.WriteLine("No symbols found.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                foreach (var r in results)
                    Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
            }
            else
            {
                foreach (var r in results)
                {
                    var lineRange = r.EndLine > r.StartLine
                        ? $"{r.StartLine}-{r.EndLine}"
                        : r.StartLine.ToString();
                    Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.Path}:{lineRange}");
                }
                Console.Error.WriteLine($"({results.Count} symbols)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunFiles(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.ListFiles(options.Query, options.Limit, options.Lang, options.PathPattern, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (!options.Json)
                    Console.Error.WriteLine("No files found.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
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
            return CommandExitCodes.Success;
        });
    }

    public static int RunExcerpt(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (options.Query == null)
        {
            Console.Error.WriteLine("Error: excerpt requires a path argument");
            Console.Error.WriteLine("Usage: cdidx excerpt <path> --start <line> [--end <line>] [--before <n>] [--after <n>] [--db <path>] [--json]");
            return CommandExitCodes.UsageError;
        }

        if (options.StartLine == null)
        {
            Console.Error.WriteLine("Error: excerpt requires --start <line>");
            return CommandExitCodes.UsageError;
        }

        var endLine = options.EndLine ?? options.StartLine.Value;
        return WithDb(options.DbPath, reader =>
        {
            var excerpt = reader.GetExcerpt(options.Query, options.StartLine.Value, endLine, options.ContextBefore, options.ContextAfter);
            if (excerpt == null)
            {
                if (!options.Json)
                    Console.Error.WriteLine("No excerpt found.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(excerpt, jsonOptions));
            }
            else
            {
                Console.WriteLine($"{excerpt.Path}:{excerpt.StartLine}-{excerpt.EndLine}");
                WriteNumberedExcerpt(excerpt.StartLine, excerpt.Content);
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunStatus(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);

        return WithDb(options.DbPath, reader =>
        {
            var status = reader.GetStatus();

            if (options.Json)
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
            return CommandExitCodes.Success;
        });
    }

    public static QueryCommandOptions ParseArgs(string[] args, bool jsonDefault)
    {
        string dbPath = Path.Combine(".cdidx", "codeindex.db");
        bool? json = null;
        int limit = 20;
        string? lang = null;
        string? kind = null;
        string? query = null;
        bool rawFts = false;
        bool includeBody = false;
        int? startLine = null;
        int? endLine = null;
        int contextBefore = 0;
        int contextAfter = 0;
        string? pathPattern = null;
        var excludePaths = new List<string>();
        bool excludeTests = false;

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
                        limit = 20;
                    }
                    break;
                case "--lang" when i + 1 < args.Length:
                    lang = args[++i];
                    break;
                case "--kind" when i + 1 < args.Length:
                    kind = args[++i];
                    break;
                case "--fts":
                    rawFts = true;
                    break;
                case "--body":
                    includeBody = true;
                    break;
                case "--path" when i + 1 < args.Length:
                    pathPattern = args[++i];
                    break;
                case "--exclude-path" when i + 1 < args.Length:
                    excludePaths.Add(args[++i]);
                    break;
                case "--exclude-tests":
                    excludeTests = true;
                    break;
                case "--start" when i + 1 < args.Length:
                    startLine = ParsePositiveInt(args[++i], "--start");
                    break;
                case "--end" when i + 1 < args.Length:
                    endLine = ParsePositiveInt(args[++i], "--end");
                    break;
                case "--before" when i + 1 < args.Length:
                    contextBefore = ParseNonNegativeInt(args[++i], "--before");
                    break;
                case "--after" when i + 1 < args.Length:
                    contextAfter = ParseNonNegativeInt(args[++i], "--after");
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Warning: unknown option '{args[i]}' (ignored) / 不明なオプション '{args[i]}'（無視されます）");
                    }
                    else if (query == null)
                    {
                        query = args[i];
                    }
                    break;
            }
        }

        return new QueryCommandOptions
        {
            DbPath = dbPath,
            Json = json ?? jsonDefault,
            Limit = limit,
            Lang = lang,
            Kind = kind,
            Query = query,
            RawFts = rawFts,
            IncludeBody = includeBody,
            StartLine = startLine,
            EndLine = endLine,
            ContextBefore = contextBefore,
            ContextAfter = contextAfter,
            PathPattern = pathPattern,
            ExcludePaths = excludePaths,
            ExcludeTests = excludeTests,
        };
    }

    private static int WithDb(string dbPath, Func<DbReader, int> action)
    {
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Error: database not found: {dbPath}");
            Console.Error.WriteLine("Run 'cdidx index <projectPath>' first to create the index.");
            return CommandExitCodes.DatabaseError;
        }

        try
        {
            using var db = new DbContext(dbPath);
            db.InitializeSchema();
            var reader = new DbReader(db.Connection);
            return action(reader);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: database error: {ex.Message}");
            return CommandExitCodes.DatabaseError;
        }
    }

    private static void WriteNumberedExcerpt(int startLine, string content)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            Console.WriteLine($"  {startLine + i,4}: {lines[i]}");
    }

    private static int? ParsePositiveInt(string rawValue, string optionName)
    {
        if (!int.TryParse(rawValue, out var value) || value <= 0)
        {
            Console.Error.WriteLine($"Error: {optionName} requires a positive integer, got '{rawValue}'");
            return null;
        }

        return value;
    }

    private static int ParseNonNegativeInt(string rawValue, string optionName)
    {
        if (!int.TryParse(rawValue, out var value) || value < 0)
        {
            Console.Error.WriteLine($"Error: {optionName} requires a non-negative integer, got '{rawValue}'");
            return 0;
        }

        return value;
    }
}

public sealed class QueryCommandOptions
{
    public string DbPath { get; init; } = Path.Combine(".cdidx", "codeindex.db");
    public bool Json { get; init; }
    public int Limit { get; init; } = 20;
    public string? Lang { get; init; }
    public string? Kind { get; init; }
    public string? Query { get; init; }
    public bool RawFts { get; init; }
    public bool IncludeBody { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public int ContextBefore { get; init; }
    public int ContextAfter { get; init; }
    public string? PathPattern { get; init; }
    public List<string> ExcludePaths { get; init; } = [];
    public bool ExcludeTests { get; init; }
}
