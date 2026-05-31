using System.Text;
using System.Text.Json;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Lsp;

namespace CodeIndex.Tests;

[Collection("SQLite pool sensitive")]
public class LspServerTests
{
    [Fact]
    public void ExtractTokenAtUtf16Position_ReturnsIdentifierUnderCursor()
    {
        Assert.Equal("Needle", LspServer.ExtractTokenAtUtf16Position("var value = Needle.Call();", 14));
        Assert.Equal("Needle", LspServer.ExtractTokenAtUtf16Position("var value = Needle.Call();", 18));
    }

    [Fact]
    public void TryReadMessage_ReadsContentLengthFramedPayload()
    {
        const string payload = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}";
        var bytes = Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}\r\n\r\n{payload}");
        using var stream = new MemoryStream(bytes);

        Assert.True(LspServer.TryReadMessage(stream, out var actual));
        Assert.Equal(payload, actual);
    }

    [Fact]
    public void HandleMessage_Initialize_AdvertisesCoreCapabilities()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_lsp_initialize");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            using var db = new DbContext(dbPath);
            using var server = new LspServer(new DbReader(db), "1.2.3", ProgramRunner.CreateDefaultJsonOptions(), projectRoot);

            var response = server.HandleMessage("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}");

            Assert.NotNull(response);
            Assert.True(response!["result"]!["capabilities"]!["definitionProvider"]!.GetValue<bool>());
            Assert.True(response["result"]!["capabilities"]!["documentSymbolProvider"]!.GetValue<bool>());
            Assert.Equal("cdidx", response["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void HandleMessage_DocumentSymbol_ReturnsIndexedSymbols()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_lsp_document_symbol");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            File.WriteAllText(sourcePath, "class App { void Needle() { } }\n");
            TestProjectHelper.InsertIndexedFile(dbPath, "app.cs", "csharp", File.ReadAllText(sourcePath));
            using var db = new DbContext(dbPath);
            using var server = new LspServer(new DbReader(db), "1.2.3", ProgramRunner.CreateDefaultJsonOptions(), projectRoot);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "textDocument/documentSymbol",
                @params = new
                {
                    textDocument = new { uri = new Uri(sourcePath).AbsoluteUri },
                },
            });

            var response = server.HandleMessage(request);

            Assert.NotNull(response);
            var symbols = response!["result"]!.AsArray();
            Assert.Contains(symbols, symbol => symbol?["name"]?.GetValue<string>() == "App");
            Assert.Contains(symbols, symbol => symbol?["name"]?.GetValue<string>() == "Needle");
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void HandleMessage_Definition_ReturnsLocationForTokenAtPosition()
    {
        var projectRoot = TestProjectHelper.CreateTempProject("cdidx_lsp_definition");
        try
        {
            var dbPath = TestProjectHelper.CreateProjectDb(projectRoot);
            var sourcePath = Path.Combine(projectRoot, "app.cs");
            var source = "class App { void Needle() { } void Call() { Needle(); } }\n";
            File.WriteAllText(sourcePath, source);
            TestProjectHelper.InsertIndexedFile(dbPath, "app.cs", "csharp", source);
            using var db = new DbContext(dbPath);
            using var server = new LspServer(new DbReader(db), "1.2.3", ProgramRunner.CreateDefaultJsonOptions(), projectRoot);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "textDocument/definition",
                @params = new
                {
                    textDocument = new { uri = new Uri(sourcePath).AbsoluteUri },
                    position = new { line = 0, character = source.IndexOf("Needle();", StringComparison.Ordinal) },
                },
            });

            var response = server.HandleMessage(request);

            Assert.NotNull(response);
            var locations = response!["result"]!.AsArray();
            Assert.NotEmpty(locations);
            Assert.Equal(new Uri(sourcePath).AbsoluteUri, locations[0]!["uri"]!.GetValue<string>());
        }
        finally
        {
            TestProjectHelper.DeleteDirectory(projectRoot);
        }
    }
}
