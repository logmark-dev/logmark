using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

public class BootstrapReaderTests : IDisposable
{
    private readonly string _tempDir;

    public BootstrapReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logmark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ReadVaultPath_ReturnsPath()
    {
        var bootstrapPath = Path.Combine(_tempDir, "bootstrap.json");
        var vaultDir = Path.Combine(_tempDir, "vault");
        Directory.CreateDirectory(vaultDir);

        File.WriteAllText(bootstrapPath, JsonSerializer.Serialize(new { VaultPath = vaultDir }));

        var result = BootstrapReader.ReadVaultPath(bootstrapPath);
        result.Should().Be(vaultDir);
    }

    [Fact]
    public void ReadVaultPath_MissingFile_ReturnsNull()
    {
        var result = BootstrapReader.ReadVaultPath(Path.Combine(_tempDir, "nonexistent.json"));
        result.Should().BeNull();
    }

    [Fact]
    public void ReadVaultPath_NullVaultPath_ReturnsNull()
    {
        var bootstrapPath = Path.Combine(_tempDir, "bootstrap.json");
        File.WriteAllText(bootstrapPath, "{}");

        var result = BootstrapReader.ReadVaultPath(bootstrapPath);
        result.Should().BeNull();
    }

    [Fact]
    public void ReadVaultPath_EmptyVaultPath_ReturnsNull()
    {
        var bootstrapPath = Path.Combine(_tempDir, "bootstrap.json");
        File.WriteAllText(bootstrapPath, JsonSerializer.Serialize(new { VaultPath = "" }));

        var result = BootstrapReader.ReadVaultPath(bootstrapPath);
        result.Should().BeNull();
    }

    [Fact]
    public void ReadVaultPath_CorruptJson_ReturnsNull()
    {
        var bootstrapPath = Path.Combine(_tempDir, "bootstrap.json");
        File.WriteAllText(bootstrapPath, "not json at all");

        var result = BootstrapReader.ReadVaultPath(bootstrapPath);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveBootstrapPath_Null_ReturnsDefault()
    {
        BootstrapReader.ResolveBootstrapPath(null)
            .Should().Be(BootstrapReader.GetDefaultBootstrapPath());
    }

    [Fact]
    public void ResolveBootstrapPath_Empty_ReturnsDefault()
    {
        // FollowActive wiring emits ["--bootstrap-path", ""]; empty must follow active.
        BootstrapReader.ResolveBootstrapPath("")
            .Should().Be(BootstrapReader.GetDefaultBootstrapPath());
    }

    [Fact]
    public void ResolveBootstrapPath_Whitespace_ReturnsDefault()
    {
        BootstrapReader.ResolveBootstrapPath("   ")
            .Should().Be(BootstrapReader.GetDefaultBootstrapPath());
    }

    [Fact]
    public void ResolveBootstrapPath_RealPath_PassesThrough()
    {
        var custom = Path.Combine(_tempDir, "alt-bootstrap.json");
        BootstrapReader.ResolveBootstrapPath(custom).Should().Be(custom);
    }
}
