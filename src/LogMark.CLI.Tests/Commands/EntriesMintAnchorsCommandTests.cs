using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests.Commands;

[Collection("EnvironmentTests")]
public class EntriesMintAnchorsCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;

    private const string IdeasFixture =
        "## 2026-01-02 09:00\n" +
        "No id line at all.\n";

    public EntriesMintAnchorsCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
        _vault.WithYearFile("_ideas", 2026, IdeasFixture);
    }

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void DryRun_PrintsCountAndSamples_WritesNothing()
    {
        var ideasPath = Path.Combine(_vault.LogMarkPath, "_ideas", "2026.md");
        var before = File.ReadAllBytes(ideasPath);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "mint-anchors");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("1 entries lack block anchors");
        result.StdOut.Should().Contain("_ideas/2026.md");

        var after = File.ReadAllBytes(ideasPath);
        after.Should().Equal(before);
    }

    [Fact]
    public void Apply_MintsAnchor_AndReportsCount()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "mint-anchors", "--apply");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Minted 1 anchor(s).");

        var ideasPath = Path.Combine(_vault.LogMarkPath, "_ideas", "2026.md");
        var text = File.ReadAllText(ideasPath);
        text.Should().MatchRegex(@"_id: [a-f0-9]{8} \^[a-f0-9]{8}");
    }

    [Fact]
    public void Apply_Twice_SecondRunReportsZero()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "entry", "mint-anchors", "--apply");
        var second = CliRunner.Run("--vault", _vault.VaultPath, "entry", "mint-anchors", "--apply");

        second.ExitCode.Should().Be(0);
        second.StdOut.Should().Contain("Minted 0 anchor(s).");
    }
}
