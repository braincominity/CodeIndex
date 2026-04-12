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
public partial class McpServer
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

    // Tool definitions are in McpToolDefinitions.cs / ツール定義は McpToolDefinitions.cs に分離


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

        Database.DbDebug.ResetContext();
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
                "impact_analysis" => ExecuteImpactAnalysis(id, args),
                "languages" => ExecuteLanguages(id),
                "validate" => ExecuteValidate(id, args),
                "unused_symbols" => ExecuteUnusedSymbols(id, args),
                "symbol_hotspots" => ExecuteSymbolHotspots(id, args),
                "ping" => ExecutePing(id),
                "index" => ExecuteIndex(id, args),
                "suggest_improvement" => ExecuteSuggestImprovement(id, args),
                _ => CreateErrorResponse(id, -32602, $"Unknown tool: {toolName}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cdidx-mcp] Tool error ({toolName}): {ex.Message}");
            Database.DbDebug.DumpToStderr(ex);
            return CreateToolErrorResponse(id, $"Error executing {toolName}: {ex.Message}");
        }
        finally
        {
            Database.DbDebug.ResetContext();
        }
    }

    // Tool implementations are in McpToolHandlers.cs / ツール実装は McpToolHandlers.cs に分離

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

    /// <summary>
    /// Build MCP tool annotations for the suggest_improvement tool.
    /// suggest_improvementツール用のMCPツールアノテーションを構築。
    /// Not read-only (writes suggestion to disk), not destructive,
    /// idempotent (duplicate submissions are safely deduplicated).
    /// 読み取り専用ではない（提案をディスクに書き込む）、破壊的ではない、
    /// 冪等（重複送信は安全に排除される）。
    /// </summary>
    private static JsonObject SuggestionAnnotations() => new()
    {
        ["readOnlyHint"] = false,
        ["destructiveHint"] = false,
        ["idempotentHint"] = true,
        ["openWorldHint"] = false
    };
}
