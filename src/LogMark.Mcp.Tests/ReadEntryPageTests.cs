using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// read_entry as the agent-side "click": page fallback for zero-entry files,
/// extensionless wikilink paths, #shortid anchors, and the vault containment
/// boundary. Viewer parity per docs/plans/2026-07-19-mcp-link-light-objects-design.md.
/// </summary>
public class ReadEntryPageTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly ReadEntryTool _tool;

    public ReadEntryPageTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-page-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        var ideasDir = Path.Combine(_testVaultPath, "logmark", "_ideas");
        Directory.CreateDirectory(Path.Combine(ideasDir, "pages"));
        File.WriteAllText(Path.Combine(ideasDir, "2026.md"), """
            ## 2026-07-19 17:21
            idea: paste observation [[logmark/_ideas/pages/2026-07-19-pasted|pasted content]]
            _id: 5fe7f4d2 ^5fe7f4d2
            """);
        File.WriteAllText(Path.Combine(ideasDir, "pages", "2026-07-19-pasted.md"),
            "full pasted transcript\n\nsee also [[logmark/_ideas/2026#5fe7f4d2|the entry]] and [[logmark/_ideas/missing-note]]");

        var entryCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        entryCache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!entryCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);

        _tool = new ReadEntryTool(new VaultServices
        {
            VaultPath = _testVaultPath,
            EntryResolver = new EntryResolver(entryCache, _testVaultPath)
        });
    }

    [Fact]
    public async Task ReadEntry_PageFile_ReturnsRawContentWithLinks()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/pages/2026-07-19-pasted.md" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Kind").GetString().Should().Be("page");
        json.GetProperty("Path").GetString().Should().Be("logmark/_ideas/pages/2026-07-19-pasted.md");
        json.GetProperty("Content").GetString().Should().Contain("full pasted transcript");

        var links = json.GetProperty("Links").EnumerateArray().ToList();
        links.Should().HaveCount(2);
        links[0].GetProperty("TargetPath").GetString().Should().Be("logmark/_ideas/2026.md");
        links[0].GetProperty("Anchor").GetString().Should().Be("5fe7f4d2");
        links[0].GetProperty("Kind").GetString().Should().Be("entries-file");
        links[0].GetProperty("Exists").GetBoolean().Should().BeTrue();
        links[1].GetProperty("Kind").GetString().Should().Be("missing");
        links[1].GetProperty("IsResolved").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ReadEntry_PageFile_ListsTheEntriesThatLinkToIt()
    {
        // Bead lmk-gr8f: outgoing links resolved on a page read, but the page
        // carried no Backlinks at all, so an agent could not walk from a page
        // back to the entry that cites it.
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/pages/2026-07-19-pasted.md" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        var backlinks = json.GetProperty("Backlinks").EnumerateArray().ToList();
        backlinks.Should().ContainSingle();
        backlinks[0].GetProperty("SourcePath").GetString().Should().Be("logmark/_ideas/2026.md");
        backlinks[0].GetProperty("EntryType").GetString().Should().Be("idea");
        backlinks[0].GetProperty("ContentPreview").GetString().Should().Contain("paste observation");
        backlinks[0].GetProperty("EntryId").GetString().Should().Be("5fe7f4d2",
            "an agent follows a backlink with read_entry(entry_id)");
    }

    [Fact]
    public async Task ReadEntry_ExtensionlessWikilinkPath_ResolvesMdSibling()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/pages/2026-07-19-pasted" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Kind").GetString().Should().Be("page");
    }

    [Fact]
    public async Task ReadEntry_AnchorInPath_SelectsEntry()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/2026#5fe7f4d2" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Entry").GetProperty("EntryId").GetString().Should().Be("5fe7f4d2");
    }

    [Fact]
    public async Task ReadEntry_PageContent_HonorsMaxContentLength()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/pages/2026-07-19-pasted", max_content_length = 10 }),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Content").GetString().Should().HaveLength(10);
        json.GetProperty("Truncated").GetBoolean().Should().BeTrue();
        json.GetProperty("SizeBytes").GetInt64().Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task ReadEntry_TraversalOutsideVault_RefusedWithGuidance()
    {
        var outside = Path.Combine(_testVaultPath, "..", $"outside-{Guid.NewGuid()}.md");
        File.WriteAllText(outside, "external");
        try
        {
            var result = await _tool.ExecuteAsync(
                Args(new { path = $"../{Path.GetFileName(outside)}" }), CancellationToken.None);

            result.IsError.Should().BeTrue();
            result.Content[0].Text.Should().Contain("outside the vault");
            result.Content[0].Text.Should().Contain("your own file tools");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task ReadEntry_TraversalToMissingExternal_ReportsNotExists()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { path = "../does-not-exist-anywhere.md" }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("outside the vault");
        result.Content[0].Text.Should().Contain("does not exist");
    }

    [Fact]
    public async Task ReadEntry_PathOnly_ResolvesEntryLinks()
    {
        // Regression (lmk-f6c3): the path-only (bulk) shape must resolve wikilinks
        // exactly like the entry_id shape. Before the fix the bulk branch used
        // ResolveOptions.Query and returned Links:[] for every entry.
        var result = await _tool.ExecuteAsync(
            Args(new { path = "logmark/_ideas/2026.md" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        var entries = json.GetProperty("Entries").EnumerateArray().ToList();
        entries.Should().ContainSingle();
        var links = entries[0].GetProperty("Links").EnumerateArray().ToList();
        links.Should().NotBeEmpty();
        links[0].GetProperty("TargetPath").GetString().Should().Contain("2026-07-19-pasted");
    }

    private static Dictionary<string, object?> Args(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();
        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testVaultPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temp vault.
        }
    }
}
