using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private const string ProtocolVersion = "2024-11-05";
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
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "cdidx",
                ["version"] = _version
            }
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
                "Full-text search across indexed code chunks using FTS5. Returns matching code snippets with file path, line numbers, and content. / FTS5を使ったコードチャンクの全文検索。ファイルパス、行番号、コンテンツ付きのコードスニペットを返す。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search query text" },
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 },
                        ["lang"] = new JsonObject { ["type"] = "string", ["description"] = "Filter by language (e.g. csharp, python, javascript)" },
                        ["rawQuery"] = new JsonObject { ["type"] = "boolean", ["description"] = "Use raw FTS5 syntax instead of literal-safe quoting", ["default"] = false }
                    },
                    ["required"] = new JsonArray { "query" }
                }),
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
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 }
                    }
                }),
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
                        ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results (default: 20)", ["default"] = 20 }
                    }
                }),
            CreateToolDefinition(
                "status",
                "Get database statistics: file count, chunk count, symbol count, and language breakdown. / DB統計情報を取得：ファイル数、チャンク数、シンボル数、言語別内訳。",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }),
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
                })
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
                "symbols" => ExecuteSymbols(id, args),
                "files" => ExecuteFiles(id, args),
                "status" => ExecuteStatus(id),
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
    /// Clamp limit to a safe range to prevent resource exhaustion.
    /// リソース枯渇を防ぐためlimitを安全な範囲にクランプ。
    /// </summary>
    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private JsonNode ExecuteSearch(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query))
            return CreateToolErrorResponse(id, "Missing required parameter: query");
        if (query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");

        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);
        var lang = args?["lang"]?.GetValue<string>();
        var rawQuery = args?["rawQuery"]?.GetValue<bool>() ?? false;

        return WithDbReader(id, reader =>
        {
            var results = reader.Search(query, limit, lang, rawQuery);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["rawQuery"] = rawQuery,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                return CreateToolResult(id, "No results found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["rawQuery"] = rawQuery,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            return CreateToolResult(id, $"Found {results.Count} search result(s).", structured);
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

        return WithDbReader(id, reader =>
        {
            var results = reader.SearchSymbols(query, limit, kind, lang);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["kind"] = kind,
                    ["lang"] = lang,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                return CreateToolResult(id, "No symbols found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["kind"] = kind,
                ["lang"] = lang,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            return CreateToolResult(id, $"Found {results.Count} symbol(s).", structured);
        });
    }

    private JsonNode ExecuteFiles(JsonNode? id, JsonNode? args)
    {
        var query = args?["query"]?.GetValue<string>();
        if (query != null && query.Length > MaxQueryLength)
            return CreateToolErrorResponse(id, $"Query too long (max {MaxQueryLength} characters)");
        var lang = args?["lang"]?.GetValue<string>();
        var limit = ClampLimit(args?["limit"]?.GetValue<int>() ?? 20);

        return WithDbReader(id, reader =>
        {
            var results = reader.ListFiles(query, limit, lang);
            if (results.Count == 0)
            {
                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["lang"] = lang,
                    ["count"] = 0,
                    ["results"] = new JsonArray()
                };
                return CreateToolResult(id, "No files found.", payload);
            }

            var structured = new JsonObject
            {
                ["query"] = query,
                ["lang"] = lang,
                ["count"] = results.Count,
                ["results"] = JsonSerializer.SerializeToNode(results, _jsonOptions)
            };
            return CreateToolResult(id, $"Found {results.Count} file(s).", structured);
        });
    }

    private JsonNode ExecuteStatus(JsonNode? id)
    {
        return WithDbReader(id, reader =>
        {
            var status = reader.GetStatus();
            var structured = JsonSerializer.SerializeToNode(status, _jsonOptions)!.AsObject();
            return CreateToolResult(id, "Database stats returned.", structured);
        });
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
                txn.Commit();
            }
            catch
            {
                errors++;
            }
            processed++;
        }

        writer.OptimizeFts();
        var (totalFiles, totalChunks, totalSymbols) = writer.GetCounts();

        var structured = new JsonObject
        {
            ["path"] = projectPath,
            ["rebuild"] = rebuild,
            ["summary"] = new JsonObject
            {
                ["files"] = totalFiles,
                ["chunks"] = totalChunks,
                ["symbols"] = totalSymbols,
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
        db.InitializeSchema();
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

    private static JsonObject CreateToolDefinition(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }
}
