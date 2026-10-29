using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LogMark.Services.Hosting;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Bead lmk-jsnz item 2: the cockpit's route page showed generic text because
/// the v0 vault summary carried no route descriptions, although routes.md
/// declares them (MCP get_routes returns them). Own vault, like the backlinks
/// tests, so the shared DaemonFixture's counts do not move.
/// </summary>
public sealed class V0RoutesRouteDescriptionTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "lm-rd-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        try { Directory.Delete(_vault, true); } catch { }
    }

    private async Task<JsonElement> Summary()
    {
        var services = VaultHostBootstrap.Build(_vault);
        services.EntryCache.WarmupTask?.Wait();
        var sse = new SseChannel();
        _owned.Add(sse);
        var routes = new V0Routes(services, new ConceptPageStore(_vault), new SettingsStore(_vault), sse, HostIdentity.Engine);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var server = new DaemonHttpServer(port, routes);
        _owned.Add(server);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        return JsonDocument.Parse(await client.GetStringAsync("/api/v0/vault/summary")).RootElement
            .GetProperty("summary").Clone();
    }

    private void Plant(string routesMd)
    {
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_system"));
        File.WriteAllText(Path.Combine(_vault, "logmark", "_system", "routes.md"), routesMd);
        var tasks = Path.Combine(_vault, "logmark", "architecture", "_tasks");
        Directory.CreateDirectory(tasks);
        File.WriteAllText(Path.Combine(tasks, "2026.md"),
            "---\n\n## 2026-07-12 11:00\n\ntask: sketch the queue design\n");
        var plain = Path.Combine(_vault, "logmark", "scratch", "_tasks");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "2026.md"),
            "---\n\n## 2026-07-12 12:00\n\ntask: tidy notes\n");
    }

    [Fact]
    public async Task Route_census_carries_the_routes_md_description()
    {
        Plant("# Routes\n\n## Active\n### arch\n- path: architecture\n- description: System design and decisions\n\n" +
              "### scratch\n- path: scratch\n");

        var all = (await Summary()).GetProperty("all_routes").EnumerateArray().ToList();

        var arch = all.Single(r => r.GetProperty("path").GetString() == "arch");
        Assert.Equal("System design and decisions", arch.GetProperty("description").GetString());
        var top = (await Summary()).GetProperty("top_routes").EnumerateArray()
            .Single(r => r.GetProperty("path").GetString() == "arch");
        Assert.Equal("System design and decisions", top.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Route_without_a_description_carries_none()
    {
        Plant("# Routes\n\n## Active\n### arch\n- path: architecture\n- description: System design and decisions\n\n" +
              "### scratch\n- path: scratch\n");

        var scratch = (await Summary()).GetProperty("all_routes").EnumerateArray()
            .Single(r => r.GetProperty("path").GetString() == "scratch");

        Assert.False(scratch.TryGetProperty("description", out _));
    }
}
