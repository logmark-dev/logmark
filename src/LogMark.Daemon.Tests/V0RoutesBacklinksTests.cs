using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LogMark.Services.Hosting;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Bead lmk-nziz: the cockpit's entry detail said "No backlinks yet" on every
/// entry because the v0 entry response dropped the backlinks the kernel
/// resolves. Own vault: the shared DaemonFixture's counts must not move.
/// </summary>
public sealed class V0RoutesBacklinksTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "lm-bl-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _owned = new();

    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
        try { Directory.Delete(_vault, true); } catch { }
    }

    private async Task<JsonElement> Get(string path)
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
        return JsonDocument.Parse(await client.GetStringAsync(path)).RootElement.Clone();
    }

    [Fact]
    public async Task Entry_detail_carries_the_entries_that_link_to_it()
    {
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_ideas"));
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_tasks"));
        File.WriteAllText(Path.Combine(_vault, "logmark", "_ideas", "2026.md"),
            "---\n\n## 2026-07-12 11:00\n\nidea: the linked idea\n_id: aa11bb22 ^aa11bb22\n");
        File.WriteAllText(Path.Combine(_vault, "logmark", "_tasks", "2026.md"),
            "---\n\n## 2026-07-13 09:00\n\ntask: act on [[logmark/_ideas/2026#^aa11bb22|the idea]]\n" +
            "_task: none | postponed: 0 | cc33dd44 ^cc33dd44\n");

        var entry = (await Get("/api/v0/entries/aa11bb22")).GetProperty("entry");

        var backlinks = entry.GetProperty("backlinks").EnumerateArray().ToList();
        var link = Assert.Single(backlinks);
        Assert.Equal("cc33dd44", link.GetProperty("id").GetString());
        Assert.Equal("task", link.GetProperty("kind").GetString());
        Assert.Contains("act on", link.GetProperty("title").GetString());
        Assert.Equal("logmark/_tasks/2026.md", link.GetProperty("sourcePath").GetString());
    }

    [Fact]
    public async Task Entry_detail_sends_an_empty_backlinks_array_when_nothing_links_to_it()
    {
        Directory.CreateDirectory(Path.Combine(_vault, "logmark", "_ideas"));
        File.WriteAllText(Path.Combine(_vault, "logmark", "_ideas", "2026.md"),
            "---\n\n## 2026-07-12 11:00\n\nidea: alone\n_id: ee55ff66 ^ee55ff66\n");

        var entry = (await Get("/api/v0/entries/ee55ff66")).GetProperty("entry");

        Assert.Empty(entry.GetProperty("backlinks").EnumerateArray());
    }
}
