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

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(BackfillFoldJsonResult))]
[JsonSerializable(typeof(CommandErrorJsonResult))]
internal partial class CliJsonSerializerContext : JsonSerializerContext;
