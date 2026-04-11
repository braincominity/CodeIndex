using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Mcp;

/// <summary>
/// MCP (Model Context Protocol) server over stdin/stdout using JSON-RPC 2.0.
/// stdin/stdout上のJSON-RPC 2.0によるMCPサーバー。
/// Protocol version: 2024-11-05
/// </summary>
public class McpServer
{
    private readonly string _dbPath;
    private readonly string _version;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _running = true;

    private const string ProtocolVersion = "2025-03-26";
    private const int MaxLimit = 200;
    private const int MaxQueryLength = 1000;
    private const int MaxLineLength = 1_000_000; // 1 MB per JSON-RPC message / 1メッセージあたり最大1MB

    public McpServer(string dbPath, string version)
    {
        _dbPath = dbPath;
        _version = version;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
    }

    /// <summary>
    /// Run the MCP server loop, reading JSON-RPC messages from stdin and writing responses to stdout.
    /// MCPサーバーループを実行。stdinからJSON-RPCメッセージを読み、stdoutにレスポンスを書く。
    /// </summary>
    public async Task RunAsync()
    {
        // Use stderr for logging so stdout stays clean for JSON-RPC
        // stdoutをJSON-RPC用にクリーンに保つため、ログはstderrに出力
        Console.Error.WriteLine($"[cdidx-mcp] Starting MCP server v{_version} (db: {_dbPath})");

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        using var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        while (_running)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                break; // stdin closed / stdinが閉じられた

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Reject oversized messages to prevent memory exhaustion
            // メモリ枯渇を防ぐため巨大メッセージを拒否
            if (line.Length > MaxLineLength)
            {
                Console.Error.WriteLine($"[cdidx-mcp] Message too large ({line.Length} bytes), rejecting");
                var errorResponse = CreateErrorResponse(null, -32700, "Message too large");
                await writer.WriteLineAsync(errorResponse.ToJsonString(_jsonOptions));
                continue;
            }

            try
            {
                var request = JsonNode.Parse(line);
                if (request == null)
                    continue;

                var response = HandleMessage(request);
                if (response != null)
                {
                    await writer.WriteLineAsync(response.ToJsonString(_jsonOptions));
                }
            }
            catch (JsonException ex)
            {
                // Parse error / パースエラー
                Console.Error.WriteLine($"[cdidx-mcp] JSON parse error: {ex.Message}");
                var errorResponse = CreateErrorResponse(null, -32700, "Parse error");
                await writer.WriteLineAsync(errorResponse.ToJsonString(_jsonOptions));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[cdidx-mcp] Error: {ex.Message}");
            }
        }

        Console.Error.WriteLine("[cdidx-mcp] Server stopped.");
    }

    /// <summary>
    /// Route a JSON-RPC message to the appropriate handler.
    /// JSON-RPCメッセージを適切なハンドラにルーティング。
    /// </summary>
    internal JsonNode? HandleMessage(JsonNode request)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"];

        // Notifications (no id) don't get a response / 通知（idなし）にはレスポンスなし
        if (method == "notifications/initialized" || method == "notifications/cancelled")
            return null;

        if (method == null)
        {
            if (id != null)
                return CreateErrorResponse(id, -32600, "Invalid request: missing method");
            return null;
        }

        return method switch
        {
            "initialize" => HandleInitialize(id, request["params"]),
            "tools/list" => HandleToolsList(id),
            "tools/call" => HandleToolsCall(id, request["params"]),
            "ping" => CreateSuccessResponse(id, new JsonObject()),
            _ => CreateErrorResponse(id, -32601, $"Method not found: {method}"),
        };
    }

    /// <summary>
    /// Handle the initialize handshake.
    /// initializeハンドシェイクを処理。
    /// </summary>
    private JsonNode HandleInitialize(JsonNode? id, JsonNode? _params)
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "cdidx",
                ["version"] = _version
            },
            // Server instructions — tool-selection guidance for AI clients
            // サーバー指示 — AIクライアント向けツール選択ガイダンス
            ["instructions"] = BuildInstructions()
        };
        return CreateSuccessResponse(id, result);
    }

    /// <summary>
    /// Return the list of available tools.
    /// 利用可能なツール一覧を返す。
    /// </summary>
    private JsonNode HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray
        {
            CreateToolDefinition(
                "search",
                "Full-text search across indexed code chunks using FTS5. Returns compact match-centered snippets with line metadata. / FTS5を使ったコードチャンクの全文検索。一致中心の軽量スニペットと行メタデータを返す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search query text" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language (e.g. csharp, python, javascript)" },
                        ["snippetLines"] = new JsonObject { ["type"] = "integer", ["description"] = "Max snippet lines per result (default: 8, max: 20)", ["default"] = 8, ["minimum"] = 1, ["maximum"] = SearchSnippetFormatter.MaxSnippetLines },
                        ["rawQuery"] = new JsonObject { ["type"] = "boolean", ["description"] = "Use raw FTS5 syntax instead of literal-safe quoting", ["default"] = false },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "definition",
                "Resolve symbol definitions with definition ranges, signatures, and optional body content. / 定義範囲、シグネチャ、必要に応じて本体内容付きでシンボル定義を解決。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Symbol name pattern to resolve" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by symbol kind" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["includeBody"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include body content when body ranges are available", ["default"] = false },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "references",
                "Search indexed symbol references such as call sites. / 呼び出し箇所などのインデックス済みシンボル参照を検索。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Referenced symbol name pattern to search for" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by reference kind (for example: call, instantiate)" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "callers",
                "Find caller symbols that reference a callee. / 指定シンボルを参照している呼び出し元シンボルを探す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Callee symbol name pattern to search for" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by reference kind (for example: call, instantiate)" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "callees",
                "Find callees used by a caller/container symbol. / 呼び出し元シンボルが使っている呼び出し先を探す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Caller/container symbol name pattern to search for" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by reference kind (for example: call, instantiate)" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "symbols",
                "Search for code symbols (functions, classes, interfaces, imports) by name pattern. / シンボル（関数、クラス、インターフェース、import）を名前パターンで検索。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Symbol name pattern to search for" },
                        ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by symbol kind (function, class, interface, import, etc.)" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict matches to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "files",
                "List indexed files, optionally filtered by name pattern and language. / インデックス済みファイルを一覧（名前パターン・言語でフィルタ可能）。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "File path pattern to filter by" },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Additional path filter text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false },
                        ["since"] = new JsonObject { ["type"] = "string", ["description"] = "Filter to files modified since this ISO 8601 timestamp" }
                    }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "excerpt",
                "Reconstruct a file excerpt from indexed chunks for a given line range. / 指定行範囲について、インデックス済みチャンクからファイル抜粋を再構成。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Indexed file path" },
                        ["startLine"] = new JsonObject { ["type"] = "integer", ["description"] = "Start line (1-based)" },
                        ["endLine"] = new JsonObject { ["type"] = "integer", ["description"] = "End line (default: startLine)" },
                        ["before"] = new JsonObject { ["type"] = "integer", ["description"] = "Extra context lines before the range", ["default"] = 0 },
                        ["after"] = new JsonObject { ["type"] = "integer", ["description"] = "Extra context lines after the range", ["default"] = 0 }
                    },
                    ["required"] = new JsonArray { "path", "startLine" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "map",
                "Return a repo-level overview with languages, modules, top files, and likely entrypoints. / 言語、モジュール、主要ファイル、推定エントリポイントを含むリポジトリ俯瞰情報を返す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max items per section (default: 10)", ["default"] = 10 },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "analyze_symbol",
                "Bundle definition, nearby symbols, references, callers, callees, file metadata, and graph-support metadata for one symbol query. / 1つのシンボルクエリに対して、定義、近傍シンボル、参照、caller、callee、ファイルメタデータ、グラフ対応メタデータをまとめて返す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Symbol name to inspect" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max items per section (default: 10)", ["default"] = 10 },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["includeBody"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include body content in definitions when available", ["default"] = false },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Prefer or restrict paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude any paths containing these texts" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude likely test files", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "status",
                "Get database statistics: file count, chunk count, symbol count, reference count, and language breakdown. / DB統計情報を取得：ファイル数、チャンク数、シンボル数、参照数、言語別内訳。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "outline",
                "Return the symbol outline of a single indexed file: all functions, classes, imports with line numbers, signatures, and nesting. / 1ファイルのシンボルアウトラインを返す: 関数、クラス、importの行番号、シグネチャ、ネスト構造。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Indexed file path (e.g. src/app.cs)" },
                    },
                    ["required"] = new JsonArray { "path" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "deps",
                "Show file-level dependency edges from the indexed reference graph. / インデックス済み参照グラフか��ファイル間の依存エッジを返す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max edges (default: 50)", ["default"] = 50 },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language" },
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Restrict source files to paths containing this text" },
                        ["excludePaths"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Exclude paths" },
                        ["excludeTests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude test files", ["default"] = false },
                        ["reverse"] = new JsonObject { ["type"] = "boolean", ["description"] = "Reverse lookup: show files that depend ON the matched path", ["default"] = false }
                    }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "languages",
                "List all supported languages with their file extensions and capabilities (symbol extraction, call-graph queries). No database required. / 対応言語一覧を拡張子・機能（シンボル抽出、コールグラフ対応）付きで返す。DB不要。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "batch_query",
                "Execute multiple read-only queries in a single call and return all results. Dramatically reduces round-trips for AI agents. / 複数の読み取り専用クエリを1回の呼び出しで実行し、全結果を返す。AIエージェントの往復回数を劇的に削減。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["queries"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Array of {tool, arguments} objects. Only read-only tools are allowed (not index).",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["tool"] = new JsonObject { ["type"] = "string", ["description"] = "Tool name (e.g. search, definition, symbols)" },
                                    ["arguments"] = new JsonObject { ["type"] = "object", ["description"] = "Tool arguments" }
                                },
                                ["required"] = new JsonArray { "tool" }
                            }
                        }
                    },
                    ["required"] = new JsonArray { "queries" }
                },
                ReadOnlyAnnotations()),
            CreateToolDefinition(
                "index",
                "Index or re-index a project directory. Scans source files, extracts symbols, and builds FTS5 search index. / プロジェクトディレクトリをインデックス（再インデックス）。ソースファイルをスキャンし、シンボルを抽出してFTS5検索インデックスを構築。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Project directory path to index" },
                        ["rebuild"] = new JsonObject { ["type"] = "boolean", ["description"] = "Delete existing index and rebuild from scratch (default: false)", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "path" }
                },
                IndexAnnotations())
        };

        var result = new JsonObject { ["tools"] = tools };
        return CreateSuccessResponse(id, result);
    }

    /// <summary>
    /// Execute a tool call.
    /// ツール呼び出しを実行。
    /// </summary>
    private JsonNode HandleToolsCall(JsonNode? id, JsonNode? callParams)
    {
        var toolName = callParams?["name"]?.GetValue<string>();
        var args = callParams?["arguments"];

        if (toolName == null)
            return CreateErrorResponse(id, -32602, "Missing tool name");

        try
        {
            return toolName switch
            {
                "search" => ExecuteSearch(id, args),
                "definition" => ExecuteDefinition(id, args),
                "references" => ExecuteReferences(id, args),
                "callers" => ExecuteCallers(id, args),
                "callees" => ExecuteCallees(id, args),
                "symbols" => ExecuteSymbols(id, args),
                "files" => ExecuteFiles(id, args),
                "excerpt" => ExecuteExcerpt(id, args),
                "map" => ExecuteMap(id, args),
                "analyze_symbol" => ExecuteAnalyzeSymbol(id, args),
                "status" => ExecuteStatus(id),
                "outline" => ExecuteOutline(id, args),
                "batch_query" => ExecuteBatchQuery(id, args),
                "deps" => ExecuteDeps(id, args),
                "languages" => ExecuteLanguages(id),
                "index" => ExecuteIndex(id, args),
                _ => CreateErrorResponse(id, -32602, $"Unknown tool: {toolName}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cdidx-mcp] Tool error ({toolName}): {ex.Message}");
            return CreateToolErrorResponse(id, $"Error executing {toolName}: {ex.Message}");
        }
    }

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

        return WithDbReader(id, reader =>
        {
            var results = reader.Search(query, limit, lang, rawQuery, pathPattern, excludePaths, excludeTests);
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

                // Extract structured content from the tool response
                // ツールレスポンスから構造化コンテンツを抽出
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

    // --- DB helper / DBヘルパー ---

    private JsonNode WithDbReader(JsonNode? id, Func<DbReader, JsonNode> action)
    {
        if (!File.Exists(_dbPath))
            return CreateToolErrorResponse(id, $"Database not found: {_dbPath}. Run 'cdidx index <projectPath>' first.");

        using var db = new DbContext(_dbPath);
        db.TryMigrateForRead();
        var reader = new DbReader(db.Connection);
        return action(reader);
    }

    // --- JSON-RPC helpers / JSON-RPCヘルパー ---

    private static JsonObject CreateSuccessResponse(JsonNode? id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (id != null)
            response["id"] = JsonNode.Parse(id.ToJsonString());
        return response;
    }

    private static JsonObject CreateErrorResponse(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        if (id != null)
            response["id"] = JsonNode.Parse(id.ToJsonString());
        return response;
    }

    /// <summary>
    /// Create a tool result response (MCP format).
    /// ツール結果レスポンスを作成（MCP形式）。
    /// </summary>
    private static JsonObject CreateToolResult(JsonNode? id, string text, JsonNode? structuredContent = null)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
        if (structuredContent != null)
            result["structuredContent"] = structuredContent;
        return CreateSuccessResponse(id, result);
    }

    /// <summary>
    /// Create a tool error response (MCP format with isError flag).
    /// ツールエラーレスポンスを作成（isErrorフラグ付きMCP形式）。
    /// </summary>
    private static JsonObject CreateToolErrorResponse(JsonNode? id, string message)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = message
                }
            },
            ["isError"] = true
        };
        return CreateSuccessResponse(id, result);
    }

    private static JsonObject CreateToolDefinition(string name, string description, JsonObject inputSchema,
        JsonObject? annotations = null)
    {
        var def = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
        if (annotations != null)
            def["annotations"] = annotations;
        return def;
    }

    /// <summary>
    /// Build MCP tool annotations for a read-only query tool.
    /// 読み取り専用クエリツール用のMCPツールアノテーションを構築。
    /// </summary>
    private static JsonObject ReadOnlyAnnotations() => new()
    {
        ["readOnlyHint"] = true,
        ["destructiveHint"] = false,
        ["idempotentHint"] = true,
        ["openWorldHint"] = false
    };

    /// <summary>
    /// Build MCP tool annotations for the index (write) tool.
    /// index（書き込み）ツール用のMCPツールアノテーションを構築。
    /// Destructive because --rebuild drops the DB; not idempotent because
    /// re-indexing replaces chunks/symbols/references per file.
    /// --rebuildでDBを削除するため破壊的。再インデックスはファイルごとに
    /// チャンク・シンボル・参照を置き換えるため冪等ではない。
    /// </summary>
    private static JsonObject IndexAnnotations() => new()
    {
        ["readOnlyHint"] = false,
        ["destructiveHint"] = true,
        ["idempotentHint"] = false,
        ["openWorldHint"] = false
    };
}
