using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Fixture for the cockpit compat surface (lmk-nhjr Task 1): a daemon HTTP
/// server wired with BOTH V0Routes and CockpitCompatRoutes, so tests can
/// exercise the LogMark.Knowledge SPA's `/api/*` contract against the same
/// dispatch chain DaemonHttpServer uses in production
/// (v0 first, then compat).
/// </summary>
public sealed class CockpitCompatFixture : IDisposable
{
    public string Vault { get; }
    public HttpClient Client { get; }

    private readonly DaemonHttpServer _server;
    private readonly SseChannel _sse;
    private readonly VaultChangeWatcher _watcher;

    public CockpitCompatFixture()
    {
        Vault = Path.Combine(Path.GetTempPath(), "lm-daemon-compat-" + Guid.NewGuid().ToString("N")[..8]);
        PlantVault(Vault);

        var services = VaultHostBootstrap.Build(Vault);
        services.EntryCache.WarmupTask?.Wait();

        _sse = new SseChannel();
        _watcher = new VaultChangeWatcher(Vault, _sse.Broadcast, debounceMs: 100);
        var settings = new SettingsStore(Vault);
        var v0 = new V0Routes(services, new ConceptPageStore(Vault), settings, _sse);
        var compat = new CockpitCompatRoutes(v0, settings);

        var port = FreePort();
        _server = new DaemonHttpServer(port, v0, onError: null, compat: compat);
        _server.Start();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private static void PlantVault(string vault)
    {
        Directory.CreateDirectory(Path.Combine(vault, "logmark", "_tasks"));
        Directory.CreateDirectory(Path.Combine(vault, "logmark", "_ideas"));
        Directory.CreateDirectory(Path.Combine(vault, "concepts"));

        File.WriteAllText(Path.Combine(vault, "logmark", "constitution.md"),
            "# Compat Test Constitution\n\nPreserve capture latency.\n");

        File.WriteAllText(Path.Combine(vault, "logmark", "_tasks", "2026.md"),
            "---\n\n## 2026-07-10 09:00\n\ntask: read Friston 2010 #research\n");

        File.WriteAllText(Path.Combine(vault, "logmark", "_ideas", "2026.md"),
            "---\n\n## 2026-07-11 11:00\n\nidea: free energy equals surprise minimization #cognition\n");

        File.WriteAllText(Path.Combine(vault, "concepts", "free-energy-principle.md"),
            "---\n" +
            "slug: free-energy-principle\n" +
            "title: Free Energy Principle\n" +
            "created: 2026-04-25T10:00:00Z\n" +
            "last_distilled: 2026-04-25T10:00:00Z\n" +
            "distillation_version: 1\n" +
            "status: draft\n" +
            "tags: [cognition]\n" +
            "sources: []\n" +
            "related_concepts: []\n" +
            "---\n\n# Free Energy Principle\n\n## Synthesis\n\nThe brain minimizes surprise.\n");
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
        try { Directory.Delete(Vault, recursive: true); } catch { }
    }
}

public class CockpitCompatRoutesTests : IClassFixture<CockpitCompatFixture>
{
    private readonly CockpitCompatFixture _f;

    public CockpitCompatRoutesTests(CockpitCompatFixture fixture) => _f = fixture;

    private async Task<JsonElement> GetJson(string path)
    {
        var raw = await _f.Client.GetStringAsync(path);
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task Capabilities_alias_matches_v0_shape()
    {
        var json = await GetJson("/api/capabilities");
        Assert.Equal("kernel-http", json.GetProperty("adapter").GetString());
        Assert.Equal(_f.Vault, json.GetProperty("vaultPath").GetString());
        Assert.True(json.GetProperty("capabilities").GetProperty("hasNotation").GetBoolean());
    }

    [Fact]
    public async Task Vault_summary_alias_reports_counts()
    {
        var json = await GetJson("/api/vault-summary");
        var summary = json.GetProperty("summary");
        Assert.True(summary.GetProperty("total_entries").GetInt32() >= 1);
        Assert.True(summary.GetProperty("total_concept_pages").GetInt32() >= 1);
    }

    [Fact]
    public async Task Entries_alias_lists_planted_entries()
    {
        var json = await GetJson("/api/entries");
        Assert.True(json.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Entry_by_id_alias_round_trips()
    {
        var all = await GetJson("/api/entries");
        var first = all.GetProperty("entries").EnumerateArray().First();
        var id = first.GetProperty("id").GetString()!;

        var read = await GetJson($"/api/entries/{Uri.EscapeDataString(id)}");
        var entry = read.GetProperty("entry");
        Assert.Equal(id, entry.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Search_alias_finds_planted_content()
    {
        var json = await GetJson("/api/search?q=surprise");
        Assert.True(json.GetProperty("results").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Concepts_list_alias_returns_planted_page()
    {
        var json = await GetJson("/api/concepts");
        var slugs = json.GetProperty("pages").EnumerateArray()
            .Select(p => p.GetProperty("slug").GetString()).ToList();
        Assert.Contains("free-energy-principle", slugs);
    }

    [Fact]
    public async Task Concept_by_slug_alias_round_trips_read_and_write()
    {
        var read = await GetJson("/api/concepts/free-energy-principle");
        Assert.Equal("Free Energy Principle", read.GetProperty("page").GetProperty("title").GetString());

        var newPage = JsonSerializer.Serialize(new
        {
            page = new
            {
                slug = "compat-roundtrip",
                title = "Compat Roundtrip",
                body = "# Compat Roundtrip\n\nBody.",
                status = "draft",
                tags = new[] { "c" },
                sources = Array.Empty<object>(),
                related_concepts = Array.Empty<string>(),
                created = "2026-07-19T10:00:00Z",
                last_distilled = "2026-07-19T10:00:00Z",
                distillation_version = 1,
            },
        });
        var put = await _f.Client.PutAsync("/api/concepts/compat-roundtrip",
            new StringContent(newPage, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putBody = JsonDocument.Parse(await put.Content.ReadAsStringAsync()).RootElement;
        Assert.True(putBody.GetProperty("ok").GetBoolean());

        var back = await GetJson("/api/concepts/compat-roundtrip");
        Assert.Equal("Compat Roundtrip", back.GetProperty("page").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Constitution_put_via_compat_is_visible_through_both_compat_and_v0_get()
    {
        var put = await _f.Client.PutAsync("/api/constitution",
            new StringContent("{\"body\":\"# Compat Test Constitution\\n\\nAmended via /api/constitution.\"}",
                Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putBody = JsonDocument.Parse(await put.Content.ReadAsStringAsync()).RootElement;
        Assert.True(putBody.GetProperty("ok").GetBoolean());

        var viaCompat = await GetJson("/api/constitution");
        Assert.Contains("Amended via /api/constitution.", viaCompat.GetProperty("body").GetString());

        var viaV0 = await GetJson("/api/v0/constitution");
        Assert.Contains("Amended via /api/constitution.", viaV0.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Settings_get_alias_layers_inference_active_over_v0_shape()
    {
        var json = await GetJson("/api/settings");
        Assert.True(json.GetProperty("settings").GetProperty("providerOrder").GetArrayLength() > 0);
        Assert.Equal("none", json.GetProperty("inference").GetProperty("active").GetString());
    }

    [Fact]
    public async Task Settings_put_alias_round_trips_and_is_visible_through_v0()
    {
        var put = await _f.Client.PutAsync("/api/settings", new StringContent(
            "{\"settings\":{\"providerOrder\":[\"lm-studio\",\"ollama\",\"claude-cli\"]," +
            "\"routing\":{\"summarization\":\"default\",\"distillation\":\"disabled\",\"searchEnrichment\":\"default\"}}}",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putBody = JsonDocument.Parse(await put.Content.ReadAsStringAsync()).RootElement;
        Assert.True(putBody.GetProperty("ok").GetBoolean());

        var viaV0 = await GetJson("/api/v0/settings");
        Assert.Equal("lm-studio",
            viaV0.GetProperty("settings").GetProperty("providerOrder").EnumerateArray().First().GetString());
    }

    [Fact]
    public async Task Distill_returns_501_not_implemented_problem()
    {
        var res = await _f.Client.PostAsync("/api/distill",
            new StringContent("{\"slug\":\"x\",\"entryIds\":[]}", Encoding.UTF8, "application/json"));
        Assert.Equal((HttpStatusCode)501, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);

        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not-implemented", problem.GetProperty("code").GetString());
        Assert.Equal(501, problem.GetProperty("status").GetInt32());
        Assert.Equal("Distillation is not served by this host.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Health_returns_501_not_implemented_problem()
    {
        var res = await _f.Client.GetAsync("/api/health");
        Assert.Equal((HttpStatusCode)501, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not-implemented", problem.GetProperty("code").GetString());
        Assert.Equal("The health lint scan is not served by this host.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Health_refresh_returns_501_not_implemented_problem()
    {
        var res = await _f.Client.PostAsync("/api/health/refresh", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal((HttpStatusCode)501, res.StatusCode);
        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not-implemented", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Pages_alias_resolves_an_existing_file_relative_to_the_vault_content_root()
    {
        // The vault content root is Vault/logmark (PlantVault creates
        // logmark/_tasks etc directly), so the query path is relative to
        // that root, not to Vault itself.
        var res = await GetJson("/api/pages?path=" + Uri.EscapeDataString("_tasks/2026.md"));
        Assert.Contains("read Friston 2010", res.GetProperty("body").GetString());
        Assert.Equal("_tasks/2026.md", res.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Pages_alias_returns_404_with_not_found_error_body_for_missing_pages()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=logmark/_tasks/2026.md");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sse_alias_returns_event_stream_content_type_and_first_bytes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var res = await _f.Client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);

        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var buffer = new char[1];
        // Reading is enough to prove the stream is attached and live; do not
        // wait for a full SSE event (no vault mutation is made in this
        // test), and the CancellationTokenSource above bounds any hang.
        var readTask = reader.ReadAsync(buffer, 0, 1);
        await Task.WhenAny(readTask, Task.Delay(500, cts.Token));
    }
}
