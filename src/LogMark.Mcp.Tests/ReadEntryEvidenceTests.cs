using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// The evidence line exists so an agent can follow a trail the transcript no
/// longer holds - git show a sha, grep a filename. That only works if read_entry
/// hands it over; an agent forced to parse vault markdown for provenance is an
/// agent that will not bother.
/// </summary>
public class ReadEntryEvidenceTests : IDisposable
{
    private readonly string _vault;
    private readonly ReadEntryTool _tool;

    private const string EvidenceValue =
        "turns:12,44; commits:LogMark@e1f8d03; files:SessionDistiller.cs +10";

    public ReadEntryEvidenceTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-evidence-{Guid.NewGuid():N}");
        var dir = Path.Combine(_vault, "logmark", "_decisions");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "2026.md"),
            "## 2026-08-17 09:00\n"
            + "Pinned the evidence wire format before entries could carry it.\n"
            + "_id: abc12345 ^abc12345\n"
            + "source: agent:session-distill\n"
            + $"evidence: {EvidenceValue}\n\n"
            + "## 2026-08-17 10:00\n"
            + "An entry with no provenance at all.\n"
            + "_id: def67890 ^def67890\n");

        var cache = new LogMark.Services.EntryCache.EntryCache(_vault);
        cache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!cache.IsReady && DateTime.UtcNow < timeout) Thread.Sleep(10);

        _tool = new ReadEntryTool(new VaultServices
        {
            VaultPath = _vault,
            EntryResolver = new EntryResolver(cache, _vault)
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_vault, recursive: true); } catch { }
    }

    private static Dictionary<string, object?> Args(object obj)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        var result = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();
        return result;
    }

    [Fact]
    public async Task ReadEntry_SurfacesTheEvidenceLine()
    {
        var result = await _tool.ExecuteAsync(Args(new { path = "logmark/_decisions/2026.md", entry_id = "abc12345" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("e1f8d03", "the sha is the breadcrumb an agent acts on");
        text.Should().Contain("SessionDistiller.cs");
    }

    [Fact]
    public async Task ReadEntry_EvidenceIsMetadataNotBody()
    {
        // Provenance must arrive as its own field. Folded into content it would
        // be indistinguishable from what the entry says, and would ride into
        // any prompt built from entry text.
        var result = await _tool.ExecuteAsync(Args(new { path = "logmark/_decisions/2026.md", entry_id = "abc12345" }), CancellationToken.None);

        var root = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        var entry = root.GetProperty("Entry");

        entry.GetProperty("Evidence").GetString().Should().Be(EvidenceValue);
        entry.GetProperty("Content").GetString().Should().NotContain("e1f8d03");
    }

    [Fact]
    public async Task ReadEntry_NoEvidence_OmitsTheFieldEntirely()
    {
        // WhenWritingNull, so an entry without provenance costs no payload and
        // no reader has to tell "absent" from "empty".
        var result = await _tool.ExecuteAsync(Args(new { path = "logmark/_decisions/2026.md", entry_id = "def67890" }), CancellationToken.None);

        var entry = JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("Entry");

        entry.TryGetProperty("Evidence", out _).Should().BeFalse();
    }
}
