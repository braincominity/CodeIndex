using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Mcp;

namespace CodeIndex.Tests;

/// <summary>
/// Issue #1558: end-to-end coverage for the optional HTTP MCP transport. Each test binds a
/// loopback HTTP listener on an ephemeral port, runs the McpServer loop on a background task,
/// and exercises the JSON-RPC catalog through HttpClient to ensure the transport is wire-
/// compatible with the existing stdio behavior.
/// Issue #1558: 任意の HTTP MCP トランスポートの end-to-end カバレッジ。各テストは loopback
/// 上の ephemeral ポートで HTTP listener を bind し、バックグラウンドで McpServer ループを動かして
/// HttpClient 経由で JSON-RPC を叩き、stdio と同じワイヤー互換性を確認する。
/// </summary>
[Collection("SQLite pool sensitive")]
public class HttpMcpTransportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContext _db;

    public HttpMcpTransportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cdidx_mcp_http_{Guid.NewGuid():N}.db");
        _db = new DbContext(_dbPath);
        _db.InitializeSchema();
    }

    [Fact]
    public async Task HttpTransport_PostInitialize_ReturnsHandshakeResult()
    {
        await using var harness = await McpHttpHarness.StartAsync(_dbPath);

        var response = await harness.PostJsonAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("2025-03-26", root.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task HttpTransport_PostNotification_Returns204NoContent()
    {
        await using var harness = await McpHttpHarness.StartAsync(_dbPath);

        var response = await harness.PostJsonAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task HttpTransport_GetRequest_Returns405()
    {
        await using var harness = await McpHttpHarness.StartAsync(_dbPath);

        using var client = new HttpClient();
        using var response = await client.GetAsync(harness.Endpoint);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task HttpTransport_TwoSequentialRequests_ShareWarmServer()
    {
        // Issue #1558: AI clients should be able to keep a single MCP server warm across
        // multiple JSON-RPC requests instead of paying subprocess-spawn cost per call.
        // Issue #1558: AI クライアントが MCP サーバーを温めた状態で複数 JSON-RPC を扱えること。
        await using var harness = await McpHttpHarness.StartAsync(_dbPath);

        var first = await harness.PostJsonAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await harness.PostJsonAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength() > 0);
    }

    [Fact]
    public async Task HttpTransport_EmptyBody_Returns204AndDoesNotKillServer()
    {
        await using var harness = await McpHttpHarness.StartAsync(_dbPath);

        var empty = await harness.PostJsonAsync(string.Empty);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        var follow = await harness.PostJsonAsync("""{"jsonrpc":"2.0","id":7,"method":"ping"}""");
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        var body = await follow.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(7, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task HttpTransport_BearerToken_RejectsMissingHeader()
    {
        const string token = "s3cret-token";
        await using var harness = await McpHttpHarness.StartAsync(_dbPath, bearerToken: token);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HttpTransport_BearerToken_AcceptsMatchingHeader()
    {
        const string token = "s3cret-token";
        await using var harness = await McpHttpHarness.StartAsync(_dbPath, bearerToken: token);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HttpTransport_BearerToken_RejectsWrongToken()
    {
        // Verifies the rejection path covers the actual constant-time compare, not just the
        // "missing header" branch — a regression where the comparison short-circuited on the
        // first matching byte would still pass the missing-header test but fail this one.
        // 不一致トークンの拒否経路も検証する（ヘッダー欠落だけでなく定数時間比較が機能していることを担保）。
        const string token = "s3cret-token";
        await using var harness = await McpHttpHarness.StartAsync(_dbPath, bearerToken: token);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HttpTransport_BearerToken_AcceptsLowerCaseScheme()
    {
        // RFC 6750 §2.1: auth-scheme tokens are case-insensitive. Clients that send
        // `authorization: bearer ...` (lowercase) must still authenticate successfully.
        // RFC 6750 §2.1 により auth-scheme は case-insensitive なので、`bearer ...` 表記でも認証成功。
        const string token = "s3cret-token";
        await using var harness = await McpHttpHarness.StartAsync(_dbPath, bearerToken: token);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, harness.Endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"bearer {token}");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void ResolveListenSpec_DefaultListen_ResolvesToLoopback()
    {
        var spec = HttpMcpTransport.ResolveListenSpec("127.0.0.1:0");
        Assert.True(spec.IsLoopback);
        Assert.Equal("127.0.0.1", spec.Host);
        Assert.True(spec.Port > 0);
        Assert.EndsWith("/", spec.Prefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("127.0.0.1:70000")]
    [InlineData("[::1]:notaport")]
    public void ResolveListenSpec_InvalidInput_Throws(string input)
    {
        Assert.Throws<FormatException>(() => HttpMcpTransport.ResolveListenSpec(input));
    }

    [Fact]
    public void ResolveListenSpec_WildcardHost_IsRejected()
    {
        Assert.Throws<FormatException>(() => HttpMcpTransport.ResolveListenSpec("+:0"));
        Assert.Throws<FormatException>(() => HttpMcpTransport.ResolveListenSpec("*:0"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private sealed class McpHttpHarness : IAsyncDisposable
    {
        private readonly McpServer _server;
        private readonly HttpMcpTransport _transport;
        private readonly CancellationTokenSource _cts;
        private readonly Task _loopTask;

        private McpHttpHarness(McpServer server, HttpMcpTransport transport, CancellationTokenSource cts, Task loopTask, string endpoint)
        {
            _server = server;
            _transport = transport;
            _cts = cts;
            _loopTask = loopTask;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public static async Task<McpHttpHarness> StartAsync(string dbPath, string? bearerToken = null)
        {
            var listen = HttpMcpTransport.ResolveListenSpec("127.0.0.1:0");
            var transport = new HttpMcpTransport(listen.Prefix, listen.Host, listen.Port, bearerToken);
            var server = new McpServer(dbPath, ConsoleUi.LoadVersion());
            var cts = new CancellationTokenSource();
            var loopTask = Task.Run(() => server.RunAsync(transport, cts.Token));
            // Give the listener a tick to start accepting; HttpListener.Start is synchronous but the
            // background task may not have entered GetContextAsync yet by the time the test posts.
            // listener が GetContextAsync に入る前に POST が来ないよう、ごく短い待機を挟む。
            await Task.Yield();
            return new McpHttpHarness(server, transport, cts, loopTask, listen.Prefix);
        }

        public async Task<HttpResponseMessage> PostJsonAsync(string body)
        {
            using var client = new HttpClient();
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            return await client.PostAsync(Endpoint, content);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _transport.DisposeAsync();
            }
            catch { /* ignored */ }
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { /* timeouts / cancellations expected when the listener stops mid-accept */ }
            _server.Dispose();
            _cts.Dispose();
        }
    }
}
