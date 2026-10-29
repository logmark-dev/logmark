using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// read_entry resolves an entry by id alone. List results carry an id and a
/// route but no file, and nested route folders read back as their parent
/// route, so a caller-built path points at the wrong file. The id must win.
/// </summary>
public class ReadEntryByIdTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly ReadEntryTool _tool;

    private const string NestedPath = "logmark/domains/finance/investing/_ideas/2026.md";
    private const string ParentPath = "logmark/domains/finance/_diary/2026.md";
    private const string UnanchoredPath = "logmark/projects/guide/_tasks/2026.md";

    public ReadEntryByIdTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-byid-test-{Guid.NewGuid()}");

        Write(NestedPath, """
            ## 2026-05-13 09:58
            idea: densification arbitrage thesis
            _id: 4b4f9906 ^4b4f9906
            """);
        Write(ParentPath, """
            ## 2026-05-14 10:00
            unrelated finance diary note
            _id: 11112222 ^11112222
            """);
        // No "_id:" anchor: a vault written elsewhere and never minted.
        Write(UnanchoredPath, """
            ## 2026-05-15 08:00
            task: wire the guide vault
            """);

        var entryCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        entryCache.StartWarmup();

        _tool = new ReadEntryTool(new VaultServices
        {
            VaultPath = _testVaultPath,
            EntryCache = entryCache,
            EntryResolver = new EntryResolver(entryCache, _testVaultPath)
        });
    }

    [Fact]
    public async Task ReadEntry_IdOnly_FindsEntryInNestedFile()
    {
        var result = await _tool.ExecuteAsync(Args(new { entry_id = "4b4f9906" }), CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Entry").GetProperty("Path").GetString().Should().Be(NestedPath);
        json.GetProperty("Entry").GetProperty("Content").GetString().Should().Contain("densification");
        json.TryGetProperty("Message", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ReadEntry_StalePathWithId_ReadsFromRealFileAndSaysSo()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { entry_id = "4b4f9906", path = ParentPath }), CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Entry").GetProperty("Path").GetString().Should().Be(NestedPath);
        json.GetProperty("Message").GetString().Should().Contain(ParentPath).And.Contain(NestedPath);
    }

    [Fact]
    public async Task ReadEntry_MissingFileWithId_ReadsFromRealFile()
    {
        var result = await _tool.ExecuteAsync(
            Args(new { entry_id = "4b4f9906", path = "logmark/domains/finance/_ideas/2026.md" }), CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Entry").GetProperty("Path").GetString().Should().Be(NestedPath);
    }

    [Fact]
    public async Task ReadEntry_UnknownId_ReportsNotFound()
    {
        var result = await _tool.ExecuteAsync(Args(new { entry_id = "deadbeef" }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Entry not found with ID: deadbeef");
    }

    [Fact]
    public async Task ReadEntry_UnaddressableId_NamesTheFileAndTheMintRemedy()
    {
        // List tools serve an unanchored entry under a computed id flagged
        // Unaddressable. The id index holds persisted anchors only, so an
        // id-only read misses it, and a bare "not found" left an MCP-only
        // agent no way to diagnose that (rc7.2 QA I2, lmk-gwy6).
        var servedId = ServedIdOf(UnanchoredPath);

        var result = await _tool.ExecuteAsync(Args(new { entry_id = servedId }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain(servedId)
            .And.Contain("Unaddressable")
            .And.Contain(UnanchoredPath)
            .And.Contain("logmark entry mint-anchors --apply");
    }

    [Fact]
    public async Task ReadEntry_NoPathNoId_ReportsMissingParameter()
    {
        var result = await _tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("path, entry_id");
    }

    [Fact]
    public async Task ReadEntry_BareIdPassedAsPath_ResolvesById()
    {
        // Agents that hold only an id put it in path (the field older schemas
        // required). A bare 8-hex path that names no file is an id.
        var result = await _tool.ExecuteAsync(Args(new { path = "4b4f9906" }), CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        json.GetProperty("Entry").GetProperty("Path").GetString().Should().Be(NestedPath);
        json.TryGetProperty("Message", out _).Should().BeFalse();
    }

    [Fact]
    public void Definition_PathIsNotRequired()
    {
        _tool.Definition.InputSchema.Required.Should().NotContain("path");
    }

    private string ServedIdOf(string relativePath)
    {
        var full = Path.Combine(_testVaultPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var entry = LogMark.Services.EntryParser.EntryParser.Parse(File.ReadAllText(full)).Single();
        entry.ShortId.Should().BeNullOrEmpty("the fixture must be unanchored");
        return EntryResolver.GetEntryId(entry);
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_testVaultPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
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
        try { Directory.Delete(_testVaultPath, recursive: true); } catch { }
    }
}
