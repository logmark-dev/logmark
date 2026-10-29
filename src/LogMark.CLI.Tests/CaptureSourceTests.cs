using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Bead lmk-baex: lm-vault spec 3.2 reads an entry with no source: line as
/// written by a human. A person typing `logmark capture` is that human, so
/// the capture must not stamp source: cli. An agent shelling out names
/// itself with --source.
/// </summary>
[Collection("EnvironmentTests")]
public class CaptureSourceTests : IDisposable
{
    private readonly TestVaultBuilder _vault = new TestVaultBuilder().WithStandardStructure();

    public void Dispose() => _vault.Dispose();

    private string AllEntryText() => string.Join("\n",
        Directory.GetFiles(Path.Combine(_vault.VaultPath, "logmark"), "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_system"))
            .Select(File.ReadAllText));

    [Fact]
    public void Capture_ByDefault_WritesNoSourceLine()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: human typed baex probe");

        result.ExitCode.Should().Be(0, result.StdErr);
        var text = AllEntryText();
        text.Should().Contain("human typed baex probe");
        text.Should().NotContain("source:");
    }

    [Fact]
    public void Capture_WithSource_WritesThatSource()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture",
            "--source", "agent:codex", "i: agent wrote baex probe");

        result.ExitCode.Should().Be(0, result.StdErr);
        var text = AllEntryText();
        text.Should().Contain("agent wrote baex probe");
        text.Should().Contain("source: agent:codex");
        text.Should().NotContain("--source");
    }
}
