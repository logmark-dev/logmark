using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Tools;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

public class UpdateTagsToolTests : IDisposable
{
    private readonly string _vault;
    private readonly UpdateTagsTool _tool;

    public UpdateTagsToolTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-tags-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_vault, PathConstants.GetSystemFolder()));

        var services = new VaultServices
        {
            VaultPath = _vault,
            VaultWriter = new VaultWriter(),
            Logger = new SystemLogger(_vault)
        };
        _tool = new UpdateTagsTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vault)) Directory.Delete(_vault, true);
    }

    private static Dictionary<string, object?> CreateArgs(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();
        return result;
    }

    [Fact]
    public async Task UpdateTags_WritesFile()
    {
        var result = await _tool.ExecuteAsync(
            CreateArgs(new { content = "# Tags\n\n- #adr: architecture decisions\n" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        File.ReadAllText(Path.Combine(_vault, PathConstants.GetTagsPath())).Should().Contain("#adr");
    }

    [Fact]
    public async Task UpdateTags_EmptyContent_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(CreateArgs(new { content = "" }), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
