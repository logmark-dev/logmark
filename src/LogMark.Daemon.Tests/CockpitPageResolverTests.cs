using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// CandidatePagePaths unit tests: direct port of
/// LogMark.Knowledge/tests/unit/lib/page-path.test.ts, verifying the
/// candidate-generation semantics without any HTTP or file-system
/// involvement.
/// </summary>
public class CockpitPageResolverCandidatePathsTests
{
    [Fact]
    public void Appends_md_and_tries_the_underscore_folder_semantics_variant()
    {
        var result = CockpitPageResolver.CandidatePagePaths("projects/LogMark/blocks/pages/2026-01-18-x");
        Assert.Equal(new[]
        {
            "projects/LogMark/blocks/pages/2026-01-18-x.md",
            "projects/LogMark/_blocks/pages/2026-01-18-x.md",
        }, result);
    }

    [Fact]
    public void Keeps_an_explicit_md_and_non_pages_paths_untouched()
    {
        var result = CockpitPageResolver.CandidatePagePaths("notes/idea.md");
        Assert.Equal(new[] { "notes/idea.md" }, result);
    }

    [Fact]
    public void Rejects_traversal()
    {
        var result = CockpitPageResolver.CandidatePagePaths("../secrets");
        Assert.Empty(result);
    }

    [Fact]
    public void Rejects_backslash_separated_traversal()
    {
        var result = CockpitPageResolver.CandidatePagePaths("..\\secrets");
        Assert.Empty(result);
    }

    [Fact]
    public void Backslash_separated_paths_get_the_same_underscore_variant_as_forward_slash_ones()
    {
        var result = CockpitPageResolver.CandidatePagePaths("projects\\LogMark\\blocks\\pages\\2026-01-18-x");
        Assert.Equal(new[]
        {
            "projects/LogMark/blocks/pages/2026-01-18-x.md",
            "projects/LogMark/_blocks/pages/2026-01-18-x.md",
        }, result);
    }

    [Fact]
    public void Absolute_paths_are_not_rejected_at_string_level_route_level_guard_is_load_bearing()
    {
        var result = CockpitPageResolver.CandidatePagePaths("C:/x");
        Assert.NotEmpty(result);
        Assert.Equal(new[] { "C:/x.md" }, result);
    }

    [Fact]
    public void Empty_input_yields_no_candidates()
    {
        Assert.Empty(CockpitPageResolver.CandidatePagePaths(""));
        Assert.Empty(CockpitPageResolver.CandidatePagePaths(null));
        Assert.Empty(CockpitPageResolver.CandidatePagePaths("   "));
    }
}

/// <summary>
/// Fixture for the GET /api/pages route (lmk-nhjr Task 2): the same
/// v0 + compat dispatch chain as CockpitCompatFixture, with a vault planted
/// to exercise pages.ts's route-level tests
/// (LogMark.Knowledge/tests/integration/routes-pages.test.ts).
/// </summary>
public sealed class CockpitPageResolverFixture : IDisposable
{
    public string Vault { get; }
    public HttpClient Client { get; }

    private readonly DaemonHttpServer _server;
    private readonly SseChannel _sse;
    private readonly VaultChangeWatcher _watcher;

    public CockpitPageResolverFixture()
    {
        Vault = Path.Combine(Path.GetTempPath(), "lm-daemon-pages-" + Guid.NewGuid().ToString("N")[..8]);
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
        // Vault content root resolves to <vault>/logmark (VaultContentRoot),
        // so page paths in the tests below are relative to that directory.
        var direct = Path.Combine(vault, "logmark", "projects", "foo");
        Directory.CreateDirectory(direct);
        File.WriteAllText(Path.Combine(direct, "2026-04-12-ggg7.md"), "# Direct\n\nDirect file body.");

        var blocksPages = Path.Combine(vault, "logmark", "projects", "foo", "_blocks", "pages");
        Directory.CreateDirectory(blocksPages);
        File.WriteAllText(Path.Combine(blocksPages, "2026-01-18-x.md"), "# Subpage\n\nHello from subpage.");
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

/// <summary>
/// Route-level tests: direct port of
/// LogMark.Knowledge/tests/integration/routes-pages.test.ts.
/// </summary>
public class CockpitPageResolverRouteTests : IClassFixture<CockpitPageResolverFixture>
{
    private readonly CockpitPageResolverFixture _f;

    public CockpitPageResolverRouteTests(CockpitPageResolverFixture fixture) => _f = fixture;

    [Fact]
    public async Task Resolves_an_existing_file_directly()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=" + Uri.EscapeDataString("projects/foo/2026-04-12-ggg7"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("Direct file body", json.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Tries_the_underscore_folder_semantics_variant()
    {
        var res = await _f.Client.GetAsync(
            "/api/pages?path=" + Uri.EscapeDataString("projects/foo/blocks/pages/2026-01-18-x"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("projects/foo/_blocks/pages/2026-01-18-x.md", json.GetProperty("path").GetString());
        Assert.Contains("Hello from subpage", json.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Returns_404_with_not_found_error_for_missing_pages()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("not_found", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Returns_404_for_absolute_windows_paths_security_guard()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=" + Uri.EscapeDataString("C:/Windows/system.ini"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.TryGetProperty("body", out _));
        Assert.False(json.TryGetProperty("path", out _));
    }

    [Fact]
    public async Task Normalizes_leading_slash_and_keeps_relative_inside_vault_root()
    {
        // Input /etc/passwd is stripped to etc/passwd (relative), resolved
        // inside rootDir, 404s via not-found rather than the prefix guard -
        // proving the leading-slash normalization runs before candidate
        // resolution.
        var res = await _f.Client.GetAsync("/api/pages?path=" + Uri.EscapeDataString("/etc/passwd"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Rejects_windows_root_relative_paths_prefix_guard()
    {
        // Input \Windows\system.ini is NOT stripped by the leading-slash
        // trim (only forward slashes are stripped). The resolved absolute
        // path discards the vault root and fails the prefix guard, so this
        // 404s the same way the not-found case does.
        var res = await _f.Client.GetAsync("/api/pages?path=" + Uri.EscapeDataString("\\Windows\\system.ini"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Rejects_traversal_with_a_problem_response()
    {
        // Divergence from the TS route (see CockpitPageResolver's class doc):
        // the TS route answers this case with a plain 400 JSON body that the
        // cockpit's fetch wrapper never parses (any non-2xx, non-404 status
        // throws before the body is read). This port raises the daemon's
        // standard RFC 9457 problem response instead.
        var res = await _f.Client.GetAsync("/api/pages?path=" + Uri.EscapeDataString("../secrets"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalid-path", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_path_query_param_is_treated_as_empty_and_rejected()
    {
        var res = await _f.Client.GetAsync("/api/pages");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }
}
