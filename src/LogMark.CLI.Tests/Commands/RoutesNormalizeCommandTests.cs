using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests.Commands;

[Collection("EnvironmentTests")]
public class RoutesNormalizeCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;

    public RoutesNormalizeCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
    }

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void RoutesNormalize_OnDirtyVault_DedupesRoutesFile()
    {
        var dirtyContent = """
# Routes

## Domains

### agency
- path: domains/agency

### agency
- path: domains/agency
""";
        _vault.WithFile("_system/routes.md", dirtyContent);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "routes-normalize");

        result.ExitCode.Should().Be(0);
        var routesPath = Path.Combine(_vault.LogMarkPath, "_system", "routes.md");
        var content = File.ReadAllText(routesPath);
        content.Split('\n').Count(l => l.Trim() == "### agency").Should().Be(1);
    }

    [Fact]
    public void RoutesNormalize_AlreadyClean_ReturnsZero()
    {
        var cleanContent = """
# Routes

## Domains

### agency
- path: domains/agency
""";
        _vault.WithFile("_system/routes.md", cleanContent);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "routes-normalize");

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void RoutesNormalize_MissingRoutesFile_ReturnsNonZero()
    {
        // Don't seed routes.md.
        var result = CliRunner.Run("--vault", _vault.VaultPath, "routes-normalize");

        result.ExitCode.Should().NotBe(0);
    }
}
