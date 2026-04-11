using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIndex.Cli;
using CodeIndex.Mcp;

// On Windows the console defaults to the OEM code page, causing Unicode
// characters (box-drawing, block elements, etc.) to appear as '?'.
// Windows のコンソールは既定で OEM コードページを使用するため、Unicode 文字が文字化けします。
Console.OutputEncoding = Encoding.UTF8;

var appVersion = ConsoleUi.LoadVersion();
var jsonOptions = new JsonSerializerOptions
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

if (args[0] == "--completions" && args.Length >= 2)
{
    return ConsoleUi.PrintCompletions(args[1])
        ? CommandExitCodes.Success
        : CommandExitCodes.UsageError;
}

var easterEgg = args.FirstOrDefault(a => a is "--sushi" or "--coffee" or "--ramen" or "--wine" or "--beer" or "--matcha" or "--whisky");
if (easterEgg != null && !args.Any(a => !a.StartsWith('-')))
{
    ConsoleUi.PrintEasterEggMessage(easterEgg);
    return CommandExitCodes.Success;
}

if (args[0] is "mcp" or "mcp-server")
    return RunMcp(args[1..]);

return args[0] switch
{
    "search" => QueryCommandRunner.RunSearch(args[1..], jsonOptions),
    "definition" => QueryCommandRunner.RunDefinition(args[1..], jsonOptions),
    "references" => QueryCommandRunner.RunReferences(args[1..], jsonOptions),
    "callers" => QueryCommandRunner.RunCallers(args[1..], jsonOptions),
    "callees" => QueryCommandRunner.RunCallees(args[1..], jsonOptions),
    "symbols" => QueryCommandRunner.RunSymbols(args[1..], jsonOptions),
    "files" => QueryCommandRunner.RunFiles(args[1..], jsonOptions),
    "excerpt" => QueryCommandRunner.RunExcerpt(args[1..], jsonOptions),
    "map" => QueryCommandRunner.RunMap(args[1..], jsonOptions),
    "inspect" => QueryCommandRunner.RunInspect(args[1..], jsonOptions),
    "outline" => QueryCommandRunner.RunOutline(args[1..], jsonOptions),
    "status" => QueryCommandRunner.RunStatus(args[1..], jsonOptions, appVersion),
    "languages" => QueryCommandRunner.RunLanguages(args[1..], jsonOptions),
    "index" => IndexCommandRunner.Run(args[1..], jsonOptions),
    _ when IsProjectPathArg(args[0])
        => IndexCommandRunner.Run(args, jsonOptions),
    _ => ShowError($"Unknown command: {args[0]}"),
};

/// <summary>
/// Check if the argument looks like a project directory path rather than a command name.
/// 引数がコマンド名ではなくプロジェクトディレクトリパスに見えるかを判定。
/// </summary>
static bool IsProjectPathArg(string arg) =>
    !arg.StartsWith('-') && (Directory.Exists(arg) || arg.Contains('/') || arg.Contains('\\') || arg == ".");

int RunMcp(string[] cmdArgs)
{
    var options = QueryCommandRunner.ParseArgs(cmdArgs, jsonDefault: true);
    var server = new McpServer(options.DbPath, appVersion);
    server.RunAsync().GetAwaiter().GetResult();
    return CommandExitCodes.Success;
}

int ShowError(string message)
{
    Console.Error.WriteLine($"Error: {message}");
    Console.Error.WriteLine("Run 'cdidx --help' for usage information.");
    return CommandExitCodes.UsageError;
}
