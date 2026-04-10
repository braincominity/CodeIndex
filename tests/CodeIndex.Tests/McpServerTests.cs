using System.Text.Json.Nodes;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Indexer;
using CodeIndex.Mcp;
using CodeIndex.Models;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for McpServer JSON-RPC message handling.
/// McpServerのJSON-RPCメッセージ処理のテスト。
/// </summary>
public class McpServerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;
    private readonly McpServer _server;

    public McpServerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_mcp_test_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();

        // Seed test data / テストデータを投入
        var writer = new DbWriter(_db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = "src/app.cs",
            Lang = "csharp",
            Size = 200,
            Lines = 10,
            Modified = new DateTime(2024, 1, 1),
            Checksum = "abc123",
        });
        writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 10,
            Content = "public class App { public void Run() { } }",
        }]);
        writer.InsertSymbols([new SymbolRecord
        {
            FileId = fileId,
            Kind = "class",
            Name = "App",
            Line = 1,
            StartLine = 1,
            EndLine = 1,
            Signature = "public class App { public void Run() { } }",
        },
        new SymbolRecord
        {
            FileId = fileId,
            Kind = "function",
            Name = "Run",
            Line = 1,
            StartLine = 1,
            EndLine = 1,
            Signature = "public void Run() { }",
            ContainerKind = "class",
            ContainerName = "App",
        }]);

        _server = new McpServer(_dbPath, ConsoleUi.LoadVersion());
    }

    private void InsertIndexedFile(string path, string lang, string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var writer = new DbWriter(_db.Connection);
        var fileId = writer.UpsertFile(new FileRecord
        {
            Path = path,
            Lang = lang,
            Size = normalized.Length,
            Lines = lines.Length,
            Modified = new DateTime(2024, 1, 1),
            Checksum = Guid.NewGuid().ToString("N"),
        });
        writer.InsertChunks([new ChunkRecord
        {
            FileId = fileId,
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = lines.Length,
            Content = normalized,
        }]);

        var symbols = SymbolExtractor.Extract(fileId, lang, normalized);
        writer.InsertSymbols(symbols);
        writer.InsertReferences(ReferenceExtractor.Extract(fileId, lang, normalized, symbols));
    }

    // --- Protocol tests / プロトコルテスト ---

    [Fact]
    public void Initialize_ReturnsProtocolVersion()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, response["id"]!.GetValue<int>());
        Assert.Equal("2024-11-05", response["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("cdidx", response["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(ConsoleUi.LoadVersion(), response["result"]!["serverInfo"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void Initialize_ReturnsToolsCapability()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.NotNull(response["result"]!["capabilities"]!["tools"]);
        Assert.False(response["result"]!["capabilities"]!["tools"]!["listChanged"]!.GetValue<bool>());
    }

    [Fact]
    public void Initialize_ReturnsInstructions()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!;
        var response = _server.HandleMessage(request)!;

        var instructions = response["result"]!["instructions"]?.GetValue<string>();
        Assert.NotNull(instructions);
        Assert.Contains("map", instructions!);
        Assert.Contains("analyze_symbol", instructions);
        Assert.Contains("search", instructions);
    }

    [Fact]
    public void Notification_Initialized_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    [Fact]
    public void Notification_Cancelled_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","method":"notifications/cancelled"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    [Fact]
    public void Ping_ReturnsEmptyResult()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":99,"method":"ping"}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(99, response["id"]!.GetValue<int>());
        Assert.NotNull(response["result"]);
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"unknown/method"}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
        Assert.Contains("Method not found", response["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void MissingMethod_ReturnsInvalidRequest()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32600, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void MissingMethodAndId_ReturnsNull()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0"}""")!;
        var response = _server.HandleMessage(request);

        Assert.Null(response);
    }

    // --- tools/list tests / ツール一覧テスト ---

    [Fact]
    public void ToolsList_Returns12Tools()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        Assert.Equal(12, tools.Count);

        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("search", names);
        Assert.Contains("definition", names);
        Assert.Contains("references", names);
        Assert.Contains("callers", names);
        Assert.Contains("callees", names);
        Assert.Contains("symbols", names);
        Assert.Contains("files", names);
        Assert.Contains("excerpt", names);
        Assert.Contains("map", names);
        Assert.Contains("analyze_symbol", names);
        Assert.Contains("status", names);
        Assert.Contains("index", names);
    }

    [Fact]
    public void ToolsList_SearchHasRequiredQueryParam()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var searchTool = tools.First(t => t!["name"]!.GetValue<string>() == "search")!;
        var required = searchTool["inputSchema"]!["required"]!.AsArray();
        Assert.Contains("query", required.Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void ToolsList_SearchIncludesPathFilterParams()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var searchTool = tools.First(t => t!["name"]!.GetValue<string>() == "search")!;
        var properties = searchTool["inputSchema"]!["properties"]!;

        Assert.NotNull(properties["path"]);
        Assert.NotNull(properties["excludePaths"]);
        Assert.NotNull(properties["excludeTests"]);
    }

    [Fact]
    public void ToolsList_IndexHasRequiredPathParam()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var indexTool = tools.First(t => t!["name"]!.GetValue<string>() == "index")!;
        var required = indexTool["inputSchema"]!["required"]!.AsArray();
        Assert.Contains("path", required.Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void ToolsList_QueryToolsHaveReadOnlyAnnotations()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var queryToolNames = new[] { "search", "definition", "references", "callers", "callees", "symbols", "files", "excerpt", "map", "analyze_symbol", "status" };

        foreach (var name in queryToolNames)
        {
            var tool = tools.First(t => t!["name"]!.GetValue<string>() == name)!;
            var annotations = tool["annotations"];
            Assert.NotNull(annotations);
            Assert.True(annotations!["readOnlyHint"]!.GetValue<bool>(), $"{name} should have readOnlyHint=true");
            Assert.False(annotations["destructiveHint"]!.GetValue<bool>(), $"{name} should have destructiveHint=false");
            Assert.True(annotations["idempotentHint"]!.GetValue<bool>(), $"{name} should have idempotentHint=true");
            Assert.False(annotations["openWorldHint"]!.GetValue<bool>(), $"{name} should have openWorldHint=false");
        }
    }

    [Fact]
    public void ToolsList_IndexToolHasWriteAnnotations()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var response = _server.HandleMessage(request)!;

        var tools = response["result"]!["tools"]!.AsArray();
        var indexTool = tools.First(t => t!["name"]!.GetValue<string>() == "index")!;
        var annotations = indexTool["annotations"];
        Assert.NotNull(annotations);
        Assert.False(annotations!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(annotations["destructiveHint"]!.GetValue<bool>());
        Assert.False(annotations["idempotentHint"]!.GetValue<bool>());
        Assert.False(annotations["openWorldHint"]!.GetValue<bool>());
    }

    // --- tools/call tests / ツール呼び出しテスト ---

    [Fact]
    public void ToolsCall_Search_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"App"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Found 1 search result", text);

        var structured = response["result"]!["structuredContent"]!;
        Assert.Equal(1, structured["count"]!.GetValue<int>());
        Assert.Equal("src/app.cs", structured["results"]![0]!["path"]!.GetValue<string>());
        Assert.NotNull(structured["results"]![0]!["snippet"]);
        Assert.NotNull(structured["results"]![0]!["matchLines"]);
        Assert.NotNull(structured["results"]![0]!["highlights"]);
        Assert.Null(structured["results"]![0]!["content"]);
    }

    [Fact]
    public void ToolsCall_Search_NoResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"nonexistent_xyz_123"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No results found", text);
        Assert.Equal(0, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void ToolsCall_Search_RawQuerySupportsFtsSyntax()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"Ap*","rawQuery":true}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(1, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
        Assert.True(response["result"]!["structuredContent"]!["rawQuery"]!.GetValue<bool>());
        Assert.Equal("src/app.cs", response["result"]!["structuredContent"]!["results"]![0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Search_SnippetLinesControlsExcerptLength()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"App","snippetLines":3}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(3, response["result"]!["structuredContent"]!["snippetLines"]!.GetValue<int>());
        var snippet = response["result"]!["structuredContent"]!["results"]![0]!["snippet"]!.GetValue<string>();
        Assert.True(snippet.Split('\n').Length <= 3);
    }

    [Fact]
    public void ToolsCall_Search_ExcludeTests_ReturnsOnlySourceMatches()
    {
        InsertIndexedFile("tests/app_test.cs", "csharp", "public class AppTests { public void RunScenario() { var app = new App(); } }");

        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"App","excludeTests":true}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(1, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
        Assert.True(response["result"]!["structuredContent"]!["excludeTests"]!.GetValue<bool>());
        Assert.Equal("src/app.cs", response["result"]!["structuredContent"]!["results"]![0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Map_ReturnsRepoOverview()
    {
        InsertIndexedFile("src/Program.cs", "csharp", "public class Program\n{\n    public static void Main(string[] args)\n    {\n        var app = new App();\n    }\n}\n");

        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"map","arguments":{"limit":5,"excludeTests":true}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(5, response["result"]!["structuredContent"]!["limit"]!.GetValue<int>());
        Assert.NotNull(response["result"]!["structuredContent"]!["languages"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["modules"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["topFiles"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["indexedAt"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["projectRoot"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["workspaceIndexedAt"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["workspaceLatestModified"]);
        Assert.Contains("Main", response["result"]!["structuredContent"]!["entrypoints"]!.ToJsonString());
    }

    [Fact]
    public void ToolsCall_AnalyzeSymbol_ReturnsBundledContext()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"analyze_symbol","arguments":{"query":"Run","includeBody":true}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("Run", response["result"]!["structuredContent"]!["query"]!.GetValue<string>());
        Assert.NotNull(response["result"]!["structuredContent"]!["file"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["definitions"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["nearbySymbols"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["callers"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["callees"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["workspaceIndexedAt"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["workspaceLatestModified"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["projectRoot"]);
        Assert.True(response["result"]!["structuredContent"]!["graphSupported"]!.GetValue<bool>());
    }

    [Fact]
    public void ToolsCall_AnalyzeSymbol_UnsupportedLanguage_ReturnsGraphSupportHint()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"analyze_symbol","arguments":{"query":"Heading","lang":"markdown"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("markdown", response["result"]!["structuredContent"]!["graphLanguage"]!.GetValue<string>());
        Assert.False(response["result"]!["structuredContent"]!["graphSupported"]!.GetValue<bool>());
        Assert.Contains("Use search, definition, excerpt, or files instead.", response["result"]!["structuredContent"]!["graphSupportReason"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_References_ReturnsIndexedReference()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return Run(user)\n");

        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"references","arguments":{"query":"Run"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(1, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
        Assert.Equal("login", response["result"]!["structuredContent"]!["results"]![0]!["containerName"]!.GetValue<string>());
        Assert.Equal("call", response["result"]!["structuredContent"]!["results"]![0]!["referenceKind"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_References_UnsupportedLanguage_ReturnsGraphSupportHint()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"references","arguments":{"query":"Run","lang":"markdown"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("markdown", response["result"]!["structuredContent"]!["graphLanguage"]!.GetValue<string>());
        Assert.False(response["result"]!["structuredContent"]!["graphSupported"]!.GetValue<bool>());
        Assert.Contains("not indexed", response["result"]!["structuredContent"]!["graphSupportReason"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Callers_ReturnsCallerSummary()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return Run(user)\n");

        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"callers","arguments":{"query":"Run"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(1, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
        Assert.Equal("login", response["result"]!["structuredContent"]!["results"]![0]!["callerName"]!.GetValue<string>());
        Assert.Equal("Run", response["result"]!["structuredContent"]!["results"]![0]!["calleeName"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Callers_UnsupportedLanguage_ReturnsGraphSupportHint()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"callers","arguments":{"query":"Run","lang":"markdown"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("markdown", response["result"]!["structuredContent"]!["graphLanguage"]!.GetValue<string>());
        Assert.False(response["result"]!["structuredContent"]!["graphSupported"]!.GetValue<bool>());
        Assert.Contains("not indexed", response["result"]!["structuredContent"]!["graphSupportReason"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Callees_ReturnsCalleeSummary()
    {
        InsertIndexedFile("src/session.py", "python", "def login(user, password):\n    return Run(user)\n");

        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"callees","arguments":{"query":"login"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(1, response["result"]!["structuredContent"]!["count"]!.GetValue<int>());
        Assert.Equal("login", response["result"]!["structuredContent"]!["results"]![0]!["callerName"]!.GetValue<string>());
        Assert.Equal("Run", response["result"]!["structuredContent"]!["results"]![0]!["calleeName"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Callees_UnsupportedLanguage_ReturnsGraphSupportHint()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"callees","arguments":{"query":"Run","lang":"markdown"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal("markdown", response["result"]!["structuredContent"]!["graphLanguage"]!.GetValue<string>());
        Assert.False(response["result"]!["structuredContent"]!["graphSupported"]!.GetValue<bool>());
        Assert.Contains("not indexed", response["result"]!["structuredContent"]!["graphSupportReason"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Search_MissingQuery_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("query", text);
    }

    [Fact]
    public void ToolsCall_Symbols_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"symbols","arguments":{"query":"App"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Found 1 symbol", text);
        Assert.Equal("App", response["result"]!["structuredContent"]!["results"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("class", response["result"]!["structuredContent"]!["results"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal("public class App { public void Run() { } }", response["result"]!["structuredContent"]!["results"]![0]!["signature"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Symbols_FilterByKind()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"symbols","arguments":{"kind":"function"}}}""")!;
        var response = _server.HandleMessage(request)!;

        var results = response["result"]!["structuredContent"]!["results"]!.AsArray();
        Assert.Single(results);
        Assert.Equal("Run", results[0]!["name"]!.GetValue<string>());
        Assert.Equal("function", results[0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Definition_ReturnsDefinitionContent()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"definition","arguments":{"query":"Run","includeBody":true}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Found 1 definition", text);
        Assert.Equal("Run", response["result"]!["structuredContent"]!["results"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("public void Run()", response["result"]!["structuredContent"]!["results"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Files_ReturnsResults()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"files","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Found 1 file", text);
        Assert.Equal("src/app.cs", response["result"]!["structuredContent"]!["results"]![0]!["path"]!.GetValue<string>());
        Assert.Equal("csharp", response["result"]!["structuredContent"]!["results"]![0]!["lang"]!.GetValue<string>());
        Assert.NotNull(response["result"]!["structuredContent"]!["results"]![0]!["modified"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["results"]![0]!["indexedAt"]);
    }

    [Fact]
    public void ToolsCall_Excerpt_ReturnsExcerpt()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"excerpt","arguments":{"path":"src/app.cs","startLine":1,"endLine":1}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Excerpt returned", text);
        Assert.Equal("src/app.cs", response["result"]!["structuredContent"]!["path"]!.GetValue<string>());
        Assert.Contains("public class App", response["result"]!["structuredContent"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ToolsCall_Status_ReturnsCounts()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"status","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Database stats returned", text);
        Assert.Equal(1, response["result"]!["structuredContent"]!["files"]!.GetValue<long>());
        Assert.Equal(1, response["result"]!["structuredContent"]!["chunks"]!.GetValue<long>());
        Assert.Equal(2, response["result"]!["structuredContent"]!["symbols"]!.GetValue<long>());
        Assert.Equal(0, response["result"]!["structuredContent"]!["references"]!.GetValue<long>());
        Assert.NotNull(response["result"]!["structuredContent"]!["indexedAt"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["latestModified"]);
        Assert.NotNull(response["result"]!["structuredContent"]!["projectRoot"]);
    }

    [Fact]
    public void ToolsCall_Index_MissingPath_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void ToolsCall_Index_NonexistentDir_ReturnsError()
    {
        // Use a path within CWD that doesn't exist / CWD内の存在しないパスを使用
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{"path":"./nonexistent_subdir_xyz_test"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void ToolsCall_UnknownTool_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"nonexistent","arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void ToolsCall_MissingToolName_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"arguments":{}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
    }

    // --- Security tests / セキュリティテスト ---

    [Fact]
    public void ToolsCall_Index_PathTraversal_ReturnsError()
    {
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"index","arguments":{"path":"/etc"}}}""")!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("current working directory", text);
    }

    [Fact]
    public void ToolsCall_Search_QueryTooLong_ReturnsError()
    {
        var longQuery = new string('a', 1001);
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"search\",\"arguments\":{\"query\":\"" + longQuery + "\"}}}";
        var request = JsonNode.Parse(json)!;
        var response = _server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("too long", text);
    }

    // --- Database not found tests / DB未検出テスト ---

    [Fact]
    public void ToolsCall_Search_DbNotFound_ReturnsError()
    {
        var server = new McpServer("/nonexistent/path/test.db", "0.1.1");
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search","arguments":{"query":"test"}}}""")!;
        var response = server.HandleMessage(request)!;

        Assert.True(response["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("not found", response["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    public void Dispose()
    {
        _db.Dispose();
        DeleteDbPath();
    }

    private void DeleteDbPath()
    {
        if (!File.Exists(_dbPath))
            return;

        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (UnauthorizedAccessException)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
    }
}
