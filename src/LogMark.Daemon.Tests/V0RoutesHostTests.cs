using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LogMark.Services.Hosting;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

public sealed class V0RoutesHostTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "lm-host-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        try { Directory.Delete(_vault, true); } catch { }
    }

    private async Task<JsonElement> Capabilities(HostIdentity? host, string path = "/api/v0/capabilities")
    {
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_tasks"));
        var services = VaultHostBootstrap.Build(_vault);
        services.EntryCache.WarmupTask?.Wait();
        var sse = new SseChannel();
        _owned.Add(sse);
        var settings = new SettingsStore(_vault);
        var routes = new V0Routes(services, new ConceptPageStore(_vault), settings, sse, host);
        var compat = new CockpitCompatRoutes(routes, settings);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var server = new DaemonHttpServer(port, routes, onError: null, compat: compat);
        _owned.Add(server);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        return JsonDocument.Parse(await client.GetStringAsync(path)).RootElement.Clone();
    }

    [Theory]
    [InlineData("/api/v0/capabilities")]
    [InlineData("/api/capabilities")]
    public async Task Engine_host_is_reported_on_v0_and_through_the_compat_alias(string path)
    {
        var json = await Capabilities(HostIdentity.Engine, path);
        var host = json.GetProperty("host");
        Assert.Equal("oss-cli", host.GetProperty("providerId").GetString());
        Assert.Equal("LogMark Engine", host.GetProperty("product").GetString());
    }

    [Fact]
    public async Task Capabilities_keep_exactly_the_six_vault_flags()
    {
        // The cockpit reads hasDistillation / hasLintScan / hasInferenceSettings
        // as "this host serves the Node-only endpoint". A C# host sets one only
        // in the same change that implements that endpoint (cockpit plan R3).
        var flags = (await Capabilities(HostIdentity.Engine)).GetProperty("capabilities")
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "hasBehaviorCatalog", "hasConceptProducer", "hasFeedSystem", "hasFolderSemantics", "hasNotation", "hasSessions" }, flags);
    }

    // Bead lmk-hqgm: only the desktop's closed dream loop writes concept
    // pages, so the cockpit hides concept surfaces where nothing produces them.
    [Fact]
    public async Task Desktop_host_reports_a_concept_producer()
    {
        var caps = (await Capabilities(HostIdentity.Desktop)).GetProperty("capabilities");
        Assert.True(caps.GetProperty("hasConceptProducer").GetBoolean());
    }

    [Fact]
    public async Task Engine_host_reports_no_concept_producer()
    {
        var caps = (await Capabilities(HostIdentity.Engine)).GetProperty("capabilities");
        Assert.False(caps.GetProperty("hasConceptProducer").GetBoolean());
    }

    [Fact]
    public async Task Unset_host_reports_no_concept_producer_explicitly()
    {
        var caps = (await Capabilities(null)).GetProperty("capabilities");
        Assert.False(caps.GetProperty("hasConceptProducer").GetBoolean());
    }

    [Fact]
    public async Task Host_is_omitted_when_unset()
    {
        var json = await Capabilities(null);
        Assert.False(json.TryGetProperty("host", out _));
    }
}
