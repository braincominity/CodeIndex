using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIndex.Mcp;

namespace CodeIndex.Cli;

internal static class ProgramRunner
{
    internal static int Run(string[] args, JsonSerializerOptions? jsonOptions = null, string? appVersion = null)
    {
        appVersion ??= ConsoleUi.LoadVersion();
        jsonOptions ??= new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            ConsoleUi.PrintUsage(showBanner: args.Length > 0);
            return args.Length == 0 ? CommandExitCodes.UsageError : CommandExitCodes.Success;
        }

        if (args[0] is "--version" or "-V")
        {
            Console.WriteLine($"cdidx v{appVersion}");
            return CommandExitCodes.Success;
        }

        if (args[0] == "--completions")
            return RunCompletions(args[1..]);

        if (args.Length > 1 && ArgHelper.WantsHelp(args.AsSpan(1)))
        {
            ConsoleUi.PrintUsage(showBanner: true);
            return CommandExitCodes.Success;
        }

        var easterEgg = args.FirstOrDefault(a => a is "--sushi" or "--coffee" or "--ramen" or "--wine" or "--beer" or "--matcha" or "--whisky");
        if (easterEgg != null && !args.Any(a => !a.StartsWith('-')))
        {
            ConsoleUi.PrintEasterEggMessage(easterEgg);
            return CommandExitCodes.Success;
        }

        try
        {
            if (args[0] is "mcp" or "mcp-server")
                return RunMcp(args[1..], appVersion);

            return args[0] switch
            {
                "search" => QueryCommandRunner.RunSearch(args[1..], jsonOptions),
                "definition" => QueryCommandRunner.RunDefinition(args[1..], jsonOptions),
                "references" => QueryCommandRunner.RunReferences(args[1..], jsonOptions),
                "callers" => QueryCommandRunner.RunCallers(args[1..], jsonOptions),
                "callees" => QueryCommandRunner.RunCallees(args[1..], jsonOptions),
                "symbols" => QueryCommandRunner.RunSymbols(args[1..], jsonOptions),
                "files" => QueryCommandRunner.RunFiles(args[1..], jsonOptions),
                "find" => QueryCommandRunner.RunFind(args[1..], jsonOptions),
                "excerpt" => QueryCommandRunner.RunExcerpt(args[1..], jsonOptions),
                "map" => QueryCommandRunner.RunMap(args[1..], jsonOptions),
                "inspect" => QueryCommandRunner.RunInspect(args[1..], jsonOptions),
                "outline" => QueryCommandRunner.RunOutline(args[1..], jsonOptions),
                "status" => QueryCommandRunner.RunStatus(args[1..], jsonOptions, appVersion),
                "validate" => QueryCommandRunner.RunValidate(args[1..], jsonOptions),
                "languages" => QueryCommandRunner.RunLanguages(args[1..], jsonOptions),
                "impact" => QueryCommandRunner.RunImpact(args[1..], jsonOptions),
                "deps" => QueryCommandRunner.RunDeps(args[1..], jsonOptions),
                "unused" => QueryCommandRunner.RunUnused(args[1..], jsonOptions),
                "hotspots" => QueryCommandRunner.RunHotspots(args[1..], jsonOptions),
                "index" => IndexCommandRunner.Run(args[1..], jsonOptions),
                "backfill-fold" => IndexCommandRunner.RunBackfillFold(args[1..], jsonOptions),
                _ when IsProjectPathArg(args[0])
                    => IndexCommandRunner.Run(args, jsonOptions),
                _ => ShowError(args, $"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            if (JsonOutputFailure.TryHandle(ex, out var exitCode))
                return exitCode;

            throw;
        }
    }

    internal static bool IsProjectPathArg(string arg) =>
        !arg.StartsWith('-') && (Directory.Exists(arg) || arg.Contains('/') || arg.Contains('\\') || arg == ".");

    private static int RunMcp(string[] cmdArgs, string appVersion)
    {
        var options = QueryCommandRunner.ParseArgs(cmdArgs, jsonDefault: true);
        if (options.ParseError != null)
        {
            Console.Error.WriteLine(options.ParseError);
            Console.Error.WriteLine("Usage: cdidx mcp [--db <path>]");
            return CommandExitCodes.UsageError;
        }

        for (var i = 0; i < cmdArgs.Length; i++)
        {
            if (cmdArgs[i].StartsWith("--db=", StringComparison.Ordinal))
                continue;

            if (cmdArgs[i] == "--db")
            {
                i++;
                continue;
            }

            Console.Error.WriteLine($"Error: {cmdArgs[i]} is not supported for mcp.");
            Console.Error.WriteLine("Hint: use `--db <path>` to point at a specific index, or run `cdidx mcp` to use the default DB.");
            Console.Error.WriteLine("Usage: cdidx mcp [--db <path>]");
            return CommandExitCodes.UsageError;
        }

        var server = new McpServer(options.DbPath, appVersion, options.DbPathExplicit);
        server.RunAsync().GetAwaiter().GetResult();
        return CommandExitCodes.Success;
    }

    private static int RunCompletions(string[] cmdArgs)
    {
        if (cmdArgs.Length == 0)
        {
            Console.Error.WriteLine("Error: --completions requires a shell value.");
            Console.Error.WriteLine("Hint: rerun with one of `bash`, `zsh`, or `fish`.");
            Console.Error.WriteLine("Usage: cdidx --completions <shell>");
            return CommandExitCodes.UsageError;
        }

        if (cmdArgs[0].StartsWith("-", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Error: --completions requires a shell value, got option-like token '{cmdArgs[0]}'.");
            Console.Error.WriteLine("Hint: rerun with one of `bash`, `zsh`, or `fish`.");
            Console.Error.WriteLine("Usage: cdidx --completions <shell>");
            return CommandExitCodes.UsageError;
        }

        if (cmdArgs.Length > 1)
        {
            Console.Error.WriteLine($"Error: --completions accepts exactly one shell value, got extra argument(s): {string.Join(", ", cmdArgs.Skip(1).Select(arg => $"`{arg}`"))}.");
            Console.Error.WriteLine("Hint: rerun with exactly one shell name: `bash`, `zsh`, or `fish`.");
            Console.Error.WriteLine("Usage: cdidx --completions <shell>");
            return CommandExitCodes.UsageError;
        }

        if (ConsoleUi.PrintCompletions(cmdArgs[0]))
            return CommandExitCodes.Success;

        Console.Error.WriteLine("Usage: cdidx --completions <shell>");
        return CommandExitCodes.UsageError;
    }

    private static int ShowError(string[] args, string message)
    {
        Console.Error.WriteLine($"Error: {message}");

        var input = args[0];
        if (!input.StartsWith('-'))
        {
            var best = ConsoleUi.FindClosestCommand(input);
            if (best != null)
                Console.Error.WriteLine($"Did you mean: cdidx {best}?");
        }

        Console.Error.WriteLine("Run 'cdidx --help' for usage information.");
        return CommandExitCodes.UsageError;
    }
}
