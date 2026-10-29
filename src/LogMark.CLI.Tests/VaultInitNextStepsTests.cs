using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class VaultInitNextStepsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lm-init-{Guid.NewGuid():N}");
    private readonly string _savedOwner = ConnectCommand.OwnerId;

    public void Dispose()
    {
        ConnectCommand.OwnerId = _savedOwner;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Engine_next_steps_use_mcp_and_name_the_vault()
    {
        ConnectCommand.OwnerId = "oss-cli";
        var result = CliRunner.Run("vault", "init", _dir);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain($"logmark connect claude-code --mcp --vault \"{_dir}\"");
        result.StdOut.Should().Contain($"logmark capture --vault \"{_dir}\" \"t: try logmark\"");
        result.StdOut.Should().NotContain("--all --vault");
    }
}
