using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using CodeIndex.Cli;
using CodeIndex.Database;
using CodeIndex.Indexer;

namespace CodeIndex.Mcp;

/// <summary>
/// MCP (Model Context Protocol) server speaking JSON-RPC 2.0 over a pluggable transport. The
/// default <see cref="StdioMcpTransport"/> preserves the historic stdin/stdout wire path, and
/// <see cref="HttpMcpTransport"/> exposes the same JSON-RPC catalog over POST so AI clients can
/// share a warm server across sessions (issue #1558).
/// プラガブルな <see cref="IMcpTransport"/> 上で JSON-RPC 2.0 を話す MCP サーバー。既定の
/// <see cref="StdioMcpTransport"/> は従来通り stdin/stdout を使い、<see cref="HttpMcpTransport"/>
/// は同じ JSON-RPC カタログを POST で公開して、複数クライアントから暖機済みサーバーを共有できるようにする
/// (issue #1558)。
/// Supported protocol versions: see <see cref="SupportedProtocolVersions"/> (negotiated per
/// `initialize` request, #1554).
/// 対応プロトコルバージョン: <see cref="SupportedProtocolVersions"/> 参照（`initialize` ごとに交渉, #1554）。
/// </summary>
public partial class McpServer : IDisposable
{
    private readonly string _dbPath;
    private readonly bool _dbPathExplicit;
    private readonly string _version;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<JsonNode, string> _serializeResponse;
    private readonly IMcpAuthenticator _authenticator;
    private readonly McpToolFilter _toolFilter;
    // Bounds the number of MCP tool calls in flight at once so an unbounded burst of
    // requests cannot exhaust memory or wedge the SQLite reader lock (#1567). The
    // stdio / HTTP loop today only ever has one frame in flight, but the gate
    // documents the contract and is the seam future async dispatch will use.
    // 同時 in-flight ツール呼び出しの上限 (#1567)。stdio / HTTP ループは現状単一スレッド
    // だが、将来の並列ディスパッチに備えて契約を明示し、testable な seam を残す。
    private readonly SemaphoreSlim _concurrencyGate;
    // Server-wide shutdown signal. Cancelled by `notifications/shutdown` (and the
    // `notifications/exit` alias) so the read loop unblocks and exits cleanly even
    // when the transport itself has not closed (#1567).
    // サーバー全体の shutdown シグナル。`notifications/shutdown` (および
    // `notifications/exit`) を受けると cancel され、トランスポート未クローズでも
    // 読み取りループが unblock して正常終了する (#1567)。
    private readonly CancellationTokenSource _shutdownCts = new();
    // Active JSON-RPC requests keyed by their serialized `id`, so MCP `$/cancelRequest`
    // notifications can cancel the exact in-flight tool instead of only shutting down the
    // whole server (#1418).
    // JSON-RPC request id ごとの実行中 CTS。MCP `$/cancelRequest` 通知でサーバー全体ではなく
    // 対象ツール呼び出しだけを cancel するため (#1418)。
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests = new(StringComparer.Ordinal);
    // Token observed by the currently executing tool call. Set just before
    // `ProcessFrame` runs and reset afterwards so `WithDbReader` can hand a live
    // cancellation token to `DbReader` for SQLite work (#1567).
    // 現在実行中のツール呼び出しが観測するトークン。`ProcessFrame` 実行直前にセットし、
    // 直後にリセットする。`WithDbReader` が `DbReader` にライブな cancellation token
    // を渡せるようにするため (#1567)。
    private readonly AsyncLocal<CancellationToken> _currentRequestToken = new();
    private readonly AsyncLocal<Action<string>?> _currentOutOfBandFrameWriter = new();
    private readonly AsyncLocal<List<Action>?> _deferredFrameLogs = new();
    private bool _running = true;
    // Per-session DbContext reused across MCP tool calls. Holding the connection open
    // avoids reopening SQLite, reapplying pragmas, and re-registering every SQL function
    // on each invocation (issue #1494).
    // セッション内で MCP ツール呼び出しごとに再利用する DbContext。接続再開・PRAGMA 再適用・
    // SQL 関数再登録のコストを毎回払わないために保持する（#1494）。
    private DbContext? _sharedDb;
    // TryMigrateForRead is a read-path concern (legacy / read-only sandbox DBs). It is
    // idempotent but does run PRAGMA table_info + CREATE INDEX IF NOT EXISTS round trips,
    // so we run it once per session. Write tools (`index`, `backfill_fold`) cover the same
    // surface via InitializeSchema, which also flips this flag through MarkSharedDbMigrated.
    // TryMigrateForRead は read path 向けの遅延移行で、レガシー DB / read-only サンドボックス
    // でのみ意味を持つ。冪等だが PRAGMA table_info などの往復が発生するため、セッションで一度だけ
    // 実行する。書き込みツールは InitializeSchema で同等以上の DDL を流すため、そこでフラグを立てる。
    private bool _sharedDbReadMigrated;
    private bool _disposed;
    // Per-call MCP audit log (#1562). Null when no `--audit-log` path was supplied. Captured
    // from the constructor so the AuditLogSink lifecycle (file handle / rotation) is owned by
    // ProgramRunner, not by every tool dispatch site.
    // ツール呼び出し監査ログ (#1562)。`--audit-log` 未指定時は null。AuditLogSink のライフサイクル
    // (ファイルハンドル / rotation) は ProgramRunner 側で所有する。
    private readonly AuditLogSink? _auditLog;
    // `initialize.clientInfo` echoed into every audit record so the trail can answer
    // "which client issued this call?" without a second log source. Updated on every
    // `initialize` so a single-session reconnection picks up the new caller identity.
    // `initialize.clientInfo` を audit に転写し、別ログを引かなくても呼び出し元を辿れるよう
    // にする。`initialize` 毎に上書きすることで再接続時に caller identity が追随する。
    private string? _clientName;
    private string? _clientVersion;
    private JsonNode? _clientCapabilities;
    private JsonArray _clientRoots = [];
    private string _mcpLogLevel = "info";
    // Opaque per-server-instance session id copied into suggestion attribution records (#1873).
    // #1873 の提案 attribution 用に保存する、サーバーインスタンス単位の不透明セッションID。
    private readonly string _sessionId = Guid.NewGuid().ToString("D");
    // Caller identity used to key the per-(tool, caller) rate limiter. Captured from the
    // `clientInfo.name` field of the `initialize` request when the client supplies it, so
    // shared / networked MCP deployments can attribute and throttle individual clients
    // instead of treating the whole server as a single bucket (#1560).
    // (tool, caller) ごとのレート制限のキーに使う呼び出し元 ID。`initialize` の
    // `clientInfo.name` から取得し、共有・ネットワーク経由の MCP でクライアント単位の
    // 計量・スロットルが効くようにする（#1560）。
    private string _caller = "unknown";

    // Preferred MCP protocol version returned when the client does not pin one. This is the
    // newest entry in `SupportedProtocolVersions` and must stay in lockstep with that array.
    // 既定の MCP プロトコルバージョン。クライアントが指定しなかった場合に返す値で、
    // `SupportedProtocolVersions` の先頭（最新）と一致させる。
    private const string ProtocolVersion = "2025-03-26";
    // MCP protocol versions this server can speak, newest first. Issue #1554: the
    // `initialize` response used to advertise a single hardcoded version and ignored the
    // client's requested `protocolVersion`, so any spec bump silently desynced clients and
    // servers. Negotiation walks this set so older clients on `2024-11-05` keep working and
    // unknown future versions surface as a structured `-32602` instead of a misleading echo.
    // このサーバーが話せる MCP プロトコルバージョン（新しい順）。Issue #1554: 旧実装は
    // ハードコードした 1 つのバージョンだけを返し、クライアントが要求した `protocolVersion`
    // を無視していたため、仕様改訂のたびに無言で互換が崩れていた。`2024-11-05` の旧クライアント
    // を引き続きサポートしつつ、未知バージョンは構造化された `-32602` で明示的に拒否する。
    internal static readonly string[] SupportedProtocolVersions = { "2025-03-26", "2024-11-05" };
    private const int MaxLimit = 200;
    // Upper bound on the `impact_analysis` `maxHops` argument. Deep monorepos can have
    // legitimate caller chains exceeding 10 hops (e.g. DI container → factory → service →
    // handler → business logic), so the previous cap of 10 silently downgraded such requests.
    // The result-set `limit` (`MaxLimit`) and BFS visited-set still bound traversal cost.
    // `impact_analysis` の `maxHops` 引数の上限。深いモノレポでは 10 hops 超の正当な caller
    // チェーン (DI container → factory → service → handler → business logic) があり、旧上限
    // 10 では黙ってダウングレードしていた。結果件数 `limit` (`MaxLimit`) と BFS の visited-set
    // が探索コストを抑える役割を担う。
    private const int MaxImpactDepth = 50;
    // Per-call cap on the `before` / `after` context-line parameters accepted by `excerpt`.
    // Without an upper bound, `int.MaxValue` previously drove `startLine - before` into underflow
    // and `endLine + after` into overflow before `Math.Max/Min` clamped, so the slice path saw
    // nonsensical ranges. Mirrors the CLI `--before` / `--after` cap (#1528).
    // `excerpt` が受け取る `before` / `after` の上限。上限が無いと `int.MaxValue` で
    // `startLine - before` が underflow、`endLine + after` が overflow し、`Math.Max/Min` で clamp
    // する前に slice 経路が破綻していたため、CLI の `--before` / `--after` 上限と揃える（#1528）。
    private const int MaxContextLines = 1000;
    private const int MaxLineLength = 1_000_000; // 1 MB per JSON-RPC message / 1メッセージあたり最大1MB
    // Stdio buffer for the JSON-RPC loop. Sized to fit typical large MCP payloads (e.g. batch_query)
    // in a single read so the StreamReader does not grow from its 1 KB default toward MaxLineLength.
    // JSON-RPCループのstdioバッファ。大きめのMCPペイロードを1回の読み取りで吸収し、
    // StreamReaderのデフォルト1KBから繰り返し拡張されるのを避けるサイズ。
    private const int StdioBufferSize = 64 * 1024;
    // Default ceiling on concurrent in-flight tool calls. Matches the issue's suggested
    // default and is generous enough for typical AI clients without letting a burst of
    // tool calls wedge the SQLite reader lock or balloon memory (#1567).
    // 同時 in-flight ツール呼び出し数の既定上限 (#1567)。
    internal const int DefaultMaxConcurrency = 8;

    public McpServer(string dbPath, string version, bool dbPathExplicit = false)
        : this(dbPath, version, dbPathExplicit, null, null, null, null, DefaultMaxConcurrency)
    {
    }

    public McpServer(string dbPath, string version, bool dbPathExplicit, IMcpAuthenticator authenticator)
        : this(dbPath, version, dbPathExplicit, null, authenticator, null, null, DefaultMaxConcurrency)
    {
    }

    public McpServer(string dbPath, string version, bool dbPathExplicit, McpToolFilter? toolFilter)
        : this(dbPath, version, dbPathExplicit, null, null, toolFilter, null, DefaultMaxConcurrency)
    {
    }

    // Legacy internal entry point retained for the existing serializer-injection tests that
    // do not need a custom authenticator or tool filter.
    // serializer 注入だけが必要な既存テスト向けの内部互換 entry。
    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse)
        : this(dbPath, version, dbPathExplicit, serializeResponse, null, null, null, DefaultMaxConcurrency)
    {
    }

    internal McpServer(string dbPath, string version, bool dbPathExplicit, AuditLogSink? auditLog)
        : this(dbPath, version, dbPathExplicit, null, null, null, auditLog, DefaultMaxConcurrency)
    {
    }

    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse, IMcpAuthenticator? authenticator)
        : this(dbPath, version, dbPathExplicit, serializeResponse, authenticator, null, null, DefaultMaxConcurrency)
    {
    }

    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse, IMcpAuthenticator? authenticator, McpToolFilter? toolFilter)
        : this(dbPath, version, dbPathExplicit, serializeResponse, authenticator, toolFilter, null, DefaultMaxConcurrency)
    {
    }

    // Concurrency-cap injection overload preserved from #1567. Maps to the master constructor
    // with a null AuditLogSink so the maxConcurrency tests do not need to thread an audit log.
    // #1567 由来の maxConcurrency 注入用 overload。auditLog は null 固定で master に流す。
    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse, IMcpAuthenticator? authenticator, McpToolFilter? toolFilter, int maxConcurrency)
        : this(dbPath, version, dbPathExplicit, serializeResponse, authenticator, toolFilter, null, maxConcurrency)
    {
    }

    // Combined entry point used by ProgramRunner so a single MCP session can carry both an
    // optional authenticator (#1559) and an optional audit log (#1562). Other combinations
    // already have dedicated convenience overloads above.
    // ProgramRunner が authenticator (#1559) と audit log (#1562) を同時に注入できる
    // 経路。それ以外の組み合わせは上の個別 overload で済む。
    internal McpServer(string dbPath, string version, bool dbPathExplicit, IMcpAuthenticator? authenticator, AuditLogSink? auditLog)
        : this(dbPath, version, dbPathExplicit, null, authenticator, null, auditLog, DefaultMaxConcurrency)
    {
    }

    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse, IMcpAuthenticator? authenticator, McpToolFilter? toolFilter, AuditLogSink? auditLog)
        : this(dbPath, version, dbPathExplicit, serializeResponse, authenticator, toolFilter, auditLog, DefaultMaxConcurrency)
    {
    }

    internal McpServer(string dbPath, string version, bool dbPathExplicit, Func<JsonNode, string>? serializeResponse, IMcpAuthenticator? authenticator, McpToolFilter? toolFilter, AuditLogSink? auditLog, int maxConcurrency)
    {
        if (maxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "MCP concurrency cap must be at least 1.");
        _dbPath = dbPath;
        _dbPathExplicit = dbPathExplicit;
        _version = version;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        _serializeResponse = serializeResponse ?? (node => node.ToJsonString(_jsonOptions));
        _authenticator = authenticator ?? LocalStdioAuthenticator.Instance;
        _toolFilter = toolFilter ?? McpToolFilter.FromEnvironment();
        _auditLog = auditLog;
        RateLimiter = new RateLimiter(RateLimiterOptions.FromEnvironment());
        _concurrencyGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>
    /// Per-(tool, caller) token bucket throttle for MCP tool calls. Disabled by default so
    /// stdio single-user sessions are unaffected; operators opt in via
    /// `CDIDX_MCP_RATE_LIMIT_RPS` (+ optional `CDIDX_MCP_RATE_LIMIT_BURST`) on the MCP server
    /// process (#1560).
    /// MCP ツール呼び出し向け (tool, caller) 単位のトークンバケットスロットル。既定では無効で
    /// stdio 単一ユーザーには影響しない。`CDIDX_MCP_RATE_LIMIT_RPS`（任意で
    /// `CDIDX_MCP_RATE_LIMIT_BURST`）を MCP サーバープロセスに設定して opt-in する（#1560）。
    /// </summary>
    internal RateLimiter RateLimiter { get; private set; }

    /// <summary>
    /// Replace the rate limiter for tests so they can inject a deterministic clock and
    /// custom options without going through environment variables.
    /// テスト用にレート制限器を差し替える。決定論的なクロックや任意のオプションを環境変数
    /// 経由ではなく直接注入できるようにする。
    /// </summary>
    internal void OverrideRateLimiterForTests(RateLimiter limiter)
    {
        RateLimiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    }

    /// <summary>
    /// Caller identifier captured from the most recent `initialize` request's
    /// `clientInfo.name` (issue #1560). Exposed for tests so they can verify the limiter is
    /// keyed off the negotiated caller.
    /// 直近の `initialize` の `clientInfo.name` から取得した呼び出し元 ID（#1560）。
    /// テストがレート制限のキーを検証するために公開する。
    /// </summary>
    internal string CurrentCaller => _caller;

    /// <summary>
    /// Opaque session id used for suggestion attribution records (#1873).
    /// 提案 attribution レコードに使う不透明セッションID (#1873)。
    /// </summary>
    internal string CurrentSessionId => _sessionId;

    internal Action<JsonNode?>? RequestRegisteredForTests { get; set; }

    /// <summary>
    /// Cap configured for concurrent in-flight tool calls (#1567). Surfaced for tests so
    /// the bound can be verified without poking at internals.
    /// 現在設定されている in-flight ツール呼び出し上限 (#1567)。テスト向けに公開。
    /// </summary>
    internal int MaxConcurrency { get; }

    /// <summary>
    /// Run the MCP server loop on the default stdio transport. Kept as a thin wrapper around
    /// <see cref="RunAsync(IMcpTransport, CancellationToken)"/> so existing callers stay
    /// source-compatible after the #1558 transport refactor. SIGINT (Ctrl+C) and SIGTERM are
    /// translated into loop cancellation so orchestrators (systemd, launchd, supervisord) can
    /// achieve a clean shutdown instead of hanging until stdin closes (#1573).
    /// 既定の stdio トランスポートで MCP ループを動かす。#1558 のトランスポート抽象化後も
    /// 既存呼び出しがソース互換となるよう <see cref="RunAsync(IMcpTransport, CancellationToken)"/>
    /// のラッパとして残す。SIGINT (Ctrl+C) と SIGTERM をループキャンセルに変換し、stdin が閉じる
    /// まで固まる旧挙動を解消する（systemd / launchd / supervisord から graceful shutdown 可能に, #1573）。
    /// </summary>
    public async Task RunAsync()
    {
        await using var transport = new StdioMcpTransport(StdioBufferSize);
        using var cts = new CancellationTokenSource();
        using (RegisterShutdownHandlers(cts))
        {
            await RunAsync(transport, cts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Register cross-platform SIGINT (Ctrl+C) and SIGTERM handlers that cancel <paramref name="cts"/>
    /// so orchestrator-driven shutdowns drain the loop cleanly instead of leaving the MCP process
    /// hung on stdin or force-killed mid-iteration (#1573). The returned IDisposable removes the
    /// handlers; dispose it before disposing the CTS to avoid races between a late signal and CTS
    /// teardown.
    /// SIGINT (Ctrl+C) と SIGTERM を `cts` のキャンセルに変換するクロスプラットフォームハンドラを登録する
    /// （#1573）。返り値の IDisposable でハンドラを解除する。late signal と CTS 破棄の競合を避けるため、
    /// CTS の Dispose より先にこれを Dispose する。
    /// </summary>
    internal static IDisposable RegisterShutdownHandlers(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            if (cts.IsCancellationRequested)
                return;
            // Honour the signal without letting the .NET runtime terminate the process before
            // the loop has a chance to drain and dispose the shared DbContext.
            // .NET runtime の即時終了を抑え、ループが DbContext を片付ける猶予を確保する。
            e.Cancel = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* signal raced disposal — nothing to cancel. */ }
        };
        Console.CancelKeyPress += cancelHandler;

        PosixSignalRegistration? sigtermRegistration = null;
        try
        {
            sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                if (cts.IsCancellationRequested)
                    return;
                ctx.Cancel = true;
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { /* see CancelKeyPress branch. */ }
            });
        }
        catch (PlatformNotSupportedException)
        {
            // PosixSignal.SIGTERM is supported on net8.0 across Windows/Linux/macOS, but a future
            // niche runtime might not implement it. Console.CancelKeyPress still covers Ctrl+C
            // everywhere, so degrade silently rather than refusing to start.
            // .NET 8 では SIGTERM がクロスプラットフォーム対応だが、将来の特殊ランタイムで未対応の
            // 可能性に備え、Console.CancelKeyPress による Ctrl+C カバレッジを残してサイレントに縮退する。
        }

        return new ShutdownHandlerRegistration(cancelHandler, sigtermRegistration);
    }

    private sealed class ShutdownHandlerRegistration : IDisposable
    {
        private ConsoleCancelEventHandler? _cancelHandler;
        private PosixSignalRegistration? _sigterm;

        public ShutdownHandlerRegistration(ConsoleCancelEventHandler cancelHandler, PosixSignalRegistration? sigterm)
        {
            _cancelHandler = cancelHandler;
            _sigterm = sigterm;
        }

        public void Dispose()
        {
            var handler = Interlocked.Exchange(ref _cancelHandler, null);
            if (handler != null)
                Console.CancelKeyPress -= handler;
            var sigterm = Interlocked.Exchange(ref _sigterm, null);
            sigterm?.Dispose();
        }
    }

    /// <summary>
    /// Run the MCP server loop on the supplied transport (issue #1558). The contract is one
    /// read followed by one write — the loop honours notifications (write-null) and ends when
    /// the transport reports end-of-stream.
    /// 指定トランスポート上で MCP ループを動かす (issue #1558)。「読み 1 回 → 書き 1 回」を
    /// 守り、通知は null 書き込みで吸収し、EOS でループを終える。
    /// </summary>
    internal async Task RunAsync(IMcpTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);

        // Link the caller-supplied token (Ctrl+C / HTTP listener stop) with the server-internal
        // shutdown signal so `notifications/shutdown` also wakes any pending `ReadFrameAsync`.
        // The MCP spec leaves shutdown to the transport, but real deployments need a wire-level
        // way to drain in-flight work without killing the process (#1567).
        // Ctrl+C 等の外部 token と内部 shutdown signal をリンクし、`notifications/shutdown` でも
        // pending な `ReadFrameAsync` を unblock できるようにする (#1567)。
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        var loopToken = linkedCts.Token;

        // Use stderr for logging so stdout stays clean for JSON-RPC
        // stdoutをJSON-RPC用にクリーンに保つため、ログはstderrに出力
        Console.Error.WriteLine($"[cdidx-mcp] Starting MCP server v{_version} (db: {_dbPath}, transport: {transport.Name} @ {transport.Endpoint}, max in-flight: {MaxConcurrency})");

        if (transport is HttpMcpTransport httpTransport)
            httpTransport.OutOfBandFrameHandler = ProcessFrame;

        try
        {
            if (string.Equals(transport.Name, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                await RunConcurrentFrameLoopAsync(transport, loopToken).ConfigureAwait(false);
                return;
            }

            while (_running)
            {
                // The full read/process/write iteration is wrapped in the same cancellation guard so
                // a Ctrl+C that lands mid-iteration (e.g. while WriteFrameAsync is flushing) still
                // exits the loop cleanly instead of bubbling OperationCanceledException out of the
                // server and past ProgramRunner.RunMcpHttp's graceful-shutdown handler.
                // Ctrl+C が WriteFrameAsync flush 中に来ても OperationCanceledException を呼び元に
                // 漏らさず正常終了するよう、read/process/write 全体を同じ cancellation guard で囲む。
                try
                {
                    var frame = await transport.ReadFrameAsync(loopToken).ConfigureAwait(false);
                    if (frame == null)
                        break; // transport closed / トランスポートが閉じられた

                    // Acquire the concurrency gate before doing any work so a future async dispatch
                    // mode (multiple frames in flight) can never run more than `MaxConcurrency` tool
                    // calls at once. Today the loop is sequential so the gate is effectively a no-op
                    // at runtime, but it documents the contract and gives tests a verifiable bound
                    // (#1567).
                    // 並列ディスパッチ時に in-flight 数が `MaxConcurrency` を超えないよう、ProcessFrame
                    // の手前で gate を取得する (#1567)。
                    await _concurrencyGate.WaitAsync(loopToken).ConfigureAwait(false);
                    string? response;
                    try
                    {
                        // Hand the per-request token to `WithDbReader` so SQLite work the tool kicks
                        // off can observe shutdown / client-disconnect cancellation through
                        // `DbReader.Cancellation` (#1567).
                        // ツールが起動する SQLite 作業が shutdown / 切断を観測できるよう per-request
                        // token を `WithDbReader` に渡す (#1567)。
                        _currentRequestToken.Value = loopToken;
                        _currentOutOfBandFrameWriter.Value = transport is IOutOfBandMcpTransport outOfBandTransport
                            ? frameToWrite => outOfBandTransport.WriteOutOfBandFrameAsync(frameToWrite, loopToken).GetAwaiter().GetResult()
                            : null;
                        BeginDeferredFrameLogs();
                        response = ProcessFrame(frame);
                    }
                    finally
                    {
                        _currentRequestToken.Value = CancellationToken.None;
                        _currentOutOfBandFrameWriter.Value = null;
                        _concurrencyGate.Release();
                    }

                    await WriteFrameSafelyAsync(transport, response, loopToken).ConfigureAwait(false);
                    FlushDeferredFrameLogs();

                    // `notifications/shutdown` flips `_running` inside `HandleMessage`; exit the loop
                    // immediately so a subsequent slow `ReadFrameAsync` does not extend the lifetime
                    // of a server that has been asked to stop.
                    // `notifications/shutdown` が `_running` を倒した直後にループを抜ける (#1567)。
                    if (!_running)
                        break;
                }
                catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
                {
                    break;
                }
                catch (DecoderFallbackException ex)
                {
                    BeginDeferredFrameLogs();
                    await WriteFrameSafelyAsync(transport, BuildInvalidUtf8ParseErrorResponse(ex), loopToken).ConfigureAwait(false);
                    FlushDeferredFrameLogs();
                    break;
                }
            }
        }
        finally
        {
            if (transport is HttpMcpTransport httpTransportToClear)
                httpTransportToClear.OutOfBandFrameHandler = null;
        }

        Console.Error.WriteLine("[cdidx-mcp] Server stopped. Restart `cdidx mcp` when your client reconnects.");
    }

    private async Task RunConcurrentFrameLoopAsync(IMcpTransport transport, CancellationToken loopToken)
    {
        using var writeGate = new SemaphoreSlim(1, 1);
        using var normalFrameGate = new SemaphoreSlim(1, 1);
        var tasks = new List<Task>();

        while (_running)
        {
            string? frame;
            try
            {
                frame = await transport.ReadFrameAsync(loopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
                break;
            }
            catch (DecoderFallbackException ex)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                await writeGate.WaitAsync(loopToken).ConfigureAwait(false);
                try
                {
                    BeginDeferredFrameLogs();
                    await WriteFrameSafelyAsync(transport, BuildInvalidUtf8ParseErrorResponse(ex), loopToken).ConfigureAwait(false);
                    FlushDeferredFrameLogs();
                }
                finally
                {
                    writeGate.Release();
                }
                break;
            }
            if (frame == null)
                break;

            if (IsCancellationFrame(frame))
            {
                BeginDeferredFrameLogs();
                var response = ProcessFrame(frame);
                await writeGate.WaitAsync(loopToken).ConfigureAwait(false);
                try
                {
                    await WriteFrameSafelyAsync(transport, response, loopToken).ConfigureAwait(false);
                    FlushDeferredFrameLogs();
                }
                finally
                {
                    writeGate.Release();
                }
                continue;
            }

            await _concurrencyGate.WaitAsync(loopToken).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await normalFrameGate.WaitAsync(loopToken).ConfigureAwait(false);
                    string? response;
                    try
                    {
                        _currentRequestToken.Value = loopToken;
                        _currentOutOfBandFrameWriter.Value = frameToWrite =>
                        {
                            writeGate.Wait(loopToken);
                            try
                            {
                                transport.WriteFrameAsync(frameToWrite, loopToken).GetAwaiter().GetResult();
                            }
                            finally
                            {
                                writeGate.Release();
                            }
                        };
                        BeginDeferredFrameLogs();
                        response = ProcessFrame(frame);
                    }
                    finally
                    {
                        _currentRequestToken.Value = CancellationToken.None;
                        _currentOutOfBandFrameWriter.Value = null;
                        normalFrameGate.Release();
                    }

                    await writeGate.WaitAsync(loopToken).ConfigureAwait(false);
                    try
                    {
                        await WriteFrameSafelyAsync(transport, response, loopToken).ConfigureAwait(false);
                        FlushDeferredFrameLogs();
                    }
                    finally
                    {
                        writeGate.Release();
                    }
                }
                finally
                {
                    _concurrencyGate.Release();
                }
            }, CancellationToken.None));
            SpinWait.SpinUntil(() => !_running || _activeRequests.Count > 0, TimeSpan.FromMilliseconds(50));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Console.Error.WriteLine("[cdidx-mcp] Server stopped. Restart `cdidx mcp` when your client reconnects.");
    }

    /// <summary>
    /// Process one MCP JSON-RPC line and write any response to the provided writer. Kept as a
    /// thin wrapper around <see cref="ProcessFrameAsync"/> so existing tests that drive a
    /// <see cref="TextWriter"/> directly stay source-compatible after the #1558 transport refactor.
    /// 1 行分の MCP JSON-RPC を処理して writer に書き込む薄いラッパ。#1558 のトランスポート抽象化後も
    /// 既存テストがソース互換となるよう、<see cref="ProcessFrameAsync"/> をそのまま呼び出す。
    /// </summary>
    internal async Task ProcessLineAsync(string line, TextWriter writer)
    {
        BeginDeferredFrameLogs();
        var response = ProcessFrame(line);
        if (response != null)
        {
            try
            {
                await WriteJsonLineAsync(writer, response).ConfigureAwait(false);
                FlushDeferredFrameLogs();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                Console.Error.WriteLine(BuildResponseWriteErrorLog(ex.Message));
                FlushDeferredFrameLogs();
            }
        }
    }

    private static async Task WriteJsonLineAsync(TextWriter writer, string response)
    {
        await writer.WriteAsync(response).ConfigureAwait(false);
        await writer.WriteAsync('\n').ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteFrameSafelyAsync(IMcpTransport transport, string? response, CancellationToken cancellationToken)
    {
        try
        {
            await transport.WriteFrameAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(BuildResponseWriteErrorLog("write operation was canceled"));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Console.Error.WriteLine(BuildResponseWriteErrorLog(ex.Message));
        }
    }

    private string BuildInvalidUtf8ParseErrorResponse(DecoderFallbackException ex)
    {
        DeferFrameLog(BuildInvalidUtf8ErrorLog(ex.Message));
        var errorResponse = CreateErrorResponse(hasId: true, id: null, code: -32700, message: "Parse error: invalid UTF-8 input",
            category: McpErrorEnvelope.CategoryParseError,
            suggestion: "Send one JSON-RPC 2.0 object per line encoded as valid UTF-8. Reject or re-encode malformed bytes before retrying.",
            retrySafe: false);
        return errorResponse.ToJsonString(_jsonOptions);
    }

    internal static string BuildInvalidUtf8ErrorLog(string detail)
        => $"[cdidx-mcp] JSON parse error: invalid UTF-8 input ({detail}). Send one UTF-8 JSON-RPC object per line; reject or re-encode malformed bytes before retrying.";

    /// <summary>
    /// Process one MCP JSON-RPC frame and return the wire-ready response string (or null when
    /// the request was a notification or otherwise yields no response). This is the
    /// transport-neutral seam used by <see cref="IMcpTransport"/> implementations (issue #1558).
    /// 1 フレーム分の MCP JSON-RPC を処理し、ワイヤー応答文字列を返す（通知などで応答なしの場合は null）。
    /// <see cref="IMcpTransport"/> 実装が共有するトランスポート非依存の合流点 (issue #1558)。
    /// </summary>
    internal string? ProcessFrame(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Reject oversized messages to prevent memory exhaustion
        // メモリ枯渇を防ぐため巨大メッセージを拒否
        if (line.Length > MaxLineLength)
        {
            DeferFrameLog(BuildOversizedMessageLog(line.Length));
            var errorResponse = CreateErrorResponse(null, -32700, "Message too large",
                category: McpErrorEnvelope.CategoryMessageTooLarge,
                suggestion: $"JSON-RPC frame exceeds the {MaxLineLength} byte cap. Split the request into smaller calls or use `batch_query` with smaller slots.",
                retrySafe: false);
            return errorResponse.ToJsonString(_jsonOptions);
        }

        JsonNode? request = null;
        var responseHasId = true;
        JsonNode? responseId = null;
        try
        {
            request = JsonNode.Parse(line);
            if (request == null)
                return null;

            ExtractResponseId(request, out responseHasId, out responseId);
            var response = HandleMessage(request);
            return response != null ? SerializeResponseOrFallback(response, responseHasId, responseId) : null;
        }
        catch (JsonException ex)
        {
            // Parse error / パースエラー
            DeferFrameLog(BuildJsonParseErrorLog(ex.Message));
            var errorResponse = CreateErrorResponse(null, -32700, "Parse error",
                category: McpErrorEnvelope.CategoryParseError,
                suggestion: "Send valid JSON-RPC 2.0 framed as a single line of UTF-8 JSON.",
                retrySafe: false);
            return errorResponse.ToJsonString(_jsonOptions);
        }
        catch (Exception ex)
        {
            // Stderr keeps the full message for local diagnostics, but the
            // wire response only carries the exception type so SQLite-style
            // "near 'foo': syntax error" detail or other content-bearing
            // strings cannot leak to the JSON-RPC client (#1530).
            // stderr には診断用に詳細を残すが、ネットワークに出るレスポンスには
            // 例外型のみを返し、SQLite の "near 'foo': syntax error" などを通じた
            // 内容漏れを防ぐ（#1530）。
            DeferFrameLog(BuildUnhandledLoopErrorLog(ex.Message));
            var classification = McpErrorEnvelope.ClassifyException(ex);
            var errorResponse = CreateErrorResponse(responseHasId, responseId, classification.JsonRpcCode,
                BuildSanitizedLoopErrorMessage(ex),
                category: classification.Category,
                suggestion: classification.Suggestion,
                retrySafe: classification.RetrySafe);
            return SerializeResponseOrFallback(errorResponse, responseHasId, responseId);
        }
    }

    private string SerializeResponseOrFallback(JsonNode response, bool hasId, JsonNode? id)
    {
        try
        {
            return _serializeResponse(response);
        }
        catch (Exception ex)
        {
            DeferFrameLog(BuildResponseSerializationErrorLog(ex.Message));
            return BuildMinimalInternalErrorResponse(hasId, id, ex);
        }
    }

    private void DeferFrameLog(string message)
        => DeferFrameLog(() => Console.Error.WriteLine(message));

    private void DeferFrameLog(Action writeLog)
    {
        var logs = _deferredFrameLogs.Value;
        if (logs is null)
        {
            writeLog();
            return;
        }

        logs.Add(writeLog);
    }

    private void BeginDeferredFrameLogs()
        => _deferredFrameLogs.Value = [];

    private void FlushDeferredFrameLogs()
    {
        var logs = _deferredFrameLogs.Value;
        if (logs is null)
            return;

        _deferredFrameLogs.Value = null;
        foreach (var log in logs)
            log();
    }

    private static void ExtractResponseId(JsonNode request, out bool hasId, out JsonNode? id)
    {
        if (request is JsonObject obj)
        {
            if (TryGetRequestId(obj, out hasId, out var requestId))
                id = requestId is null ? null : JsonNode.Parse(requestId.ToJsonString());
            else
                id = null;
            return;
        }

        // For malformed non-object JSON values, JSON-RPC error responses should still carry
        // id:null instead of disappearing when handling or serialization fails.
        hasId = true;
        id = null;
    }

    private static string BuildMinimalInternalErrorResponse(bool hasId, JsonNode? id, Exception ex)
    {
        var message = $"Internal error while serializing MCP response ({ex.GetType().Name}). See cdidx server stderr for details.";
        var builder = new StringBuilder("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":");
        builder.Append(JsonSerializer.Serialize(message));
        builder.Append('}');
        if (hasId)
        {
            builder.Append(",\"id\":");
            builder.Append(id is null ? "null" : id.ToJsonString());
        }
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Route a JSON-RPC message to the appropriate handler.
    /// JSON-RPCメッセージを適切なハンドラにルーティング。
    /// </summary>
    internal JsonNode? HandleMessage(JsonNode request)
    {
        if (request is not JsonObject obj)
            return CreateErrorResponse(hasId: false, id: null, code: -32600, message: "Invalid request: expected JSON object",
                category: McpErrorEnvelope.CategoryInvalidRequest,
                suggestion: "Send a JSON-RPC 2.0 object (e.g. {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}).",
                retrySafe: false);

        // Extract `method` defensively: a non-string `method` (e.g. `"method":42`) must not
        // throw before the auth gate runs, otherwise a token-protected server would surface
        // `-32603 "Internal error"` to an unauthenticated caller instead of `-32001
        // "Unauthorized"`, leaking that the request reached dispatch internals (#1559).
        // `method` は防御的に取り出す。`"method":42` のような非文字列が GetValue<string>()
        // で例外を投げると、認証ゲート前に -32603 が返ってしまい、未認証呼び出し元に dispatch
        // 内部まで届いた事実が漏れる (#1559)。
        var method = TryGetStringMember(obj, "method");
        if (!TryGetRequestId(obj, out var hasId, out var id))
            return CreateErrorResponse(hasId: true, id: null, code: -32600, message: "Invalid request: id must be string, number, or null",
                category: McpErrorEnvelope.CategoryInvalidRequest,
                suggestion: "JSON-RPC 2.0 `id` must be a string, integer, or null. Booleans/objects/arrays are not allowed.",
                retrySafe: false);

        if (method == "$/cancelRequest" || method == "notifications/cancelled")
        {
            var cancelAuth = _authenticator.Authenticate(request);
            if (cancelAuth.IsAuthenticated)
                TryCancelRequest(request["params"]);
            else
                Console.Error.WriteLine(BuildAuthFailureLog(method, cancelAuth.FailureReason));
            return null;
        }

        // Notifications (no id) don't get a response / 通知（idなし）にはレスポンスなし
        if (method == "notifications/initialized")
            return null;

        // Graceful shutdown via JSON-RPC notification (#1567). Without this, the only way to
        // stop a long-lived `cdidx mcp` server was to close the transport (stdin EOF / HTTP
        // listener stop), which races with in-flight work and forces clients to send SIGINT.
        // Treating both `notifications/shutdown` (the MCP spec-aligned name) and the legacy
        // LSP-style `notifications/exit` alias as graceful-stop signals lets clients drain the
        // current request and exit cleanly. Cancelling `_shutdownCts` unblocks any pending
        // `ReadFrameAsync` in the loop.
        // JSON-RPC 通知による graceful shutdown (#1567)。`_shutdownCts.Cancel()` でループ側の
        // `ReadFrameAsync` を unblock し、`_running = false` で次のイテレーション開始を抑止する。
        if (string.Equals(method, "notifications/shutdown", StringComparison.Ordinal)
            || string.Equals(method, "notifications/exit", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[cdidx-mcp] Received {method}; draining in-flight work and shutting down.");
            _running = false;
            try
            {
                if (!_shutdownCts.IsCancellationRequested)
                    _shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Server is already disposing — nothing more to cancel.
                // dispose 中なので追加 cancel は不要。
            }
            return null;
        }

        if (!hasId)
        {
            if (method != null && method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine(BuildUnknownNotificationLog(method));
            return null;
        }

        // Authenticate every responded request before dispatch so the auth contract is
        // uniform across `initialize`, `tools/list`, `tools/call`, and `ping`. Run auth even
        // when `method` is missing or malformed so a token-protected server cannot be probed
        // for method-shape errors without credentials (#1559). Notifications already
        // short-circuited above because they produce no response and cannot leak an error code.
        // すべての応答対象リクエストを dispatch 前に認証する。`method` が欠落・不正でも
        // 認証は走らせ、トークン保護下のサーバーで未認証呼び出し元に method 形式エラーを
        // 漏らさない (#1559)。通知は応答が無いため上のブランチで先に return している。
        var authResult = _authenticator.Authenticate(request);
        if (!authResult.IsAuthenticated)
        {
            DeferFrameLog(BuildAuthFailureLog(method, authResult.FailureReason));
            return CreateErrorResponse(hasId: true, id: id, code: McpErrorEnvelope.CodeUnauthorized, message: "Unauthorized",
                category: McpErrorEnvelope.CategoryPermissionDenied,
                suggestion: "Set CDIDX_MCP_AUTH_TOKEN on the server and include a matching params.auth.token (or an `Authorization: Bearer <token>` header for HTTP) on each request.",
                retrySafe: false);
        }

        if (method == null)
        {
            return CreateErrorResponse(hasId: true, id: id, code: -32600, message: "Invalid request: missing method",
                category: McpErrorEnvelope.CategoryInvalidRequest,
                suggestion: "JSON-RPC 2.0 requires a string `method` field.",
                retrySafe: false);
        }

        return DispatchWithRequestCancellation(id, () => method switch
        {
            "initialize" => HandleInitialize(id, request["params"]),
            "tools/list" => HandleToolsList(id),
            "tools/call" => HandleToolsCall(id, request["params"]),
            "resources/list" => HandleResourcesList(id, request["params"]),
            "resources/read" => HandleResourcesRead(id, request["params"]),
            "prompts/list" => HandlePromptsList(id),
            "prompts/get" => HandlePromptsGet(id, request["params"]),
            "logging/setLevel" => HandleLoggingSetLevel(id, request["params"]),
            "ping" => CreateSuccessResponse(hasId, id, new JsonObject()),
            _ => CreateErrorResponse(hasId: true, id: id, code: -32601, message: $"Method not found: {method}",
                category: McpErrorEnvelope.CategoryMethodNotFound,
                suggestion: "Supported methods: initialize, tools/list, tools/call, resources/list, resources/read, prompts/list, prompts/get, logging/setLevel, ping, notifications/initialized, notifications/cancelled, notifications/shutdown.",
                retrySafe: false),
        });
    }

    private JsonNode DispatchWithRequestCancellation(JsonNode? id, Func<JsonNode> action)
    {
        var requestKey = SerializeRequestId(id);
        if (requestKey == null)
            return action();

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_currentRequestToken.Value, _shutdownCts.Token);
        if (!_activeRequests.TryAdd(requestKey, requestCts))
        {
            return CreateErrorResponse(hasId: true, id: id, code: -32600, message: "Duplicate in-flight request id",
                category: McpErrorEnvelope.CategoryInvalidRequest,
                suggestion: "JSON-RPC request ids must be unique while a previous request with the same id is still running.",
                retrySafe: true);
        }
        RequestRegisteredForTests?.Invoke(id);

        var previousToken = _currentRequestToken.Value;
        try
        {
            _currentRequestToken.Value = requestCts.Token;
            requestCts.Token.ThrowIfCancellationRequested();
            return action();
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            return CreateCancelledResponse(id);
        }
        finally
        {
            _currentRequestToken.Value = previousToken;
            _activeRequests.TryRemove(requestKey, out _);
        }
    }

    private void TryCancelRequest(JsonNode? cancelParams)
    {
        var requestId = cancelParams?["id"] ?? cancelParams?["requestId"];
        var requestKey = SerializeRequestId(requestId);
        if (requestKey == null)
            return;
        if (_activeRequests.TryGetValue(requestKey, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* completed while cancellation was being delivered. */ }
        }
    }

    private static bool IsCancellationFrame(string frame)
    {
        try
        {
            var node = JsonNode.Parse(frame);
            if (node is not JsonObject obj)
                return false;
            var method = obj["method"]?.GetValue<string>();
            return string.Equals(method, "$/cancelRequest", StringComparison.Ordinal)
                || string.Equals(method, "notifications/cancelled", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    // Safe accessor that returns null instead of throwing when `name` is missing OR present
    // with a non-string value. JsonNode's `GetValue<string>()` throws InvalidOperationException
    // on non-string scalars, which would bubble out of HandleMessage and turn into -32603
    // before the auth gate runs.
    // `name` が無いケースと文字列以外で存在するケースのどちらでも null を返す安全アクセサ。
    // JsonNode の `GetValue<string>()` は非文字列で例外を投げ、認証ゲート前に -32603 化して
    // しまう。
    private static string? TryGetStringMember(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    // Cap on the logged `method` label. Long enough for every spec method (`notifications/cancelled`
    // is 23 chars) and any plausible client extension, short enough to keep one log line readable.
    // ログ出力する `method` の長さ上限。仕様メソッド全てと拡張も収まる長さで、1 行を読みやすく保つ。
    private const int LoggedMethodMaxLength = 64;

    // Strip caller-controlled control characters from `method` and clamp its length before
    // interpolating into a stderr log line. Prevents log forging: a malicious client could
    // otherwise send `"method":"evil\n[forged]"` and split the diagnostic across two lines
    // (#1559).
    // stderr 行に method を埋め込む前に制御文字を除去し、長さを切る。これをしないと
    // `"method":"evil\n[forged]"` で診断ログを 2 行に分割するログ偽造ができてしまう (#1559)。
    internal static string SanitizeMethodForLog(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return "(none)";
        var sb = new StringBuilder(Math.Min(method.Length, LoggedMethodMaxLength));
        var truncated = false;
        foreach (var ch in method)
        {
            if (sb.Length >= LoggedMethodMaxLength)
            {
                truncated = true;
                break;
            }
            if (ch < 0x20 || ch == 0x7F)
                sb.Append('?');
            else
                sb.Append(ch);
        }
        if (truncated)
            sb.Append('…');
        return sb.ToString();
    }

    // Stderr log for an auth failure. Mirrors the #1530 sanitization pattern: keep the
    // wire response generic and put the detail on stderr for local diagnostics. The method
    // label is run through SanitizeMethodForLog because it is caller-controlled and reaches
    // stderr before any allow-list check (#1559).
    // 認証失敗の stderr ログ。#1530 のサニタイズ方針に倣い、ワイヤ応答は一般化したまま
    // 詳細だけを stderr に残す。method は認証前に通るため SanitizeMethodForLog で
    // 制御文字除去と長さ切詰めを行う (#1559)。
    internal static string BuildAuthFailureLog(string? method, string? reason) =>
        $"[cdidx-mcp] Auth failed for method {SanitizeMethodForLog(method)}: {reason ?? "(unspecified)"}. Set CDIDX_MCP_AUTH_TOKEN on the server and include a matching params.auth.token on each request.";

    /// <summary>
    /// Handle the initialize handshake.
    /// initializeハンドシェイクを処理。
    /// </summary>
    private JsonNode HandleInitialize(JsonNode? id, JsonNode? _params)
    {
        CaptureClientInfo(_params);
        CaptureClientSession(_params);
        // Caller stickiness: allow upgrading from the default "unknown" bucket to a named
        // identity, but reject re-initialize attempts that swap one named identity for
        // another. Otherwise a single networked session could reset its rate-limit bucket
        // mid-flight by re-initializing under a fresh name (issue #1560 evidence — DoS
        // surface for networked MCP deployments).
        // caller の sticky 制御: 既定の "unknown" バケットからは名前付き ID への昇格を許すが、
        // 名前付き ID 同士のスワップは拒否する。これを許すと 1 セッション内で再 initialize により
        // 新しい名前でレート制限バケットをリセットできてしまい、#1560 が指摘する DoS 経路になる。
        var resolved = ResolveCallerIdentity(_params);
        if (_caller == "unknown")
        {
            _caller = resolved;
        }
        else if (resolved != _caller && resolved != "unknown")
        {
            DeferFrameLog(BuildCallerSwapRejectionLog(_caller, resolved));
        }
        var negotiated = NegotiateProtocolVersion(_params, out var requestedVersion);
        if (negotiated == null)
        {
            // No overlap between the client's requested version and this server's supported
            // set. Issue #1554: respond with structured `-32602` (invalid params) carrying the
            // requested + supported versions in `error.data` so clients can branch on it
            // instead of guessing why the handshake silently failed.
            // クライアント要求バージョンとサーバー対応集合に重なりがない場合。Issue #1554:
            // クライアントが分岐判定できるよう、`error.data` に要求バージョンと対応バージョン
            // を入れた -32602 (invalid params) を返す。
            DeferFrameLog(BuildUnsupportedProtocolLog(requestedVersion));
            return CreateUnsupportedProtocolError(id, requestedVersion);
        }

        var result = new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false
                },
                ["resources"] = new JsonObject
                {
                    ["subscribe"] = false,
                    ["listChanged"] = false
                },
                ["prompts"] = new JsonObject
                {
                    ["listChanged"] = false
                },
                ["logging"] = new JsonObject()
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
        return CreateSuccessResponse(true, id, result);
    }

    /// <summary>
    /// Resolve the protocol version to advertise back to the client. Returns the version
    /// string on success and `null` when the client pinned an unsupported version.
    /// Issue #1554: the previous handshake hardcoded a single version, so a future MCP
    /// spec bump would silently break clients. The negotiation now mirrors the MCP spec:
    /// echo the client's requested version when it is in our supported set, fall back to
    /// the preferred version when no version was supplied, and surface a structured error
    /// when there is no overlap (no silent downgrade so clients cannot mistakenly proceed
    /// against an unsupported wire format).
    /// クライアントに返すプロトコルバージョンを決める。成功時はバージョン文字列、
    /// クライアントが対応外バージョンを指定した場合は `null` を返す。Issue #1554:
    /// 旧実装はハードコードした 1 つのバージョンだけを返していたため、将来の仕様改訂で
    /// 無言で互換が壊れる。本ロジックは MCP 仕様準拠で、要求バージョンが対応集合にあれば
    /// それをそのまま返し、未指定なら既定バージョンを返し、重なりが無い場合は構造化エラー
    /// を返す（黙ってダウングレードしないことでクライアントが誤った wire format で進むのを防ぐ）。
    /// </summary>
    /// <summary>
    /// Capture `initialize.clientInfo.{name,version}` onto the per-session caller fields so
    /// audit records (#1562) can identify the requester without a parallel log source. Best-
    /// effort: malformed shapes leave the fields unset rather than failing the handshake.
    /// `initialize.clientInfo.{name,version}` をセッションの caller フィールドに記録し、
    /// audit ログ (#1562) で別ソースを引かなくても呼び出し元を辿れるようにする。形が壊れていても
    /// handshake は失敗させない（ベストエフォート）。
    /// </summary>
    private void CaptureClientInfo(JsonNode? initializeParams)
    {
        // Every initialize reseats caller identity so a reconnect that omits or malforms
        // clientInfo cannot inherit the previous client's name/version. Leaving the stale
        // values would mis-attribute later audit records to the wrong caller (#1562 review).
        // initialize ごとに caller を再設定する。clientInfo を省略 / 不正型で送ってきた
        // 再接続が前回のクライアント名/version を引き継がないようにするため。
        _clientName = null;
        _clientVersion = null;
        if (initializeParams is not JsonObject obj)
            return;
        if (obj["clientInfo"] is not JsonObject info)
            return;
        _clientName = TryReadStringMember(info, "name");
        _clientVersion = TryReadStringMember(info, "version");
    }

    private void CaptureClientSession(JsonNode? initializeParams)
    {
        _clientCapabilities = null;
        _clientRoots = [];
        if (initializeParams is not JsonObject obj)
            return;

        if (!obj.TryGetPropertyValue("capabilities", out var capabilities))
            obj.TryGetPropertyValue("clientCapabilities", out capabilities);
        if (capabilities is not null)
            _clientCapabilities = JsonNode.Parse(capabilities.ToJsonString());

        if (TryReadStringValue(obj["rootUri"]) is { Length: > 0 } rootUri)
            _clientRoots.Add(rootUri);

        if (obj["roots"] is JsonArray roots)
        {
            foreach (var root in roots)
            {
                var uri = TryReadStringValue(root?["uri"]) ?? TryReadStringValue(root);
                if (!string.IsNullOrWhiteSpace(uri))
                    _clientRoots.Add(uri);
            }
        }
    }

    internal JsonNode? ClientCapabilitiesForTests => _clientCapabilities is null ? null : JsonNode.Parse(_clientCapabilities.ToJsonString());

    internal string[] ClientRootsForTests => _clientRoots
        .Select(root => root?.GetValue<string>())
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Cast<string>()
        .ToArray();

    internal string McpLogLevelForTests => _mcpLogLevel;

    private static string? TryReadStringMember(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node))
            return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            return s;
        return null;
    }

    private JsonNode HandleResourcesList(JsonNode? id, JsonNode? listParams)
    {
        const int pageSize = 200;
        var offset = 0;
        if (listParams?["cursor"] is JsonValue cursorValue
            && cursorValue.TryGetValue<string>(out var cursor)
            && int.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            offset = parsed;
        }

        return WithDbReader(id, args: null, reader =>
        {
            var files = reader.ListFiles(limit: offset + pageSize + 1);
            var page = files.Skip(offset).Take(pageSize).ToArray();
            var resources = new JsonArray();
            foreach (var file in page)
            {
                resources.Add(new JsonObject
                {
                    ["uri"] = BuildResourceUri(file.Path),
                    ["name"] = file.Path,
                    ["description"] = $"{file.Path} ({file.Lang ?? "unknown"}, {file.Lines} lines)",
                    ["mimeType"] = GetResourceMimeType(file.Lang),
                });
            }

            var result = new JsonObject
            {
                ["resources"] = resources,
            };
            if (offset + pageSize < files.Count)
                result["nextCursor"] = (offset + pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return CreateSuccessResponse(true, id, result);
        });
    }

    private JsonNode HandleResourcesRead(JsonNode? id, JsonNode? readParams)
    {
        var uri = TryReadStringValue(readParams?["uri"]);
        if (string.IsNullOrWhiteSpace(uri))
            return CreateErrorResponse(hasId: true, id: id, code: -32602, message: "Missing resource uri",
                category: McpErrorEnvelope.CategoryMissingParameter,
                suggestion: "resources/read requires `params.uri` from resources/list, such as `cdidx://file/src/app.cs`.",
                retrySafe: false);

        if (!TryParseResourceUri(uri, out var path))
            return CreateErrorResponse(hasId: true, id: id, code: -32602, message: $"Invalid resource uri: {uri}",
                category: McpErrorEnvelope.CategoryInvalidArgument,
                suggestion: "Use a cdidx file resource URI returned by resources/list (`cdidx://file/<indexed-path>`).",
                retrySafe: false);

        return WithDbReader(id, args: null, reader =>
        {
            var files = reader.ListFiles(query: path, limit: 2);
            var file = files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal));
            if (file == null)
                return CreateErrorResponse(hasId: true, id: id, code: -32602, message: $"Resource not found: {uri}",
                    category: McpErrorEnvelope.CategoryInvalidArgument,
                    suggestion: "Call resources/list again and retry with one of the returned resource URIs.",
                    retrySafe: true);

            var excerpt = reader.GetExcerpt(file.Path, 1, Math.Max(1, file.Lines));
            var contents = new JsonArray
            {
                new JsonObject
                {
                    ["uri"] = BuildResourceUri(file.Path),
                    ["mimeType"] = GetResourceMimeType(file.Lang),
                    ["text"] = excerpt?.Content ?? string.Empty,
                }
            };
            return CreateSuccessResponse(true, id, new JsonObject { ["contents"] = contents });
        });
    }

    private JsonNode HandlePromptsList(JsonNode? id)
    {
        var prompts = new JsonArray
        {
            CreatePromptDefinition("summarize_file", "Summarize the API surface and responsibilities of an indexed file.", "path", "Indexed file path to summarize."),
            CreatePromptDefinition("find_unused", "Find likely unused symbols in an optional language or path scope.", "scope", "Optional language, module, or path scope."),
            CreatePromptDefinition("impact_of_changing", "Plan impact analysis for changing a symbol.", "symbol", "Symbol name to analyze."),
        };
        return CreateSuccessResponse(true, id, new JsonObject { ["prompts"] = prompts });
    }

    private JsonNode HandlePromptsGet(JsonNode? id, JsonNode? getParams)
    {
        var name = TryReadStringValue(getParams?["name"]);
        if (string.IsNullOrWhiteSpace(name))
            return CreateErrorResponse(hasId: true, id: id, code: -32602, message: "Missing prompt name",
                category: McpErrorEnvelope.CategoryMissingParameter,
                suggestion: "prompts/get requires `params.name`; call prompts/list to enumerate available names.",
                retrySafe: false);

        var args = getParams?["arguments"] as JsonObject;
        string? ReadArg(string key)
            => args != null && args.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var s)
                ? s
                : null;

        var text = name switch
        {
            "summarize_file" => $"Use the `outline` tool for `{ReadArg("path") ?? "<path>"}`, then use `excerpt` only for the ranges needed to summarize public API, key symbols, and responsibilities.",
            "find_unused" => $"Use `unused_symbols` with the requested scope `{ReadArg("scope") ?? "<scope>"}`. Cross-check surprising results with `references` or `callers` before recommending deletions.",
            "impact_of_changing" => $"Use `impact_analysis` for `{ReadArg("symbol") ?? "<symbol>"}`. Summarize direct callers, transitive callers, and files that likely need tests.",
            _ => null,
        };
        if (text == null)
            return CreateErrorResponse(hasId: true, id: id, code: -32602, message: $"Unknown prompt: {name}",
                category: McpErrorEnvelope.CategoryInvalidArgument,
                suggestion: "Call prompts/list and request one of the advertised prompt names.",
                retrySafe: false);

        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                },
            },
        };
        return CreateSuccessResponse(true, id, new JsonObject
        {
            ["description"] = name,
            ["messages"] = messages,
        });
    }

    private JsonNode HandleLoggingSetLevel(JsonNode? id, JsonNode? setLevelParams)
    {
        var level = TryReadStringValue(setLevelParams?["level"]);
        if (!IsSupportedMcpLogLevel(level))
            return CreateErrorResponse(hasId: true, id: id, code: -32602, message: "Invalid logging level",
                category: McpErrorEnvelope.CategoryInvalidArgument,
                suggestion: "logging/setLevel requires params.level to be one of: debug, info, notice, warning, error, critical, alert, emergency.",
                retrySafe: false);

        var previous = _mcpLogLevel;
        _mcpLogLevel = level!;
        EmitLogNotification("info", $"MCP logging level changed from {previous} to {_mcpLogLevel}.");
        return CreateSuccessResponse(true, id, new JsonObject());
    }

    private static JsonObject CreatePromptDefinition(string name, string description, string argumentName, string argumentDescription)
        => new()
        {
            ["name"] = name,
            ["description"] = description,
            ["arguments"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = argumentName,
                    ["description"] = argumentDescription,
                    ["required"] = false,
                },
            },
        };

    private static string BuildResourceUri(string path)
        => "cdidx://file/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static bool TryParseResourceUri(string uri, out string path)
    {
        path = string.Empty;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, "cdidx", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parsed.Host, "file", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parsed.AbsolutePath))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
        if (decoded.Length == 0
            || Path.IsPathRooted(decoded)
            || decoded.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }
        path = decoded;
        return true;
    }

    private static string? TryReadStringValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string GetResourceMimeType(string? lang)
        => lang?.ToLowerInvariant() switch
        {
            "csharp" => "text/x-csharp",
            "fsharp" => "text/x-fsharp",
            "vb" => "text/x-vb",
            "javascript" => "text/javascript",
            "typescript" => "text/typescript",
            "json" => "application/json",
            "markdown" => "text/markdown",
            "python" => "text/x-python",
            "rust" => "text/x-rust",
            "shell" => "text/x-shellscript",
            "sql" => "application/sql",
            "yaml" => "application/yaml",
            "xml" => "application/xml",
            _ => "text/plain",
        };

    /// <summary>
    /// Resolve the caller identity used by the per-(tool, caller) rate limiter from an
    /// `initialize` request's `clientInfo`. Falls back to `"unknown"` when the client did
    /// not supply a name so anonymous callers still get a coherent bucket of their own
    /// (instead of accidentally sharing one with named clients) (#1560).
    /// (tool, caller) ごとのレート制限で使う呼び出し元 ID を `initialize` の `clientInfo` から
    /// 解決する。`name` が無い場合は `"unknown"` を返し、匿名クライアントが他の名前付きクライアントと
    /// バケットを共有しないようにする（#1560）。
    /// </summary>
    internal static string ResolveCallerIdentity(JsonNode? initializeParams)
    {
        if (initializeParams is not JsonObject obj)
            return "unknown";
        if (obj["clientInfo"] is not JsonObject clientInfo)
            return "unknown";

        string? Read(string key)
        {
            if (clientInfo.TryGetPropertyValue(key, out var node)
                && node is JsonValue value
                && value.TryGetValue<string>(out var s)
                && !string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
            return null;
        }

        var name = Read("name");
        if (name == null)
            return "unknown";
        var version = Read("version");
        return version == null ? name : $"{name}/{version}";
    }

    internal static string? NegotiateProtocolVersion(JsonNode? initializeParams, out string? requestedVersion)
    {
        requestedVersion = null;
        if (initializeParams is JsonObject obj
            && obj.TryGetPropertyValue("protocolVersion", out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var versionString)
            && !string.IsNullOrWhiteSpace(versionString))
        {
            requestedVersion = versionString;
            foreach (var supported in SupportedProtocolVersions)
            {
                if (string.Equals(supported, versionString, StringComparison.Ordinal))
                    return supported;
            }
            return null;
        }

        // Field absent / null / malformed: fall back to the preferred version so clients
        // that omit the field (or send a non-string sentinel) keep working as before.
        // 未指定 / null / 不正型: 既定バージョンに fallback して既存クライアントの互換を保つ。
        return ProtocolVersion;
    }

    private static JsonObject CreateUnsupportedProtocolError(JsonNode? id, string? requestedVersion)
    {
        var supportedArray = new JsonArray();
        foreach (var supported in SupportedProtocolVersions)
            supportedArray.Add(JsonValue.Create(supported));

        // Keep the #1554 version-negotiation fields, then layer the #1581 canonical envelope
        // on top via BuildData so this path also carries `category` / `suggestion` /
        // `retry_safe` like every other JSON-RPC error.
        // #1554 のバージョン交渉用フィールドを保ちつつ、#1581 の canonical envelope を
        // BuildData で重ねて、他の JSON-RPC エラーと同様に category / suggestion / retry_safe
        // を含めるようにする。
        var extra = new JsonObject
        {
            ["supportedVersions"] = supportedArray
        };
        if (requestedVersion != null)
            extra["requestedVersion"] = requestedVersion;

        var data = McpErrorEnvelope.BuildData(
            McpErrorEnvelope.CategoryInvalidArgument,
            "Reissue `initialize` with one of `data.supportedVersions` in `params.protocolVersion`, or omit the field to fall back to the server's newest supported version.",
            retrySafe: false,
            extra);

        var error = new JsonObject
        {
            ["code"] = -32602,
            ["message"] = BuildUnsupportedProtocolMessage(requestedVersion),
            ["data"] = data
        };
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id is null ? JsonNode.Parse("null") : JsonNode.Parse(id.ToJsonString())
        };
        return response;
    }

    internal static string BuildUnsupportedProtocolMessage(string? requestedVersion)
    {
        var supported = string.Join(", ", SupportedProtocolVersions);
        var requested = string.IsNullOrEmpty(requestedVersion) ? "(unspecified)" : requestedVersion;
        return $"Unsupported MCP protocolVersion '{requested}'. Server supports: {supported}.";
    }

    internal static string BuildUnsupportedProtocolLog(string? requestedVersion)
    {
        var supported = string.Join(", ", SupportedProtocolVersions);
        var requested = string.IsNullOrEmpty(requestedVersion) ? "(unspecified)" : requestedVersion;
        return $"[cdidx-mcp] Rejecting initialize: client requested protocolVersion '{requested}', server supports {supported}. Upgrade the server or pin a supported version on the client.";
    }

    /// <summary>
    /// Build a structured `-32000` JSON-RPC error for a rate-limited tool call. Surfacing
    /// the limit category in `error.data.error_category` (alongside `tool`, `caller`, and
    /// `retry_after_ms`) lets MCP clients branch on the failure type without parsing the
    /// human-readable `message` (#1560).
    /// レート制限で拒否されたツール呼び出し用の構造化 `-32000` JSON-RPC エラーを構築する。
    /// `error.data.error_category` を併記することでクライアントが `message` 文字列を解析せず
    /// 失敗カテゴリで分岐できるようにする（#1560）。
    /// </summary>
    internal static JsonObject CreateRateLimitedErrorResponse(JsonNode? id, string tool, string caller, long retryAfterMs)
    {
        // #1560 contract preserved: `error_category`, `tool`, `caller`, `retry_after_ms`.
        // #1581 adds the canonical envelope (`category`, `suggestion`, `retry_safe`) alongside.
        // #1560 の契約（`error_category`, `tool`, `caller`, `retry_after_ms`）を維持しつつ、
        // #1581 で導入した canonical envelope（`category`, `suggestion`, `retry_safe`）を併記する。
        var extraData = new JsonObject
        {
            ["error_category"] = "rate_limited",
            ["tool"] = tool,
            ["caller"] = caller,
            ["retry_after_ms"] = retryAfterMs,
        };
        var data = McpErrorEnvelope.BuildData(
            category: McpErrorEnvelope.CategoryRateLimited,
            suggestion: $"Back off for at least {retryAfterMs} ms before retrying this tool, or raise {RateLimiterOptions.RpsEnvVar} / {RateLimiterOptions.BurstEnvVar} on the server.",
            retrySafe: true,
            extraData: extraData);
        var error = new JsonObject
        {
            ["code"] = -32000,
            ["message"] = $"Rate limit exceeded for tool '{tool}' (retry after {retryAfterMs} ms).",
            ["data"] = data,
        };
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error,
            ["id"] = id is null ? JsonNode.Parse("null") : JsonNode.Parse(id.ToJsonString())
        };
        return response;
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
        var progressToken = TryReadProgressToken(callParams);

        if (toolName == null)
        {
            var missingNameResponse = CreateErrorResponse(hasId: true, id: id, code: -32602, message: "Missing tool name",
                category: McpErrorEnvelope.CategoryMissingParameter,
                suggestion: "tools/call requires `params.name`. Send the tool identifier (e.g. \"search\", \"definition\") as a string.",
                retrySafe: false);
            // Even malformed tool-call requests are audited so a misbehaving client cannot
            // hide its activity by sending invalid params on every call (#1562).
            // 不正な tools/call も audit する。不正引数でログから消えるのを防ぐため (#1562)。
            TryEmitAudit("(missing)", id, args, missingNameResponse, DateTimeOffset.UtcNow, 0.0, errorType: "missing_tool_name");
            return missingNameResponse;
        }

        // Per-deployment enablement gate (#1561). Disabled known tools return `-32601 method
        // not found` so clients can branch on a structured JSON-RPC code; truly unknown names
        // still fall through to the existing `-32602 Unknown tool` path so typos remain
        // distinguishable from operator-disabled tools.
        // デプロイ単位の有効化ゲート (#1561)。既知ツールが無効化されている場合は `-32601`
        // を返し、クライアントが構造化 code で判定できるようにする。サーバーに無い名前は
        // 既存の `-32602 Unknown tool` 経路に流し、オペレータによる無効化と typo を区別する。
        if (McpToolFilter.IsKnownTool(toolName) && !_toolFilter.IsEnabled(toolName))
        {
            // Wire code stays at -32601 (#1561 contract) so existing clients keep working;
            // the `data.category = "tool_disabled"` envelope (#1581) is what new clients should
            // branch on to distinguish operator-disabled tools from typos (`tool_unknown`) and
            // missing methods (`method_not_found`).
            // ワイヤコードは #1561 契約に従い -32601 のまま維持し、既存クライアントを壊さない。
            // 新クライアントは `data.category = "tool_disabled"` で typo (`tool_unknown`) や
            // 未知メソッド (`method_not_found`) と区別する（#1581）。
            var disabledResponse = CreateErrorResponse(hasId: true, id: id, code: -32601, message: $"Tool not enabled: {toolName}",
                category: McpErrorEnvelope.CategoryToolDisabled,
                suggestion: "This tool is disabled on the server (CDIDX_MCP_TOOLS_ALLOW / CDIDX_MCP_TOOLS_DENY). Ask the operator to enable it or use a different tool.",
                retrySafe: false,
                extraData: new JsonObject { ["tool"] = toolName });
            // Audit operator-disabled attempts so the policy can be reviewed after the fact;
            // skipping them would let a deny-listed caller silently retry without trace
            // even though missing/unknown tools are captured (#1562 review).
            // オペレータ拒否された呼び出しも audit する。missing/unknown は記録されるのに
            // disabled だけ消えると、deny リストの効果を後から検証できなくなる。
            TryEmitAudit(toolName, id, args, disabledResponse, DateTimeOffset.UtcNow, 0.0, errorType: "tool_disabled");
            return disabledResponse;
        }

        Database.DbDebug.ResetContext();
        var metricsStartedAt = DateTimeOffset.UtcNow;
        var metricsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? metricsError = null;
        JsonNode response;
        try
        {
            if (ValidateCommonListArguments(args) is JsonObject listArgumentError)
            {
                metricsError = "invalid_list_argument";
                response = CreateToolErrorResponse(id, listArgumentError["message"]!.GetValue<string>(),
                    category: McpErrorEnvelope.CategoryInvalidArgument,
                    suggestion: "Send only non-empty string entries within the documented MCP array bounds.",
                    retrySafe: false,
                    extraData: listArgumentError);
            }
            else
            {
                // Per-(tool, caller) rate limiter check (#1560). Disabled by default; when an
                // operator opts in via CDIDX_MCP_RATE_LIMIT_RPS we still keep the assignment-then-
                // emit pattern so the rate-limit refusal lands in the audit log (#1562) instead of
                // disappearing into a direct return.
                // (tool, caller) ごとのレート制限 (#1560)。既定は無効。opt-in 時もアサインしてから
                // 監査出力する構造を保ち、refusal が audit log (#1562) から消えないようにする。
                var decision = RateLimiter.TryAcquire(toolName, _caller);
                if (!decision.Allowed)
                {
                    metricsError = "rate_limited";
                    DeferFrameLog(BuildRateLimitedLog(toolName, _caller, decision.RetryAfterMs));
                    response = CreateRateLimitedErrorResponse(id, toolName, _caller, decision.RetryAfterMs);
                }
                else
                {
                    response = toolName switch

                    {
                        "search" => ExecuteSearch(id, args),
                        "definition" => ExecuteDefinition(id, args),
                        "references" => ExecuteReferences(id, args),
                        "callers" => ExecuteCallers(id, args),
                        "callees" => ExecuteCallees(id, args),
                        "symbols" => ExecuteSymbols(id, args),
                        "files" => ExecuteFiles(id, args),
                        "find_in_file" => ExecuteFindInFile(id, args),
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
                        "index" => ExecuteIndex(id, args, progressToken),
                        "backfill_fold" => ExecuteBackfillFold(id, progressToken),
                        "suggest_improvement" => ExecuteSuggestImprovement(id, args),
                        _ => CreateErrorResponse(hasId: true, id: id, code: -32602, message: $"Unknown tool: {toolName}",
                            category: McpErrorEnvelope.CategoryToolUnknown,
                            suggestion: "Call tools/list to enumerate the available tool names for this server. Tool name match is case-sensitive.",
                            retrySafe: false,
                            extraData: new JsonObject { ["tool"] = toolName }),
                    };
                }
            }
        }
        catch (OperationCanceledException) when (_currentRequestToken.Value.IsCancellationRequested)
        {
            metricsError = nameof(OperationCanceledException);
            throw;
        }
        catch (Exception ex)
        {
            // Stderr captures the full ex.Message for local debugging, but the
            // JSON-RPC tool result is sanitized down to the tool name +
            // exception type. ex.Message can otherwise echo bound parameter
            // values (e.g. SQLite errors quote the offending literal) or path
            // / content fragments, which would leak to the client through the
            // MCP transcript (#1530).
            // stderr には ex.Message をそのまま残してローカルデバッグを支えるが、
            // JSON-RPC のツール結果は tool 名 + 例外型のみに絞る。SQLite 例外などは
            // バインド値や該当リテラルを含むため、生のメッセージをクライアントに渡すと
            // パスや索引内容が漏れる（#1530）。
            DeferFrameLog(() =>
            {
                Console.Error.WriteLine(BuildToolErrorLog(toolName, ex.Message));
                Database.DbDebug.DumpToStderr(ex);
            });
            metricsError = ex.GetType().Name;
            var classification = McpErrorEnvelope.ClassifyException(ex);
            response = CreateToolErrorResponse(true, id, BuildSanitizedToolErrorMessage(toolName, ex),
                category: classification.Category,
                suggestion: classification.Suggestion,
                retrySafe: classification.RetrySafe,
                extraData: new JsonObject
                {
                    ["tool"] = toolName,
                    ["exception_type"] = ex.GetType().Name,
                });
        }
        finally
        {
            Database.DbDebug.ResetContext();
            if (MetricsSink.IsActive)
            {
                metricsStopwatch.Stop();
                MetricsSink.Record(new MetricsEvent(
                    Timestamp: metricsStartedAt,
                    Tool: toolName,
                    Source: "mcp",
                    ElapsedMs: metricsStopwatch.Elapsed.TotalMilliseconds,
                    ExitCode: metricsError == null ? 0 : 1,
                    Language: TryReadStringArg(args, "language") ?? TryReadStringArg(args, "lang"),
                    Error: metricsError));
            }
        }

        // Audit observes both the wire response (for result_count / error_code / isError)
        // and any sanitized exception type, so emission happens after the metrics finally
        // block. Stop the stopwatch idempotently — the metrics path may have already
        // stopped it. TryEmitAudit is best-effort internally (#1562).
        // audit はワイヤーレスポンスと例外型の両方を参照するため metrics finally の後で
        // 出力する。Stopwatch.Stop は冪等。TryEmitAudit 内部でベストエフォート化済み (#1562)。
        metricsStopwatch.Stop();
        TryEmitAudit(toolName, id, args, response, metricsStartedAt, metricsStopwatch.Elapsed.TotalMilliseconds, errorType: metricsError);
        return response;
    }

    private static JsonNode? TryReadProgressToken(JsonNode? callParams)
    {
        var token = callParams?["_meta"]?["progressToken"];
        return token is null ? null : JsonNode.Parse(token.ToJsonString());
    }

    private void EmitProgressNotification(JsonNode? progressToken, long progress, long? total, string? message = null)
    {
        if (progressToken is null || _currentOutOfBandFrameWriter.Value is not { } writer)
            return;

        var parameters = new JsonObject
        {
            ["progressToken"] = JsonNode.Parse(progressToken.ToJsonString()),
            ["progress"] = progress,
        };
        if (total.HasValue)
            parameters["total"] = total.Value;
        if (!string.IsNullOrWhiteSpace(message))
            parameters["message"] = message;

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/progress",
            ["params"] = parameters,
        };
        writer(notification.ToJsonString(_jsonOptions));
    }

    private void EmitLogNotification(string level, string message)
    {
        if (_currentOutOfBandFrameWriter.Value is not { } writer)
            return;

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/message",
            ["params"] = new JsonObject
            {
                ["level"] = level,
                ["logger"] = "cdidx",
                ["data"] = message,
            },
        };
        writer(notification.ToJsonString(_jsonOptions));
    }

    /// <summary>
    /// Emit a single audit record for the just-executed tool call. Inspects the wire
    /// response to derive the result count and error code so the audit trail matches what
    /// the client actually observed (#1562). Failures are swallowed because audit emission
    /// must never break the underlying tool call.
    /// 直前に実行したツール呼び出しを 1 レコード分監査出力する。クライアントが実際に観測する
    /// 値と一致させるため、wire response から result count / error code を抽出する (#1562)。
    /// audit 失敗で本体ツール呼び出しを壊さないようベストエフォート化する。
    /// </summary>
    private void TryEmitAudit(string toolName, JsonNode? id, JsonNode? args, JsonNode response, DateTimeOffset startedAt, double elapsedMs, string? errorType)
    {
        if (_auditLog is null)
            return;

        try
        {
            var (errorCode, observedErrorType) = ExtractErrorCode(response);
            var resultCount = ExtractResultCount(response);
            var (argKeys, argLengths, argValuesEcho) = SanitizeArgs(args, _auditLog.IncludeValues);
            var evt = new AuditLogSink.AuditEvent(
                Timestamp: startedAt,
                Tool: toolName,
                CallerName: _clientName,
                CallerVersion: _clientVersion,
                RequestId: SerializeRequestId(id),
                ArgKeys: argKeys,
                ArgLengths: argLengths,
                ArgValues: argValuesEcho,
                ResultCount: resultCount,
                ElapsedMs: elapsedMs,
                ErrorCode: errorCode,
                ErrorType: errorType ?? observedErrorType);
            _auditLog.Record(evt);
        }
        catch
        {
            // Best-effort: an audit failure must not break the tool call.
            // ベストエフォート: audit 失敗で本体ツール呼び出しを壊さない。
        }
    }

    /// <summary>
    /// Translate the wire response into `(error_code, error_type)` for the audit record.
    /// 0 means success, positive means a tool-level error (isError=true), and negative is
    /// the verbatim JSON-RPC error code (e.g. -32602 invalid params).
    /// レスポンスを audit 用の `(error_code, error_type)` に変換する。0=成功、正値=
    /// tool エラー (isError=true)、負値=JSON-RPC エラーコード（例: -32602）。
    /// </summary>
    internal static (int Code, string? Type) ExtractErrorCode(JsonNode response)
    {
        if (response is not JsonObject obj)
            return (0, null);
        if (obj.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject errorObj)
        {
            var code = -32603;
            if (errorObj.TryGetPropertyValue("code", out var codeNode) && codeNode is JsonValue codeValue
                && codeValue.TryGetValue<int>(out var parsed))
                code = parsed;
            return (code, "jsonrpc_error");
        }
        if (obj.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonObject resultObj)
        {
            if (resultObj.TryGetPropertyValue("isError", out var isErrorNode)
                && isErrorNode is JsonValue isErrorValue
                && isErrorValue.TryGetValue<bool>(out var isError)
                && isError)
                return (1, "tool_error");
        }
        return (0, null);
    }

    /// <summary>
    /// Extract the result count from a successful tool response. Prefers
    /// `structuredContent.count`, falls back to the length of `structuredContent.results`,
    /// and returns null when neither shape is present (e.g. ping). Tool errors and JSON-RPC
    /// errors return null because there is no meaningful result-set count for those cases.
    /// 成功レスポンスから result count を抽出する。`structuredContent.count` を優先、
    /// `structuredContent.results` の長さに fallback。どちらも無い場合（例: ping）と
    /// tool/JSON-RPC エラー時は null を返す。
    /// </summary>
    internal static int? ExtractResultCount(JsonNode response)
    {
        if (response is not JsonObject obj)
            return null;
        if (obj["result"] is not JsonObject result)
            return null;
        if (result["isError"] is JsonValue isErrorValue
            && isErrorValue.TryGetValue<bool>(out var isError) && isError)
            return null;
        if (result["structuredContent"] is not JsonObject structured)
            return null;
        if (structured["count"] is JsonValue countValue && countValue.TryGetValue<int>(out var count))
            return count;
        if (structured["results"] is JsonArray results)
            return results.Count;
        return null;
    }

    /// <summary>
    /// Build the `(arg_keys, arg_lengths, arg_values?)` audit triple. Values are echoed
    /// only when the operator has opted in via `--audit-log-include-values`; otherwise we
    /// keep keys + per-key length so AI argument shapes can be reconstructed without
    /// persisting query bodies that may contain sensitive substrings (#1562).
    /// audit 用の `(arg_keys, arg_lengths, arg_values?)` を組み立てる。値は
    /// `--audit-log-include-values` がオンの場合のみ転写し、それ以外はキーと長さだけ残す
    /// （secret 風の検索クエリを取り込まないため）。
    /// </summary>
    internal static (IReadOnlyList<string> Keys, IReadOnlyList<KeyValuePair<string, int>> Lengths, JsonNode? ValuesEcho)
        SanitizeArgs(JsonNode? args, bool includeValues)
    {
        if (args is not JsonObject argsObj)
            return (Array.Empty<string>(), Array.Empty<KeyValuePair<string, int>>(), null);

        var keys = new List<string>(argsObj.Count);
        var lengths = new List<KeyValuePair<string, int>>(argsObj.Count);
        foreach (var (key, value) in argsObj)
        {
            keys.Add(key);
            lengths.Add(new KeyValuePair<string, int>(key, AuditLogSink.MeasureArgLength(value)));
        }

        JsonNode? echo = null;
        if (includeValues)
        {
            try
            {
                echo = argsObj.DeepClone();
            }
            catch
            {
                echo = null;
            }
        }
        return (keys, lengths, echo);
    }

    private static string? SerializeRequestId(JsonNode? id)
    {
        if (id is null)
            return null;
        try
        {
            return id.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadStringArg(JsonNode? args, string key)
    {
        if (args is null)
            return null;

        try
        {
            var node = args[key];
            if (node is null)
                return null;
            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
                return string.IsNullOrWhiteSpace(stringValue) ? null : stringValue;
        }
        catch
        {
            // Best-effort: any oddity in argument shape just suppresses the language hint.
            // ベストエフォート: 引数形状が不正でも language ヒントを抑止するだけ。
        }
        return null;
    }

    internal static string BuildOversizedMessageLog(int lineLength) =>
        $"[cdidx-mcp] Message too large ({lineLength} bytes), rejecting. Split the request into smaller JSON-RPC messages or shorter arguments, then retry.";

    internal static string BuildJsonParseErrorLog(string detail) =>
        $"[cdidx-mcp] JSON parse error: {detail}. Send one UTF-8 JSON-RPC object per line and retry.";

    internal static string BuildUnhandledLoopErrorLog(string detail) =>
        $"[cdidx-mcp] Error: {detail}. This request was skipped; fix the request or inspect the server environment, then retry.";

    internal static string BuildResponseSerializationErrorLog(string detail) =>
        $"[cdidx-mcp] Error serializing response: {detail}. Returning a minimal JSON-RPC error response when possible.";

    internal static string BuildResponseWriteErrorLog(string detail) =>
        $"[cdidx-mcp] Error writing response: {detail}. The request was handled but the client connection may already be closed.";

    internal static string BuildToolErrorLog(string toolName, string detail) =>
        $"[cdidx-mcp] Tool error ({toolName}): {detail}. Fix the tool arguments, refresh the index if needed, then retry.";

    // Stderr log emitted when the rate limiter denies a tool call. Mirrors the JSON-RPC
    // `-32000` payload (tool + caller + retry_after_ms) so operators tailing the MCP log
    // can correlate spikes with the structured error returned on the wire (#1560).
    // レート制限で拒否されたツール呼び出しを stderr に記録する。配線上の JSON-RPC `-32000`
    // ペイロードと内容を揃え、運用側がログ追跡から状況把握できるようにする（#1560）。
    internal static string BuildRateLimitedLog(string toolName, string caller, long retryAfterMs) =>
        $"[cdidx-mcp] Rate limit exceeded: tool='{toolName}', caller='{caller}', retry_after_ms={retryAfterMs}. Increase {RateLimiterOptions.RpsEnvVar} / {RateLimiterOptions.BurstEnvVar} on the server, or back off and retry.";

    internal static string BuildCallerSwapRejectionLog(string current, string attempted) =>
        $"[cdidx-mcp] Ignoring re-initialize with new clientInfo identity '{attempted}': retaining original caller '{current}' so rate-limit buckets cannot be reset mid-session.";

    internal static string BuildUnknownNotificationLog(string method) =>
        $"[cdidx-mcp] Ignoring unknown notification: {method}";

    internal static bool IsSupportedMcpLogLevel(string? level)
        => level is "debug" or "info" or "notice" or "warning" or "error" or "critical" or "alert" or "emergency";

    // Wire-safe error body for the tool catch-all. Mentions the tool and the
    // exception type so the client can branch (retry vs. surface to user)
    // while keeping bound values or matched content out of the response (#1530).
    // For CodeIndexException (#1580) the Code / Category / Path / Hint fields
    // are author-controlled and therefore safe to echo verbatim, so the client
    // gets the structured failure metadata it needs without re-introducing the
    // ex.Message leak vector #1530 closed.
    // ツール catch-all のワイヤー向け本文。クライアントが分岐できるよう tool 名と
    // 例外型は残し、バインド値や一致内容は含めない（#1530）。CodeIndexException (#1580)
    // の Code / Category / Path / Hint は実装側で固定したフィールドなのでそのまま転写し、
    // #1530 で封じた ex.Message 漏れを再現させずに失敗詳細をクライアントへ届ける。
    internal static string BuildSanitizedToolErrorMessage(string toolName, Exception ex)
    {
        if (ex is CodeIndexException codeIndexEx)
            return $"Error executing {toolName} ({ex.GetType().Name}) [{codeIndexEx.Code}/{codeIndexEx.Category}]{BuildPathFragment(codeIndexEx)}{BuildHintFragment(codeIndexEx)}. See cdidx server stderr for details.";
        return $"Error executing {toolName} ({ex.GetType().Name}). See cdidx server stderr for details.";
    }

    // Wire-safe error body for the JSON-RPC loop catch-all. Same rationale as
    // the tool catch-all (#1530, #1580).
    // JSON-RPC ループ catch-all のワイヤー向け本文。理由はツール catch-all と同じ（#1530, #1580）。
    internal static string BuildSanitizedLoopErrorMessage(Exception ex)
    {
        if (ex is CodeIndexException codeIndexEx)
            return $"Internal error ({ex.GetType().Name}) [{codeIndexEx.Code}/{codeIndexEx.Category}]{BuildPathFragment(codeIndexEx)}{BuildHintFragment(codeIndexEx)}. See cdidx server stderr for details.";
        return $"Internal error ({ex.GetType().Name}). See cdidx server stderr for details.";
    }

    // Quote so paths/hints with spaces stay one token. Single quotes are kept
    // for human readability — this is a display contract, not a shell-parsing one.
    // 空白を含む path / hint が 2 トークンに見えないよう単引用符でラップする。
    private static string BuildPathFragment(CodeIndexException ex) =>
        string.IsNullOrEmpty(ex.Path) ? string.Empty : $" path='{ex.Path}'";

    private static string BuildHintFragment(CodeIndexException ex) =>
        string.IsNullOrEmpty(ex.Hint) ? string.Empty : $" hint='{ex.Hint}'";

    // Tool implementations are in McpToolHandlers.cs / ツール実装は McpToolHandlers.cs に分離

    // --- DB helper / DBヘルパー ---

    private JsonNode WithDbReader(JsonNode? id, JsonNode? args, Func<DbReader, JsonNode> action)
    {
        // Accept SQLite file: URIs the same way the CLI does (QueryCommandRunner.WithDb),
        // so AI agents on read-only mounts can pass `--db file:///abs/path?immutable=1` and
        // reach the read-only escape hatch in DbContext. File.Exists is skipped for URI-
        // shaped values because they may carry query params meaningless to the filesystem.
        // CLI と同じく file: URI を受け付け、サンドボックス用の escape hatch に到達できるようにする。
        var isUri = _dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!isUri && !File.Exists(LongPath.EnsureWindowsPrefix(_dbPath)))
        {
            // Drop any stale cached context so the next tool call can re-open after the user
            // creates the DB (e.g. via an external `cdidx index`). Without this, a missed
            // file lookup would leave a closed/disposed handle blocking later open attempts.
            // ユーザーが後から DB を作った場合に再オープンできるよう、キャッシュをここで破棄。
            CloseSharedDb();
            return CreateToolErrorResponse(true, id, $"Database not found: {_dbPath}. Run 'cdidx index <projectPath>' first.",
                category: McpErrorEnvelope.CategoryIndexMissing,
                suggestion: "Run `cdidx index <projectPath>` to build the index before retrying. The DB lives at `.cdidx/codeindex.db` by default.",
                retrySafe: true);
        }

        var db = GetOrOpenSharedDb();
        if (!_sharedDbReadMigrated)
        {
            db.TryMigrateForRead();
            _sharedDbReadMigrated = true;
        }
        // Reuse the connection-scoped schema cache so each MCP tool call no longer
        // re-runs PRAGMA table_info / PRAGMA index_list per DbReader (issue #1565),
        // and hand the per-request cancellation token to the reader so SQLite work
        // the tool kicks off can observe shutdown / client-disconnect cancellation
        // (#1567). The token is `CancellationToken.None` outside an in-flight request,
        // preserving the existing behaviour for ad-hoc callers like tests that drive
        // `WithDbReader` through internals.
        // MCP ツール呼び出しごとの schema 再走査を排除し (issue #1565)、
        // per-request cancellation token を reader に渡して SQLite 作業が
        // shutdown / 切断を観測できるようにする (#1567)。
        var requestToken = _currentRequestToken.Value;
        requestToken.ThrowIfCancellationRequested();
        var reader = new DbReader(db, requestToken);
        reader.IncludeGenerated = args?["includeGenerated"]?.GetValue<bool>() ?? false;
        return reader.RunWithGeneratedScope(() => action(reader));
    }

    /// <summary>
    /// Open the per-session DbContext on first use and reuse it on every subsequent call.
    /// Centralising the open lets us pay the connection setup, pragma application, and SQL
    /// function registration once per MCP session instead of once per tool invocation
    /// (#1494). The MCP loop is single-threaded, so no locking is required.
    /// MCP セッション初回呼び出し時に DbContext を開き、以後は再利用する。接続セットアップや
    /// PRAGMA・SQL 関数登録のコストを毎ツール呼び出しごとに払わないようにする（#1494）。
    /// MCP ループは単一スレッドのためロック不要。
    /// </summary>
    internal DbContext GetOrOpenSharedDb()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sharedDb != null)
            return _sharedDb;

        _sharedDb = new DbContext(_dbPath);
        return _sharedDb;
    }

    /// <summary>
    /// Mark the shared DbContext as already covered by `TryMigrateForRead`. Write tools that
    /// run `InitializeSchema` reuse the same connection, so the read path can skip the
    /// migration round trip on later calls.
    /// 書き込みツールが InitializeSchema を流した後の共有 DbContext に対し、read path の
    /// TryMigrateForRead を省略するためのマーカ。
    /// </summary>
    internal void MarkSharedDbMigrated() => _sharedDbReadMigrated = true;

    private void CloseSharedDb()
    {
        _sharedDb?.Dispose();
        _sharedDb = null;
        _sharedDbReadMigrated = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CloseSharedDb();
        try
        {
            if (!_shutdownCts.IsCancellationRequested)
                _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed / 既に dispose 済み
        }
        _shutdownCts.Dispose();
        _concurrencyGate.Dispose();
        GC.SuppressFinalize(this);
    }

    // --- JSON-RPC helpers / JSON-RPCヘルパー ---

    private static bool TryGetRequestId(JsonObject request, out bool hasId, out JsonNode? id)
    {
        hasId = request.TryGetPropertyValue("id", out id);
        if (!hasId)
            return true;

        if (id is null)
            return true;

        if (id is JsonValue)
        {
            var serialized = id.ToJsonString();
            if (serialized.Length == 0)
                return false;

            var first = serialized[0];
            return first == '"' || first == '-' || char.IsDigit(first) || first == 'n';
        }

        return false;
    }

    private static JsonObject CreateSuccessResponse(JsonNode? id, JsonNode result)
        => CreateSuccessResponse(id is not null, id, result);

    private static JsonObject CreateSuccessResponse(bool hasId, JsonNode? id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = result
        };
        if (hasId)
            response["id"] = id is null ? JsonNode.Parse("null") : JsonNode.Parse(id.ToJsonString());
        return response;
    }

    private static JsonObject CreateErrorResponse(JsonNode? id, int code, string message,
        string category, string suggestion, bool retrySafe, JsonObject? extraData = null)
        => CreateErrorResponse(id is not null, id, code, message, category, suggestion, retrySafe, extraData);

    // Issue #1581: every MCP error response carries a structured `data` envelope
    // (`category` / `suggestion` / `retry_safe`) so clients can branch on a stable
    // category instead of parsing the human-readable `message`. Category-specific
    // extras (e.g. rate-limited's `retry_after_ms`) merge in via `extraData`.
    // #1581: すべての MCP エラー応答に `category` / `suggestion` / `retry_safe` を含む
    // 構造化 `data` を載せ、クライアントが文字列解析せず分岐できるようにする。カテゴリ
    // 固有フィールド（rate-limited の `retry_after_ms` 等）は `extraData` で合流する。
    private static JsonObject CreateErrorResponse(bool hasId, JsonNode? id, int code, string message,
        string category, string suggestion, bool retrySafe, JsonObject? extraData = null)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = McpErrorEnvelope.BuildData(category, suggestion, retrySafe, extraData),
            }
        };
        if (hasId)
            response["id"] = id is null ? JsonNode.Parse("null") : JsonNode.Parse(id.ToJsonString());
        return response;
    }

    private static JsonObject CreateCancelledResponse(JsonNode? id)
        => CreateErrorResponse(hasId: true, id: id, code: McpErrorEnvelope.CodeRequestCancelled,
            message: "Request cancelled",
            category: McpErrorEnvelope.CategoryRequestCancelled,
            suggestion: "The client cancelled this request before completion. Reissue the call if the work is still needed.",
            retrySafe: true);

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
        return CreateSuccessResponse(true, id, result);
    }

    /// <summary>
    /// Create a tool error response (MCP format with isError flag).
    /// Optional <paramref name="similarValues"/> attach a structured
    /// <c>data.similar_values</c> array to the result so MCP clients can offer
    /// recovery alternatives without parsing the human-readable message (#1582).
    /// ツールエラーレスポンスを作成（isError フラグ付き MCP 形式）。
    /// <paramref name="similarValues"/> を渡すと結果に構造化された
    /// <c>data.similar_values</c> 配列を添えるので、MCP クライアントは
    /// 人間向けメッセージを解析せずに代替候補を提示できる (#1582)。
    /// </summary>
    private static JsonObject CreateToolErrorResponse(JsonNode? id, string message,
        string category, string suggestion, bool retrySafe, JsonObject? extraData = null,
        IReadOnlyList<string>? similarValues = null)
        => CreateToolErrorResponse(id is not null, id, message, category, suggestion, retrySafe, extraData, similarValues);

    // Backward-compatible overload for tool handlers that return argument-validation
    // failures (#1581). These were all "missing parameter / invalid argument" call sites
    // before the envelope was introduced, so the default classification is `invalid_argument`
    // / retry_safe=false. The optional `similarValues` carries the structured did-you-mean
    // candidates for unknown enum values (#1582). Sites that have richer context should
    // call the explicit overload.
    // 引数バリデーション失敗を返す既存ツールハンドラ向けの互換オーバーロード（#1581）。
    // envelope 導入前の呼び出しは全て「引数不正」系だったため既定カテゴリを `invalid_argument`
    // / retry_safe=false とする。任意の `similarValues` は未知 enum 値に対する構造化された
    // did-you-mean 候補 (#1582)。より具体的なカテゴリを持てる呼び出し元は明示オーバーロード
    // を使う。
    private static JsonObject CreateToolErrorResponse(JsonNode? id, string message,
        IReadOnlyList<string>? similarValues = null)
        => CreateToolErrorResponse(id, message,
            category: McpErrorEnvelope.CategoryInvalidArgument,
            suggestion: "Tool argument validation failed. Inspect the tool's `inputSchema` via tools/list and adjust the call.",
            retrySafe: false,
            similarValues: similarValues);

    // Issue #1581: tool-result errors mirror the JSON-RPC error envelope by including
    // the same `category` / `suggestion` / `retry_safe` triple under `result.structuredContent`.
    // Existing clients that only read `content[0].text` + `isError` keep working; new clients
    // can read `structuredContent` to branch on the category.
    // #1581: ツール結果エラーにも JSON-RPC エラーと同じ `category` / `suggestion` / `retry_safe`
    // を `result.structuredContent` に載せる。既存の `content[0].text` + `isError` だけを読む
    // クライアントは互換のまま、新規クライアントは `structuredContent` でカテゴリ分岐できる。
    private static JsonObject CreateToolErrorResponse(bool hasId, JsonNode? id, string message,
        string category, string suggestion, bool retrySafe, JsonObject? extraData = null,
        IReadOnlyList<string>? similarValues = null)
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
            ["isError"] = true,
            ["structuredContent"] = McpErrorEnvelope.BuildData(category, suggestion, retrySafe, extraData),
        };
        if (similarValues != null && similarValues.Count > 0)
        {
            var similarArray = new JsonArray();
            foreach (var value in similarValues)
                similarArray.Add(JsonValue.Create(value));
            result["data"] = new JsonObject
            {
                ["similar_values"] = similarArray,
            };
        }
        return CreateSuccessResponse(hasId, id, result);
    }

    private static JsonObject CreateToolDefinition(string name, string description, JsonObject inputSchema,
        JsonObject? annotations = null)
    {
        var def = new JsonObject
        {
            ["name"] = name,
            ["description"] = AppendLanguageSupportClause(name, description),
            ["inputSchema"] = inputSchema
        };
        if (annotations != null)
            def["annotations"] = annotations;
        return def;
    }

    private static string AppendLanguageSupportClause(string name, string description)
    {
        var clause = name switch
        {
            "references" or "callers" or "callees" or "deps" or "impact_analysis" or "unused_symbols" or "symbol_hotspots"
                => $"Language support: Supports graph/reference extraction for: {GraphLanguageList()}. Unsupported `lang` values are reported with graph-support metadata when the tool returns graph-support fields; use `search`, `definition`, `excerpt`, or `files` for non-graph languages.",
            "definition" or "symbols" or "outline" or "analyze_symbol"
                => $"Language support: Supports symbol extraction for: {SymbolLanguageList()}. Search-only languages can still be indexed and filtered by file tools but may have no symbol rows.",
            "search" or "find_in_file" or "files" or "map"
                => $"Language support: Supports indexed file/content filters for every detected language listed by `languages`: {DetectedLanguageList()}. Symbol and graph fields are available only for the languages whose capabilities are advertised by `languages`.",
            "excerpt" or "status" or "validate"
                => $"Language support: Language-agnostic over indexed files and diagnostics for every detected language listed by `languages`: {DetectedLanguageList()}. This tool does not interpret a `lang` filter.",
            "languages"
                => "Language support: This is the authoritative language catalog for MCP tools; it lists every detected language plus symbol_extraction and graph_queries capability flags.",
            "index"
                => $"Language support: Indexes every detected language listed by `languages`: {DetectedLanguageList()}, then extracts symbols and graph references only where the catalog advertises those capabilities.",
            "batch_query"
                => "Language support: Language behavior is inherited from each nested read-only tool; consult each returned payload and the `languages` tool for capabilities.",
            "backfill_fold" or "ping" or "suggest_improvement"
                => "Language support: Language-independent tool; it does not interpret `lang` filters.",
            _ => "Language support: See the `languages` tool for detected languages and per-language symbol_extraction / graph_queries capabilities.",
        };

        return $"{description} {clause}";
    }

    private static string DetectedLanguageList()
        => string.Join(", ", FileIndexer.GetLanguageExtensions()
            .Values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(lang => lang, StringComparer.Ordinal));

    private static string SymbolLanguageList()
        => string.Join(", ", SymbolExtractor.GetSupportedLanguages()
            .OrderBy(lang => lang, StringComparer.Ordinal));

    private static string GraphLanguageList()
        => string.Join(", ", ReferenceExtractor.GetSupportedLanguages()
            .OrderBy(lang => lang, StringComparer.Ordinal));

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
