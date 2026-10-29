using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

public sealed class DaemonFixture : IDisposable
{
    public string Vault { get; }
    public string BaseUrl { get; }
    public HttpClient Client { get; }

    private readonly DaemonHttpServer _server;
    private readonly SseChannel _sse;
    private readonly VaultChangeWatcher _watcher;

    public DaemonFixture()
    {
        Vault = Path.Combine(Path.GetTempPath(), "lm-daemon-" + Guid.NewGuid().ToString("N")[..8]);
        PlantVault(Vault);

        var services = VaultHostBootstrap.Build(Vault);
        services.EntryCache.WarmupTask?.Wait();

        _sse = new SseChannel();
        _watcher = new VaultChangeWatcher(Vault, _sse.Broadcast, debounceMs: 100);
        var routes = new V0Routes(services, new ConceptPageStore(Vault), new SettingsStore(Vault), _sse);

        var port = FreePort();
        _server = new DaemonHttpServer(port, routes);
        _server.Start();
        BaseUrl = $"http://127.0.0.1:{port}";
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    private static void PlantVault(string vault)
    {
        Directory.CreateDirectory(Path.Combine(vault, "logmark", "_tasks"));
        Directory.CreateDirectory(Path.Combine(vault, "logmark", "_ideas"));
        Directory.CreateDirectory(Path.Combine(vault, "concepts"));

        File.WriteAllText(Path.Combine(vault, "logmark", "constitution.md"),
            "# Test Constitution\n\nPreserve capture latency.\n");

        File.WriteAllText(Path.Combine(vault, "logmark", "_tasks", "2026.md"),
            "---\n\n## 2026-07-10 09:00\n\ntask: read Friston 2010 #research\n" +
            "---\n\n## 2026-07-11 10:00\n\ntask: plan the daemon cut #planning\n");

        File.WriteAllText(Path.Combine(vault, "logmark", "_ideas", "2026.md"),
            "---\n\n## 2026-07-12 11:00\n\nidea: free energy equals surprise minimization #cognition\n");

        // 11 distinct routes so the vault summary's route census exercises
        // Take(10) truncation: 10 routes with 2 entries each outrank a lone
        // low-count route, which must still appear in all_routes but not in
        // the top-10-capped top_routes.
        for (int i = 1; i <= 10; i++)
        {
            var routeDir = Path.Combine(vault, "logmark", "projects", $"r{i:D2}", "_tasks");
            Directory.CreateDirectory(routeDir);
            File.WriteAllText(Path.Combine(routeDir, "2026.md"),
                $"---\n\n## 2026-07-0{i % 9 + 1} 09:00\n\ntask: route r{i:D2} item one #census\n" +
                $"---\n\n## 2026-07-0{i % 9 + 1} 09:05\n\ntask: route r{i:D2} item two #census\n");
        }
        var lowRouteDir = Path.Combine(vault, "logmark", "projects", "route-lo", "_tasks");
        Directory.CreateDirectory(lowRouteDir);
        File.WriteAllText(Path.Combine(lowRouteDir, "2026.md"),
            "---\n\n## 2026-07-01 09:10\n\ntask: route-lo lone item #census\n");

        File.WriteAllText(Path.Combine(vault, "concepts", "free-energy-principle.md"),
            "---\n" +
            "slug: free-energy-principle\n" +
            "title: Free Energy Principle\n" +
            "created: 2026-04-25T10:00:00Z\n" +
            "last_distilled: 2026-04-25T10:00:00Z\n" +
            "distillation_version: 1\n" +
            "status: draft\n" +
            "tags: [cognition]\n" +
            "sources:\n" +
            "  - id: 2026-04-10-ccc3\n" +
            "    snapshot_hash: 3e646dde458ecbf40204041b2f24916a0f72690e4f43cf771dbdca699aeb360b\n" +
            "    distilled_at: 2026-04-25T10:00:00Z\n" +
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

public class V0RoutesTests : IClassFixture<DaemonFixture>
{
    private readonly DaemonFixture _f;

    public V0RoutesTests(DaemonFixture fixture) => _f = fixture;

    private async Task<JsonElement> GetJson(string path)
    {
        var raw = await _f.Client.GetStringAsync(path);
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task Capabilities_reports_kernel_http_and_vault_path()
    {
        var json = await GetJson("/api/v0/capabilities");
        Assert.Equal("kernel-http", json.GetProperty("adapter").GetString());
        Assert.Equal(_f.Vault, json.GetProperty("vaultPath").GetString());
        Assert.True(json.GetProperty("capabilities").GetProperty("hasNotation").GetBoolean());
    }

    [Fact]
    public async Task Capabilities_reports_versions()
    {
        var json = await GetJson("/api/v0/capabilities");
        var versions = json.GetProperty("versions");
        Assert.Equal("v0", versions.GetProperty("api").GetString());
        Assert.False(string.IsNullOrEmpty(versions.GetProperty("server").GetString()));
        Assert.Equal(1, versions.GetProperty("envelopeSpec").GetInt32());
    }

    [Fact]
    public async Task Capabilities_server_version_matches_the_version_flag()
    {
        // rc7.2 QA I3: capabilities said 0.1.0.0 (the four-part assembly
        // version) while --version said 0.1.0-rc7.2. Both are the display version.
        var json = await GetJson("/api/v0/capabilities");
        Assert.Equal(
            BuildInfo.DisplayVersion(typeof(V0Routes).Assembly),
            json.GetProperty("versions").GetProperty("server").GetString());
    }

    [Fact]
    public async Task Health_reports_ok_and_vault_path()
    {
        var json = await GetJson("/api/v0/health");
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.Equal(_f.Vault, json.GetProperty("vaultPath").GetString());
        Assert.True(json.GetProperty("uptimeSeconds").GetInt64() >= 0);
    }

    [Fact]
    public async Task Validation_error_is_rfc9457_problem_json()
    {
        var res = await _f.Client.PutAsync("/api/v0/constitution",
            new StringContent("{\"body\":42}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);

        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalid-request", problem.GetProperty("code").GetString());
        Assert.Equal("https://logmark.dev/errors/invalid-request", problem.GetProperty("type").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Unknown_route_is_problem_json_404()
    {
        var res = await _f.Client.GetAsync("/api/v0/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not-found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Entries_lists_planted_entries_and_filters_by_type()
    {
        var all = await GetJson("/api/v0/entries");
        Assert.True(all.GetProperty("entries").GetArrayLength() >= 3);

        var tasks = await GetJson("/api/v0/entries?type=task");
        var entries = tasks.GetProperty("entries").EnumerateArray().ToList();
        Assert.True(entries.Count >= 2);
        Assert.All(entries, e => Assert.Equal("task", e.GetProperty("type").GetString()));
        Assert.Contains(entries, e => e.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == "research"));
    }

    [Fact]
    public async Task Entry_by_id_round_trips_and_missing_id_returns_null()
    {
        // Target the seeded idea by its planted timestamp, not positionally:
        // the shared class fixture's vault is also mutated by the SSE test
        // (which appends a newer idea), so First() is test-execution-order
        // dependent. List items carry summaries only (no body), so the
        // timestamp is the deterministic key.
        var all = await GetJson("/api/v0/entries?type=idea");
        var seeded = all.GetProperty("entries").EnumerateArray()
            .First(e => e.GetProperty("createdAt").GetString() == "2026-07-12T11:00:00");
        var id = seeded.GetProperty("id").GetString()!;

        var read = await GetJson($"/api/v0/entries/{Uri.EscapeDataString(id)}");
        var entry = read.GetProperty("entry");
        Assert.Equal(id, entry.GetProperty("id").GetString());
        Assert.Contains("surprise minimization", entry.GetProperty("body").GetString());

        var missing = await GetJson("/api/v0/entries/nonexistent-id-xyz");
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("entry").ValueKind);
    }

    [Fact]
    public async Task Entries_serialize_naive_vault_timestamps_without_utc_suffix()
    {
        // Vault fixture timestamps are naive local wall-clock values parsed from
        // markdown headers (## yyyy-MM-dd HH:mm, DateTimeKind.Unspecified).
        // The Iso serializer must not falsely add the 'Z' suffix, which would
        // mislabel them as UTC and cause the cockpit to shift them by the machine's
        // UTC offset (e.g., an entry at 14:45 local would display as next day in UTC+10).
        var json = await GetJson("/api/v0/entries");
        var entries = json.GetProperty("entries").EnumerateArray().ToList();

        Assert.True(entries.Count >= 1);
        Assert.All(entries, entry =>
        {
            var createdAt = entry.GetProperty("createdAt").GetString();
            // Naive vault timestamps must not carry Z suffix; format is yyyy-MM-ddTHH:mm:ss
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", createdAt!);
        });
    }

    [Fact]
    public async Task Openapi_document_is_served_as_json_with_expected_paths()
    {
        var res = await _f.Client.GetAsync("/api/v0/openapi.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("application/json", res.Content.Headers.ContentType?.ToString());

        var body = await res.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        var paths = json.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v0/health", out _));
        Assert.True(paths.TryGetProperty("/api/v0/openapi.json", out _));

        var expectedJson = JsonDocument.Parse(ReadEmbeddedOpenApiDocumentBytes()).RootElement;
        Assert.Equal(expectedJson.GetProperty("openapi").GetString(), json.GetProperty("openapi").GetString());
        Assert.Equal(expectedJson.GetProperty("info").GetProperty("version").GetString(),
            json.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Openapi_document_matches_embedded_resource_byte_for_byte()
    {
        var res = await _f.Client.GetAsync("/api/v0/openapi.json");
        var served = await res.Content.ReadAsByteArrayAsync();
        var expected = ReadEmbeddedOpenApiDocumentBytes();

        Assert.Equal(expected, served);
    }

    private static byte[] ReadEmbeddedOpenApiDocumentBytes()
    {
        var assembly = typeof(V0Routes).Assembly;
        using var stream = assembly.GetManifestResourceStream("LogMark.Resources.openapi.daemon-v0.json");
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        stream!.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Search_finds_planted_content()
    {
        var json = await GetJson("/api/v0/search?q=surprise");
        Assert.True(json.GetProperty("results").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Summary_counts_entries_and_concepts()
    {
        var json = await GetJson("/api/v0/vault/summary");
        var summary = json.GetProperty("summary");
        Assert.True(summary.GetProperty("total_entries").GetInt32() >= 3);
        Assert.True(summary.GetProperty("total_concept_pages").GetInt32() >= 1);
        Assert.True(summary.GetProperty("by_type").GetProperty("task").GetInt32() >= 2);
    }

    [Fact]
    public async Task Summary_all_routes_carries_the_full_census_beyond_top_10()
    {
        // The planted vault has 11 distinct routes (r01..r10 at count 2, plus
        // the low-count "route-lo" at count 1). top_routes stays capped at 10
        // (compatibility), but all_routes must carry the full census and
        // route_count must reflect it, not the truncated top_routes count.
        var json = await GetJson("/api/v0/vault/summary");
        var summary = json.GetProperty("summary");

        var topRoutes = summary.GetProperty("top_routes").EnumerateArray().ToList();
        var allRoutes = summary.GetProperty("all_routes").EnumerateArray().ToList();

        Assert.True(topRoutes.Count <= 10);
        Assert.True(allRoutes.Count >= 11);
        Assert.Equal(allRoutes.Count, summary.GetProperty("route_count").GetInt32());

        var allPaths = allRoutes.Select(r => r.GetProperty("path").GetString()).ToList();
        Assert.Contains("route-lo", allPaths);

        var topPaths = topRoutes.Select(r => r.GetProperty("path").GetString()).ToList();
        Assert.DoesNotContain("route-lo", topPaths);

        // Every route surfaced in top_routes must also be present in all_routes.
        Assert.All(topPaths, p => Assert.Contains(p, allPaths));

        // Verify deterministic ordering: counts are non-increasing, and routes
        // with equal counts are ordinal-ascending by path.
        var routeList = allRoutes.Select(r => new
        {
            path = r.GetProperty("path").GetString(),
            count = r.GetProperty("count").GetInt32()
        }).ToList();

        // Adjacent pairs must have non-increasing counts.
        for (int i = 0; i < routeList.Count - 1; i++)
        {
            Assert.True(routeList[i].count >= routeList[i + 1].count,
                $"Routes not sorted by count: {routeList[i].path}({routeList[i].count}) > {routeList[i + 1].path}({routeList[i + 1].count})");
        }

        // Among the tied routes at count 2 (r01..r10), verify ordinal ascending by path.
        var tiedAtTwo = routeList.Where(r => r.count == 2).Select(r => r.path!).ToList();
        var expectedOrder = Enumerable.Range(1, 10).Select(i => $"r{i:D2}").ToList();
        Assert.Equal(expectedOrder, tiedAtTwo);

        // The sole route at count 1 (route-lo) comes last.
        Assert.Equal("route-lo", routeList.Last().path);
        Assert.Equal(1, routeList.Last().count);
    }

    [Fact]
    public async Task Constitution_round_trips_and_rejects_non_string()
    {
        var read = await GetJson("/api/v0/constitution");
        Assert.Contains("Test Constitution", read.GetProperty("body").GetString());

        var put = await _f.Client.PutAsync("/api/v0/constitution",
            new StringContent("{\"body\":\"# Test Constitution\\n\\nAmended by daemon test.\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var reread = await GetJson("/api/v0/constitution");
        Assert.Contains("Amended by daemon test.", reread.GetProperty("body").GetString());

        var bad = await _f.Client.PutAsync("/api/v0/constitution",
            new StringContent("{\"body\":42}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Settings_default_round_trip_and_validation()
    {
        var defaults = await GetJson("/api/v0/settings");
        Assert.Equal("claude-cli",
            defaults.GetProperty("settings").GetProperty("providerOrder").EnumerateArray().First().GetString());

        var put = await _f.Client.PutAsync("/api/v0/settings", new StringContent(
            "{\"settings\":{\"providerOrder\":[\"ollama\",\"claude-cli\",\"lm-studio\"]," +
            "\"routing\":{\"summarization\":\"disabled\",\"distillation\":\"default\",\"searchEnrichment\":\"default\"}}}",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var reread = await GetJson("/api/v0/settings");
        Assert.Equal("ollama",
            reread.GetProperty("settings").GetProperty("providerOrder").EnumerateArray().First().GetString());

        var bad = await _f.Client.PutAsync("/api/v0/settings",
            new StringContent("{\"settings\":{\"providerOrder\":[\"nope\"],\"routing\":{}}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // Bead lmk-vbhl: a body whose JSON types do not match the DTO (or that is
    // not JSON at all) threw JsonException out of ReadBody and surfaced as 500.
    [Theory]
    [InlineData("/api/v0/settings", "{\"settings\":\"nope\"}")]
    [InlineData("/api/v0/settings", "{\"settings\":{\"providerOrder\":42}}")]
    [InlineData("/api/v0/settings", "not json")]
    [InlineData("/api/v0/concepts/bad-shape", "{\"page\":\"nope\"}")]
    [InlineData("/api/v0/constitution", "{\"body\":")]
    public async Task Put_with_a_mistyped_or_malformed_body_is_400_problem_json(string path, string body)
    {
        var res = await _f.Client.PutAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Concepts_list_read_and_write_round_trip()
    {
        var list = await GetJson("/api/v0/concepts");
        var slugs = list.GetProperty("pages").EnumerateArray()
            .Select(p => p.GetProperty("slug").GetString()).ToList();
        Assert.Contains("free-energy-principle", slugs);

        var read = await GetJson("/api/v0/concepts/free-energy-principle");
        var page = read.GetProperty("page");
        Assert.Equal("Free Energy Principle", page.GetProperty("title").GetString());
        Assert.Equal(1, page.GetProperty("sources").GetArrayLength());
        Assert.Matches("^[0-9a-f]{64}$",
            page.GetProperty("sources")[0].GetProperty("snapshot_hash").GetString());

        var missing = await GetJson("/api/v0/concepts/does-not-exist");
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("page").ValueKind);

        var newPage = JsonSerializer.Serialize(new
        {
            page = new
            {
                slug = "daemon-roundtrip",
                title = "Daemon Roundtrip",
                body = "# Daemon Roundtrip\n\nBody.",
                status = "draft",
                tags = new[] { "c" },
                sources = Array.Empty<object>(),
                related_concepts = Array.Empty<string>(),
                created = "2026-07-13T10:00:00Z",
                last_distilled = "2026-07-13T10:00:00Z",
                distillation_version = 1,
            },
        });
        var put = await _f.Client.PutAsync("/api/v0/concepts/daemon-roundtrip",
            new StringContent(newPage, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var back = await GetJson("/api/v0/concepts/daemon-roundtrip");
        Assert.Equal("Daemon Roundtrip", back.GetProperty("page").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Sse_emits_vault_event_when_an_entry_file_changes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = await _f.Client.GetStreamAsync("/api/v0/events", cts.Token);
        using var reader = new StreamReader(stream);

        // Give the subscription a beat, then mutate the vault.
        await Task.Delay(200, cts.Token);
        var file = Path.Combine(_f.Vault, "logmark", "_ideas", "2026.md");
        File.AppendAllText(file, "---\n\n## 2026-07-13 12:00\n\nidea: sse live test\n");

        // The fixture's SSE stream is shared with other tests in this class
        // (IClassFixture), so a debounced flush from an unrelated prior test
        // (a constitution PUT, a concepts write) can interleave ahead of the
        // event this test provoked. Scan every event/data pair for the
        // entries-changed kind rather than asserting on the first one seen.
        string? eventLine = null;
        var found = false;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.StartsWith("event:")) eventLine = line;
            else if (line.StartsWith("data:") && line.Contains("entries-changed"))
            {
                Assert.Equal("event: vault-event", eventLine);
                found = true;
                break;
            }
        }

        Assert.True(found, "no entries-changed vault-event arrived before the 10s deadline");
    }

    [Fact]
    public async Task Sse_emits_concepts_changed_when_a_file_is_added_under_canonical_concepts_dir()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = await _f.Client.GetStreamAsync("/api/v0/events", cts.Token);
        using var reader = new StreamReader(stream);

        // Give the subscription a beat, then add a file under the canonical
        // _concepts/ directory (VaultChangeWatcher must classify this the
        // same as the legacy concepts/ directory - release review follow-up).
        await Task.Delay(200, cts.Token);
        var dir = Path.Combine(_f.Vault, "logmark", "_concepts");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "new-concept.md"),
            "---\nslug: new-concept\ntitle: New Concept\n---\n\nBody.\n");

        // DaemonFixture's SSE stream is shared with other tests in this class
        // (IClassFixture), so a debounced flush from an unrelated prior test
        // (e.g. a constitution PUT) can interleave. Scan every "data:" line
        // for the kind we care about rather than assuming it is the first.
        var found = false;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.StartsWith("data:") && line.Contains("concepts-changed"))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "expected a concepts-changed vault-event on the SSE stream");
    }
}

/// <summary>
/// P0 fix (constitution-contract review): a vault that only has vault.md
/// (no legacy constitution.md) must still resolve through the
/// /api/v0/constitution route. Separate fixture from DaemonFixture because
/// that one plants the legacy constitution.md filename.
/// </summary>
public sealed class VaultMdConstitutionFixture : IDisposable
{
    public string Vault { get; }
    public HttpClient Client { get; }

    private readonly DaemonHttpServer _server;
    private readonly SseChannel _sse;
    private readonly VaultChangeWatcher _watcher;

    public const string ExpectedBody = "# Canonical Constitution\n\nOnly vault.md exists in this fixture.\n";

    public VaultMdConstitutionFixture()
    {
        Vault = Path.Combine(Path.GetTempPath(), "lm-daemon-vaultmd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(Vault, "logmark"));
        File.WriteAllText(Path.Combine(Vault, "logmark", "vault.md"), ExpectedBody);

        var services = VaultHostBootstrap.Build(Vault);
        services.EntryCache.WarmupTask?.Wait();

        _sse = new SseChannel();
        _watcher = new VaultChangeWatcher(Vault, _sse.Broadcast, debounceMs: 100);
        var routes = new V0Routes(services, new ConceptPageStore(Vault), new SettingsStore(Vault), _sse);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        _server = new DaemonHttpServer(port, routes);
        _server.Start();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
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

public class V0RoutesVaultMdConstitutionTests : IClassFixture<VaultMdConstitutionFixture>
{
    private readonly VaultMdConstitutionFixture _f;

    public V0RoutesVaultMdConstitutionTests(VaultMdConstitutionFixture fixture) => _f = fixture;

    [Fact]
    public async Task Constitution_route_reads_vault_md_when_no_legacy_file_exists()
    {
        var raw = await _f.Client.GetStringAsync("/api/v0/constitution");
        var json = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(VaultMdConstitutionFixture.ExpectedBody, json.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Sse_emits_constitution_changed_when_vault_md_changes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var stream = await _f.Client.GetStreamAsync("/api/v0/events", cts.Token);
        using var reader = new StreamReader(stream);

        // Give the subscription a beat, then mutate vault.md.
        await Task.Delay(200, cts.Token);
        var file = Path.Combine(_f.Vault, "logmark", "vault.md");
        File.AppendAllText(file, "\nUpdated constitution content for SSE test.\n");

        string? eventLine = null;
        string? dataLine = null;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.StartsWith("event:")) eventLine = line;
            else if (line.StartsWith("data:") && eventLine is not null)
            {
                dataLine = line;
                break;
            }
        }

        Assert.Equal("event: vault-event", eventLine);
        Assert.Contains("constitution-changed", dataLine);
    }
}
