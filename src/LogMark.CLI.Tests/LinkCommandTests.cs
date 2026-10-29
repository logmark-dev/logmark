using FluentAssertions;
using LogMark.Services;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class LinkCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault = new TestVaultBuilder().WithStandardStructure();
    private readonly string _routesPath;

    public LinkCommandTests()
    {
        _routesPath = new RouteParser(_vault.VaultPath).GetRoutesFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(_routesPath)!);
        File.WriteAllText(_routesPath, """
            # Routes

            ## Projects

            ### photomind
            - path: projects/photomind
            - description: keep this prose
            """);
    }

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Link_Route_AppendsWorkdirToRouteBlock_PreservingContent()
    {
        var cwd = Directory.GetCurrentDirectory();

        var result = CliRunner.Run("--vault", _vault.VaultPath, "link", "photomind");

        result.ExitCode.Should().Be(0);
        var config = RouteParser.ParseContent(File.ReadAllText(_routesPath));
        var route = config.Routes.Single(r => r.Name == "photomind");
        route.Workdirs.Should().Contain(Path.GetFullPath(cwd));
        route.Description.Should().Be("keep this prose", "the surgical edit preserves other fields");
    }

    [Fact]
    public void Link_Route_IsIdempotent()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "link", "photomind");
        CliRunner.Run("--vault", _vault.VaultPath, "link", "photomind");

        var config = RouteParser.ParseContent(File.ReadAllText(_routesPath));
        config.Routes.Single(r => r.Name == "photomind").Workdirs
            .Count(w => w == Path.GetFullPath(Directory.GetCurrentDirectory()))
            .Should().Be(1);
    }

    [Fact]
    public void Link_NoArg_AppendsVaultLevelWorkdir()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "link");

        result.ExitCode.Should().Be(0);
        var config = RouteParser.ParseContent(File.ReadAllText(_routesPath));
        config.VaultWorkdirs.Should().Contain(Path.GetFullPath(Directory.GetCurrentDirectory()));
    }

    [Fact]
    public void Link_UnknownRoute_ExitsNonZero()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "link", "no-such-route");

        result.ExitCode.Should().NotBe(0);
    }
}
