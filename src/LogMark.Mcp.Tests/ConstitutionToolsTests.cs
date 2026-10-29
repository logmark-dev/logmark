using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Tools;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

public class ConstitutionToolsTests : IDisposable
{
    private readonly string _vault;
    private readonly UpdateConstitutionTool _updateTool;
    private readonly GetConstitutionTool _getTool;

    public ConstitutionToolsTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-constitution-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_vault, PathConstants.GetSystemFolder()));

        var services = new VaultServices
        {
            VaultPath = _vault,
            VaultWriter = new VaultWriter(),
            Logger = new SystemLogger(_vault)
        };
        _updateTool = new UpdateConstitutionTool(services);
        _getTool = new GetConstitutionTool(services);
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
    public async Task UpdateConstitution_WritesFileAndReportsConventions()
    {
        var md = "# My vault\n\nWhy it exists.\n\n## Conventions\n\nADRs use #adr.\n";

        var result = await _updateTool.ExecuteAsync(CreateArgs(new { content = md }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text!.ShouldHaveJsonProperty("HasConventions", "true");
        File.ReadAllText(Path.Combine(_vault, "logmark", "vault.md")).Should().Contain("## Conventions");
    }

    [Fact]
    public async Task UpdateConstitution_ContentWithoutFrontmatter_KeepsExistingDeclaration()
    {
        // get_constitution returns parsed sections only, so an agent that reads,
        // edits and writes back never sees the frontmatter. Replacing the file
        // verbatim would silently drop the vault's format declaration.
        var path = Path.Combine(_vault, "logmark", "vault.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\nschema: lm-vault/1.0.0\n---\n\n# Old\n\nOld purpose.\n");

        var result = await _updateTool.ExecuteAsync(
            CreateArgs(new { content = "# New\n\nNew purpose.\n" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var written = File.ReadAllText(path);
        written.Should().StartWith("---\nschema: lm-vault/1.0.0\n---\n");
        written.Should().Contain("# New").And.NotContain("# Old");
    }

    [Fact]
    public async Task UpdateConstitution_ContentWithOwnFrontmatter_ReplacesIt()
    {
        var path = Path.Combine(_vault, "logmark", "vault.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\nschema: lm-vault/1.0.0\n---\n\n# Old\n");

        await _updateTool.ExecuteAsync(
            CreateArgs(new { content = "---\nschema: lm-vault/1.1.0\n---\n\n# New\n" }), CancellationToken.None);

        var written = File.ReadAllText(path);
        written.Should().Contain("lm-vault/1.1.0").And.NotContain("lm-vault/1.0.0");
    }

    [Fact]
    public async Task GetConstitution_SurfacesDeclaredSchema()
    {
        await _updateTool.ExecuteAsync(
            CreateArgs(new { content = "---\nschema: lm-vault/1.0.0\n---\n\n# V\n\nPurpose.\n" }), CancellationToken.None);

        var result = await _getTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.Content[0].Text!.ShouldHaveJsonProperty("Schema", "\"lm-vault/1.0.0\"");
    }

    [Fact]
    public async Task GetConstitution_UnsupportedMajor_WarnsInsteadOfReadingSilently()
    {
        await _updateTool.ExecuteAsync(
            CreateArgs(new { content = "---\nschema: lm-vault/2.0.0\n---\n\n# V\n\nPurpose.\n" }), CancellationToken.None);

        var result = await _getTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text!.Should().Contain("SchemaWarning").And.Contain("lm-vault/2.0.0");
    }

    [Fact]
    public async Task GetConstitution_SupportedSchema_HasNoWarning()
    {
        await _updateTool.ExecuteAsync(
            CreateArgs(new { content = "---\nschema: lm-vault/1.3.0\n---\n\n# V\n\nPurpose.\n" }), CancellationToken.None);

        var result = await _getTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.Content[0].Text!.Should().NotContain("SchemaWarning");
    }

    [Fact]
    public async Task UpdateConstitution_EmptyContent_ReturnsError()
    {
        var result = await _updateTool.ExecuteAsync(CreateArgs(new { content = "" }), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetConstitution_NoFile_ReportsUnestablished()
    {
        var result = await _getTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text!.ShouldHaveJsonProperty("Established", "false");
    }

    [Fact]
    public async Task GetConstitution_AfterWrite_SurfacesConventions()
    {
        var md = "# V\n\nPurpose.\n\n## Conventions\n\nGroom weekly.\n";
        await _updateTool.ExecuteAsync(CreateArgs(new { content = md }), CancellationToken.None);

        var result = await _getTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.Content[0].Text!.ShouldHaveJsonProperty("Established", "true");
        result.Content[0].Text!.Should().Contain("Groom weekly");
    }
}
