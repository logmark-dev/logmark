using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Minimal loopback harness calling <see cref="CockpitStaticServer.Handle"/>
/// directly (not the full v0/compat/static dispatch chain - that is covered
/// separately by <see cref="CockpitStaticServerFixture"/>). A real
/// HttpListenerContext can only come from a live HttpListener, so this runs
/// one, dispatching every accepted request straight to the server under
/// test.
/// </summary>
internal sealed class DirectHandlerFixture : IDisposable
{
    public HttpClient Client { get; }

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public DirectHandlerFixture(CockpitStaticServer server)
    {
        var port = FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();

        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
                try
                {
                    server.Handle(ctx);
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { }
                }
            }
        });

        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        Client.Dispose();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}

/// <summary>
/// Unit tests exercising the "no cockpit baked in" branch deterministically:
/// pass an assembly with no LogMark.cockpit.* resources (this test
/// assembly itself) rather than depending on whether
/// LogMark.Knowledge/dist/ui existed when LogMark.Kernel was last built.
/// </summary>
public class CockpitStaticServerNoCockpitTests
{
    private static CockpitStaticServer NoCockpitServer() =>
        new(typeof(CockpitStaticServerNoCockpitTests).Assembly);

    [Fact]
    public void HasCockpit_is_false_for_an_assembly_with_no_embedded_dist()
    {
        Assert.False(NoCockpitServer().HasCockpit);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/timeline")]
    [InlineData("/assets/whatever.js")]
    public async Task Handle_writes_an_inline_api_only_notice_for_any_path_never_a_500(string path)
    {
        using var fixture = new DirectHandlerFixture(NoCockpitServer());

        var res = await fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("API only", body);
        Assert.Contains("npm run build", body);
    }
}

/// <summary>
/// Unit tests against the real embedded cockpit (LogMark.Kernel's own
/// assembly, the default constructor). Gated on
/// <see cref="CockpitStaticServer.HasCockpit"/> as the resource probe: a
/// build that never ran `npm run build` in LogMark.Knowledge/ carries no
/// LogMark.cockpit.* resources, so these tests skip gracefully with a clear
/// message instead of failing.
/// </summary>
public class CockpitStaticServerRealResourceTests
{
    private readonly ITestOutputHelper _output;

    public CockpitStaticServerRealResourceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private bool SkipUnlessCockpitBuilt(CockpitStaticServer server)
    {
        if (server.HasCockpit) return false;
        _output.WriteLine(
            "SKIPPED: no LogMark.cockpit.* resources in LogMark.Kernel.dll. " +
            "Run `npm run build` in LogMark.Knowledge/ before building LogMark.Kernel to exercise this test.");
        return true;
    }

    [Fact]
    public async Task Root_path_serves_index_html()
    {
        var server = new CockpitStaticServer();
        if (SkipUnlessCockpitBuilt(server)) return;
        using var fixture = new DirectHandlerFixture(server);

        var res = await fixture.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("<!doctype html>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unmatched_client_side_route_falls_back_to_index_html()
    {
        var server = new CockpitStaticServer();
        if (SkipUnlessCockpitBuilt(server)) return;
        using var fixture = new DirectHandlerFixture(server);

        var res = await fixture.Client.GetAsync("/timeline");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("<!doctype html>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assets_get_content_type_by_extension()
    {
        var server = new CockpitStaticServer();
        if (SkipUnlessCockpitBuilt(server)) return;
        using var fixture = new DirectHandlerFixture(server);

        // Reconstruct real request paths from the embedded resource names
        // rather than hardcoding a content hash that changes every rebuild.
        // "LogMark.cockpit.assets.<filename>" -> "/assets/<filename>" is
        // valid because the glob's Link-derived naming keeps the trailing
        // filename segment verbatim (see CockpitStaticServer's class doc).
        var assembly = typeof(CockpitStaticServer).Assembly;
        var assetPrefix = CockpitStaticServer.ResourcePrefix + "assets.";
        var assetNames = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(assetPrefix, StringComparison.Ordinal))
            .ToList();

        var jsFile = assetNames.First(n => n.EndsWith(".js", StringComparison.Ordinal));
        var cssFile = assetNames.First(n => n.EndsWith(".css", StringComparison.Ordinal));
        // Two woff2 fonts ship (Inter + JetBrains Mono) - either exercises
        // the same content-type mapping, so First rather than Single.
        var woff2File = assetNames.First(n => n.EndsWith(".woff2", StringComparison.Ordinal));

        await AssertServesWithContentType(fixture, ToRequestPath(assetPrefix, jsFile), "text/javascript");
        await AssertServesWithContentType(fixture, ToRequestPath(assetPrefix, cssFile), "text/css");
        await AssertServesWithContentType(fixture, ToRequestPath(assetPrefix, woff2File), "font/woff2");
    }

    private static string ToRequestPath(string assetPrefix, string resourceName) =>
        "/assets/" + resourceName[assetPrefix.Length..];

    private static async Task AssertServesWithContentType(DirectHandlerFixture fixture, string path, string expectedContentType)
    {
        var res = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith(expectedContentType, res.Content.Headers.ContentType?.MediaType);
    }
}

/// <summary>
/// Full-chain integration: a real DaemonHttpServer (v0 + compat + static,
/// the same composition production uses) over loopback HTTP, proving the
/// static server's addition does not disturb the existing /api/* contract
/// and that DaemonHttpServer.AcceptLoopAsync actually reaches
/// CockpitStaticServer for non-/api paths.
/// </summary>
public sealed class CockpitStaticServerFixture : IDisposable
{
    public HttpClient Client { get; }
    public bool HasCockpit { get; }

    private readonly string _vault;
    private readonly DaemonHttpServer _server;
    private readonly SseChannel _sse;
    private readonly VaultChangeWatcher _watcher;

    public CockpitStaticServerFixture()
    {
        HasCockpit = new CockpitStaticServer().HasCockpit;

        _vault = Path.Combine(Path.GetTempPath(), "lm-daemon-static-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_tasks"));
        File.WriteAllText(Path.Combine(_vault, "logmark", "constitution.md"), "# Static Test Vault\n");

        var services = VaultHostBootstrap.Build(_vault);
        services.EntryCache.WarmupTask?.Wait();

        _sse = new SseChannel();
        _watcher = new VaultChangeWatcher(_vault, _sse.Broadcast, debounceMs: 100);
        var settings = new SettingsStore(_vault);
        var v0 = new V0Routes(services, new ConceptPageStore(_vault), settings, _sse);
        var compat = new CockpitCompatRoutes(v0, settings);

        var port = FreePort();
        _server = new DaemonHttpServer(port, v0, onError: null, compat: compat);
        _server.Start();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        Client.Dispose();
        _server.Dispose();
        _watcher.Dispose();
        _sse.Dispose();
        try { Directory.Delete(_vault, recursive: true); } catch { }
    }
}

public class CockpitStaticServerRouteTests : IClassFixture<CockpitStaticServerFixture>
{
    private readonly CockpitStaticServerFixture _f;
    private readonly ITestOutputHelper _output;

    public CockpitStaticServerRouteTests(CockpitStaticServerFixture fixture, ITestOutputHelper output)
    {
        _f = fixture;
        _output = output;
    }

    private bool Skip()
    {
        if (_f.HasCockpit) return false;
        _output.WriteLine(
            "SKIPPED: no LogMark.cockpit.* resources in LogMark.Kernel.dll. " +
            "Run `npm run build` in LogMark.Knowledge/ before building LogMark.Kernel to exercise this test.");
        return true;
    }

    [Fact]
    public async Task Root_serves_html_over_the_full_dispatch_chain()
    {
        if (Skip()) return;

        var res = await _f.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Client_side_route_falls_back_to_index_html_over_http()
    {
        if (Skip()) return;

        var res = await _f.Client.GetAsync("/timeline");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unmatched_api_path_still_returns_problem_json_not_html()
    {
        // Not gated on HasCockpit: this proves adding the static server did
        // not change /api/* behavior, regardless of whether a cockpit was
        // baked in - CockpitCompatRoutes claims and terminates every /api/*
        // request before the static server ever sees it.
        var res = await _f.Client.GetAsync("/api/nope");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not-found", json.GetProperty("code").GetString());
    }
}
