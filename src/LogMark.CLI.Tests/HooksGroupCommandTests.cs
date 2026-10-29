using System.Text.Json;
using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class HooksGroupCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string _projectDir;

    public HooksGroupCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
        _projectDir = Path.Combine(Path.GetTempPath(), $"lm-hg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        _vault.Dispose();
        try { Directory.Delete(_projectDir, recursive: true); } catch { }
    }

    [Fact]
    public void Install_DefaultAgent_WritesClaudeProjectSettings()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath,
            "hooks", "install", "--project", _projectDir);

        result.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(_projectDir, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public void Install_CodexAgent_WritesCodexProjectHooks()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath,
            "hooks", "install", "--agent", "codex", "--project", _projectDir);

        result.ExitCode.Should().Be(0);
        var path = Path.Combine(_projectDir, ".codex", "hooks.json");
        File.Exists(path).Should().BeTrue();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var hooks = doc.RootElement.GetProperty("hooks");
        // SessionEnd, not the per-turn Stop this used to assert
        // (lmk-codex-hook-profile-stale-untn).
        hooks.TryGetProperty("SessionEnd", out _).Should().BeTrue();
        hooks.TryGetProperty("Stop", out _).Should().BeFalse();
        hooks.TryGetProperty("PreCompact", out _).Should().BeTrue();
    }

    [Fact]
    public void Install_UnknownAgent_ExitsNonZero()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath,
            "hooks", "install", "--agent", "windsurf", "--project", _projectDir);

        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public void Uninstall_CodexAgent_RemovesHooks()
    {
        CliRunner.Run("--vault", _vault.VaultPath,
            "hooks", "install", "--agent", "codex", "--project", _projectDir);

        var result = CliRunner.Run("--vault", _vault.VaultPath,
            "hooks", "uninstall", "--agent", "codex", "--project", _projectDir);

        result.ExitCode.Should().Be(0);
        var path = Path.Combine(_projectDir, ".codex", "hooks.json");
        File.ReadAllText(path).Should().NotContain("agent-capture");
    }
}
