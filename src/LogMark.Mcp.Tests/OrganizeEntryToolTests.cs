using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using LogMark.Services.Verbs;
using Xunit;

namespace LogMark.Mcp.Tests;

public class OrganizeEntryToolTests : IDisposable
{
    private readonly string _vaultPath;
    private readonly OrganizeEntryTool _tool;
    private readonly EntryCache _cache;

    public OrganizeEntryToolTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"logmark-organize-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_vaultPath, PathConstants.GetSystemFolder()));

        File.WriteAllText(
            Path.Combine(_vaultPath, "logmark", "_system", "routes.md"),
            "## Projects\n\n### alpha\n- path: alpha\n\n### beta\n- path: beta\n");

        var entryFile = Path.Combine(_vaultPath, "logmark", "alpha", "_ideas", "2026.md");
        Directory.CreateDirectory(Path.GetDirectoryName(entryFile)!);
        File.WriteAllText(entryFile,
            "## 2026-07-02 14:30\n\ni: capture note #draft\n_id: abc12345 ^abc12345\n");

        var cache = new EntryCache(_vaultPath);
        cache.StartWarmup();
        _cache = cache;
        var writer = new VaultWriter(cache);

        var services = new VaultServices
        {
            VaultPath = _vaultPath,
            Logger = new SystemLogger(_vaultPath),
            VaultWriter = writer,
            EntryCache = cache,
        };

        var registry = new VerbRegistry();
        var organizer = new EntryOrganizer(_vaultPath, writer);
        var routeService = new RouteService(_vaultPath, new SystemLogger(_vaultPath));
        BuiltinVerbs.RegisterReroute(registry, cache, organizer, routeService);
        BuiltinVerbs.RegisterConvertType(registry, cache, organizer);
        BuiltinVerbs.RegisterAmend(registry, cache, organizer);
        BuiltinVerbs.RegisterLinkEntryToPage(registry, cache, organizer);
        BuiltinVerbs.RegisterRetag(registry, cache, organizer);

        _tool = new OrganizeEntryTool(services, registry);
    }

    public void Dispose()
    {
        // Let the background warmup finish scanning before deleting the vault -
        // tests that short-circuit before awaiting WarmupTask (e.g. an invalid
        // action) otherwise race the reader and Directory.Delete throws IOException.
        try { _cache.WarmupTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
        try
        {
            if (Directory.Exists(_vaultPath))
                Directory.Delete(_vaultPath, true);
        }
        catch (IOException) { /* best-effort temp cleanup */ }
    }

    private static Dictionary<string, object?> Args(object shape)
    {
        var json = JsonSerializer.Serialize(shape);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        return dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }

    private string FindEntryFile() =>
        Directory.EnumerateFiles(Path.Combine(_vaultPath, "logmark"), "*.md", SearchOption.AllDirectories)
            .First(f => File.ReadAllText(f).Contains("^abc12345"));

    [Fact]
    public async Task Retag_AddsTag()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "retag", entry_id = "abc12345", add = "reviewed" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        File.ReadAllText(FindEntryFile()).Should().Contain("#reviewed");
    }

    [Fact]
    public async Task Retag_RemovesTag()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "retag", entry_id = "abc12345", remove = "draft" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        File.ReadAllText(FindEntryFile()).Should().NotContain("#draft");
    }

    [Fact]
    public async Task Reroute_MovesEntryToTargetRoute()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "reroute", entry_id = "abc12345", target_route = "beta" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        FindEntryFile().Should().Contain("beta");
    }

    [Fact]
    public async Task Convert_MovesEntryToTypeFolder()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "convert", entry_id = "abc12345", new_type = "task" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        FindEntryFile().Should().Contain("_tasks");
    }

    [Fact]
    public async Task Amend_AppendsTrail()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "amend", entry_id = "abc12345", amend_text = "looks good" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        File.ReadAllText(FindEntryFile()).Should().Contain("looks good");
    }

    [Fact]
    public async Task Link_InsertsWikiLink()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "link", entry_id = "abc12345", wiki_path = "alpha/pages/overview" }), CancellationToken.None);
        r.IsError.Should().BeFalse(r.Content.FirstOrDefault()?.Text ?? "no content");
        File.ReadAllText(FindEntryFile()).Should().Contain("[[alpha/pages/overview");
    }

    [Fact]
    public async Task InvalidAction_ReturnsError()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "frobnicate", entry_id = "abc12345" }), CancellationToken.None);
        r.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownEntry_ReturnsError()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "retag", entry_id = "deadbeef", add = "x" }), CancellationToken.None);
        r.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Reroute_MissingTargetRoute_SurfacesVerbFailure()
    {
        var r = await _tool.ExecuteAsync(Args(new { action = "reroute", entry_id = "abc12345" }), CancellationToken.None);
        r.IsError.Should().BeTrue();
    }
}
