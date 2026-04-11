using System.Text.Json;
using System.Text.Json.Nodes;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Mcp;

/// <summary>
/// MCP tool execution handlers (partial class split from McpServer.cs).
/// MCPツール実行ハンドラ（McpServer.csからのpartial class分割）。
/// </summary>
public partial class McpServer
{
    // --- Tool implementations / ツール実装 ---

    /// <summary>
    /// Build the server instructions string for the initialize response.
    /// Uses the actual supported-language list from ReferenceExtractor.
    /// initializeレスポンス用のサーバー指示文字列を構築。
    /// ReferenceExtractorの実際の対応言語リストを使用。
    /// </summary>
    private static string BuildInstructions()
    {
        var langs = string.Join(", ", ReferenceExtractor.GetSupportedLanguages());
        return "cdidx is a code-index server. "
            + "If queries fail because no index exists, run 'index' first to build it. "
            + "Start with 'map' for repo orientation, then use 'search' for text queries or 'definition' for symbol lookup. "
            + "Use 'analyze_symbol' to get definition, callers, callees, and references in one call instead of chaining separate tools. "
            + $"Graph tools (references, callers, callees) only work for supported languages ({langs}); "
            + "for other languages, use 'search' instead. "
            + "Use 'outline' to see the full symbol structure of a single file (functions, classes, imports with line numbers) without reading the file content. "
            + "Use 'excerpt' to read specific line ranges from indexed files. "
            + "Check 'status' to verify index freshness before trusting results. "
            + "Use 'languages' to discover all supported languages, file extensions, and which languages support call-graph queries. "
            + "Use 'files' with 'since' to find recently modified files without scanning all results. "
            + "Use 'batch_query' to execute multiple read-only queries in a single call (max 10), dramatically reducing round-trips. "
            + "Use 'deps' to see file-level dependency edges — which files reference symbols from which other files.";
    }

    /// <summary>
    /// Add freshness hint fields to a zero-result payload so AI clients
    /// can self-diagnose stale or empty indexes without a separate status call.
    /// 0件レスポンスに鮮度ヒントを追加し、AIクライアントが別途statusを
    /// 呼ばなくてもインデックスの古さや空を自己診断できるようにする。
    /// </summary>
    private static void AddFreshnessHint(JsonObject payload, DbReader reader)
    {
        var (fileCount, indexedAt) = reader.GetFreshnessHint();
        payload["indexed_file_count"] = fileCount;
        if (indexedAt.HasValue)
            payload["indexed_at"] = JsonSerializer.SerializeToNode(indexedAt.Value);
    }

    /// <summary>
    /// Clamp limit to a safe range to prevent resource exhaustion.
    /// リソース枯渇を防ぐためlimitを安全な範囲にクランプ。
    /// </summary>
    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static List<string> ReadStringList(JsonNode? args, string propertyName)
    {
        return args?[propertyName] is JsonArray array
            ? array.Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList()
            : [];
    }

    private static string BuildGraphSummary(string label, int count, string? lang, bool? graphSupported)
    {
        if (count > 0)
            return $"Found {count} {label}.";

        if (graphSupported == false && lang != null)
            return $"No {label} found. Call-graph queries are not indexed for '{lang}'.";

        return $"No {label} found.";
    }

    private JsonNode ExecuteSearch(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var lang = args?["lang"]?.GetValue<string>();
        var snippetLines = SearchSnippetFormatter.ClampSnippetLines(args?["snippetLines"]?.GetValue<int>() ?? SearchSnippetFormatter.DefaultSnippetLines);
        var rawQuery = args?["rawQuery"]?.GetValue<bool>() ?? false;
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;
        var sinceStr = args?["since"]?.GetValue<string>();
        DateTime? since = null;
        if (sinceStr != null && DateTime.TryParse(sinceStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedSince))
            since = parsedSince.ToUniversalTime();
        var deduplicate = !(args?["noDedup"]?.GetValue<bool>() ?? false);

        return WithDbReader(id, reader =>
        {
            var results = reader.Search(query, limit, lang, rawQuery, pathPattern, excludePaths, excludeTests, deduplicate, since);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["rawQuery"] = rawQuery,
                    ["snippetLines"] = snippetLines,
                    ["path"] = pathPattern,
                    ["excludeTests"] = excludeTests,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                AddFreshnessHint(payload, reader);
                return CreateToolResult(id, "No results found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["rawQuery"] = rawQuery,
                ["snippetLines"] = snippetLines,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results.Select(result => SearchSnippetFormatter.ToCompactResult(result, query, snippetLines)), _jsonOptions)
            };
            // Include top file paths in summary for quick AI orientation
            // AIが素早く位置把握できるよう、サマリにトップファイルパスを含める
            var topPaths = results.Select(r => r.Path).Distinct().Take(3);
            var summary = $"Found {results.Count} search result(s) in {string.Join(", ", topPaths)}.";
            return CreateToolResult(id, summary, structured);
        });
    }

    private JsonNode ExecuteSymbols(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (query != null && query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");
        var kind = args?["kind"]?.GetValue<string>();
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.SearchSymbols(query, limit, kind, lang, pathPattern, excludePaths, excludeTests);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["kind"] = kind,
                    ["lang"] = lang,
                    ["path"] = pathPattern,
                    ["excludeTests"] = excludeTests,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                AddFreshnessHint(payload, reader);
                return CreateToolResult(id, "No symbols found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            return CreateToolResult(id, $"Found {results.Count} symbol(s).", structured);
        });
    }

    private JsonNode ExecuteDefinition(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var kind = args?["kind"]?.GetValue<string>();
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var includeBody = args?["includeBody"]?.GetValue<bool>() ?? false;
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.GetDefinitions(query, limit, kind, lang, includeBody, pathPattern, excludePaths, excludeTests);
            var payload = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["includeBody"] = includeBody,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            if (results.Count == 0)
                AddFreshnessHint(payload, reader);
            return CreateToolResult(id,
                results.Count == 0 ? "No definitions found." : $"Found {results.Count} definition(s).",
                payload);
        });
    }

    private JsonNode ExecuteReferences(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var kind = args?["kind"]?.GetValue<string>();
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.SearchReferences(query, limit, lang, kind, pathPattern, excludePaths, excludeTests);
            bool? graphSupported = lang == null ? null : ReferenceExtractor.SupportsLanguage(lang);
            var payload = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["graphLanguage"] = lang,
                ["graphSupported"] = graphSupported,
                ["graphSupportReason"] = ReferenceExtractor.BuildGraphSupportReason(lang, graphSupported),
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            if (results.Count == 0)
                AddFreshnessHint(payload, reader);
            return CreateToolResult(id,
                BuildGraphSummary("references", results.Count, lang, graphSupported),
                payload);
        });
    }

    private JsonNode ExecuteCallers(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var kind = args?["kind"]?.GetValue<string>();
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.GetCallers(query, limit, lang, kind, pathPattern, excludePaths, excludeTests);
            bool? graphSupported = lang == null ? null : ReferenceExtractor.SupportsLanguage(lang);
            var payload = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["graphLanguage"] = lang,
                ["graphSupported"] = graphSupported,
                ["graphSupportReason"] = ReferenceExtractor.BuildGraphSupportReason(lang, graphSupported),
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            if (results.Count == 0)
                AddFreshnessHint(payload, reader);
            return CreateToolResult(id,
                BuildGraphSummary("callers", results.Count, lang, graphSupported),
                payload);
        });
    }

    private JsonNode ExecuteCallees(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var kind = args?["kind"]?.GetValue<string>();
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.GetCallees(query, limit, lang, kind, pathPattern, excludePaths, excludeTests);
            bool? graphSupported = lang == null ? null : ReferenceExtractor.SupportsLanguage(lang);
            var payload = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["graphLanguage"] = lang,
                ["graphSupported"] = graphSupported,
                ["graphSupportReason"] = ReferenceExtractor.BuildGraphSupportReason(lang, graphSupported),
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            if (results.Count == 0)
                AddFreshnessHint(payload, reader);
            return CreateToolResult(id,
                BuildGraphSummary("callees", results.Count, lang, graphSupported),
                payload);
        });
    }

    private JsonNode ExecuteFiles(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (query != null && query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;
        var sinceStr = args?["since"]?.GetValue<string>();
        DateTime? since = null;
        if (sinceStr != null && DateTime.TryParse(sinceStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedSince))
            since = parsedSince.ToUniversalTime();

        return WithDbReader(id, reader =>
        {
            var results = reader.ListFiles(query, limit, lang, pathPattern, excludePaths, excludeTests, since);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["lang"] = lang,
                    ["path"] = pathPattern,
                    ["excludeTests"] = excludeTests,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                AddFreshnessHint(payload, reader);
                return CreateToolResult(id, "No files found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["lang"] = lang,
                ["path"] = pathPattern,
                ["excludeTests"] = excludeTests,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            return CreateToolResult(id, $"Found {results.Count} file(s).", structured);
        });
    }

    private JsonNode ExecuteMap(JsonNode? id, JsonNode? args)
    {
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 10);
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var map = reader.GetRepoMap(limit, lang, pathPattern, excludePaths, excludeTests);
            WorkspaceMetadataEnricher.Enrich(map, _dbPath);
            var structured = JsonSerializer.SerializeToNode(map, _jsonOptions)!.AsObject();
            structured["limit"] = limit;
            structured["lang"] = lang;
            structured["path"] = pathPattern;
            structured["excludeTests"] = excludeTests;
            return CreateToolResult(id, "Repo map returned.", structured);
        });
    }

    private JsonNode ExecuteAnalyzeSymbol(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 10);
        var lang = args?["lang"]?.GetValue<string>();
        var includeBody = args?["includeBody"]?.GetValue<bool>() ?? false;
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var analysis = reader.AnalyzeSymbol(query, limit, lang, includeBody, pathPattern, excludePaths, excludeTests);
            WorkspaceMetadataEnricher.Enrich(analysis, _dbPath);
            var structured = JsonSerializer.SerializeToNode(analysis, _jsonOptions)!.AsObject();
            structured["lang"] = lang;
            structured["path"] = pathPattern;
            structured["excludeTests"] = excludeTests;
            return CreateToolResult(id, "Symbol analysis returned.", structured);
        });
    }

    private JsonNode ExecuteStatus(JsonNode? id)
    {
        return WithDbReader(id, reader =>
        {
            var status = reader.GetStatus();
            WorkspaceMetadataEnricher.Enrich(status, _dbPath);
            status.GraphSupportedLanguages = ReferenceExtractor.GetSupportedLanguages().OrderBy(l => l).ToList();
            status.Version = _version;
            var structured = JsonSerializer.SerializeToNode(status, _jsonOptions)!.AsObject();
            return CreateToolResult(id, "Database stats returned.", structured);
        });
    }

    private JsonNode ExecuteOutline(JsonNode? id, JsonNode? args)
    {
        var path = args?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return CreateToolErrorResponse(id, "Missing required parameter: path");

        return WithDbReader(id, reader =>
        {
            var outline = reader.GetOutline(path);
            if (outline == null)
            {
                var emptyPayload = new JsonObject
                {
                    ["path"] = path,
                    ["error"] = "file not found in index"
                };
                AddFreshnessHint(emptyPayload, reader);
                return CreateToolResult(id, "File not found in index.", emptyPayload);
            }

            var structured = JsonSerializer.SerializeToNode(outline, _jsonOptions)!.AsObject();
            return CreateToolResult(id, $"Outline: {outline.SymbolCount} symbol(s) in {outline.TotalLines} lines.", structured);
        });
    }

    private JsonNode ExecuteExcerpt(JsonNode? id, JsonNode? args)
    {
        var path = args?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return CreateToolErrorResponse(id, "Missing required parameter: path");

        var startLine = args?["startLine"]?.GetValue<int>();
        if (startLine == null || startLine <= 0)
            return CreateToolErrorResponse(id, "Missing or invalid required parameter: startLine");

        var endLine = args?["endLine"]?.GetValue<int>() ?? startLine.Value;
        if (endLine < startLine.Value)
            return CreateToolErrorResponse(id, "endLine must be greater than or equal to startLine");

        var before = Math.Max(0, args?["before"]?.GetValue<int>() ?? 0);
        var after = Math.Max(0, args?["after"]?.GetValue<int>() ?? 0);

        return WithDbReader(id, reader =>
        {
            var excerpt = reader.GetExcerpt(path, startLine.Value, endLine, before, after);
            if (excerpt == null)
            {
                var emptyPayload = new JsonObject
                {
                    ["path"] = path,
                    ["count"] = 0
                };
                AddFreshnessHint(emptyPayload, reader);
                return CreateToolResult(id, "No excerpt found.", emptyPayload);
            }

            var payload = JsonSerializer.SerializeToNode(excerpt, _jsonOptions)!.AsObject();
            return CreateToolResult(id, "Excerpt returned.", payload);
        });
    }

    private JsonNode ExecuteBatchQuery(JsonNode? id, JsonNode? args)
    {
        var queries = args?["queries"]?.AsArray();
        if (queries == null || queries.Count == 0)
            return CreateToolErrorResponse(id, "Missing or empty required parameter: queries");

        const int maxBatchSize = 10;
        if (queries.Count > maxBatchSize)
            return CreateToolErrorResponse(id, $"Batch too large: {queries.Count} queries (max {maxBatchSize})");

        var resultsArray = new JsonArray();
        foreach (var q in queries)
        {
            var toolName = q?["tool"]?.GetValue<string>();
            var toolArgs = q?["arguments"];

            if (string.IsNullOrEmpty(toolName))
            {
                resultsArray.Add(new JsonObject { ["tool"] = toolName, ["error"] = "Missing tool name" });
                continue;
            }

            // Block write operations in batch / バッチ内では書き込み操作をブロック
            if (toolName == "index")
            {
                resultsArray.Add(new JsonObject { ["tool"] = toolName, ["error"] = "index is not allowed in batch_query (write operation)" });
                continue;
            }

            try
            {
                // Execute the tool and extract the structured content / ツールを実行し構造化コンテンツを抽出
                var response = toolName switch
                {
                    "search" => ExecuteSearch(null, toolArgs),
                    "definition" => ExecuteDefinition(null, toolArgs),
                    "references" => ExecuteReferences(null, toolArgs),
                    "callers" => ExecuteCallers(null, toolArgs),
                    "callees" => ExecuteCallees(null, toolArgs),
                    "symbols" => ExecuteSymbols(null, toolArgs),
                    "files" => ExecuteFiles(null, toolArgs),
                    "excerpt" => ExecuteExcerpt(null, toolArgs),
                    "map" => ExecuteMap(null, toolArgs),
                    "analyze_symbol" => ExecuteAnalyzeSymbol(null, toolArgs),
                    "status" => ExecuteStatus(null),
                    "outline" => ExecuteOutline(null, toolArgs),
                    "deps" => ExecuteDeps(null, toolArgs),
                    "languages" => ExecuteLanguages(null),
                    _ => null,
                };

                if (response == null)
                {
                    resultsArray.Add(new JsonObject { ["tool"] = toolName, ["error"] = $"Unknown tool: {toolName}" });
                    continue;
                }

                // Check for tool-level errors (validation failures return isError=true)
                // ツールレベルのエラーを確認（バリデーション失敗は isError=true を返す）
                var isError = response["result"]?["isError"]?.GetValue<bool>() ?? false;
                if (isError)
                {
                    var errorText = response["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "Unknown error";
                    resultsArray.Add(new JsonObject { ["tool"] = toolName, ["error"] = errorText });
                    continue;
                }

                var structured = response["result"]?["structuredContent"];
                resultsArray.Add(new JsonObject
                {
                    ["tool"] = toolName,
                    ["result"] = structured?.DeepClone()
                });
            }
            catch (Exception ex)
            {
                resultsArray.Add(new JsonObject { ["tool"] = toolName, ["error"] = ex.Message });
            }
        }

        var payload = new JsonObject
        {
            ["count"] = resultsArray.Count,
            ["results"] = resultsArray,
        };
        return CreateToolResult(id, $"Executed {resultsArray.Count} queries.", payload);
    }

    private JsonNode ExecuteDeps(JsonNode? id, JsonNode? args)
    {
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 50);
        var lang = args?["lang"]?.GetValue<string>();
        var pathPattern = args?["path"]?.GetValue<string>();
        var excludePaths = ReadStringList(args, "excludePaths");
        var excludeTests = args?["excludeTests"]?.GetValue<bool>() ?? false;
        var reverse = args?["reverse"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.GetFileDependencies(limit, lang, pathPattern, excludePaths, excludeTests, reverse);
            var payload = new JsonObject
            {
                ["count"] = results.Count,
                ["edges"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            var summary = results.Count > 0
                ? $"Found {results.Count} dependency edge(s)."
                : "No file dependencies found.";
            if (results.Count == 0)
                AddFreshnessHint(payload, reader);
            return CreateToolResult(id, summary, payload);
        });
    }

    private JsonNode ExecuteLanguages(JsonNode? id)
    {
        var langExtensions = FileIndexer.GetLanguageExtensions();
        var symbolLangs = SymbolExtractor.GetSupportedLanguages();
        var graphLangs = ReferenceExtractor.GetSupportedLanguages();

        // Build consolidated language info / 統合言語情報を構築
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

        var sorted = allLangs.OrderBy(kv => kv.Key).ToList();
        var languagesArray = new JsonArray();
        foreach (var (lang, info) in sorted)
        {
            var extArray = new JsonArray();
            foreach (var ext in info.Extensions.OrderBy(e => e))
                extArray.Add(ext);

            languagesArray.Add(new JsonObject
            {
                ["lang"] = lang,
                ["extensions"] = extArray,
                ["symbol_extraction"] = info.Symbols,
                ["graph_queries"] = info.Graph,
            });
        }

        var payload = new JsonObject { ["languages"] = languagesArray };
        var summary = $"{sorted.Count} languages supported. {symbolLangs.Count} with symbol extraction, {graphLangs.Count} with call-graph queries.";
        return CreateToolResult(id, summary, payload);
    }

    private JsonNode ExecuteIndex(JsonNode? id, JsonNode? args)
    {
        var path = args?["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path))
            return CreateToolErrorResponse(id, "Missing required parameter: path");

        var rebuild = args?["rebuild"]?.GetValue<bool>() ?? false;
        var projectPath = Path.GetFullPath(path);

        // Prevent path traversal — only allow indexing within current working directory
        // パストラバーサル防止 — カレントディレクトリ配下のみインデックスを許可
        var cwd = Path.GetFullPath(".");
        if (!projectPath.StartsWith(cwd + Path.DirectorySeparatorChar) && projectPath != cwd)
            return CreateToolErrorResponse(id, "Path must be within the current working directory");

        if (!Directory.Exists(projectPath))
            return CreateToolErrorResponse(id, "Directory not found");

        // Determine DB path — use the provided _dbPath or default
        // DBパスを決定 — 指定された_dbPathまたはデフォルトを使用
        using var db = new DbContext(_dbPath);

        if (rebuild)
            db.DropAll();

        db.InitializeSchema();

        var writer = new DbWriter(db.Connection);
        var indexer = new FileIndexer(projectPath);

        // Purge stale files / 古いファイルをパージ
        var purged = writer.PurgeStaleFiles(projectPath);

        // Scan and index / スキャン・インデックス
        var files = indexer.ScanFiles();
        int processed = 0, skipped = 0, errors = 0;

        foreach (var filePath in files)
        {
            try
            {
                var (record, content, _) = indexer.BuildRecord(filePath);
                var existingId = writer.GetUnchangedFileId(record.Path, record.Modified, record.Checksum);
                if (existingId != null)
                {
                    skipped++;
                    processed++;
                    continue;
                }

                using var txn = writer.BeginTransaction();
                var fileId = writer.UpsertFile(record);
                var chunks = ChunkSplitter.Split(fileId, content);
                writer.InsertChunks(chunks);
                var symbols = SymbolExtractor.Extract(fileId, record.Lang, content);
                writer.InsertSymbols(symbols);
                var references = ReferenceExtractor.Extract(fileId, record.Lang, content, symbols);
                writer.InsertReferences(references);
                txn.Commit();
            }
            catch
            {
                errors++;
            }
            processed++;
        }

        writer.OptimizeFts();
        var (totalFiles, totalChunks, totalSymbols, totalReferences) = writer.GetCounts();

        var structured = new JsonObject
        {
            ["path"] = projectPath,
            ["rebuild"] = rebuild,
            ["summary"] = new JsonObject
            {
                ["files"] = totalFiles,
                ["chunks"] = totalChunks,
                ["symbols"] = totalSymbols,
                ["references"] = totalReferences,
                ["scanned"] = files.Count,
                ["skipped"] = skipped,
                ["purged"] = purged,
                ["errors"] = errors
            }
        };
        return CreateToolResult(id, "Indexing complete.", structured);
    }

}
