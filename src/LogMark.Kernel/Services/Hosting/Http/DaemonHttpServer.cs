using System.Net;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Loopback HttpListener host for the v0 API (lifted CaptureBridgeServer
/// pattern). Binds 127.0.0.1 only and refuses any request whose Host header
/// names another host (DNS rebinding); LOGMARK_DAEMON_TOKEN stays
/// reserved-unimplemented per the contract.
/// </summary>
public sealed class DaemonHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly V0Routes _routes;
    private readonly CockpitCompatRoutes? _compat;
    private readonly CockpitStaticServer _static = new();
    private readonly int _port;
    private readonly Action<string>? _onError;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    // `compat` is a trailing optional param (not inserted before `onError`)
    // so the app (AppKernelServer) and daemon (Program.cs) composition
    // roots, which construct this with `(port, routes, onError)` today,
    // keep compiling unchanged until they are wired to build a
    // CockpitCompatRoutes of their own.
    public DaemonHttpServer(int port, V0Routes routes, Action<string>? onError = null, CockpitCompatRoutes? compat = null)
    {
        _port = port;
        _routes = routes;
        _onError = onError;
        _compat = compat;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        // HTTP.sys on Windows serves both loopback names through the one
        // prefix, and IsLoopbackHost below refuses the rest. The managed
        // listener on macOS and Linux matches Host against its prefixes
        // before our code runs, so localhost:<port> got its own 404 until it
        // was named here too (rc7 osx QA). It still binds loopback only.
        if (!OperatingSystem.IsWindows()) _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port => _port;

    public void Start()
    {
        if (_listener.IsListening) return;
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested && _listener.IsListening)
                {
                    _onError?.Invoke($"accept loop faulted: {ex}");
                }
                break; // listener stopped or disposed
            }
            _ = Task.Run(() =>
            {
                try
                {
                    // Dispatch order: v0 first (claims only /api/v0/*), then
                    // the cockpit compat surface (claims and terminates
                    // every /api/* request - 200, its own 404, or 501), then
                    // the embedded cockpit static/SPA server (claims and
                    // terminates every remaining path: an exact asset match,
                    // index.html as the SPA fallback, or an inline API-only
                    // notice when no cockpit was baked in). The bare
                    // fallback below is unreachable in normal operation -
                    // kept only as a last-resort safety net.
                    if (!IsLoopbackHost(ctx.Request.Headers["Host"], _port))
                    {
                        V0Routes.WriteProblem(ctx, 421, "misdirected-host", "Misdirected request",
                            $"This daemon only answers requests addressed to 127.0.0.1:{_port} or localhost:{_port}.");
                        return;
                    }
                    if (_routes.Handle(ctx)) return;
                    if (_compat is not null && _compat.Handle(ctx)) return;
                    if (_static.Handle(ctx)) return;
                    WriteFallbackNotFound(ctx);
                }
                catch (Exception ex)
                {
                    _onError?.Invoke($"request handler faulted ({ctx.Request.RawUrl}): {ex}");
                    try { ctx.Response.Abort(); } catch { }
                }
            }, ct);
        }
    }

    // DNS rebinding: a page on an attacker's name that resolves to 127.0.0.1
    // is same-origin to the browser, so binding loopback alone does not stop
    // it reading every GET. Its Host header still carries the attacker's
    // name, so only a loopback host on our port is served.
    internal static bool IsLoopbackHost(string? host, int port) =>
        string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase);

    // Last-resort safety net: CockpitStaticServer.Handle always returns true
    // (it terminates every non-/api request one way or another), so this is
    // not expected to run in normal operation. Plain text, not problem+json -
    // this path is outside the API surface entirely.
    private static void WriteFallbackNotFound(HttpListenerContext ctx)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Not found");
        ctx.Response.StatusCode = 404;
        ctx.Response.ContentType = "text/plain";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _cts?.Dispose();
    }
}
