using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeIndex.Database;
using CodeIndex.Models;

namespace CodeIndex.Cli;

internal static class SuggestionsCommandRunner
{
    private const string Usage = "Usage: cdidx suggestions <list|show|export> [id] [--db <path>] [--json] [--status <all|submitted|unsubmitted>] [--language <lang>] [--category <category>] [--since <datetime>] [--agent <name>] [--format <json|markdown>]";

    public static int Run(string[] args, JsonSerializerOptions jsonOptions)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? CommandExitCodes.UsageError : CommandExitCodes.Success;
        }

        var verb = args[0];
        var options = Parse(args[1..]);
        if (options.Error != null)
        {
            Console.Error.WriteLine(options.Error);
            Console.Error.WriteLine(Usage);
            return CommandExitCodes.UsageError;
        }

        var store = CreateStore(options.DbPath);
        var records = ApplyFilters(store.LoadAll(), options)
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Hash, StringComparer.Ordinal)
            .ToList();

        return verb switch
        {
            "list" => RunList(records, options, jsonOptions),
            "show" => RunShow(records, options, jsonOptions),
            "export" => RunExport(records, options, jsonOptions),
            _ => WriteUsageError($"Unknown suggestions subcommand: {verb}")
        };
    }

    private static int RunList(List<SuggestionRecord> records, Options options, JsonSerializerOptions jsonOptions)
    {
        if (options.Json)
        {
            foreach (var item in records.Select(ToListItem))
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    item,
                    CliJsonSerializerContextFactory.Create(jsonOptions).SuggestionListItemJsonResult));
            }
            return CommandExitCodes.Success;
        }

        if (records.Count == 0)
        {
            Console.Error.WriteLine("No suggestions found.");
            return CommandExitCodes.NotFound;
        }

        foreach (var record in records)
        {
            var id = ShortId(record.Hash);
            var status = GetStatus(record);
            var language = string.IsNullOrWhiteSpace(record.Language) ? "-" : record.Language;
            Console.WriteLine($"{id}  {record.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}  {status,-11}  {record.Category,-20}  {language,-10}  {FormatTitle(record.Description, 80)}");
        }

        return CommandExitCodes.Success;
    }

    private static int RunShow(List<SuggestionRecord> records, Options options, JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(options.Id))
            return WriteUsageError("suggestions show requires an id.");

        var record = ResolveById(records, options.Id);
        if (record == null)
        {
            Console.Error.WriteLine($"Suggestion not found: {options.Id}");
            return CommandExitCodes.NotFound;
        }

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                ToDetail(record),
                CliJsonSerializerContextFactory.Create(jsonOptions).SuggestionDetailJsonResult));
            return CommandExitCodes.Success;
        }

        Console.WriteLine($"id: {record.Hash}");
        Console.WriteLine($"created_at: {record.CreatedAt:O}");
        Console.WriteLine($"status: {GetStatus(record)}");
        Console.WriteLine($"category: {record.Category}");
        Console.WriteLine($"language: {record.Language ?? "-"}");
        if (!string.IsNullOrWhiteSpace(record.Agent))
            Console.WriteLine($"agent: {record.Agent}");
        Console.WriteLine($"submitted_to_github: {record.SubmittedToGitHub.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(record.GitHubIssueUrl))
            Console.WriteLine($"github_issue_url: {record.GitHubIssueUrl}");
        Console.WriteLine();
        Console.WriteLine(record.Description);
        if (!string.IsNullOrWhiteSpace(record.Context))
        {
            Console.WriteLine();
            Console.WriteLine("context:");
            Console.WriteLine(record.Context);
        }

        return CommandExitCodes.Success;
    }

    private static int RunExport(List<SuggestionRecord> records, Options options, JsonSerializerOptions jsonOptions)
    {
        if (options.ExportFormat == "markdown")
        {
            Console.WriteLine(FormatMarkdown(records));
            return CommandExitCodes.Success;
        }

        var payload = new SuggestionExportJsonResult(
            JsonOutputContract.ApiVersion,
            records.Count,
            records.Select(ToDetail).ToList());
        Console.WriteLine(JsonSerializer.Serialize(
            payload,
            CliJsonSerializerContextFactory.Create(jsonOptions).SuggestionExportJsonResult));
        return CommandExitCodes.Success;
    }

    private static SuggestionStore CreateStore(string? dbPath)
    {
        var normalizedDbPath = string.IsNullOrWhiteSpace(dbPath)
            ? Path.Combine(Environment.CurrentDirectory, ".cdidx", "codeindex.db")
            : DbPathResolver.NormalizeDbPath(dbPath);
        var fullDbPath = Path.GetFullPath(normalizedDbPath);
        var cdidxDir = Path.GetDirectoryName(fullDbPath) ?? Path.Combine(Environment.CurrentDirectory, ".cdidx");
        var dbName = Path.GetFileNameWithoutExtension(fullDbPath);
        return new SuggestionStore(cdidxDir, dbName);
    }

    private static IEnumerable<SuggestionRecord> ApplyFilters(IEnumerable<SuggestionRecord> records, Options options)
    {
        foreach (var record in records)
        {
            if (options.Status != "all" && !string.Equals(GetStatus(record), options.Status, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.Language != null && !string.Equals(record.Language, options.Language, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.Category != null && !string.Equals(record.Category, options.Category, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.Agent != null && !string.Equals(record.Agent, options.Agent, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.Since != null && new DateTimeOffset(DateTime.SpecifyKind(record.CreatedAt, DateTimeKind.Utc)) < options.Since.Value)
                continue;
            yield return record;
        }
    }

    private static SuggestionRecord? ResolveById(List<SuggestionRecord> records, string id)
    {
        var matches = records
            .Where(r => r.Hash.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : records.FirstOrDefault(r => string.Equals(r.Hash, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStatus(SuggestionRecord record) => record.SubmittedToGitHub ? "submitted" : "unsubmitted";

    private static string ShortId(string hash) => hash.Length <= 12 ? hash : hash[..12];

    private static string FormatTitle(string description, int maxLength)
    {
        var firstLine = description.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return firstLine.Length <= maxLength ? firstLine : firstLine[..(maxLength - 1)] + "...";
    }

    private static SuggestionListItemJsonResult ToListItem(SuggestionRecord record) => new(
        JsonOutputContract.ApiVersion,
        record.Hash,
        ShortId(record.Hash),
        record.CreatedAt,
        GetStatus(record),
        record.Category,
        record.Language,
        record.Agent,
        FormatTitle(record.Description, 120),
        record.SubmittedToGitHub,
        record.GitHubIssueUrl);

    private static SuggestionDetailJsonResult ToDetail(SuggestionRecord record) => new(
        JsonOutputContract.ApiVersion,
        record.Hash,
        record.CreatedAt,
        GetStatus(record),
        record.Category,
        record.Language,
        record.Agent,
        record.Description,
        record.Context,
        record.SubmittedToGitHub,
        record.GitHubIssueUrl);

    private static string FormatMarkdown(List<SuggestionRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# cdidx Suggestions");
        sb.AppendLine();
        sb.AppendLine($"Exported suggestions: {records.Count}");
        foreach (var record in records)
        {
            sb.AppendLine();
            sb.AppendLine($"## {ShortId(record.Hash)} - {FormatTitle(record.Description, 100)}");
            sb.AppendLine();
            sb.AppendLine($"- id: `{record.Hash}`");
            sb.AppendLine($"- created_at: `{record.CreatedAt:O}`");
            sb.AppendLine($"- status: `{GetStatus(record)}`");
            sb.AppendLine($"- category: `{record.Category}`");
            sb.AppendLine($"- language: `{record.Language ?? "-"}`");
            if (!string.IsNullOrWhiteSpace(record.Agent))
                sb.AppendLine($"- agent: `{record.Agent}`");
            if (!string.IsNullOrWhiteSpace(record.GitHubIssueUrl))
                sb.AppendLine($"- github_issue_url: {record.GitHubIssueUrl}");
            sb.AppendLine();
            sb.AppendLine(record.Description);
            if (!string.IsNullOrWhiteSpace(record.Context))
            {
                sb.AppendLine();
                sb.AppendLine("Context:");
                sb.AppendLine();
                sb.AppendLine(record.Context);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static int WriteUsageError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine(Usage);
        return CommandExitCodes.UsageError;
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--json":
                    options.Json = true;
                    break;
                case "--db":
                    if (!TryReadValue(args, ref i, "--db", out var dbPath, out var dbError))
                    {
                        options.Error = dbError;
                        return options;
                    }
                    options.DbPath = dbPath;
                    break;
                case "--status":
                    if (!TryReadValue(args, ref i, "--status", out var status, out var statusError))
                    {
                        options.Error = statusError;
                        return options;
                    }
                    options.Status = status;
                    if (options.Status is not ("all" or "submitted" or "unsubmitted"))
                        options.Error = "Error: --status must be one of all, submitted, unsubmitted.";
                    break;
                case "--language":
                case "--lang":
                    if (!TryReadValue(args, ref i, arg, out var language, out var languageError))
                    {
                        options.Error = languageError;
                        return options;
                    }
                    options.Language = language;
                    break;
                case "--category":
                    if (!TryReadValue(args, ref i, "--category", out var category, out var categoryError))
                    {
                        options.Error = categoryError;
                        return options;
                    }
                    options.Category = category;
                    break;
                case "--agent":
                    if (!TryReadValue(args, ref i, "--agent", out var agent, out var agentError))
                    {
                        options.Error = agentError;
                        return options;
                    }
                    options.Agent = agent;
                    break;
                case "--since":
                    if (!TryReadValue(args, ref i, "--since", out var since, out var sinceError))
                    {
                        options.Error = sinceError;
                        return options;
                    }
                    if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedSince))
                        options.Error = $"Error: could not parse --since value '{since}' as a date/time.";
                    else
                        options.Since = parsedSince;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref i, "--format", out var format, out var formatError))
                    {
                        options.Error = formatError;
                        return options;
                    }
                    options.ExportFormat = format;
                    if (options.ExportFormat is not ("json" or "markdown"))
                        options.Error = "Error: --format must be one of json, markdown.";
                    break;
                default:
                    if (arg.StartsWith("--db=", StringComparison.Ordinal))
                        options.DbPath = arg["--db=".Length..];
                    else if (arg.StartsWith("--status=", StringComparison.Ordinal))
                        options.Status = arg["--status=".Length..];
                    else if (arg.StartsWith("--language=", StringComparison.Ordinal))
                        options.Language = arg["--language=".Length..];
                    else if (arg.StartsWith("--lang=", StringComparison.Ordinal))
                        options.Language = arg["--lang=".Length..];
                    else if (arg.StartsWith("--category=", StringComparison.Ordinal))
                        options.Category = arg["--category=".Length..];
                    else if (arg.StartsWith("--agent=", StringComparison.Ordinal))
                        options.Agent = arg["--agent=".Length..];
                    else if (arg.StartsWith("--format=", StringComparison.Ordinal))
                        options.ExportFormat = arg["--format=".Length..];
                    else if (arg.StartsWith("--since=", StringComparison.Ordinal))
                    {
                        var inlineSince = arg["--since=".Length..];
                        if (!DateTimeOffset.TryParse(inlineSince, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedInlineSince))
                            options.Error = $"Error: could not parse --since value '{inlineSince}' as a date/time.";
                        else
                            options.Since = parsedInlineSince;
                    }
                    else if (arg.StartsWith("-", StringComparison.Ordinal))
                        options.Error = $"Error: {arg} is not supported for suggestions.";
                    else if (options.Id == null)
                        options.Id = arg;
                    else
                        options.Error = $"Error: unexpected argument '{arg}'.";
                    break;
            }

            if (options.Error != null)
                return options;
        }

        options.Status = options.Status.ToLowerInvariant();
        options.ExportFormat = options.ExportFormat.ToLowerInvariant();
        if (options.Status is not ("all" or "submitted" or "unsubmitted"))
            options.Error = "Error: --status must be one of all, submitted, unsubmitted.";
        if (options.ExportFormat is not ("json" or "markdown"))
            options.Error = "Error: --format must be one of json, markdown.";
        return options;
    }

    private static bool TryReadValue(string[] args, ref int i, string option, out string value, out string? error)
    {
        value = string.Empty;
        error = null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
        {
            error = $"Error: {option} requires a value.";
            return false;
        }

        value = args[++i];
        return true;
    }

    private sealed class Options
    {
        public string? Id { get; set; }
        public string? DbPath { get; set; }
        public bool Json { get; set; }
        public string Status { get; set; } = "all";
        public string ExportFormat { get; set; } = "json";
        public string? Language { get; set; }
        public string? Category { get; set; }
        public string? Agent { get; set; }
        public DateTimeOffset? Since { get; set; }
        public string? Error { get; set; }
    }
}

internal sealed record SuggestionListItemJsonResult(
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("short_id")] string ShortId,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("agent")] string? Agent,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("submitted_to_github")] bool SubmittedToGitHub,
    [property: JsonPropertyName("github_issue_url")] string? GitHubIssueUrl);

internal sealed record SuggestionDetailJsonResult(
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("agent")] string? Agent,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("submitted_to_github")] bool SubmittedToGitHub,
    [property: JsonPropertyName("github_issue_url")] string? GitHubIssueUrl);

internal sealed record SuggestionExportJsonResult(
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("suggestions")] List<SuggestionDetailJsonResult> Suggestions);
