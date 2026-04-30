using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeIndex.Cli;

internal sealed record BackfillFoldJsonResult(
    [property: JsonPropertyName("symbols")] int Symbols,
    [property: JsonPropertyName("symbol_references")] int SymbolReferences,
    [property: JsonPropertyName("rewrite_all")] bool RewriteAll,
    [property: JsonPropertyName("verified")] bool Verified,
    [property: JsonPropertyName("user_version_before")] int UserVersionBefore,
    [property: JsonPropertyName("user_version_after")] int UserVersionAfter,
    [property: JsonPropertyName("fold_ready")] bool FoldReady);

internal sealed record CommandErrorJsonResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("hint")] string? Hint);

[JsonSerializable(typeof(QueryCountJsonResult))]
[JsonSerializable(typeof(QueryCountFilesJsonResult))]
[JsonSerializable(typeof(QueryFindCountJsonResult))]
[JsonSerializable(typeof(LanguageEntryJsonResult))]
[JsonSerializable(typeof(LanguagesJsonResult))]
[JsonSerializable(typeof(QueryPathErrorJsonResult))]
[JsonSerializable(typeof(CommandErrorJsonResult))]
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(BackfillFoldJsonResult))]
internal partial class CliJsonSerializerContext : JsonSerializerContext;

internal sealed record QueryCountJsonResult(
    [property: JsonPropertyName("count")] int Count);

internal sealed record QueryCountFilesJsonResult(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("files")] int Files);

internal sealed record QueryFindCountJsonResult(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("files")] int Files,
    [property: JsonPropertyName("file_count")] int FileCount);

internal sealed record QueryPathErrorJsonResult(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("error")] string Error);

internal sealed record LanguageEntryJsonResult(
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("extensions")] List<string> Extensions,
    [property: JsonPropertyName("symbol_extraction")] bool SymbolExtraction,
    [property: JsonPropertyName("graph_queries")] bool GraphQueries);

internal sealed record LanguagesJsonResult(
    [property: JsonPropertyName("languages")] List<LanguageEntryJsonResult> Languages);
