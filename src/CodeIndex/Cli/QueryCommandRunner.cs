using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Cli;

/// <summary>
/// Runs query-style CLI commands.
/// クエリ系CLIコマンドを実行する。
/// </summary>
public static class QueryCommandRunner
{
    // Cap OR-joined `symbols` names well below SQLite's 1000 expression-tree depth so oversized
    // batches fail fast with a clear usage error instead of a confusing SQLite exception.
    // OR 結合の `symbols` 名は SQLite の式木深さ上限 1000 を十分下回る値で頭打ちにし、
    // 大量バッチを SQLite 例外ではなく明確な usage error で早期に弾く。
    internal const int MaxSymbolQueryNames = 256;
    internal const int ExactZeroHintProbeLimit = 1;
    internal const int ExactZeroHintSampleLimit = 5;
    private static readonly HashSet<string> ValueTakingOptions =
    [
        "--db",
        "--limit",
        "--top",
        "--lang",
        "--kind",
        "--since",
        "--start",
        "--end",
        "--before",
        "--after",
        "--name",
        "--snippet-lines",
        "--path",
        "--exclude-path",
        "--depth",
    ];
    private static readonly HashSet<string> FlagOnlyOptions =
    [
        "--json",
        "--no-json",
        "--fts",
        "--body",
        "--count",
        "--no-dedup",
        "--exact",
        "--reverse",
        "--help",
        "-h",
        "--version",
        "-V",
    ];
    public static int RunSearch(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "search"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("search", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--path", "--exclude-path", "--exclude-tests", "--snippet-lines", "--fts", "--count", "--since", "--no-dedup", "--exact"]))
            return CommandExitCodes.UsageError;
        if (options.Query == null)
        {
            WriteUsageError(
                "search requires a query argument",
                GetUsageLineOrThrow("search"),
                "Add the text you want to search for after the command, for example: `cdidx search authenticate`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("search", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.Search(options.Query, options.Limit, options.Lang, options.RawFts, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, !options.NoDedup, options.Since, options.Exact);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    Console.WriteLine(options.Json
                        ? BuildJsonZeroResultPayload(reader, jsonOptions, includeFiles: true).ToJsonString(jsonOptions)
                        : "0");
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "results").ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No results found.");
                    WriteZeroResultHints(options, reader);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                Console.WriteLine(options.Json
                    ? JsonSerializer.Serialize(new { count = results.Count, files = fc }, jsonOptions)
                    : $"{results.Count}");
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                    Console.WriteLine(JsonSerializer.Serialize(SearchSnippetFormatter.ToCompactResult(r, options.Query, options.SnippetLines, options.Exact), jsonOptions));
            }
            else
            {
                foreach (var r in results)
                {
                    Console.WriteLine($"{r.Path}:{r.StartLine}-{r.EndLine}");
                    var snippetLines = SearchSnippetFormatter.Format(r.Content, options.Query, options.SnippetLines);
                    foreach (var line in snippetLines)
                        Console.WriteLine($"  {line}");
                    Console.WriteLine();
                }
                var fileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} results in {fileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunDefinition(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "definition"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("definition", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--kind", "--body", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--exact"]))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "definition requires a symbol query argument",
                GetUsageLineOrThrow("definition"),
                "Add the symbol name after the command, for example: `cdidx definition QueryCommandRunner`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("definition", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.GetDefinitions(options.Query, options.Limit, options.Kind, options.Lang, options.IncludeBody, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, options.Exact);
            var exactSignal = reader.GetDefinitionExactQuerySignal();
            var exactZeroHint = BuildExactZeroHint(
                options.Exact,
                () => reader.CountSearchSymbols(options.Query, ExactZeroHintProbeLimit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false) > 0,
                () => reader.CountSearchSymbols(options.Query, options.Limit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                () => reader.SearchSymbols(options.Query, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                r => r.Name);
            WriteExactSymbolWarningIfNeeded(options.Exact, options.Json, exactSignal);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    Console.WriteLine(options.Json
                        ? BuildJsonZeroResultPayload(reader, jsonOptions, includeFiles: true, exactZeroHint: exactZeroHint, exactSignal: options.Exact ? exactSignal : null).ToJsonString(jsonOptions)
                        : "0");
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "definitions", exactZeroHint: exactZeroHint, exactSignal: options.Exact ? exactSignal : null).ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No definitions found.");
                    WriteExactZeroHint(exactZeroHint);
                    WriteKindHint(options.Kind, reader);
                    WriteZeroResultHints(options, reader, "Try 'search' for full-text matches instead of symbol lookup.");
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                if (options.Json)
                {
                    var payload = new JsonObject
                    {
                        ["count"] = results.Count,
                        ["files"] = fc,
                    };
                    if (options.Exact)
                        AddExactJsonFields(payload, exactSignal);
                    Console.WriteLine(payload.ToJsonString(jsonOptions));
                }
                else
                {
                    Console.WriteLine($"{results.Count}");
                }
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                {
                    if (options.Exact)
                        WriteJsonResultWithExactSignal(r, exactSignal, jsonOptions);
                    else
                        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
                }
            }
            else
            {
                foreach (var r in results)
                {
                    var container = r.ContainerName != null ? $" in {r.ContainerName}" : "";
                    Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.Path}:{r.StartLine}-{r.EndLine}{container}");
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
                var defFileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} definitions in {defFileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunReferences(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "references"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("references", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--kind", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--exact"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "references"))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "references requires a symbol query argument",
                GetUsageLineOrThrow("references"),
                "Add the symbol name you want to trace, for example: `cdidx references QueryCommandRunner`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("references", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.SearchReferences(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Exact);
            var exactSignal = reader.GetReferencesExactQuerySignal();
            var exactZeroHint = BuildExactZeroHint(
                options.Exact && reader._hasReferencesTable,
                () => reader.CountSearchReferences(options.Query, ExactZeroHintProbeLimit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false) > 0,
                () => reader.CountSearchReferences(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                () => reader.SearchReferences(options.Query, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                r => r.SymbolName);
            WriteExactGraphWarningIfNeeded(options.Exact, options.Json, exactSignal);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    WriteGraphCountResult(reader, 0, 0, options, jsonOptions, reader._hasReferencesTable, exactSignal, exactZeroHint);
                else if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "references", json: true, graphAvailable: false, jsonOptions, options.Exact ? exactSignal : (ExactQuerySignal?)null);
                else if (options.Json)
                    WriteGraphZeroJsonResult(reader, "references", jsonOptions, reader._hasReferencesTable,
                        options.Exact ? exactSignal : (ExactQuerySignal?)null,
                        exactZeroHint);
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No references found.");
                    WriteExactZeroHint(exactZeroHint);
                    WriteGraphSupportHint(options.Lang);
                    WriteLangHint(options.Lang, reader);
                    WriteDegradedGraphZeroResult(reader, "references", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                WriteGraphCountResult(reader, results.Count, fc, options, jsonOptions, reader._hasReferencesTable, exactSignal);
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                {
                    if (options.Exact)
                        WriteGraphJsonResult(r, exactSignal, jsonOptions);
                    else
                        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
                }
            }
            else
            {
                foreach (var r in results)
                {
                    var owner = r.ContainerName != null ? $"  in {r.ContainerName}" : "";
                    Console.WriteLine($"{r.ReferenceKind,-12} {r.SymbolName,-32} {r.Path}:{r.Line}:{r.Column}{owner}");
                    Console.WriteLine($"  {r.Context}");
                }
                var refFileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} references in {refFileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunCallers(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "callers"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("callers", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--kind", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--exact"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "callers"))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "callers requires a symbol query argument",
                GetUsageLineOrThrow("callers"),
                "Add the callee symbol name after the command, for example: `cdidx callers QueryCommandRunner`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("callers", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.GetCallers(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Exact);
            var exactSignal = reader.GetCallersExactQuerySignal();
            var exactZeroHint = BuildExactZeroHint(
                options.Exact && reader._hasReferencesTable,
                () => reader.CountCallers(options.Query, ExactZeroHintProbeLimit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false) > 0,
                () => reader.CountCallers(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                () => reader.GetCallers(options.Query, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                r => r.CalleeName);
            WriteExactGraphWarningIfNeeded(options.Exact, options.Json, exactSignal);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    WriteGraphCountResult(reader, 0, 0, options, jsonOptions, reader._hasReferencesTable, exactSignal, exactZeroHint);
                else if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "callers", json: true, graphAvailable: false, jsonOptions, options.Exact ? exactSignal : (ExactQuerySignal?)null);
                else if (options.Json)
                    WriteGraphZeroJsonResult(reader, "callers", jsonOptions, reader._hasReferencesTable,
                        options.Exact ? exactSignal : (ExactQuerySignal?)null,
                        exactZeroHint);
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No callers found.");
                    WriteExactZeroHint(exactZeroHint);
                    WriteGraphSupportHint(options.Lang);
                    WriteLangHint(options.Lang, reader);
                    WriteDegradedGraphZeroResult(reader, "callers", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                WriteGraphCountResult(reader, results.Count, fc, options, jsonOptions, reader._hasReferencesTable, exactSignal);
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                {
                    if (options.Exact)
                        WriteGraphJsonResult(r, exactSignal, jsonOptions);
                    else
                        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
                }
            }
            else
            {
                foreach (var r in results)
                    Console.WriteLine($"{r.CallerKind ?? "?",-10} {r.CallerName ?? "<top-level>",-32} {r.Path}:{r.FirstLine}  -> {r.CalleeName} ({r.ReferenceCount} refs)");
                var callerFileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} callers in {callerFileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunCallees(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "callees"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("callees", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--kind", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--exact"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "callees"))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "callees requires a caller query argument",
                GetUsageLineOrThrow("callees"),
                "Add the caller symbol name after the command, for example: `cdidx callees RunIndex`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("callees", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.GetCallees(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Exact);
            var exactSignal = reader.GetCalleesExactQuerySignal();
            var exactZeroHint = BuildExactZeroHint(
                options.Exact && reader._hasReferencesTable,
                () => reader.CountCallees(options.Query, ExactZeroHintProbeLimit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false) > 0,
                () => reader.CountCallees(options.Query, options.Limit, options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                () => reader.GetCallees(options.Query, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Lang, options.Kind, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, exact: false),
                r => r.CallerName);
            WriteExactGraphWarningIfNeeded(options.Exact, options.Json, exactSignal);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    WriteGraphCountResult(reader, 0, 0, options, jsonOptions, reader._hasReferencesTable, exactSignal, exactZeroHint);
                else if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "callees", json: true, graphAvailable: false, jsonOptions, options.Exact ? exactSignal : (ExactQuerySignal?)null);
                else if (options.Json)
                    WriteGraphZeroJsonResult(reader, "callees", jsonOptions, reader._hasReferencesTable,
                        options.Exact ? exactSignal : (ExactQuerySignal?)null,
                        exactZeroHint);
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No callees found.");
                    WriteExactZeroHint(exactZeroHint);
                    WriteGraphSupportHint(options.Lang);
                    WriteLangHint(options.Lang, reader);
                    WriteDegradedGraphZeroResult(reader, "callees", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                WriteGraphCountResult(reader, results.Count, fc, options, jsonOptions, reader._hasReferencesTable, exactSignal);
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                {
                    if (options.Exact)
                        WriteGraphJsonResult(r, exactSignal, jsonOptions);
                    else
                        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
                }
            }
            else
            {
                foreach (var r in results)
                    Console.WriteLine($"{r.ReferenceKind,-12} {r.CalleeName,-32} {r.Path}:{r.FirstLine}  <- {r.CallerName ?? "<top-level>"} ({r.ReferenceCount} refs)");
                var calleeFileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} callees in {calleeFileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    /// <summary>
    /// Build the OR-joined name list for `symbols`: first positional + extra positionals + --name values.
    /// Pipe characters are treated as literal name characters so operator symbols like `operator |` remain searchable.
    /// Multi-name queries must use repeated positional args or `--name` flags.
    /// `symbols` コマンド用の名前リストを組み立て（最初の positional + 追加 positional + --name）。
    /// `|` は名前文字として扱うので `operator |` などの演算子シンボルも検索可能。複数名指定は繰り返し positional か `--name` で行う。
    /// </summary>
    internal static (List<string>? Queries, bool HadExplicitInput) BuildSymbolQueryList(QueryCommandOptions options)
    {
        var raw = new List<string>();
        if (options.Query != null)
            raw.Add(options.Query);
        raw.AddRange(options.ExtraNames);
        var hadExplicitInput = raw.Count > 0;
        if (!hadExplicitInput)
            return (null, false);
        var deduped = raw.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        return (deduped.Count == 0 ? null : deduped, hadExplicitInput);
    }

    public static int RunSymbols(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "symbols"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("symbols", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--kind", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--exact", "--name"]))
            return CommandExitCodes.UsageError;
        var (symbolQueries, hadExplicitInput) = BuildSymbolQueryList(options);
        if (hadExplicitInput && symbolQueries == null)
        {
            // Fail closed: an explicit name/query was provided but normalized to empty (e.g. "|",
            // --name ""). Returning null here would broaden into an unfiltered symbol dump. /
            // 明示入力が正規化で空になった場合、null のまま検索すると全件検索に化けるので必ず拒否する。
            Console.Error.WriteLine("Error: symbol name list is empty after normalization. Check for empty --name values or bare '|' separators. / シンボル名リストが正規化の結果空です。--name の空値や単独の '|' を確認してください。");
            return CommandExitCodes.UsageError;
        }
        if (symbolQueries != null && symbolQueries.Count > MaxSymbolQueryNames)
        {
            Console.Error.WriteLine($"Error: too many symbol names ({symbolQueries.Count}); maximum is {MaxSymbolQueryNames}. Split the request into smaller batches. / シンボル名が多すぎます（{symbolQueries.Count}件、上限は {MaxSymbolQueryNames} 件）。分割してください。");
            return CommandExitCodes.UsageError;
        }

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.SearchSymbols(symbolQueries, options.Limit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, options.Exact);
            var hasExactPredicate = options.Exact && symbolQueries is { Count: > 0 };
            var exactSignal = reader.GetSymbolsExactQuerySignal();
            var multiNameExactHint = symbolQueries != null && symbolQueries.Count > 1;
            var exactZeroHint = multiNameExactHint
                ? BuildExactZeroHint(
                    options.Exact,
                    () => reader.AnySearchSymbols(symbolQueries, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                    () => reader.SearchSymbols(symbolQueries, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                    r => r.Name)
                : BuildExactZeroHint(
                    options.Exact && symbolQueries != null && symbolQueries.Count > 0,
                    () => reader.CountSearchSymbols(symbolQueries, ExactZeroHintProbeLimit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false) > 0,
                    () => reader.CountSearchSymbols(symbolQueries, options.Limit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                    () => reader.SearchSymbols(symbolQueries, Math.Min(options.Limit, ExactZeroHintSampleLimit), options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since, exact: false),
                    r => r.Name);
            WriteExactSymbolWarningIfNeeded(hasExactPredicate, options.Json, exactSignal);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    Console.WriteLine(options.Json
                        ? BuildJsonZeroResultPayload(reader, jsonOptions, includeFiles: true, exactZeroHint: exactZeroHint, exactSignal: hasExactPredicate ? exactSignal : null).ToJsonString(jsonOptions)
                        : "0");
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "symbols", exactZeroHint: exactZeroHint, exactSignal: hasExactPredicate ? exactSignal : null).ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No symbols found.");
                    WriteExactZeroHint(exactZeroHint);
                    WriteKindHint(options.Kind, reader);
                    WriteZeroResultHints(options, reader);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                if (options.Json)
                {
                    var payload = new JsonObject
                    {
                        ["count"] = results.Count,
                        ["files"] = fc,
                    };
                    if (hasExactPredicate)
                        AddExactJsonFields(payload, exactSignal);
                    Console.WriteLine(payload.ToJsonString(jsonOptions));
                }
                else
                {
                    Console.WriteLine($"{results.Count}");
                }
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                foreach (var r in results)
                {
                    if (hasExactPredicate)
                        WriteJsonResultWithExactSignal(r, exactSignal, jsonOptions);
                    else
                        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
                }
            }
            else
            {
                foreach (var r in results)
                {
                    var lineRange = r.EndLine > r.StartLine
                        ? $"{r.StartLine}-{r.EndLine}"
                        : r.StartLine.ToString();
                    Console.WriteLine($"{ConsoleUi.ColorizeKind(r.Kind, 10)} {r.Name,-40} {r.Path}:{lineRange}");
                }
                var symFileCount = results.Select(r => r.Path).Distinct().Count();
                Console.Error.WriteLine($"({results.Count} symbols in {symFileCount} files)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunFiles(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "files"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("files", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--count", "--path", "--exclude-path", "--exclude-tests", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedExtraPositionals("files", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.ListFiles(options.Query, options.Limit, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Since);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    Console.WriteLine(options.Json
                        ? BuildJsonZeroResultPayload(reader, jsonOptions).ToJsonString(jsonOptions)
                        : "0");
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "files").ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No files found.");
                    WriteLangHint(options.Lang, reader);
                    WriteZeroResultHints(options, reader);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                Console.WriteLine(options.Json
                    ? JsonSerializer.Serialize(new { count = results.Count }, jsonOptions)
                    : $"{results.Count}");
                return CommandExitCodes.Success;
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
        if (TryWriteParseError(options, "excerpt"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("excerpt", cmdArgs, ["--db", "--json", "--no-json", "--start", "--end", "--before", "--after", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "excerpt"))
            return CommandExitCodes.UsageError;
        if (options.Query == null)
        {
            WriteUsageError(
                "excerpt requires a path argument",
                GetUsageLineOrThrow("excerpt"),
                "Pass the indexed file path after `excerpt`, for example: `cdidx excerpt src/CodeIndex/Program.cs --start 20`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("excerpt", options))
            return CommandExitCodes.UsageError;

        if (options.StartLine == null)
        {
            WriteValidationError(
                "excerpt requires --start <line>",
                "Add a starting line number, for example: `cdidx excerpt src/CodeIndex/Program.cs --start 20`.");
            return CommandExitCodes.UsageError;
        }

        var endLine = options.EndLine ?? options.StartLine.Value;
        if (endLine < options.StartLine.Value)
        {
            WriteValidationError(
                $"--start ({options.StartLine.Value}) must be less than or equal to --end ({endLine}).",
                "Use `--start` less than or equal to `--end`, or omit `--end` to read a single line.");
            return CommandExitCodes.UsageError;
        }

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

    public static int RunMap(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "map"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("map", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--path", "--exclude-path", "--exclude-tests", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "map"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("map", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var map = reader.GetRepoMap(options.Limit, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests);
            WorkspaceMetadataEnricher.Enrich(map, options.DbPath);

            // Return not-found only when a narrowing filter is active and produces zero files.
            // Unfiltered empty indexes return success (valid state for health probes).
            // フィルタ指定時に該当0件なら未検出を返す。フィルタなしの空DBは正常（ヘルスチェック用途）。
            var hasFilter = options.PathPatterns.Count > 0 || options.ExcludePaths.Count > 0
                || options.ExcludeTests || options.Lang != null;
            if (map.FileCount == 0 && hasFilter)
            {
                if (options.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(map, jsonOptions));
                }
                else
                {
                    Console.Error.WriteLine("No files found matching the given filters.");
                }
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(map, jsonOptions));
            }
            else
            {
                Console.WriteLine($"Files      : {map.FileCount:N0}");
                Console.WriteLine($"Lines      : {map.TotalLines:N0}");
                Console.WriteLine($"Symbols    : {map.TotalSymbols:N0}");
                Console.WriteLine($"References : {map.TotalReferences:N0}");
                if (map.IndexedAt != null)
                    Console.WriteLine($"Scope Indexed At     : {map.IndexedAt:O}");
                if (map.LatestModified != null)
                    Console.WriteLine($"Scope Modified       : {map.LatestModified:O}");
                if (map.WorkspaceIndexedAt != null)
                    Console.WriteLine($"Workspace Indexed At : {map.WorkspaceIndexedAt:O}");
                if (map.WorkspaceLatestModified != null)
                    Console.WriteLine($"Workspace Modified   : {map.WorkspaceLatestModified:O}");
                if (map.GitHead != null)
                    Console.WriteLine($"Git HEAD   : {map.GitHead}");
                if (map.GitIsDirty != null)
                    Console.WriteLine($"Git Dirty  : {map.GitIsDirty}");
                if (!map.GraphTableAvailable)
                    Console.WriteLine("WARN       : symbol_references table missing — reference counts are synthesized 0. Do not use ReferenceRich / reference-derived ranking as authoritative.");
                WriteRepoMapSection("Languages", map.Languages.Select(item => $"{item.Lang,-12} {item.Files,4} files  {item.Symbols,5} syms  {item.References,5} refs"));
                WriteRepoMapSection("Modules", map.Modules.Select(item => $"{item.Module,-24} {item.Files,4} files  {item.Symbols,5} syms  {item.References,5} refs"));
                WriteRepoMapSection("Top files", map.TopFiles.Select(item => $"{item.Path}  [score {item.Score}, {item.SymbolCount} syms, {item.ReferenceCount} refs]"));
                WriteRepoMapSection("Largest files", map.LargestFiles.Select(item => $"{item.Path}  [{item.Lines} lines, {item.Size} bytes]"));
                WriteRepoMapSection("Symbol-rich files", map.SymbolRichFiles.Select(item => $"{item.Path}  [{item.SymbolCount} syms, {item.ReferenceCount} refs]"));
                WriteRepoMapSection("Reference-rich files", map.ReferenceRichFiles.Select(item => $"{item.Path}  [{item.ReferenceCount} refs, {item.SymbolCount} syms]"));
                WriteRepoMapSection("Entrypoints", map.Entrypoints.Select(item => $"{item.Kind,-10} {item.Name,-24} {item.Path}:{item.Line}  [score {item.Score}]"));
            }

            return CommandExitCodes.Success;
        });
    }

    public static int RunInspect(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "inspect"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("inspect", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--path", "--exclude-path", "--exclude-tests", "--since", "--body", "--exact"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "inspect"))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "inspect requires a symbol query argument",
                GetUsageLineOrThrow("inspect"),
                "Add the symbol you want to inspect, for example: `cdidx inspect QueryCommandRunner`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("inspect", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var analysis = reader.AnalyzeSymbol(options.Query, options.Limit, options.Lang, options.IncludeBody, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, options.Exact);
            var exactSignal = options.Exact && analysis.ExactIndexAvailable.HasValue
                ? new ExactQuerySignal(
                    analysis.ExactIndexAvailable.Value,
                    analysis.ExactHasMissingIndex ?? false,
                    analysis.ExactHasMissingTable ?? false,
                    analysis.DegradedReason)
                : (ExactQuerySignal?)null;
            WorkspaceMetadataEnricher.Enrich(analysis, options.DbPath);
            if (exactSignal.HasValue)
                WriteExactBundleWarningIfNeeded(options.Exact, options.Json, exactSignal.Value);
            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(analysis, jsonOptions));
            }
            else
            {
                Console.WriteLine($"Query: {analysis.Query}");
                if (analysis.File != null)
                    Console.WriteLine($"File : {analysis.File.Path} ({analysis.File.Lang ?? "?"}, {analysis.File.Lines} lines)");
                if (analysis.WorkspaceIndexedAt != null)
                    Console.WriteLine($"Workspace Indexed At : {analysis.WorkspaceIndexedAt:O}");
                if (analysis.WorkspaceLatestModified != null)
                    Console.WriteLine($"Workspace Modified   : {analysis.WorkspaceLatestModified:O}");
                if (analysis.GitHead != null)
                    Console.WriteLine($"Git HEAD             : {analysis.GitHead}");
                if (analysis.GitIsDirty != null)
                    Console.WriteLine($"Git Dirty            : {analysis.GitIsDirty}");
                if (analysis.GraphLanguage != null)
                    Console.WriteLine($"Graph Language       : {analysis.GraphLanguage}");
                if (analysis.GraphSupported != null)
                    Console.WriteLine($"Graph Supported      : {analysis.GraphSupported}");
                if (analysis.GraphSupportReason != null)
                    Console.WriteLine($"Graph Note           : {analysis.GraphSupportReason}");
                if (!analysis.GraphTableAvailable)
                    Console.WriteLine("Graph Table          : MISSING — empty References/Callers/Callees are degraded, NOT real zero-hit results.");
                if (exactSignal is ExactQuerySignal signal && signal.HasMissingIndex && signal.DegradedReason != null)
                    Console.WriteLine($"Exact Index          : DEGRADED — {signal.DegradedReason}. Results are correct but may be slow.");
                WriteExactZeroHint(analysis.ExactZeroHint);
                WriteRepoMapSection("Definitions", analysis.Definitions.Select(item => $"{item.Kind,-10} {item.Name,-24} {item.Path}:{item.StartLine}-{item.EndLine}"));
                WriteRepoMapSection("Nearby symbols", analysis.NearbySymbols.Select(item => $"{item.Kind,-10} {item.Name,-24} {item.Path}:{item.StartLine}-{item.EndLine}"));
                WriteRepoMapSection("References", analysis.References.Select(item => $"{item.Path}:{item.Line}:{item.Column}  {item.Context}"));
                WriteRepoMapSection("Callers", analysis.Callers.Select(item => $"{item.CallerName ?? "<top-level>"} -> {item.CalleeName}  ({item.ReferenceCount} refs)"));
                WriteRepoMapSection("Callees", analysis.Callees.Select(item => $"{item.CallerName ?? "<top-level>"} -> {item.CalleeName}  ({item.ReferenceCount} refs)"));
            }

            return CommandExitCodes.Success;
        });
    }

    public static int RunOutline(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        if (cmdArgs.Length == 0 || cmdArgs[0].StartsWith('-'))
        {
            WriteUsageError(
                "outline requires a file path.",
                GetUsageLineOrThrow("outline"),
                "Pass the indexed file path, for example: `cdidx outline src/CodeIndex/Program.cs`.");
            return CommandExitCodes.UsageError;
        }

        var filePath = cmdArgs[0].Replace('\\', '/');
        var options = ParseArgs(cmdArgs[1..], jsonDefault: false);
        if (TryWriteParseError(options, "outline"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("outline", cmdArgs[1..], ["--db", "--json", "--no-json", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "outline"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("outline", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var outline = reader.GetOutline(filePath);
            if (outline == null)
            {
                if (options.Json)
                    Console.WriteLine(JsonSerializer.Serialize(new { path = filePath, error = "file not found in index" }, jsonOptions));
                else
                    Console.Error.WriteLine($"Error: '{filePath}' not found in index.");
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(outline, jsonOptions));
            }
            else
            {
                Console.WriteLine($"# {outline.Path}  ({outline.Lang ?? "unknown"}, {outline.TotalLines} lines, {outline.SymbolCount} symbols)");
                Console.WriteLine();
                foreach (var sym in outline.Symbols)
                {
                    // Indent nested symbols under their container / コンテナ内のシンボルをインデント
                    var indent = sym.ContainerName != null ? "    " : "";
                    var ret = sym.ReturnType != null ? $": {sym.ReturnType} " : "";
                    var sig = sym.Signature ?? $"{sym.Kind} {sym.Name}";
                    // Avoid duplicating visibility when signature already contains it
                    // シグネチャに既に visibility が含まれている場合は重複を避ける
                    var vis = sym.Visibility != null && !sig.TrimStart().StartsWith(sym.Visibility, StringComparison.Ordinal)
                        ? $"{sym.Visibility} "
                        : "";
                    Console.WriteLine($"  {sym.Line,5}  {indent}{vis}{sig} {ret}");
                }
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunStatus(string[] cmdArgs, JsonSerializerOptions jsonOptions, string? appVersion = null)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "status"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("status", cmdArgs, ["--db", "--json", "--no-json", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "status"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("status", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var status = reader.GetStatus();
            WorkspaceMetadataEnricher.Enrich(status, options.DbPath);
            // Attach runtime metadata / ランタイムメタデータを付加
            status.SymbolKinds = reader.GetSymbolKindCounts();
            status.GraphSupportedLanguages = ReferenceExtractor.GetSupportedLanguages().OrderBy(l => l).ToList();
            if (appVersion != null)
                status.Version = appVersion;

            // Build one-line summary for AI orientation / AI向けの1行サマリーを構築
            var topLangs = status.Languages.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key);
            var freshness = status.IndexedAt.HasValue
                ? (DateTime.UtcNow - status.IndexedAt.Value).TotalMinutes < 5 ? "fresh" : "stale"
                : "unknown";
            var dirty = status.GitIsDirty == true ? ", dirty" : "";
            var degraded = (!status.GraphTableAvailable || !status.IssuesTableAvailable) ? ", DEGRADED" : "";
            status.Summary = $"{status.Files} files, {status.Symbols} symbols, {status.References} refs across {status.Languages.Count} languages ({string.Join(", ", topLangs)}); index {freshness}{dirty}{degraded}";

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(status, jsonOptions));
            }
            else
            {
                if (status.Summary != null)
                    Console.WriteLine(status.Summary);
                Console.WriteLine();
                if (status.Version != null)
                    Console.WriteLine($"Version : cdidx v{status.Version}");
                Console.WriteLine($"Files   : {status.Files:N0}");
                Console.WriteLine($"Chunks  : {status.Chunks:N0}");
                Console.WriteLine($"Symbols : {status.Symbols:N0}");
                Console.WriteLine($"Refs    : {status.References:N0}");
                if (status.IndexedAt != null)
                    Console.WriteLine($"Indexed : {status.IndexedAt:O}");
                if (status.LatestModified != null)
                    Console.WriteLine($"Source  : {status.LatestModified:O}");
                if (status.GitHead != null)
                    Console.WriteLine($"Git HEAD: {status.GitHead}");
                if (status.GitIsDirty != null)
                    Console.WriteLine($"Git Dirty: {status.GitIsDirty}");
                if (status.Languages.Count > 0)
                {
                    Console.WriteLine("Languages:");
                    foreach (var (lang, count) in status.Languages)
                        Console.WriteLine($"  {lang,-12} {count,6}");
                }
                if (status.SymbolKinds is { Count: > 0 })
                {
                    Console.WriteLine("Kinds:");
                    foreach (var (kind, count) in status.SymbolKinds)
                        Console.WriteLine($"  {kind,-12} {count,6}");
                }
                if (status.GraphSupportedLanguages is { Count: > 0 })
                    Console.WriteLine($"Graph   : {status.GraphSupportedLanguages.Count} languages ({string.Join(", ", status.GraphSupportedLanguages)})");
                if (!status.GraphTableAvailable)
                    Console.WriteLine("WARN    : symbol_references table missing — reference / caller / callee / unused counts are degraded to 0.");
                if (!status.IssuesTableAvailable)
                    Console.WriteLine("WARN    : file_issues table missing — validate output is degraded to empty.");
                // #86: tell the user when `--exact` is running on the ASCII NOCASE fallback.
                // #86: --exact が ASCII NOCASE fallback で動いているときは明示する。
                if (!status.FoldReady)
                    Console.WriteLine("WARN    : --exact falls back to ASCII COLLATE NOCASE. Non-ASCII casing (e.g. Ä/ä) won't match. Run `cdidx backfill-fold` to upgrade without reparsing files, or `cdidx index . --rebuild` for a full rebuild.");
                var totalLangs = FileIndexer.GetLanguageExtensions().Values.Distinct().Count();
                var symbolLangs = SymbolExtractor.GetSupportedLanguages().Count;
                Console.WriteLine($"Support : {totalLangs} detected, {symbolLangs} with symbols, {status.GraphSupportedLanguages?.Count ?? 0} with graph");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunImpact(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "impact"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("impact", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--count", "--path", "--exclude-path", "--exclude-tests", "--since", "--depth"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "impact"))
            return CommandExitCodes.UsageError;
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            WriteUsageError(
                "impact requires a symbol query argument",
                GetUsageLineOrThrow("impact"),
                "Add the symbol whose callers you want to inspect, for example: `cdidx impact QueryCommandRunner`.");
            return CommandExitCodes.UsageError;
        }
        if (TryWriteUnexpectedExtraPositionals("impact", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var maxDepth = options.ContextAfter > 0 ? options.ContextAfter : 5; // --depth is parsed into ContextAfter
            var analysis = reader.AnalyzeImpact(options.Query, maxDepth, options.Limit, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests);
            var confirmedCount = analysis.Callers.Count;
            var confirmedFileCount = analysis.Callers.Select(r => r.Path).Distinct().Count();
            var hintCount = analysis.FileImpacts.Count;
            var hintFileCount = analysis.FileImpacts.Select(r => r.SourcePath).Distinct().Count();
            var hasHeuristicHints = analysis.ImpactMode == "file_dependency_hints";
            var visibleCount = hasHeuristicHints ? hintCount : confirmedCount;
            var visibleFileCount = hasHeuristicHints ? hintFileCount : confirmedFileCount;

            if (confirmedCount == 0 && !hasHeuristicHints)
            {
                if (options.CountOnly)
                {
                    if (options.Json)
                    {
                        var payload = new JsonObject
                        {
                            ["query"] = options.Query,
                            ["resolved_name"] = analysis.ResolvedName,
                            ["count"] = 0,
                            ["file_count"] = 0,
                            ["confirmed_count"] = 0,
                            ["confirmed_file_count"] = 0,
                            ["impact_mode"] = analysis.ImpactMode,
                            ["heuristic"] = analysis.Heuristic,
                            ["hint_count"] = analysis.HintCount,
                            ["hint_file_count"] = 0,
                            ["definition_count"] = analysis.DefinitionCount,
                            ["definition_file_count"] = analysis.DefinitionFileCount,
                            ["has_multiple_definitions"] = analysis.HasMultipleDefinitions,
                            ["has_class_like_definitions"] = analysis.HasClassLikeDefinitions,
                            ["has_multiple_definition_files"] = analysis.HasMultipleDefinitionFiles,
                            ["graph_table_available"] = analysis.GraphTableAvailable,
                            ["degraded"] = !analysis.GraphTableAvailable,
                        };
                        if (analysis.ZeroResultReason != null)
                            payload["zero_result_reason"] = analysis.ZeroResultReason;
                        if (analysis.Suggestion != null)
                            payload["suggestion"] = analysis.Suggestion;
                        if (!analysis.GraphTableAvailable)
                            payload["note"] = "symbol_references table is missing in this index (legacy or read-only DB). Zero result is degraded, not authoritative.";
                        AddFreshnessHint(payload, reader);
                        Console.WriteLine(payload.ToJsonString(jsonOptions));
                    }
                    else
                    {
                        Console.WriteLine("0");
                        if (!analysis.GraphTableAvailable)
                            Console.Error.WriteLine("WARN: symbol_references table missing — this count result is degraded, not authoritative.");
                    }
                }
                else if (options.Json)
                {
                    var payload = BuildJsonZeroResultPayload(
                        reader,
                        jsonOptions,
                        resultsKey: "callers",
                        graphTableAvailable: analysis.GraphTableAvailable,
                        degraded: !analysis.GraphTableAvailable,
                        extraFields: zeroPayload =>
                        {
                            zeroPayload["query"] = options.Query;
                            zeroPayload["resolved_name"] = analysis.ResolvedName;
                            zeroPayload["file_count"] = 0;
                            zeroPayload["confirmed_count"] = 0;
                            zeroPayload["confirmed_file_count"] = 0;
                            zeroPayload["hint_count"] = 0;
                            zeroPayload["hint_file_count"] = 0;
                            zeroPayload["max_depth"] = maxDepth;
                            zeroPayload["actual_depth"] = 0;
                            zeroPayload["truncated"] = analysis.Truncated;
                            zeroPayload["impact_mode"] = analysis.ImpactMode;
                            zeroPayload["heuristic"] = analysis.Heuristic;
                            zeroPayload["file_impacts"] = new JsonArray();
                            zeroPayload["definition_count"] = analysis.DefinitionCount;
                            zeroPayload["definition_file_count"] = analysis.DefinitionFileCount;
                            zeroPayload["has_multiple_definitions"] = analysis.HasMultipleDefinitions;
                            zeroPayload["has_class_like_definitions"] = analysis.HasClassLikeDefinitions;
                            zeroPayload["has_multiple_definition_files"] = analysis.HasMultipleDefinitionFiles;
                            zeroPayload["definitions"] = JsonSerializer.SerializeToNode(analysis.Definitions, jsonOptions);
                            if (analysis.ZeroResultReason != null)
                                zeroPayload["zero_result_reason"] = analysis.ZeroResultReason;
                            if (analysis.Suggestion != null)
                                zeroPayload["suggestion"] = analysis.Suggestion;
                        });
                    if (!analysis.GraphTableAvailable)
                        payload["note"] = "symbol_references table is missing in this index (legacy or read-only DB). Zero result is degraded, not authoritative.";
                    Console.WriteLine(payload.ToJsonString(jsonOptions));
                }
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No impact found.");
                    WriteImpactResolutionHint(analysis);
                    WriteGraphSupportHint(options.Lang);
                    WriteDegradedGraphZeroResult(reader, "callers", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                Console.WriteLine(options.Json
                    ? JsonSerializer.Serialize(new
                    {
                        query = options.Query,
                        resolved_name = analysis.ResolvedName,
                        count = visibleCount,
                        file_count = visibleFileCount,
                        confirmed_count = confirmedCount,
                        confirmed_file_count = confirmedFileCount,
                        impact_mode = analysis.ImpactMode,
                        heuristic = analysis.Heuristic,
                        hint_count = hintCount,
                        hint_file_count = hintFileCount,
                        truncated = analysis.Truncated,
                    }, jsonOptions)
                    : $"{visibleCount}");
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    query = options.Query,
                    resolved_name = analysis.ResolvedName,
                    count = visibleCount,
                    file_count = visibleFileCount,
                    confirmed_count = confirmedCount,
                    confirmed_file_count = confirmedFileCount,
                    hint_count = hintCount,
                    hint_file_count = hintFileCount,
                    max_depth = maxDepth,
                    actual_depth = analysis.Callers.Count > 0 ? analysis.Callers.Max(r => r.Depth) : 0,
                    truncated = analysis.Truncated,
                    impact_mode = analysis.ImpactMode,
                    heuristic = analysis.Heuristic,
                    callers = analysis.Callers,
                    file_impacts = analysis.FileImpacts,
                    definition_count = analysis.DefinitionCount,
                    definition_file_count = analysis.DefinitionFileCount,
                    has_multiple_definitions = analysis.HasMultipleDefinitions,
                    has_class_like_definitions = analysis.HasClassLikeDefinitions,
                    has_multiple_definition_files = analysis.HasMultipleDefinitionFiles,
                    definitions = analysis.Definitions,
                    suggestion = analysis.Suggestion,
                }, jsonOptions));
            }
            else
            {
                if (hasHeuristicHints)
                {
                    Console.Error.WriteLine($"No symbol-level callers found for '{analysis.ResolvedName}'. Possible file-level dependents follow.");
                    WriteImpactResolutionHint(analysis);
                    Console.Error.WriteLine("WARN: these file-level dependents are heuristic only; the current graph does not record resolved target file/type for each call.");
                    if (analysis.Truncated)
                        Console.Error.WriteLine("WARN: heuristic file-level dependents were truncated by the current limit.");
                    foreach (var edge in analysis.FileImpacts)
                        Console.WriteLine($"  {edge.SourcePath,-40} -> {edge.TargetPath} ({edge.ReferenceCount} refs: {edge.Symbols})");
                }
                else
                {
                    var grouped = analysis.Callers.GroupBy(r => r.Depth).OrderBy(g => g.Key);
                    foreach (var group in grouped)
                    {
                        Console.Error.WriteLine($"--- Depth {group.Key} ---");
                        foreach (var r in group)
                        {
                            var indent = new string(' ', (r.Depth - 1) * 2);
                            Console.WriteLine($"  {indent}{r.CallerKind ?? "?",-10} {r.CallerName ?? "<top-level>",-32} {r.Path}:{r.FirstLine}  -> {r.CalleeName} ({r.ReferenceCount} refs)");
                        }
                    }
                }

                var truncNote = analysis.Truncated ? " [TRUNCATED]" : "";
                if (hasHeuristicHints)
                    Console.Error.WriteLine($"\n({hintCount} heuristic dependency hints across {hintFileCount} files{truncNote})");
                else
                    Console.Error.WriteLine($"\n({confirmedCount} callers across {confirmedFileCount} files, max depth {maxDepth}{truncNote})");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunDeps(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "deps"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("deps", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--lang", "--path", "--exclude-path", "--exclude-tests", "--since", "--reverse"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "deps"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("deps", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var reverse = cmdArgs.Any(a => a == "--reverse");
            var results = reader.GetFileDependencies(options.Limit, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests, reverse);
            if (results.Count == 0)
            {
                if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "edges", json: true, graphAvailable: false, jsonOptions);
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "edges", graphTableAvailable: true, degraded: false).ToJsonString(jsonOptions));
                else
                {
                    Console.Error.WriteLine("No file dependencies found.");
                    WriteDegradedGraphZeroResult(reader, "edges", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return CommandExitCodes.NotFound;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { count = results.Count, edges = results }, jsonOptions));
            }
            else
            {
                foreach (var r in results)
                {
                    var syms = r.Symbols.Length > 60 ? r.Symbols[..57] + "..." : r.Symbols;
                    Console.WriteLine($"{r.SourcePath,-45} -> {r.TargetPath,-45} ({r.ReferenceCount} refs: {syms})");
                }
                Console.Error.WriteLine($"({results.Count} dependency edges)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunHotspots(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "hotspots"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("hotspots", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--kind", "--lang", "--count", "--path", "--exclude-path", "--exclude-tests", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "hotspots"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("hotspots", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var results = reader.GetSymbolHotspots(options.Limit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    WriteGraphCountResult(reader, 0, 0, options, jsonOptions, reader._hasReferencesTable, new ExactQuerySignal(true, HasMissingIndex: false, HasMissingTable: false, null));
                else if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "hotspots", json: true, graphAvailable: false, jsonOptions);
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "hotspots", graphTableAvailable: true, degraded: false).ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No symbol hotspots found.");
                    WriteZeroResultHints(options, reader);
                    WriteKindHint(options.Kind, reader);
                    WriteLangHint(options.Lang, reader);
                    WriteDegradedGraphZeroResult(reader, "hotspots", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Symbol.Path).Distinct().Count();
                Console.WriteLine(options.Json
                    ? JsonSerializer.Serialize(new { count = results.Count, files = fc }, jsonOptions)
                    : $"{results.Count}");
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                var items = results.Select(r => new { name = r.Symbol.Name, kind = r.Symbol.Kind, path = r.Symbol.Path, line = r.Symbol.Line, reference_count = r.ReferenceCount, visibility = r.Symbol.Visibility, container = r.Symbol.ContainerName });
                Console.WriteLine(JsonSerializer.Serialize(new { count = results.Count, hotspots = items }, jsonOptions));
            }
            else
            {
                foreach (var (s, refCount) in results)
                {
                    var vis = s.Visibility != null ? $" [{s.Visibility}]" : "";
                    Console.WriteLine($"{refCount,5} refs  {ConsoleUi.ColorizeKind(s.Kind, 12)} {s.Name,-40} {s.Path}:{s.Line}{vis}");
                }
                Console.Error.WriteLine($"({results.Count} symbol hotspots)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunUnused(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "unused"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("unused", cmdArgs, ["--db", "--json", "--no-json", "--limit", "--top", "--kind", "--lang", "--count", "--path", "--exclude-path", "--exclude-tests", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "unused"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("unused", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            // Warn if user specified an unsupported language / 未対応言語の場合は警告
            if (options.Lang != null && !ReferenceExtractor.SupportsLanguage(options.Lang) && !options.Json)
                Console.Error.WriteLine($"Warning: '{options.Lang}' does not support reference extraction. Results may contain false positives.");

            var results = reader.GetUnusedSymbols(options.Limit, options.Kind, options.Lang, options.PathPatterns, options.ExcludePaths, options.ExcludeTests);
            if (results.Count == 0)
            {
                if (options.CountOnly)
                    WriteGraphCountResult(reader, 0, 0, options, jsonOptions, reader._hasReferencesTable, new ExactQuerySignal(true, HasMissingIndex: false, HasMissingTable: false, null));
                else if (options.Json && !reader._hasReferencesTable)
                    WriteDegradedGraphZeroResult(reader, "symbols", json: true, graphAvailable: false, jsonOptions);
                else if (options.Json)
                    Console.WriteLine(BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: "symbols", graphTableAvailable: true, degraded: false).ToJsonString(jsonOptions));
                else if (!options.Json)
                {
                    Console.Error.WriteLine("No unused symbols found.");
                    WriteZeroResultHints(options, reader);
                    WriteKindHint(options.Kind, reader);
                    WriteLangHint(options.Lang, reader);
                    WriteDegradedGraphZeroResult(reader, "symbols", json: false, graphAvailable: reader._hasReferencesTable, jsonOptions);
                }
                return options.CountOnly ? CommandExitCodes.Success : CommandExitCodes.NotFound;
            }

            if (options.CountOnly)
            {
                var fc = results.Select(r => r.Path).Distinct().Count();
                Console.WriteLine(options.Json
                    ? JsonSerializer.Serialize(new { count = results.Count, files = fc }, jsonOptions)
                    : $"{results.Count}");
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { count = results.Count, symbols = results }, jsonOptions));
            }
            else
            {
                foreach (var s in results)
                {
                    var vis = s.Visibility != null ? $" [{s.Visibility}]" : "";
                    var container = s.ContainerName != null ? $" in {s.ContainerName}" : "";
                    Console.WriteLine($"{ConsoleUi.ColorizeKind(s.Kind, 12)} {s.Name,-40} {s.Path}:{s.Line}{vis}{container}");
                }
                Console.Error.WriteLine($"({results.Count} potentially unused symbols)");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunValidate(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "validate"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("validate", cmdArgs, ["--db", "--json", "--no-json", "--kind", "--path", "--since"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedSinceError(options, "validate"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("validate", options))
            return CommandExitCodes.UsageError;

        return WithDb(options.DbPath, reader =>
        {
            var issues = reader.GetIssues(options.Kind, options.PathPatterns);
            var issuesAvailable = reader._hasIssuesTable;
            if (issues.Count == 0)
            {
                if (options.Json)
                    Console.WriteLine(JsonSerializer.Serialize(new { count = 0, issues = Array.Empty<object>(), issues_table_available = issuesAvailable, degraded = !issuesAvailable }, jsonOptions));
                else if (!issuesAvailable)
                    Console.Error.WriteLine("WARN: file_issues table missing in this index (legacy or read-only DB) — validate output is degraded, not a real clean signal.");
                else
                    Console.Error.WriteLine("No encoding issues found.");
                return CommandExitCodes.Success;
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { count = issues.Count, issues }, jsonOptions));
            }
            else
            {
                foreach (var issue in issues)
                {
                    var location = issue.Line > 0 ? $":{issue.Line}" : "";
                    Console.WriteLine($"  {issue.Kind,-20} {issue.Path}{location}  {issue.Message}");
                }
                var kindCounts = issues.GroupBy(i => i.Kind).Select(g => $"{g.Key}: {g.Count()}");
                Console.Error.WriteLine($"\n({issues.Count} issues: {string.Join(", ", kindCounts)})");
            }
            return CommandExitCodes.Success;
        });
    }

    public static int RunLanguages(string[] cmdArgs, JsonSerializerOptions jsonOptions)
    {
        var options = ParseArgs(cmdArgs, jsonDefault: false);
        if (TryWriteParseError(options, "languages"))
            return CommandExitCodes.UsageError;
        if (TryWriteUnsupportedOptionError("languages", cmdArgs, ["--json"]))
            return CommandExitCodes.UsageError;
        if (TryWriteUnexpectedPositionals("languages", options))
            return CommandExitCodes.UsageError;
        var json = options.Json;

        var langExtensions = FileIndexer.GetLanguageExtensions();
        var symbolLangs = SymbolExtractor.GetSupportedLanguages();
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();

        // Build a consolidated view: language -> (extensions, hasSymbols, hasGraph)
        // 統合ビュー: 言語 -> (拡張子, シンボル対応, グラフ対応)
        var allLangs = new Dictionary<string, (List<string> Extensions, bool Symbols, bool Graph)>(StringComparer.Ordinal);

        foreach (var (ext, lang) in langExtensions)
        {
            if (!allLangs.TryGetValue(lang, out var info))
            {
                info = (new List<string>(), symbolLangs.Contains(lang), graphLangs.Contains(lang));
                allLangs[lang] = info;
            }
            info.Extensions.Add(ext);
        }

        // Sort by language name / 言語名でソート
        var sorted = allLangs.OrderBy(kv => kv.Key).ToList();

        if (json)
        {
            var entries = sorted.Select(kv => new
            {
                lang = kv.Key,
                extensions = kv.Value.Extensions.OrderBy(e => e).ToList(),
                symbol_extraction = kv.Value.Symbols,
                graph_queries = kv.Value.Graph,
            });
            Console.WriteLine(JsonSerializer.Serialize(new { languages = entries }, jsonOptions));
        }
        else
        {
            Console.WriteLine($"{"Language",-14} {"Extensions",-36} {"Symbols",-9} {"Graph",-7}");
            Console.WriteLine(new string('-', 66));
            foreach (var (lang, info) in sorted)
            {
                var exts = string.Join(" ", info.Extensions.OrderBy(e => e));
                var sym = info.Symbols ? "yes" : "-";
                var graph = info.Graph ? "yes" : "-";
                Console.WriteLine($"{lang,-14} {exts,-36} {sym,-9} {graph,-7}");
            }
            Console.Error.WriteLine($"\n({sorted.Count} languages)");
        }
        return CommandExitCodes.Success;
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
        bool countOnly = false;
        int? startLine = null;
        int? endLine = null;
        int contextBefore = 0;
        int contextAfter = 0;
        int snippetLines = SearchSnippetFormatter.DefaultSnippetLines;
        var pathPatterns = new List<string>();
        var excludePaths = new List<string>();
        bool excludeTests = false;
        DateTime? since = null;
        bool noDedup = false;
        bool exact = false;
        List<string>? parseErrors = null;
        var extraNames = new List<string>();

        void AddParseError(string error)
        {
            parseErrors ??= [];
            parseErrors.Add(error);
        }

        for (int i = 0; i < args.Length; i++)
        {
            var currentArg = args[i];
            var inlineValue = TrySplitInlineOptionValue(currentArg, out var inlineOptionName)
                ? currentArg[(inlineOptionName!.Length + 1)..]
                : null;
            var normalizedArg = inlineOptionName ?? currentArg;

            switch (normalizedArg)
            {
                case "--db":
                    if (TryReadStringOptionValue(args, ref i, "--db", inlineValue, allowSeparatedDashPrefixedLiteralValue: true, out var dbPathValue, out var dbPathError))
                        dbPath = dbPathValue!;
                    else
                        AddParseError(dbPathError!);
                    break;
                case "--json":
                    json = true;
                    break;
                case "--no-json":
                    json = false;
                    break;
                case "--limit":
                case "--top":
                    if (!TryReadRawOptionValue(args, ref i, "--limit", inlineValue, out var limitValue, out var missingLimitError))
                        AddParseError(missingLimitError!);
                    else if (TryParsePositiveInt(limitValue!, "--limit", out var parsedLimit, out var limitError))
                        limit = parsedLimit;
                    else
                        AddParseError(limitError!);
                    break;
                case "--lang":
                    if (TryReadStringOptionValue(args, ref i, "--lang", inlineValue, allowSeparatedDashPrefixedLiteralValue: false, out var langValue, out var langError))
                        lang = langValue;
                    else
                        AddParseError(langError!);
                    break;
                case "--kind":
                    if (TryReadStringOptionValue(args, ref i, "--kind", inlineValue, allowSeparatedDashPrefixedLiteralValue: false, out var kindValue, out var kindError))
                        kind = kindValue;
                    else
                        AddParseError(kindError!);
                    break;
                case "--fts":
                    rawFts = true;
                    break;
                case "--body":
                    includeBody = true;
                    break;
                case "--count":
                    countOnly = true;
                    break;
                case "--no-dedup":
                    noDedup = true;
                    break;
                case "--exact":
                    exact = true;
                    break;
                case "--depth":
                    if (!TryReadRawOptionValue(args, ref i, "--depth", inlineValue, out var depthValue, out var missingDepthError))
                        AddParseError(missingDepthError!);
                    else if (TryParseNonNegativeInt(depthValue!, "--depth", out var parsedDepth, out var depthError))
                        contextAfter = parsedDepth; // reused as depth for impact / impact用に再利用
                    else
                        AddParseError(depthError!);
                    break;
                case "--reverse":
                    break; // handled by specific commands / 特定コマンドで処理
                case "--path":
                    if (TryReadStringOptionValue(args, ref i, "--path", inlineValue, allowSeparatedDashPrefixedLiteralValue: true, out var pathPattern, out var pathError))
                        pathPatterns.Add(pathPattern!); // Repeatable; multiple values OR together / 繰り返し可、複数値は OR で結合
                    else
                        AddParseError(pathError!);
                    break;
                case "--exclude-path":
                    if (TryReadStringOptionValue(args, ref i, "--exclude-path", inlineValue, allowSeparatedDashPrefixedLiteralValue: true, out var excludePath, out var excludePathError))
                        excludePaths.Add(excludePath!);
                    else
                        AddParseError(excludePathError!);
                    break;
                case "--exclude-tests":
                    excludeTests = true;
                    break;
                case "--since":
                    if (!TryReadStringOptionValue(args, ref i, "--since", inlineValue, allowSeparatedDashPrefixedLiteralValue: false, out var sinceValue, out var sinceError))
                        AddParseError(sinceError!);
                    else if (TryParseIso8601Since(sinceValue!, out var parsedSince))
                        since = parsedSince;
                    else
                        AddParseError($"Error: could not parse --since value '{sinceValue}' as a date/time. Use ISO 8601 format (e.g. 2024-01-01 or 2024-01-01T00:00:00Z).");
                    break;
                case "--start":
                    if (!TryReadRawOptionValue(args, ref i, "--start", inlineValue, out var startValue, out var missingStartError))
                        AddParseError(missingStartError!);
                    else if (TryParsePositiveInt(startValue!, "--start", out var parsedStart, out var startError))
                        startLine = parsedStart;
                    else
                        AddParseError(startError!);
                    break;
                case "--end":
                    if (!TryReadRawOptionValue(args, ref i, "--end", inlineValue, out var endValue, out var missingEndError))
                        AddParseError(missingEndError!);
                    else if (TryParsePositiveInt(endValue!, "--end", out var parsedEnd, out var endError))
                        endLine = parsedEnd;
                    else
                        AddParseError(endError!);
                    break;
                case "--before":
                    if (!TryReadRawOptionValue(args, ref i, "--before", inlineValue, out var beforeValue, out var missingBeforeError))
                        AddParseError(missingBeforeError!);
                    else if (TryParseNonNegativeInt(beforeValue!, "--before", out var parsedBefore, out var beforeError))
                        contextBefore = parsedBefore;
                    else
                        AddParseError(beforeError!);
                    break;
                case "--after":
                    if (!TryReadRawOptionValue(args, ref i, "--after", inlineValue, out var afterValue, out var missingAfterError))
                        AddParseError(missingAfterError!);
                    else if (TryParseNonNegativeInt(afterValue!, "--after", out var parsedAfter, out var afterError))
                        contextAfter = parsedAfter;
                    else
                        AddParseError(afterError!);
                    break;
                case "--name":
                    if (TryReadStringOptionValue(args, ref i, "--name", inlineValue, allowSeparatedDashPrefixedLiteralValue: false, out var extraName, out var nameError))
                        extraNames.Add(extraName!); // Repeatable; OR-joined with other --name values and extra positional names / 繰り返し可、他の --name や追加の positional 引数と OR 結合
                    else
                        AddParseError($"{nameError} / --name には値（シンボル名パターン）が必要です。");
                    break;
                case "--snippet-lines":
                    if (!TryReadRawOptionValue(args, ref i, "--snippet-lines", inlineValue, out var snippetLinesValue, out var missingSnippetLinesError))
                        AddParseError(missingSnippetLinesError!);
                    else if (TryParsePositiveInt(snippetLinesValue!, "--snippet-lines", out var parsedSnippetLines, out var snippetLinesError))
                        snippetLines = SearchSnippetFormatter.ClampSnippetLines(parsedSnippetLines);
                    else
                        AddParseError(snippetLinesError!);
                    break;
                default:
                    if (args[i].StartsWith('-'))
                        break;
                    else if (query == null)
                    {
                        query = args[i];
                    }
                    else
                    {
                        // Extra positional args become additional symbol names / 追加の positional 引数を追加の symbol name として扱う
                        extraNames.Add(args[i]);
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
            SnippetLines = snippetLines,
            PathPatterns = pathPatterns,
            ExcludePaths = excludePaths,
            ExcludeTests = excludeTests,
            CountOnly = countOnly,
            Since = since,
            NoDedup = noDedup,
            Exact = exact,
            ExtraNames = extraNames,
            ParseError = parseErrors == null ? null : string.Join(Environment.NewLine, parseErrors),
        };
    }

    private static int WithDb(string dbPath, Func<DbReader, int> action)
    {
        // Allow SQLite URI forms (file:///abs/path?immutable=1 etc.) so users and AI agents
        // on read-only mounts / sandboxes can opt into the immutable read-only escape hatch
        // explicitly when the automatic DbContext fallback cannot recover. File.Exists is
        // skipped for URI-shaped inputs because they may carry query params and schemes that
        // are meaningless to the filesystem API but are understood by SQLite.
        // URI 形式の --db を受け入れるため、file: で始まる値は File.Exists チェックをスキップ。
        var isUri = dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && !File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Error: database not found at {Path.GetFullPath(dbPath)}");
            Console.Error.WriteLine("Hint: create or refresh the index with `cdidx index <projectPath>` (or `cdidx .`) and then rerun this command.");
            return CommandExitCodes.DatabaseError;
        }

        Database.DbDebug.ResetContext();
        try
        {
            using var db = new DbContext(dbPath);
            db.TryMigrateForRead();
            var reader = new DbReader(db.Connection, db.IsReadOnly);
            return action(reader);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: database error: {ex.Message}");
            Console.Error.WriteLine("Hint: check `--db`, or rebuild the index with `cdidx index <projectPath>` if the DB may be stale or corrupted.");
            Database.DbDebug.DumpToStderr(ex);
            return CommandExitCodes.DatabaseError;
        }
        finally
        {
            Database.DbDebug.ResetContext();
        }
    }

    private static void WriteNumberedExcerpt(int startLine, string content)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            Console.WriteLine($"  {startLine + i,4}: {lines[i]}");
    }

    private static bool TryWriteParseError(QueryCommandOptions options, string commandName)
    {
        if (options.ParseError == null)
            return false;

        Console.Error.WriteLine(options.ParseError);
        Console.Error.WriteLine("Hint: fix the invalid or missing option value, then rerun with the command shape below.");
        Console.Error.WriteLine($"Usage: {GetUsageLineOrThrow(commandName)}");
        return true;
    }

    private static bool TryWriteUnsupportedSinceError(QueryCommandOptions options, string commandName)
    {
        if (!options.Since.HasValue)
            return false;

        Console.Error.WriteLine($"Error: --since is not supported for {commandName}.");
        Console.Error.WriteLine("Hint: remove `--since` and rerun, or use it with `search`, `definition`, `symbols`, or `files`.");
        Console.Error.WriteLine($"Usage: {GetUsageLineOrThrow(commandName)}");
        return true;
    }

    private static bool TryWriteUnsupportedOptionError(string commandName, string[] cmdArgs, IEnumerable<string> supportedOptions)
    {
        var supported = supportedOptions.ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < cmdArgs.Length; i++)
        {
            var arg = cmdArgs[i];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            var normalizedArg = TrySplitInlineOptionValue(arg, out var inlineOptionName)
                ? inlineOptionName!
                : arg;

            if (supported.Contains(normalizedArg))
            {
                if (normalizedArg == arg && ValueTakingOptions.Contains(normalizedArg) && i + 1 < cmdArgs.Length)
                    i++;
                continue;
            }

            if (normalizedArg == arg && ValueTakingOptions.Contains(normalizedArg) && i + 1 < cmdArgs.Length)
                i++;

            Console.Error.WriteLine($"Error: {arg} is not supported for {commandName}.");
            Console.Error.WriteLine($"Hint: remove `{arg}` and rerun, or use only the options shown in `{commandName} --help`.");
            Console.Error.WriteLine($"Usage: {GetUsageLineOrThrow(commandName)}");
            return true;
        }

        return false;
    }

    private static bool TryWriteUnexpectedExtraPositionals(string commandName, QueryCommandOptions options)
    {
        if (options.ExtraNames.Count == 0)
            return false;

        Console.Error.WriteLine($"Error: unexpected extra positional argument(s) for {commandName}: {string.Join(", ", options.ExtraNames.Select(name => $"`{name}`"))}.");
        Console.Error.WriteLine("Hint: quote multi-word queries as a single argument, or remove the extra positional values.");
        Console.Error.WriteLine($"Usage: {GetUsageLineOrThrow(commandName)}");
        return true;
    }

    private static bool TryWriteUnexpectedPositionals(string commandName, QueryCommandOptions options)
    {
        var unexpected = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Query))
            unexpected.Add($"`{options.Query}`");
        unexpected.AddRange(options.ExtraNames.Select(name => $"`{name}`"));
        if (unexpected.Count == 0)
            return false;

        Console.Error.WriteLine($"Error: {commandName} does not accept positional arguments: {string.Join(", ", unexpected)}.");
        Console.Error.WriteLine("Hint: remove the extra positional argument(s) and use the documented flags only.");
        Console.Error.WriteLine($"Usage: {GetUsageLineOrThrow(commandName)}");
        return true;
    }

    private static string GetUsageLineOrThrow(string commandName) =>
        ConsoleUi.GetUsageLine(commandName)
        ?? throw new InvalidOperationException($"Missing usage line for command '{commandName}'.");

    private static void WriteUsageError(string message, string usage, string hint)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine($"Hint: {hint}");
        Console.Error.WriteLine($"Usage: {usage}");
    }

    private static void WriteValidationError(string message, string hint)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine($"Hint: {hint}");
    }

    private static void WriteRepoMapSection(string title, IEnumerable<string> rows)
    {
        var materialized = rows.ToList();
        if (materialized.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"{title}:");
        foreach (var row in materialized)
            Console.WriteLine($"  {row}");
    }

    /// <summary>
    /// Write actionable hints when a query returns zero results.
    /// 0件時に実行可能なヒントを出力する。
    /// </summary>
    private static void WriteZeroResultHints(QueryCommandOptions options, DbReader reader, string? alternativeHint = null)
    {
        var freshness = reader.GetFreshnessHint();
        if (freshness.FileCount == 0)
        {
            Console.Error.WriteLine("Hint: the index is empty. Run 'cdidx index <projectPath>' first.");
            return;
        }

        if (options.Lang != null || options.PathPatterns.Count > 0 || options.ExcludeTests || options.ExcludePaths.Count > 0)
            Console.Error.WriteLine("Hint: try removing --lang, --path, --exclude-path, or --exclude-tests to broaden the search.");

        if (alternativeHint != null)
            Console.Error.WriteLine($"Hint: {alternativeHint}");

        if (freshness.IndexedAt.HasValue && (DateTime.UtcNow - freshness.IndexedAt.Value).TotalHours > 24)
            Console.Error.WriteLine("Hint: the index may be stale. Run 'cdidx index <projectPath>' to refresh.");
    }

    internal static ExactZeroHintResult? BuildExactZeroHint<T>(bool shouldProbe, Func<bool> anyRelaxedMatch, Func<List<T>> relaxedSampleQuery, Func<T, string?> nameSelector)
    {
        return BuildExactZeroHint(shouldProbe, anyRelaxedMatch, relaxedCountQuery: null, relaxedSampleQuery, nameSelector);
    }

    internal static ExactZeroHintResult? BuildExactZeroHint<T>(bool shouldProbe, Func<bool> anyRelaxedMatch, Func<int>? relaxedCountQuery, Func<List<T>> relaxedSampleQuery, Func<T, string?> nameSelector)
    {
        if (!shouldProbe)
            return null;

        if (!anyRelaxedMatch())
            return null;

        int? relaxedCount = null;
        if (relaxedCountQuery != null)
        {
            relaxedCount = relaxedCountQuery();
            if (relaxedCount == 0)
                return null;
        }

        var relaxedResults = relaxedSampleQuery();
        if (relaxedResults.Count == 0)
            return null;

        var sampleNames = relaxedResults
            .Select(nameSelector)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .Select(name => name!)
            .ToList();

        return new ExactZeroHintResult
        {
            RelaxedCount = relaxedCount,
            SampleNames = sampleNames,
            Suggestion = ExactZeroHintResult.DefaultSuggestion,
        };
    }

    private static void AddFreshnessHint(JsonObject payload, DbReader reader)
    {
        var freshness = reader.GetFreshnessHint();
        payload["indexed_file_count"] = freshness.FileCount;
        payload["indexed_at"] = freshness.IndexedAt.HasValue
            ? JsonSerializer.SerializeToNode(freshness.IndexedAt.Value)
            : null;
        payload["freshness_available"] = freshness.FreshnessAvailable;
        if (!freshness.FreshnessAvailable && freshness.FreshnessDegradedReason != null)
            payload["freshness_degraded_reason"] = freshness.FreshnessDegradedReason;
    }

    private static JsonObject BuildJsonZeroResultPayload(
        DbReader reader,
        JsonSerializerOptions jsonOptions,
        string? resultsKey = null,
        ExactZeroHintResult? exactZeroHint = null,
        bool includeFiles = false,
        bool? graphTableAvailable = null,
        bool? degraded = null,
        ExactQuerySignal? exactSignal = null,
        Action<JsonObject>? extraFields = null)
    {
        var payload = new JsonObject
        {
            ["count"] = 0,
        };

        if (resultsKey != null)
            payload[resultsKey] = new JsonArray();
        if (includeFiles)
            payload["files"] = 0;
        if (graphTableAvailable.HasValue)
            payload["graph_table_available"] = graphTableAvailable.Value;
        if (degraded.HasValue)
            payload["degraded"] = degraded.Value;
        if (exactSignal.HasValue)
        {
            payload["exact_index_available"] = exactSignal.Value.ExactIndexAvailable;
            if (exactSignal.Value.DegradedReason != null)
                payload["degraded_reason"] = exactSignal.Value.DegradedReason;
        }
        if (exactZeroHint != null)
            payload["exact_zero_hint"] = JsonSerializer.SerializeToNode(exactZeroHint, jsonOptions);
        extraFields?.Invoke(payload);
        AddFreshnessHint(payload, reader);

        return payload;
    }

    private static void WriteExactZeroHint(ExactZeroHintResult? exactZeroHint)
    {
        if (exactZeroHint == null)
            return;

        var examples = exactZeroHint.SampleNames.Count == 0
            ? string.Empty
            : $" (e.g. {string.Join(", ", exactZeroHint.SampleNames.Select(name => $"`{name}`"))})";
        if (exactZeroHint.RelaxedCount.HasValue)
            Console.Error.WriteLine($"Hint: --exact found 0 matches, but substring matching would return {exactZeroHint.RelaxedCount}{examples}. Drop --exact or use the exact indexed name.");
        else
            Console.Error.WriteLine($"Hint: --exact found 0 matches, but substring matching would return results{examples}. Drop --exact or use the exact indexed name.");
    }

    private static void WriteExactSymbolWarningIfNeeded(bool exact, bool json, ExactQuerySignal signal)
    {
        if (!exact || json || signal.ExactIndexAvailable || signal.DegradedReason == null || !signal.HasMissingIndex)
            return;

        Console.Error.WriteLine($"WARN: --exact symbol query ran without the supporting index ({signal.DegradedReason}). Results are correct but may be slow.");
        Console.Error.WriteLine("Hint: re-index with `cdidx index <projectPath>` to upgrade the DB layout.");
    }

    /// <summary>
    /// Show available symbol kinds when --kind produces zero results.
    /// --kind で 0 件のとき、有効なシンボル種別を表示する。
    /// </summary>
    /// <summary>
    /// Show available languages when --lang produces zero results.
    /// --lang で 0 件のとき、有効な言語を表示する。
    /// </summary>
    private static void WriteLangHint(string? lang, DbReader reader)
    {
        if (lang == null) return;
        var status = reader.GetStatus();
        if (status.Languages.Count > 0 && !status.Languages.ContainsKey(lang))
            Console.Error.WriteLine($"Hint: '{lang}' not found in index. Available: {string.Join(", ", status.Languages.Keys.OrderBy(l => l))}");
    }

    // All valid symbol kinds emitted by SymbolExtractor / SymbolExtractor が出力する全有効シンボル種別
    private static readonly string[] AllValidKinds =
        ["class", "delegate", "enum", "event", "function", "import", "interface", "namespace", "property", "struct"];

    private static void WriteKindHint(string? kind, DbReader reader)
    {
        if (kind == null) return;
        if (!AllValidKinds.Contains(kind))
        {
            Console.Error.WriteLine($"Hint: '{kind}' is not a known kind. Available: {string.Join(", ", AllValidKinds)}");
            return;
        }
        // Kind is valid but not found in this index — hint that no symbols of this kind exist
        // 種別は有効だがインデックスに存在しない場合のヒント
        var existingKinds = reader.GetDistinctKinds();
        if (!existingKinds.Contains(kind))
            Console.Error.WriteLine($"Hint: no '{kind}' symbols in the index. Indexed kinds: {string.Join(", ", existingKinds)}");
    }

    private static void WriteGraphSupportHint(string? lang)
    {
        if (lang != null && !ReferenceExtractor.SupportsLanguage(lang))
            Console.Error.WriteLine($"Note: call-graph queries are not indexed for '{lang}'. Use search, definition, excerpt, or files instead.");
    }

    private static void WriteImpactResolutionHint(ImpactAnalysisResult analysis)
    {
        if (analysis.DefinitionCount > 0)
        {
            var kinds = string.Join(", ", analysis.Definitions.Select(d => d.Kind).Distinct().OrderBy(k => k));
            var pathPreview = analysis.Definitions
                .Select(d => d.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            var extra = analysis.DefinitionFileCount > pathPreview.Count
                ? $" (+{analysis.DefinitionFileCount - pathPreview.Count} more)"
                : string.Empty;
            Console.Error.WriteLine($"Note: '{analysis.Query}' resolved to '{analysis.ResolvedName}' ({kinds}) as {analysis.DefinitionCount} definition(s) across {analysis.DefinitionFileCount} file(s): {string.Join(", ", pathPreview)}{extra}");
        }
        else if (analysis.ZeroResultReason == "no_matching_definition")
        {
            Console.Error.WriteLine($"Note: no indexed definition matched '{analysis.Query}'.");
        }

        if (!string.IsNullOrWhiteSpace(analysis.Suggestion))
            Console.Error.WriteLine($"Hint: {analysis.Suggestion}");
    }

    // Emit a zero-result payload that distinguishes "real 0 hits" from "graph table missing
    // (degraded)". Without this, AI agents and humans cannot tell the index from a legacy /
    // read-only DB apart from a DB that genuinely has no callers for the query.
    // graph テーブル欠損による 0 と本物の 0 を JSON で区別できるようにする。
    private static void WriteDegradedGraphZeroResult(DbReader reader, string resultsKey, bool json, bool graphAvailable, JsonSerializerOptions jsonOptions,
        ExactQuerySignal? exactSignal = null, Action<JsonObject>? extraFields = null)
    {
        if (graphAvailable) return;
        if (json)
        {
            var payload = BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: resultsKey, graphTableAvailable: false, degraded: true, exactSignal: exactSignal, extraFields: extraFields);
            payload["note"] = "symbol_references table is missing in this index (legacy or read-only DB). Zero result is degraded, not authoritative.";
            Console.WriteLine(payload.ToJsonString(jsonOptions));
        }
        else
        {
            Console.Error.WriteLine("WARN: symbol_references table missing — this 0-result is degraded, not authoritative.");
        }
    }

    private static void WriteExactGraphWarningIfNeeded(bool exact, bool json, ExactQuerySignal signal)
    {
        if (!exact || json || signal.ExactIndexAvailable || signal.DegradedReason == null || !signal.HasMissingIndex)
            return;

        Console.Error.WriteLine($"WARN: --exact graph query ran without the supporting index ({signal.DegradedReason}). Results are correct but may be slow.");
        Console.Error.WriteLine("Hint: re-index with `cdidx index <projectPath>` to upgrade the DB layout.");
    }

    private static void WriteExactBundleWarningIfNeeded(bool exact, bool json, ExactQuerySignal signal)
    {
        if (!exact || json || signal.ExactIndexAvailable || signal.DegradedReason == null || !signal.HasMissingIndex)
            return;

        Console.Error.WriteLine($"WARN: --exact inspect bundle ran without all supporting indexes ({signal.DegradedReason}). Results are correct but may be slow.");
        Console.Error.WriteLine("Hint: re-index with `cdidx index <projectPath>` to upgrade the DB layout.");
    }

    private static void WriteGraphCountResult(DbReader reader, int count, int files, QueryCommandOptions options, JsonSerializerOptions jsonOptions,
        bool graphAvailable, ExactQuerySignal exactSignal, ExactZeroHintResult? exactZeroHint = null)
    {
        if (!options.Json)
        {
            Console.WriteLine($"{count}");
            if (!graphAvailable)
                Console.Error.WriteLine("WARN: symbol_references table missing — this count result is degraded, not authoritative.");
            return;
        }

        var payload = new JsonObject
        {
            ["count"] = count,
            ["files"] = files,
            ["graph_table_available"] = graphAvailable,
        };
        if (!graphAvailable)
            payload["degraded"] = true;
        if (options.Exact)
            AddExactGraphJsonFields(payload, exactSignal);
        if (exactZeroHint != null)
            payload["exact_zero_hint"] = JsonSerializer.SerializeToNode(exactZeroHint, jsonOptions);
        if (count == 0)
            AddFreshnessHint(payload, reader);
        Console.WriteLine(payload.ToJsonString(jsonOptions));
    }

    private static void WriteGraphZeroJsonResult(DbReader reader, string resultsKey, JsonSerializerOptions jsonOptions, bool graphAvailable,
        ExactQuerySignal? exactSignal, ExactZeroHintResult? exactZeroHint = null)
    {
        var payload = BuildJsonZeroResultPayload(reader, jsonOptions, resultsKey: resultsKey, graphTableAvailable: graphAvailable);
        if (exactSignal != null)
            AddExactGraphJsonFields(payload, exactSignal.Value);
        if (exactZeroHint != null)
            payload["exact_zero_hint"] = JsonSerializer.SerializeToNode(exactZeroHint, jsonOptions);
        Console.WriteLine(payload.ToJsonString(jsonOptions));
    }

    private static void WriteGraphJsonResult<T>(T result, ExactQuerySignal exactSignal, JsonSerializerOptions jsonOptions)
    {
        var payload = JsonSerializer.SerializeToNode(result, jsonOptions)!.AsObject();
        AddExactJsonFields(payload, exactSignal);
        Console.WriteLine(payload.ToJsonString(jsonOptions));
    }

    private static void WriteJsonResultWithExactSignal<T>(T result, ExactQuerySignal exactSignal, JsonSerializerOptions jsonOptions)
    {
        var payload = JsonSerializer.SerializeToNode(result, jsonOptions)!.AsObject();
        AddExactJsonFields(payload, exactSignal);
        Console.WriteLine(payload.ToJsonString(jsonOptions));
    }

    private static void AddExactGraphJsonFields(JsonObject payload, ExactQuerySignal exactSignal)
    {
        AddExactJsonFields(payload, exactSignal);
    }

    private static void AddExactJsonFields(JsonObject payload, ExactQuerySignal exactSignal)
    {
        payload["exact_index_available"] = exactSignal.ExactIndexAvailable;
        if (exactSignal.DegradedReason != null)
            payload["degraded_reason"] = exactSignal.DegradedReason;
    }

    private static bool TryParsePositiveInt(string rawValue, string optionName, out int value, out string? error)
    {
        if (int.TryParse(rawValue, out value) && value > 0)
        {
            error = null;
            return true;
        }

        value = 0;
        error = $"Error: {optionName} requires a positive integer, got '{rawValue}'. Hint: retry with `{optionName} 1` or another positive integer.";
        return false;
    }

    private static bool TryParseNonNegativeInt(string rawValue, string optionName, out int value, out string? error)
    {
        if (int.TryParse(rawValue, out value) && value >= 0)
        {
            error = null;
            return true;
        }

        value = 0;
        error = $"Error: {optionName} requires a non-negative integer, got '{rawValue}'. Hint: retry with `{optionName} 0` or another non-negative integer.";
        return false;
    }

    private static bool TryReadRawOptionValue(string[] args, ref int index, string optionName, string? inlineValue, out string? value, out string? error)
    {
        if (inlineValue != null)
        {
            value = inlineValue;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"Error: {optionName} requires a value.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static bool TryReadStringOptionValue(string[] args, ref int index, string optionName, string? inlineValue, bool allowSeparatedDashPrefixedLiteralValue, out string? value, out string? error)
    {
        if (inlineValue != null)
        {
            value = inlineValue;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"Error: {optionName} requires a value.";
            return false;
        }

        var candidate = args[index + 1];
        if (IsRejectedSeparatedStringValue(candidate, allowSeparatedDashPrefixedLiteralValue))
        {
            value = null;
            error = allowSeparatedDashPrefixedLiteralValue && candidate.StartsWith("--", StringComparison.Ordinal)
                ? $"Error: {optionName} requires a value. Hint: if the literal value starts with `--`, pass it as `{optionName}=<value>`."
                : $"Error: {optionName} requires a value.";
            return false;
        }

        index++;
        value = candidate;
        error = null;
        return true;
    }

    private static bool IsRejectedSeparatedStringValue(string candidate, bool allowSeparatedDashPrefixedLiteralValue)
    {
        if (!candidate.StartsWith("-", StringComparison.Ordinal))
            return false;

        if (!allowSeparatedDashPrefixedLiteralValue)
            return true;

        return candidate.StartsWith("--", StringComparison.Ordinal);
    }

    private static bool IsRecognizedOptionToken(string value) =>
        ValueTakingOptions.Contains(value) || FlagOnlyOptions.Contains(value);

    private static bool TrySplitInlineOptionValue(string token, out string? optionName)
    {
        optionName = null;
        var separator = token.IndexOf('=');
        if (separator <= 0)
            return false;

        var candidate = token[..separator];
        if (!ValueTakingOptions.Contains(candidate))
            return false;

        optionName = candidate;
        return true;
    }

    // Accepted ISO 8601 formats for --since / --sinceフィルタで受け付けるISO 8601書式
    private static readonly string[] Iso8601Formats =
    [
        // date only / 日付のみ
        "yyyy-MM-dd",
        // minute precision / 分精度
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-ddTHH:mmZ",
        "yyyy-MM-ddTHH:mmzzz",
        // second precision / 秒精度
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:sszzz",
        // fractional seconds (1-7 digits via 'F') / 小数秒（1-7桁、'F'で可変長）
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        // round-trip format / ラウンドトリップ書式
        "o",
    ];

    /// <summary>
    /// Parse a --since value using invariant ISO 8601 formats only.
    /// Rejects ambiguous locale-dependent formats like MM/dd/yyyy.
    /// Offsetless inputs are treated as local time (matching prior behavior).
    /// ISO 8601形式のみで--since値をパースする。MM/dd/yyyyなどロケール依存の曖昧な形式は拒否する。
    /// オフセットなしの入力はローカル時刻として扱う（従来の動作を維持）。
    /// </summary>
    internal static bool TryParseIso8601Since(string value, out DateTime result)
    {
        if (DateTimeOffset.TryParseExact(value, Iso8601Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            result = dto.UtcDateTime;
            return true;
        }
        result = default;
        return false;
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
    public int SnippetLines { get; init; } = SearchSnippetFormatter.DefaultSnippetLines;
    public List<string> PathPatterns { get; init; } = [];
    public List<string> ExcludePaths { get; init; } = [];
    public bool ExcludeTests { get; init; }
    public bool CountOnly { get; init; }
    public DateTime? Since { get; init; }
    public bool NoDedup { get; init; }
    public bool Exact { get; init; }
    public List<string> ExtraNames { get; init; } = [];
    public string? ParseError { get; init; }
}
