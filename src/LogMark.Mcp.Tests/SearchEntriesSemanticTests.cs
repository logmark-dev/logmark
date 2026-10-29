using FluentAssertions;
using LogMark.Interfaces;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Tests for the semantic parameter on the search tool (renamed from search_entries).
/// </summary>
public class SearchEntriesSemanticTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly VaultServices _services;

    public SearchEntriesSemanticTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-semantic-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create minimal vault structure
        var logmarkDir = Path.Combine(_testVaultPath, "logmark", "_system");
        Directory.CreateDirectory(logmarkDir);
        var diaryDir = Path.Combine(_testVaultPath, "logmark", "_diary");
        Directory.CreateDirectory(diaryDir);
        File.WriteAllText(Path.Combine(diaryDir, "2026.md"),
            "## 2026-01-15 10:00\n\nDeployed auth service to production\n");

        var entryCache = new EntryCache(_testVaultPath);
        entryCache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!entryCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);

        _services = new VaultServices
        {
            VaultPath = _testVaultPath,
            SearchService = new SearchService(entryCache),
            EntryResolver = new EntryResolver(entryCache, _testVaultPath),
            EntryCache = entryCache
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testVaultPath))
                Directory.Delete(_testVaultPath, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void SearchTool_IsNamedSearch_AndRequiresQuery()
    {
        var def = new SearchTool(new VaultServices()).Definition;
        def.Name.Should().Be("search");
        def.InputSchema.Required.Should().Contain("query");
    }

    [Fact]
    public async Task SearchEntries_WithSemanticFalse_DoesNotIncludeSemanticResults()
    {
        var tool = new SearchTool(_services);

        var args = new Dictionary<string, object?>
        {
            ["query"] = ToJsonElement("auth service"),
            ["semantic"] = ToJsonElement(false)
        };
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.Content.First().Text!;
        text.Should().NotContain("SemanticMatches");
        text.Should().NotContain("SemanticNote");
    }

    [Fact]
    public async Task SearchEntries_WithSemanticTrue_NoService_ReturnsNote()
    {
        // SemanticSearchService is null on _services
        var tool = new SearchTool(_services);

        var args = new Dictionary<string, object?>
        {
            ["query"] = ToJsonElement("auth service"),
            ["semantic"] = ToJsonElement(true)
        };
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.Content.First().Text!;
        text.Should().Contain("SemanticNote");
        text.Should().Contain("unavailable");
    }

    [Fact]
    public async Task SearchEntries_WithSemanticTrue_WithService_ReturnsSemanticMatches()
    {
        var fakeSearch = new FakeSemanticSearchService(new List<SemanticSearchResult>
        {
            new("entry-999", "Authentication flow redesign", "backend", 0.85f)
        });
        _services.SemanticSearchService = fakeSearch;
        var tool = new SearchTool(_services);

        var args = new Dictionary<string, object?>
        {
            ["query"] = ToJsonElement("auth service"),
            ["semantic"] = ToJsonElement(true)
        };
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.Content.First().Text!;
        text.Should().Contain("SemanticMatches");
        text.Should().Contain("entry-999");
        text.Should().Contain("0.85");
    }

    [Fact]
    public async Task SearchEntries_WithoutSemanticParam_DefaultsToTextOnly()
    {
        var tool = new SearchTool(_services);

        var args = new Dictionary<string, object?>
        {
            ["query"] = ToJsonElement("auth service")
        };
        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.Content.First().Text!;
        text.Should().NotContain("SemanticMatches");
        text.Should().NotContain("SemanticNote");
    }

    private static object ToJsonElement(object value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }

    private class FakeSemanticSearchService : ISemanticSearchService
    {
        private readonly IReadOnlyList<SemanticSearchResult> _results;

        public FakeSemanticSearchService(IReadOnlyList<SemanticSearchResult> results)
        {
            _results = results;
        }

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(string query, int topK = 10, CancellationToken ct = default)
            => Task.FromResult(_results);

        public Task<IReadOnlyList<string>> SuggestRoutesAsync(string content, int topK = 3, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string?> CorrectTypoAsync(string input, IReadOnlyList<string> knownEntities, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<LogMark.Models.EntryType?> ClassifyEntryTypeAsync(string content, CancellationToken ct = default)
            => Task.FromResult<LogMark.Models.EntryType?>(null);
    }
}
