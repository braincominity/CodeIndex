using System.Text.Json.Serialization;

namespace CodeIndex.Database;

// Result DTOs for query operations / クエリ操作用の結果DTO
// Extracted from DbReader.cs for file-size reduction.
// ファイルサイズ削減のため DbReader.cs から分離。

public class SearchResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class SymbolResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Line { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int? BodyStartLine { get; set; }
    public int? BodyEndLine { get; set; }
    public string? Signature { get; set; }
    public string? ContainerKind { get; set; }
    public string? ContainerName { get; set; }
    public string? Visibility { get; set; }
    public string? ReturnType { get; set; }
}

public class FileResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public long Size { get; set; }
    public int Lines { get; set; }
    public int SymbolCount { get; set; }
    public int ReferenceCount { get; set; }
    public string? Checksum { get; set; }
    public DateTime? Modified { get; set; }
    public DateTime? IndexedAt { get; set; }
}

public class FileExcerptResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class DefinitionResult : SymbolResult
{
    public string Content { get; set; } = string.Empty;
    public string? BodyContent { get; set; }
    public int? Complexity { get; set; }
}

public class ExactZeroHintResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RelaxedCount { get; set; }
    public const string DefaultSuggestion = "drop --exact or use the exact indexed name";
    public List<string> SampleNames { get; set; } = [];
    public string Suggestion { get; set; } = DefaultSuggestion;

    public static ExactZeroHintResult? FromRelaxedMatches(int relaxedCount, IEnumerable<string?> names, int sampleLimit = 5)
    {
        if (relaxedCount <= 0)
            return null;

        var sampleNames = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Take(sampleLimit)
            .Select(name => name!)
            .ToList();

        return new ExactZeroHintResult
        {
            RelaxedCount = relaxedCount,
            SampleNames = sampleNames,
            Suggestion = DefaultSuggestion,
        };
    }
}

public class FreshnessHintResult
{
    public long FileCount { get; set; }
    public DateTime? IndexedAt { get; set; }
    public bool FreshnessAvailable { get; set; }
    public string? FreshnessDegradedReason { get; set; }
}

public class ReferenceResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string ReferenceKind { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Context { get; set; } = string.Empty;
    public string? ContainerKind { get; set; }
    public string? ContainerName { get; set; }
}

public class CallerResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string? CallerKind { get; set; }
    public string? CallerName { get; set; }
    public string CalleeName { get; set; } = string.Empty;
    public int FirstLine { get; set; }
    public int ReferenceCount { get; set; }
}

public class CalleeResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string? CallerKind { get; set; }
    public string? CallerName { get; set; }
    public string CalleeName { get; set; } = string.Empty;
    public string ReferenceKind { get; set; } = string.Empty;
    public int FirstLine { get; set; }
    public int ReferenceCount { get; set; }
}

public class ImpactResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string? CallerKind { get; set; }
    public string? CallerName { get; set; }
    public string CalleeName { get; set; } = string.Empty;
    public int Depth { get; set; }
    public int FirstLine { get; set; }
    public int ReferenceCount { get; set; }
}

public class StatusResult
{
    public long Files { get; set; }
    public long Chunks { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime? LatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public Dictionary<string, long> Languages { get; set; } = new();
    public Dictionary<string, long>? SymbolKinds { get; set; }
    public List<string>? GraphSupportedLanguages { get; set; }
    public string? Version { get; set; }
    /// <summary>
    /// One-line human-readable summary for quick orientation.
    /// クイックオリエンテーション用の1行サマリー。
    /// </summary>
    public string? Summary { get; set; }
    /// <summary>
    /// True when the index exposes the full reference / validation tables. False signals a
    /// degraded read (legacy or read-only DB where TryMigrateForRead could not create
    /// symbol_references / file_issues), so a zero reference or issue count must not be
    /// trusted as a real "no callers" or "clean" signal.
    /// インデックスに参照／検証テーブルが揃っているかの信頼シグナル。false の場合、references
    /// や issues の 0 件は「本当に 0 件」なのか「テーブルが無いから 0 件」なのか区別できない。
    /// </summary>
    public bool GraphTableAvailable { get; set; } = true;
    public bool IssuesTableAvailable { get; set; } = true;
    /// <summary>
    /// True when every row in symbols / symbol_references has its name_folded column
    /// populated AND the FoldReadyFlag bit is set on the DB. False means `--exact` still
    /// falls back to ASCII `COLLATE NOCASE` (non-ASCII casing pairs like Ä/ä won't match).
    /// AI clients should prefer `cdidx backfill-fold` to upgrade an older DB without
    /// reparsing files; `cdidx index . --rebuild` remains the full-rescan fallback.
    /// true のとき --exact は Unicode fold 経路、false のとき ASCII NOCASE fallback。
    /// </summary>
    public bool FoldReady { get; set; }
}

public class RepoMapResult
{
    public int FileCount { get; set; }
    public long TotalLines { get; set; }
    public long TotalSymbols { get; set; }
    public long TotalReferences { get; set; }
    public DateTime? IndexedAt { get; set; }
    public DateTime? LatestModified { get; set; }
    public DateTime? WorkspaceIndexedAt { get; set; }
    public DateTime? WorkspaceLatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public List<RepoLanguageResult> Languages { get; set; } = [];
    public List<RepoModuleResult> Modules { get; set; } = [];
    public List<RepoFileSummaryResult> TopFiles { get; set; } = [];
    public List<RepoFileSummaryResult> LargestFiles { get; set; } = [];
    public List<RepoFileSummaryResult> SymbolRichFiles { get; set; } = [];
    public List<RepoFileSummaryResult> ReferenceRichFiles { get; set; } = [];
    public List<RepoEntrypointResult> Entrypoints { get; set; } = [];
    /// <summary>
    /// False when reference-derived aggregates (TotalReferences, per-file / per-module /
    /// per-language reference counts, ReferenceRichFiles) were synthesized as 0 because the
    /// graph table was missing (legacy / read-only DB). Callers must not rank or prioritize
    /// based on reference counts when this is false — the repo may actually be reference-rich.
    /// 参照系集計が欠損テーブルによりゼロ合成されている場合 false。ランキングに使わないこと。
    /// </summary>
    public bool GraphTableAvailable { get; set; } = true;
}

public class RepoLanguageResult
{
    public string Lang { get; set; } = string.Empty;
    public int Files { get; set; }
    public long Lines { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
}

public class RepoModuleResult
{
    public string Module { get; set; } = string.Empty;
    public int Files { get; set; }
    public long Lines { get; set; }
    public long Symbols { get; set; }
    public long References { get; set; }
}

public class RepoFileSummaryResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int Lines { get; set; }
    public long Size { get; set; }
    public int SymbolCount { get; set; }
    public int ReferenceCount { get; set; }
    public long? Score { get; set; }
}

public class RepoEntrypointResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Score { get; set; }
}

public class SymbolAnalysisResult
{
    public string Query { get; set; } = string.Empty;
    public FileResult? File { get; set; }
    public DateTime? WorkspaceIndexedAt { get; set; }
    public DateTime? WorkspaceLatestModified { get; set; }
    public string? ProjectRoot { get; set; }
    public string? GitHead { get; set; }
    public bool? GitIsDirty { get; set; }
    public string? GraphLanguage { get; set; }
    public bool? GraphSupported { get; set; }
    public string? GraphSupportReason { get; set; }
    public List<DefinitionResult> Definitions { get; set; } = [];
    public List<SymbolResult> NearbySymbols { get; set; } = [];
    public List<ReferenceResult> References { get; set; } = [];
    public List<CallerResult> Callers { get; set; } = [];
    public List<CalleeResult> Callees { get; set; } = [];
    /// <summary>
    /// False when the index does not contain the reference table (legacy / read-only DB),
    /// meaning empty References / Callers / Callees are degraded — not a true "no callers".
    /// インデックスに参照テーブルが無いと true / false で区別可能。空が本物かどうか見極める。
    /// </summary>
    public bool GraphTableAvailable { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExactZeroHintResult? ExactZeroHint { get; set; }
    /// <summary>
    /// True when every active `--exact` sub-query in the bundle can use its supporting indexes.
    /// False means the bundled result still returns correct hits, but at least one exact
    /// sub-query degraded to a slower legacy fallback path.
    /// bundle 内の `--exact` sub-query がすべて対応 index を使えるか。false でも結果は正しい。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ExactIndexAvailable { get; set; }
    [JsonIgnore]
    public bool? ExactHasMissingIndex { get; set; }
    [JsonIgnore]
    public bool? ExactHasMissingTable { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DegradedReason { get; set; }
}

/// <summary>
/// Structured symbol outline for a single file.
/// 1ファイルの構造化シンボルアウトライン。
/// </summary>
public class OutlineResult
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public int TotalLines { get; set; }
    public int SymbolCount { get; set; }
    public List<OutlineSymbol> Symbols { get; set; } = [];
}

public class OutlineSymbol
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Line { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int? BodyStartLine { get; set; }
    public int? BodyEndLine { get; set; }
    public string? Signature { get; set; }
    public string? ContainerKind { get; set; }
    public string? ContainerName { get; set; }
    public string? Visibility { get; set; }
    public string? ReturnType { get; set; }
}

internal sealed class RepoFileStat
{
    public string Path { get; set; } = string.Empty;
    public string? Lang { get; set; }
    public long Size { get; set; }
    public int Lines { get; set; }
    public int SymbolCount { get; set; }
    public int ReferenceCount { get; set; }
    public string? Checksum { get; set; }
    public DateTime? Modified { get; set; }
    public DateTime? IndexedAt { get; set; }
}

public class FileDependencyResult
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public int ReferenceCount { get; set; }
    public string Symbols { get; set; } = string.Empty;
}
