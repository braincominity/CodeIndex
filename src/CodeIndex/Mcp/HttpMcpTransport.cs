using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CodeIndex.Mcp;

/// <summary>
/// HTTP MCP transport (issue #1558). Each HTTP POST carries one JSON-RPC request frame in the
/// body and the matching JSON-RPC response is returned as the response body (or 204 No Content
/// for notifications). The implementation is intentionally single-session — one in-flight request
/// at a time — to mirror the existing stdio loop's request/response pairing and to keep the
/// JSON-RPC ordering invariant the rest of the MCP server depends on. SSE / multi-client
/// fan-out is left as a follow-up because the underlying handler today never emits unsolicited
/// server→client messages.
/// HTTP MCP トランスポート (issue #1558)。HTTP POST 1 件が JSON-RPC リクエスト 1 件と対応し、
/// 応答も同じ HTTP レスポンスのボディに乗せる（通知の場合は 204 No Content）。stdio ループと
/// 同様にシングルセッションで「リクエスト 1 件 → レスポンス 1 件」の順序不変条件を維持する。
/// SSE / マルチクライアント対応は将来作業として切り出す（現サーバーは自発的なサーバー→クライアント
/// メッセージを発生させないため、最小単位として POST/response で十分）。
/// </summary>
internal sealed class HttpMcpTransport : IMcpTransport
{
    private readonly HttpListener _listener;
    private readonly string _endpoint;
    // The configured bearer token's SHA-256 digest, precomputed once at construction so the
    // per-request auth path never hashes the secret. Storing the digest (not the token) keeps the
    // per-request work proportional only to the attacker-supplied input length, eliminating the
    // configured-token length side channel that a per-request hash would still leak.
    // 設定トークンの SHA-256 をコンストラクタで一度だけ計算し、リクエスト毎の auth では
    // 攻撃者入力のみハッシュ計算する。これにより設定トークン長による timing 漏洩を排除する。
    private readonly byte[]? _bearerTokenHash;
    private HttpListenerContext? _pendingContext;
    private bool _disposed;

    /// <summary>
    /// Build an HTTP transport bound to the supplied loopback prefix. If <paramref name="bearerToken"/>
    /// is non-empty, every request must carry a matching `Authorization: Bearer ...` header; otherwise
    /// the transport refuses to bind to non-loopback hosts to avoid exposing the MCP catalog to the
    /// local network without an explicit secret.
    /// 指定された loopback プレフィックスに HTTP トランスポートを bind する。<paramref name="bearerToken"/>
    /// が空でない場合、すべてのリクエストに `Authorization: Bearer ...` ヘッダーが必要。トークン未指定で
    /// loopback 以外に bind しようとした場合は明示的に拒否し、秘密情報なしの LAN 露出を防ぐ。
    /// </summary>
    internal HttpMcpTransport(string prefix, string host, int boundPort, string? bearerToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _bearerTokenHash = string.IsNullOrEmpty(bearerToken)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken));
        _endpoint = $"http://{host}:{boundPort}/";
    }

    public string Name => "http";

    public string Endpoint => _endpoint;

    internal bool RequiresBearerToken => _bearerTokenHash is not null;

    /// <summary>
    /// Resolve a `host:port` listen spec into the corresponding HTTP prefix. Ephemeral ports
    /// (port `0`) are resolved up-front by binding a temporary <see cref="TcpListener"/> so the
    /// caller can immediately log the bound port — there is a small TOCTOU window between
    /// closing the probe and binding <see cref="HttpListener"/>, accepted because the HTTP
    /// transport is documented as local-only / single-tenant.
    /// `host:port` の listen 仕様を HTTP プレフィックスに解決する。ポート 0 (ephemeral) は
    /// <see cref="TcpListener"/> を一時的に bind して空きポートを取得してから返すため、呼び出し側は
    /// 即座にバインドされたポートを stderr に出せる。TOCTOU は存在するが、HTTP トランスポートは
    /// ローカル単独利用を想定しているため許容する。
    /// </summary>
    internal static HttpListenSpec ResolveListenSpec(string listenSpec)
    {
        if (string.IsNullOrWhiteSpace(listenSpec))
            throw new FormatException("--http-listen value must not be empty.");

        var (host, port) = ParseHostPort(listenSpec);
        var displayHost = host;
        var prefixHost = NormalizePrefixHost(host);
        var ipAddress = ResolveLoopbackIp(host);

        var isLoopback = ipAddress is not null && IPAddress.IsLoopback(ipAddress);

        if (port == 0)
            port = FindFreePort(ipAddress ?? IPAddress.Loopback);

        var prefix = $"http://{prefixHost}:{port.ToString(CultureInfo.InvariantCulture)}/";
        return new HttpListenSpec(prefix, displayHost, port, isLoopback);
    }

    private static (string host, int port) ParseHostPort(string spec)
    {
        // Accept `host:port` and `[ipv6]:port`. Reject anything else so we don't silently
        // bind to surprising endpoints. The default listen string `127.0.0.1:38080` keeps
        // `cdidx mcp --transport http` usable without any extra flags.
        // `host:port` と `[ipv6]:port` を受け付ける。それ以外は黙って予想外のアドレスに
        // bind しないよう拒否する。既定 `127.0.0.1:38080` でフラグ追加なしに使えるようにする。
        if (spec.StartsWith('['))
        {
            var close = spec.IndexOf(']');
            if (close <= 1 || close + 2 >= spec.Length || spec[close + 1] != ':')
                throw new FormatException($"--http-listen value '{spec}' is not a valid host:port (expected '[ipv6]:port').");
            var host6 = spec.Substring(1, close - 1);
            var portText6 = spec.Substring(close + 2);
            if (!int.TryParse(portText6, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port6) || port6 < 0 || port6 > 65535)
                throw new FormatException($"--http-listen value '{spec}' has an invalid port '{portText6}'.");
            return (host6, port6);
        }

        var colon = spec.LastIndexOf(':');
        if (colon <= 0 || colon >= spec.Length - 1)
            throw new FormatException($"--http-listen value '{spec}' is not a valid host:port.");
        var host = spec.Substring(0, colon);
        var portText = spec.Substring(colon + 1);
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port < 0 || port > 65535)
            throw new FormatException($"--http-listen value '{spec}' has an invalid port '{portText}'.");
        return (host, port);
    }

    private static string NormalizePrefixHost(string host)
    {
        // HttpListener accepts `localhost`, `127.0.0.1`, or `+` / `*` (which we reject up-front
        // to avoid surprise public bind). IPv6 hosts must be wrapped in `[...]` to satisfy the
        // prefix grammar.
        if (host is "+" or "*")
            throw new FormatException("--http-listen rejects wildcard hosts; bind to a loopback address explicitly.");
        if (host.Contains(':'))
            return $"[{host}]";
        return host;
    }

    private static IPAddress? ResolveLoopbackIp(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;
        return IPAddress.TryParse(host, out var ip) ? ip : null;
    }

    private static int FindFreePort(IPAddress address)
    {
        var probe = new TcpListener(address, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public async Task<string?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pendingContext is not null)
            throw new InvalidOperationException("HttpMcpTransport: ReadFrameAsync called twice without an intervening WriteFrameAsync.");

        while (true)
        {
            HttpListenerContext context;
            try
            {
                using (cancellationToken.Register(() => _listener.Stop()))
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
            }
            catch (HttpListenerException)
            {
                // Listener was stopped — treat as end-of-stream so the MCP loop exits cleanly.
                // listener が止められた場合は EOS と同じ扱いでループを正常終了させる。
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            if (!await TryAuthorizeAsync(context).ConfigureAwait(false))
                continue;

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                // RFC 9110 §15.5.6: a 405 response MUST generate an `Allow` header listing the
                // methods the resource supports so generic HTTP clients can react without parsing
                // the response body.
                // RFC 9110 §15.5.6 に従い 405 にはサポートメソッドを示す `Allow` ヘッダを付ける。
                context.Response.AddHeader("Allow", "POST");
                await RespondAsync(context, (int)HttpStatusCode.MethodNotAllowed, "MCP HTTP transport only accepts POST.\n").ConfigureAwait(false);
                continue;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            // An empty body is treated like a closed stdio line — skip it without producing a
            // response so a misbehaving client cannot pin the loop on a junk frame.
            // 空ボディは stdio の空行と同じ扱いでスキップ。応答は返さない。
            if (string.IsNullOrWhiteSpace(body))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                context.Response.Close();
                continue;
            }

            _pendingContext = context;
            return body;
        }
    }

    public async Task WriteFrameAsync(string? frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = _pendingContext
            ?? throw new InvalidOperationException("HttpMcpTransport: WriteFrameAsync called without a pending ReadFrameAsync.");
        _pendingContext = null;

        try
        {
            if (frame is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                context.Response.Close();
                return;
            }

            var payload = Encoding.UTF8.GetBytes(frame);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.LongLength;
            await context.Response.OutputStream.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // Best-effort: close the response so the listener doesn't leak the context.
            // best-effort で response を閉じる。listener が context を持ち続けないようにする。
            try { context.Response.Abort(); } catch { /* ignore */ }
            throw;
        }
    }

    private async Task<bool> TryAuthorizeAsync(HttpListenerContext context)
    {
        if (_bearerTokenHash is null)
            return true;

        // RFC 6750 §2.1: the auth-scheme token is case-insensitive — clients sending
        // `authorization: bearer ...` are valid and must be accepted.
        // RFC 6750 §2.1 で auth-scheme は case-insensitive と規定されているため、
        // `bearer ...` のような小文字スキームも受理する。
        var header = context.Request.Headers["Authorization"];
        if (header is not null && header.Length >= "Bearer ".Length
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var provided = header.Substring("Bearer ".Length).Trim();
            if (HashEqualsConfiguredToken(provided))
                return true;
        }

        // RFC 7235 §4.1: 401 responses SHOULD carry a WWW-Authenticate challenge so
        // generic HTTP clients (and humans poking at the listener) know which scheme
        // and (optionally) realm to use.
        // RFC 7235 §4.1 に従い 401 には WWW-Authenticate を付け、汎用 HTTP クライアントや
        // 手動デバッグ時に必要なスキームを示す。
        context.Response.AddHeader("WWW-Authenticate", "Bearer realm=\"cdidx-mcp\"");
        await RespondAsync(context, (int)HttpStatusCode.Unauthorized, "Missing or invalid bearer token.\n").ConfigureAwait(false);
        return false;
    }

    private static async Task RespondAsync(HttpListenerContext context, int statusCode, string body)
    {
        try
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.LongLength;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.OutputStream.Close();
        }
        catch
        {
            try { context.Response.Abort(); } catch { /* ignore */ }
        }
    }

    private bool HashEqualsConfiguredToken(string provided)
    {
        // Hash only the attacker-supplied input and compare to the pre-computed configured-token
        // digest via FixedTimeEquals. Hashing the configured token on every request would still
        // leak its length through SHA-256's per-block work; pre-computing in the constructor and
        // hashing only the request side eliminates that channel. The digest is unsalted on
        // purpose — the goal is constant-time equality of two same-length 32-byte buffers, not
        // password storage.
        // 攻撃者入力のみハッシュ計算し、コンストラクタで事前計算した設定トークン digest と
        // FixedTimeEquals で比較する。リクエスト毎に設定トークンをハッシュすると SHA-256 の
        // ブロック処理量で設定トークン長が漏れるため、設定側を事前計算しておく。
        // salt 無しは「同じ長さの 32 byte 配列の定数時間比較」が目的だから。
        Span<byte> providedHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), providedHash);
        return CryptographicOperations.FixedTimeEquals(providedHash, _bearerTokenHash);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        try
        {
            if (_pendingContext is not null)
            {
                try { _pendingContext.Response.Abort(); } catch { /* ignore */ }
                _pendingContext = null;
            }
            _listener.Close();
        }
        catch
        {
            // Disposal must not throw — the parent server is already on its way down.
            // dispose は例外を投げない方針: 親サーバーは既に終了処理中なので。
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Resolved listen spec returned by <see cref="ResolveListenSpec"/>.</summary>
    internal readonly record struct HttpListenSpec(string Prefix, string Host, int Port, bool IsLoopback);
}
