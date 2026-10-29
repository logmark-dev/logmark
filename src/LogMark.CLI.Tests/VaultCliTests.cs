using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

public class VaultCliTests : IDisposable
{
    private readonly string _tempPath;

    public VaultCliTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"logmark-cli-test-{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    [Fact]
    public void Templates_ListsAllBundles()
    {
        var result = CliRunner.Run("vault", "templates");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("tracker");
        result.StdOut.Should().Contain("research");
        result.StdOut.Should().Contain("build");
        result.StdOut.Should().Contain("code-context");
        result.StdOut.Should().Contain("operator");
        result.StdOut.Should().Contain("journal");
        result.StdOut.Should().Contain("source");
    }

    [Fact]
    public void Templates_ShowsDefaultMarker()
    {
        var result = CliRunner.Run("vault", "templates");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("(default)");
        result.StdOut.Should().Contain("build");
    }

    [Fact]
    public void Templates_OnOssBinary_MarksOpenAsDefault()
    {
        var prior = ConnectCommand.OwnerId;
        ConnectCommand.OwnerId = "oss-cli";
        try
        {
            var result = CliRunner.Run("vault", "templates");
            result.ExitCode.Should().Be(0, result.StdErr);
            result.StdOut.Should().MatchRegex(@"open\s.*\(default\)");
            result.StdOut.Should().NotMatchRegex(@"build\s.*\(default\)");
        }
        finally { ConnectCommand.OwnerId = prior; }
    }

    [Fact]
    public void Init_OnOssBinary_DefaultsToOpenBundle_WithNoEntriesAndNoRoutes()
    {
        var prior = ConnectCommand.OwnerId;
        ConnectCommand.OwnerId = "oss-cli";
        try
        {
            var target = Path.Combine(_tempPath, "open");
            var result = CliRunner.Run("vault", "init", target);
            result.ExitCode.Should().Be(0, result.StdErr);

            var logmark = Path.Combine(target, "logmark");
            Directory.GetFiles(logmark, "20*.md", SearchOption.AllDirectories).Should().BeEmpty("no seeded entries");
            Directory.GetDirectories(logmark).Select(Path.GetFileName)
                .Should().OnlyContain(n => n!.StartsWith("_"), "no demo routes");
            File.ReadAllText(Path.Combine(logmark, "vault.md")).Should().Contain("## How agents should help");
            File.ReadAllText(Path.Combine(logmark, "_system", "routes.md")).Should().NotContain("### ");
            result.StdOut.Should().NotContain("from the build bundle");
        }
        finally { ConnectCommand.OwnerId = prior; }
    }

    [Fact]
    public void Init_OnDesktopCli_StillDefaultsToBuildBundle()
    {
        var target = Path.Combine(_tempPath, "build");
        var result = CliRunner.Run("vault", "init", target);
        result.ExitCode.Should().Be(0, result.StdErr);
        Directory.Exists(Path.Combine(target, "logmark", "architecture")).Should().BeTrue();
    }

    [Fact]
    public void Init_CreatesBuildByDefault()
    {
        var result = CliRunner.Run("vault", "init", _tempPath);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Initialized");
        result.StdOut.Should().Contain("build");

        // Verify build structure
        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        File.Exists(routesPath).Should().BeTrue();

        var content = File.ReadAllText(routesPath);
        content.Should().Contain("### arch");
        content.Should().Contain("### features");
        content.Should().Contain("### infra");
        content.Should().Contain("## Archived");
    }

    [Theory]
    [InlineData("tracker")]
    [InlineData("research")]
    [InlineData("build")]
    [InlineData("code-context")]
    [InlineData("operator")]
    [InlineData("journal")]
    [InlineData("source")]
    public void Init_CreatesBundleStructure(string bundle)
    {
        var result = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Initialized");
        result.StdOut.Should().Contain(bundle);

        // Verify basic structure exists
        Directory.Exists(Path.Combine(_tempPath, "logmark")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempPath, "logmark", "_inbox")).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempPath, "logmark", "_system")).Should().BeTrue();
        File.Exists(Path.Combine(_tempPath, "logmark", "_system", "routes.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempPath, "logmark", "_system", "config.md")).Should().BeTrue();
    }

    [Theory]
    [InlineData("operator", new[] { "## Clients", "## Operations" })]
    [InlineData("research", new[] { "## Reading", "## Ideas", "## Active" })]
    [InlineData("build", new[] { "## Projects" })]
    [InlineData("journal", new string[] { })]
    [InlineData("source", new string[] { })]
    public void Init_CreatesCorrectRouteSections(string bundle, string[] expectedSections)
    {
        var result = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");

        result.ExitCode.Should().Be(0);

        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        var content = File.ReadAllText(routesPath);

        foreach (var section in expectedSections)
            content.Should().Contain(section);

        // All should have Archived
        content.Should().Contain("## Archived");
    }

    [Fact]
    public void Init_ExistingVault_FailsWithoutForce()
    {
        // First init
        var firstResult = CliRunner.Run("vault", "init", _tempPath);
        firstResult.ExitCode.Should().Be(0);

        // Second init should fail
        var secondResult = CliRunner.Run("vault", "init", _tempPath);

        secondResult.ExitCode.Should().Be(1);
        secondResult.StdErr.Should().Contain("already exists");
        secondResult.StdErr.Should().Contain("--force");
    }

    [Fact]
    public void Init_ExistingVault_SucceedsWithForce()
    {
        // First init with operator
        var firstResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=operator");
        firstResult.ExitCode.Should().Be(0);

        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        var originalContent = File.ReadAllText(routesPath);
        originalContent.Should().Contain("## Clients");

        // Second init with journal and force
        var secondResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=journal", "--force");

        secondResult.ExitCode.Should().Be(0);

        var newContent = File.ReadAllText(routesPath);
        newContent.Should().NotContain("## Clients");
    }

    [Fact]
    public void Init_InvalidBundle_Fails()
    {
        var result = CliRunner.Run("vault", "init", _tempPath, "--bundle=bogus");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown bundle");
    }

    [Fact]
    public void Init_NonexistentPath_CreatesDirectory()
    {
        var nestedPath = Path.Combine(_tempPath, "nested", "deep", "vault");

        var result = CliRunner.Run("vault", "init", nestedPath);

        result.ExitCode.Should().Be(0);
        Directory.Exists(nestedPath).Should().BeTrue();
        Directory.Exists(Path.Combine(nestedPath, "logmark")).Should().BeTrue();
    }

    [Fact]
    public void Help_ShowsUsage()
    {
        var result = CliRunner.Run("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Usage:");
        result.StdOut.Should().Contain("vault");
    }

    [Fact]
    public void Version_ShowsVersion()
    {
        var result = CliRunner.Run("version");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("LogMark CLI");
    }

    [Fact]
    public void UnknownCommand_Fails()
    {
        var result = CliRunner.Run("notacommand");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown command");
    }

    [Fact]
    public void VaultWithoutSubcommand_Fails()
    {
        var result = CliRunner.Run("vault");

        result.ExitCode.Should().Be(1);
        result.StdOut.Should().Contain("Usage:");
    }

    [Fact]
    public void Init_UsesGlobalVaultFlag_WhenNoPositionalPath()
    {
        var target = Path.Combine(_tempPath, "via-flag");

        var result = CliRunner.Run("vault", "init", "--vault", target, "--no-seed");

        result.ExitCode.Should().Be(0, result.StdErr);
        Directory.Exists(Path.Combine(target, "logmark", "_system")).Should().BeTrue();
    }
}
