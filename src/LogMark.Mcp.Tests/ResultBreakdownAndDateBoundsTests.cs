using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// List tools carry a Breakdown when the result is truncated, so an agent can
/// narrow instead of paging; search/get_tasks/get_decisions take after/before,
/// with before inclusive because entries share minute-precision timestamps.
/// Design: docs/plans/2026-09-28-route-scope-resolution-design.md, increment 3.
/// </summary>
public class ResultBreakdownAndDateBoundsTests : IDisposable
{
    private readonly string _vaultPath =
        Path.Combine(Path.GetTempPath(), $"logmark-mcp-breakdown-{Guid.NewGuid()}");
    private readonly VaultServices _services;

    public ResultBreakdownAndDateBoundsTests()
    {
        Write("logmark/projects/alpha/_diary/2026.md", """
            ## 2026-09-10 09:00

            note one

            ## 2026-09-11 09:00

            note two

            ## 2026-09-12 09:00

            note three
            """);
        // Same minute as "note three": an exclusive before would lose one.
        Write("logmark/projects/alpha/research/_ideas/2026.md", """
            ## 2026-09-12 09:00

            note idea
            """);
        Write("logmark/projects/beta/_tasks/2026.md", """
            ## 2026-09-10 10:00

            note task early
            _task: 2026-10-01, 12:00 | postponed: 0 | aaaa0001

            ## 2026-09-12 10:00

            note task late
            _task: 2026-10-02, 12:00 | postponed: 0 | aaaa0002
            """);
        Write("logmark/projects/beta/_decisions/2026.md", """
            ## 2026-09-10 11:00

            note decision early

            ## 2026-09-12 11:00

            note decision late
            """);
        Write("logmark/projects/beta/_blocks/2026.md", """
            ## 2026-09-10 12:00

            note block one

            ## 2026-09-11 12:00

            note block two
            """);

        var cache = new EntryCache(_vaultPath);
        cache.StartWarmup();
        cache.WarmupTask!.GetAwaiter().GetResult();

        _services = new VaultServices
        {
            VaultPath = _vaultPath,
            EntryCache = cache,
            SearchService = new SearchService(cache),
            EntryResolver = new EntryResolver(cache, _vaultPath),
        };
    }

    // --- Breakdown ---

    [Fact]
    public async Task Search_Truncated_CarriesBreakdownOverAllMatches()
    {
        var json = await Run(new SearchTool(_services), new { query = "note", limit = 2 });

        json.GetProperty("TotalCount").GetInt32().Should().Be(10);
        var b = json.GetProperty("Breakdown");
        b.GetProperty("Routes").GetProperty("alpha").GetInt32().Should().Be(4);
        b.GetProperty("Routes").GetProperty("beta").GetInt32().Should().Be(6);
        b.GetProperty("Scopes").GetProperty("alpha/research").GetInt32().Should().Be(1);
        b.GetProperty("Types").GetProperty("log").GetInt32().Should().Be(3);
        b.GetProperty("Types").GetProperty("task").GetInt32().Should().Be(2);
        b.GetProperty("Oldest").GetDateTime().Should().Be(new DateTime(2026, 9, 10, 9, 0, 0));
        b.GetProperty("Newest").GetDateTime().Should().Be(new DateTime(2026, 9, 12, 11, 0, 0));
    }

    [Fact]
    public async Task Search_NotTruncated_HasNoBreakdown()
    {
        var json = await Run(new SearchTool(_services), new { query = "note", limit = 50 });

        json.TryGetProperty("Breakdown", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Search_NoScopedMatches_OmitsScopes()
    {
        var json = await Run(new SearchTool(_services), new { query = "note", route = "beta", limit = 1 });

        var b = json.GetProperty("Breakdown");
        b.TryGetProperty("Scopes", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ListRecent_Truncated_CarriesBreakdown()
    {
        var json = await Run(new ListRecentTool(_services), new { days = 100000, limit = 1 });

        json.GetProperty("Breakdown").GetProperty("Routes").GetProperty("alpha").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task GetTasks_Truncated_CarriesBreakdown()
    {
        var json = await Run(new GetTasksTool(_services), new { limit = 1 });

        json.GetProperty("Breakdown").GetProperty("Types").GetProperty("task").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetDecisions_Truncated_CarriesBreakdown()
    {
        var json = await Run(new GetDecisionsTool(_services), new { route = "beta", limit = 1 });

        json.GetProperty("Breakdown").GetProperty("Types").GetProperty("decision").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetBlocks_Truncated_CarriesBreakdown()
    {
        var json = await Run(new GetBlocksTool(_services), new { limit = 1 });

        json.GetProperty("Breakdown").GetProperty("Types").GetProperty("block").GetInt32().Should().Be(2);
    }

    // --- Date bounds ---

    [Fact]
    public async Task Search_BeforeDatetime_IsInclusiveOfThatMinute()
    {
        var json = await Run(new SearchTool(_services),
            new { query = "note", route = "alpha", before = "2026-09-11T09:00" });

        // "note two" is AT 09:00 on the 11th and must survive the bound.
        Contents(json).Should().BeEquivalentTo("note one", "note two");
    }

    [Fact]
    public async Task Search_BeforeDate_IncludesTheWholeDay()
    {
        var json = await Run(new SearchTool(_services),
            new { query = "note", route = "alpha", before = "2026-09-11" });

        Contents(json).Should().BeEquivalentTo("note one", "note two");
    }

    [Fact]
    public async Task Search_After_IsInclusive()
    {
        var json = await Run(new SearchTool(_services),
            new { query = "note", route = "alpha", after = "2026-09-12" });

        Contents(json).Should().BeEquivalentTo("note three", "note idea");
    }

    [Fact]
    public async Task Search_InvalidDate_IsAnError()
    {
        var result = await new SearchTool(_services).ExecuteAsync(
            Args(new { query = "note", before = "last tuesday" }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("before");
    }

    [Fact]
    public async Task GetTasks_After_Filters()
    {
        var json = await Run(new GetTasksTool(_services), new { after = "2026-09-11" });

        json.GetProperty("Tasks").EnumerateArray().Select(e => e.GetProperty("Content").GetString())
            .Should().ContainSingle().Which.Should().Contain("note task late");
    }

    [Fact]
    public async Task GetDecisions_Before_Filters()
    {
        var json = await Run(new GetDecisionsTool(_services), new { route = "beta", before = "2026-09-11" });

        json.GetProperty("Decisions").EnumerateArray().Select(e => e.GetProperty("Content").GetString())
            .Should().ContainSingle().Which.Should().Contain("note decision early");
    }

    [Theory]
    [InlineData("search")]
    [InlineData("get_tasks")]
    [InlineData("get_decisions")]
    public void Definition_ExposesAfterAndBefore(string tool)
    {
        ITool t = tool switch
        {
            "search" => new SearchTool(_services),
            "get_tasks" => new GetTasksTool(_services),
            _ => new GetDecisionsTool(_services),
        };

        t.Definition.InputSchema.Properties.Should().ContainKeys("after", "before");
        t.Definition.InputSchema.Properties["before"].Description.Should().ContainEquivalentOf("inclusive");
    }

    // --- helpers ---

    private static IEnumerable<string?> Contents(JsonElement json) =>
        json.GetProperty("Entries").EnumerateArray().Select(e => e.GetProperty("Content").GetString());

    private static async Task<JsonElement> Run(ITool tool, object args)
    {
        var result = await tool.ExecuteAsync(Args(args), CancellationToken.None);
        result.IsError.Should().BeFalse(result.Content[0].Text);
        return JsonDocument.Parse(result.Content[0].Text!).RootElement;
    }

    private static Dictionary<string, object?> Args(object obj)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
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
