using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Route and scope as the MCP tools report and filter them, on the vault
/// shapes that broke: a route named differently from its folder (quant ->
/// projects/quant-trading-platform) and a route nested in another route's
/// folder (fin-inv -> domains/finance/investing inside fin -> domains/finance).
/// Design: docs/plans/2026-09-28-route-scope-resolution-design.md.
/// </summary>
public class RouteResolutionEndToEndTests : IDisposable
{
    private readonly string _vaultPath =
        Path.Combine(Path.GetTempPath(), $"logmark-mcp-routes-{Guid.NewGuid()}");
    private readonly VaultServices _services;
    private readonly EntryCache _cache;

    public RouteResolutionEndToEndTests()
    {
        Write("logmark/_system/routes.md", """
            # Routes

            ## Domains

            ### fin
            - path: domains/finance
            - scopes:
              - tax-au: tax

            ### fin-inv
            - path: domains/finance/investing

            ## Projects

            ### quant
            - path: projects/quant-trading-platform
            - scopes:
              - research: research
            """);
        Write("logmark/domains/finance/_decisions/2026.md", """
            ## 2026-09-01 09:00

            fin root decision #frozen
            _id: aaaa0001 ^aaaa0001

            ## 2026-09-01 09:30

            fin second decision #frozen
            _id: aaaa0006 ^aaaa0006
            """);
        Write("logmark/domains/finance/tax-au/_ideas/2026.md", """
            ## 2026-09-02 09:00

            fin tax idea
            _id: aaaa0002 ^aaaa0002
            """);
        Write("logmark/domains/finance/investing/_decisions/2026.md", """
            ## 2026-09-03 09:00

            investing decision #frozen
            _id: aaaa0003 ^aaaa0003
            """);
        Write("logmark/domains/finance/investing/_tasks/2026.md", """
            ## 2026-09-03 10:00

            investing task
            _task: 2026-10-01, 12:00 | postponed: 0 | aaaa0004
            """);
        Write("logmark/projects/quant-trading-platform/research/_ideas/2026.md", """
            ## 2026-09-04 09:00

            quant research idea
            _id: aaaa0005 ^aaaa0005
            """);

        _cache = new EntryCache(_vaultPath);
        _cache.StartWarmup();
        _cache.WarmupTask!.GetAwaiter().GetResult();

        _services = new VaultServices
        {
            VaultPath = _vaultPath,
            EntryCache = _cache,
            SearchService = new SearchService(_cache),
            EntryResolver = new EntryResolver(_cache, _vaultPath),
        };
    }

    [Fact]
    public async Task Search_ScopeUnderRouteNamedDifferentlyFromFolder()
    {
        var json = await Run(new SearchTool(_services), new { query = "quant", route = "quant", scope = "research" });

        var e = json.GetProperty("Entries").EnumerateArray().Should().ContainSingle().Subject;
        e.GetProperty("Route").GetString().Should().Be("quant");
        e.GetProperty("Scope").GetString().Should().Be("research");
    }

    [Fact]
    public async Task Search_NestedRoute_ReportsItsOwnRoute()
    {
        var json = await Run(new SearchTool(_services), new { query = "investing", route = "fin-inv" });

        json.GetProperty("TotalCount").GetInt32().Should().Be(2);
        json.GetProperty("Entries").EnumerateArray()
            .Select(e => e.GetProperty("Route").GetString()).Should().AllBe("fin-inv");
    }

    [Fact]
    public async Task Search_ParentRoute_IncludesChildRoute()
    {
        var json = await Run(new SearchTool(_services), new { query = "i", route = "fin", limit = 50 });

        var routes = json.GetProperty("Entries").EnumerateArray()
            .Select(e => e.GetProperty("Route").GetString()).ToList();
        routes.Should().Contain("fin").And.Contain("fin-inv");
    }

    [Fact]
    public async Task Search_ParentRouteWithScope_DoesNotReachIntoChild()
    {
        var json = await Run(new SearchTool(_services), new { query = "i", route = "fin", scope = "tax-au" });

        json.GetProperty("Entries").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Id").GetString().Should().Be("aaaa0002");
    }

    [Fact]
    public async Task ReadEntry_ReportsSameRouteAsSearch()
    {
        var json = await Run(new ReadEntryTool(_services), new { entry_id = "aaaa0003" });

        json.GetProperty("Entry").GetProperty("Route").GetString().Should().Be("fin-inv");
    }

    [Fact]
    public async Task GetTasks_ParentRoute_IncludesChildTask()
    {
        var json = await Run(new GetTasksTool(_services), new { route = "fin" });

        json.GetProperty("Tasks").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Route").GetString().Should().Be("fin-inv");
    }

    [Fact]
    public async Task ProjectContext_ListsChildRoutes_AndKeepsDecisionsOwn()
    {
        var json = await Run(new GetProjectContextTool(_services), new { route = "fin" });

        var child = json.GetProperty("ChildRoutes").EnumerateArray().Should().ContainSingle().Subject;
        child.GetProperty("Route").GetString().Should().Be("fin-inv");
        child.GetProperty("OpenTasks").GetInt32().Should().Be(1);

        var decisionIds = json.GetProperty("Scopes").EnumerateArray()
            .SelectMany(s => s.GetProperty("Decisions").EnumerateArray())
            .Select(d => d.GetProperty("Id").GetString()).ToList();
        decisionIds.Should().BeEquivalentTo("aaaa0001", "aaaa0006");

        // Tasks are parent-inclusive; the row names its route.
        json.GetProperty("Summary").GetProperty("ActiveTasks").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task RoutesMdChange_IsPickedUpWithoutRestart()
    {
        Write("logmark/_system/routes.md", """
            # Routes

            ### money
            - path: domains/finance
            """);
        File.SetLastWriteTimeUtc(
            Path.Combine(_vaultPath, "logmark", "_system", "routes.md"), DateTime.UtcNow.AddMinutes(1));

        // The recheck is debounced; wait it out rather than reach into internals.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? route = null;
        while (DateTime.UtcNow < deadline)
        {
            route = _cache.GetRouteForPath(Path.Combine(_vaultPath, "logmark", "domains", "finance", "_decisions", "2026.md"));
            if (route == "money") break;
            await Task.Delay(200);
        }

        route.Should().Be("money");
    }

    private static async Task<JsonElement> Run(ITool tool, object args)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(args));
        var dict = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        var result = await tool.ExecuteAsync(dict, CancellationToken.None);
        result.IsError.Should().BeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement;
    }

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_vaultPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_vaultPath, recursive: true); } catch { }
    }
}
