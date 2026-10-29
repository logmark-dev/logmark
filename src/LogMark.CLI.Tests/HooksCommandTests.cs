using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class HooksCommandTests : IDisposable
{
    private readonly string _projectDir;

    public HooksCommandTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), $"lm-hookscmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_projectDir, ".claude", "settings.json");

    [Fact]
    public void Install_WritesHooks_AndReportsPath()
    {
        var result = CliRunner.Run("hooks", "install", "--project", _projectDir, "--route", "backend");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain(".claude");
        File.Exists(SettingsPath).Should().BeTrue();
        File.ReadAllText(SettingsPath).Should().Contain("agent-context").And.Contain("--route backend");
    }

    [Fact]
    public void Uninstall_RemovesHooks()
    {
        CliRunner.Run("hooks", "install", "--project", _projectDir);

        var result = CliRunner.Run("hooks", "uninstall", "--project", _projectDir);

        result.ExitCode.Should().Be(0);
        File.ReadAllText(SettingsPath).Should().NotContain("agent-context");
    }

    [Fact]
    public void Install_UnparseableSettings_Fails_WithExitCode2()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ broken");

        var result = CliRunner.Run("hooks", "install", "--project", _projectDir);

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("not valid JSON");
    }
}
