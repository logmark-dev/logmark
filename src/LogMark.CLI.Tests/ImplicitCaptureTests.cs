using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class ImplicitCaptureTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    public ImplicitCaptureTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
        _originalTier = Environment.GetEnvironmentVariable("LOGMARK_LICENSE_TIER");
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", _originalTier);
        _vault.Dispose();
    }

    [Fact]
    public void ImplicitCapture_NotationPrefix_SavesTask()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "t: fix bug");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
    }

    [Fact]
    public void ImplicitCapture_LongNotationPrefix_SavesIdea()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "idea: cache layer");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved idea");
    }

    [Fact]
    public void ImplicitCapture_MultiWordUnquoted_SavesLog()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "deployed", "to", "prod");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");
    }

    [Fact]
    public void ImplicitCapture_RoutePrefix_SavesEntry()
    {
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n");

        var result = CliRunner.Run("--vault", _vault.VaultPath, "+backend", "t: fix API");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
    }

    [Fact]
    public void ImplicitCapture_JsonFlag_ReturnsJson()
    {
        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "t: task via implicit");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"path\":");
        result.StdOut.Should().Contain("task");
    }

    [Fact]
    public void ImplicitCapture_VaultFlag_Works()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "i: idea via implicit");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved idea");
    }

    [Fact]
    public void NearTypo_ShowsDidYouMean()
    {
        // "captur" is distance 1 from "capture"
        var result = CliRunner.Run("--vault", _vault.VaultPath, "captur");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Did you mean");
    }

    [Fact]
    public void UnknownSingleWord_ShowsError()
    {
        // Single unknown word with no similar commands -- not capture
        var result = CliRunner.Run("--vault", _vault.VaultPath, "zzzzzzz");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown command");
    }

    [Theory]
    [InlineData("t:")]
    [InlineData("i:")]
    [InlineData("b:")]
    [InlineData("d:")]
    [InlineData("task:")]
    [InlineData("idea:")]
    [InlineData("block:")]
    [InlineData("decision:")]
    public void LooksLikeCapture_NotationPrefixes_ReturnTrue(string prefix)
    {
        CommandRunner.LooksLikeCapture(prefix + " content", Array.Empty<string>())
            .Should().BeTrue();
    }

    [Fact]
    public void LooksLikeCapture_RoutePrefix_ReturnTrue()
    {
        CommandRunner.LooksLikeCapture("+backend", new[] { "t:", "fix", "api" })
            .Should().BeTrue();
    }

    [Fact]
    public void LooksLikeCapture_MultiWord_ReturnTrue()
    {
        CommandRunner.LooksLikeCapture("deployed", new[] { "to", "prod" })
            .Should().BeTrue();
    }

    [Fact]
    public void LooksLikeCapture_SingleUnknownWord_ReturnFalse()
    {
        CommandRunner.LooksLikeCapture("zzzzzzz", Array.Empty<string>())
            .Should().BeFalse();
    }

    [Fact]
    public void HelpText_ShowsImplicitCapture()
    {
        var result = CliRunner.Run("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("implicit capture");
    }
}
