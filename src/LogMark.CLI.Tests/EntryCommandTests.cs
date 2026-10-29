using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class EntryCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    private const string TestEntryId = "abcd1234";
    private const string TestEntryId2 = "ef567890";

    public EntryCommandTests()
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

    /// <summary>
    /// Create a test entry with a known ShortId in the vault.
    /// </summary>
    private void CreateEntryWithId(string id, string content = "i: test idea about caching")
    {
        var entryContent = $"---\n\n## 2026-03-16 07:00\n\n{content}\n_id: {id}\nsource: cli\n";
        _vault.WithFile("_inbox/_ideas/2026.md", entryContent);
    }

    /// <summary>
    /// Create a second entry in a different file to avoid overwriting the first.
    /// </summary>
    private void CreateSecondEntryWithId(string id, string content = "i: another idea")
    {
        var entryContent = $"---\n\n## 2026-03-16 08:00\n\n{content}\n_id: {id}\nsource: cli\n";
        _vault.WithFile("_inbox/_ideas/2025.md", entryContent);
    }

    // --- EntryGroupCommand tests ---

    [Fact]
    public void Entry_NoSubcommand_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry");

        result.ExitCode.Should().Be(1);
        result.StdOut.Should().Contain("rate");
        result.StdOut.Should().Contain("flag");
    }

    [Fact]
    public void Entry_UnknownSubcommand_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "unknown");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown");
    }

    // --- EntryRateCommand tests ---

    [Fact]
    public void EntryRate_NoArgs_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("entry-id");
    }

    [Fact]
    public void EntryRate_MissingRating_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", "abcd1234");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("rating");
    }

    [Fact]
    public void EntryRate_InvalidRating_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", "abcd1234", "6");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("0-5");
    }

    [Fact]
    public void EntryRate_NegativeRating_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", "abcd1234", "-1");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("0-5");
    }

    [Fact]
    public void EntryRate_NonNumericRating_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", "abcd1234", "abc");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("0-5");
    }

    [Fact]
    public void EntryRate_EntryNotFound_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", "00000000", "3");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("not found");
    }

    [Fact]
    public void EntryRate_SetsRating()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", TestEntryId, "4");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("rating 4");

        // Verify the rating was written to the file
        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("rating: 4");
    }

    [Fact]
    public void EntryRate_JsonOutput_ReturnsJson()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "entry", "rate", TestEntryId, "5");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"modified\":");
        result.StdOut.Should().Contain("\"rating\":");
    }

    [Fact]
    public void EntryRate_ZeroRating_SetsUnrated()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "rate", TestEntryId, "0");

        result.ExitCode.Should().Be(0);

        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("rating: 0");
    }

    // --- EntryFlagCommand tests ---

    [Fact]
    public void EntryFlag_NoArgs_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("entry-id");
    }

    [Fact]
    public void EntryFlag_MissingFlagType_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", "abcd1234");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("flag-type");
    }

    [Fact]
    public void EntryFlag_InvalidFlagType_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", "abcd1234", "INVALID!");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Invalid flag-type");
    }

    [Fact]
    public void EntryFlag_UppercaseFlagType_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", "abcd1234", "UpperCase");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Invalid flag-type");
    }

    [Fact]
    public void EntryFlag_EntryNotFound_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", "00000000", "review-needed");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("not found");
    }

    [Fact]
    public void EntryFlag_AppendsFlag()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", TestEntryId, "review-needed");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("review-needed");

        // Verify the flag was written to the file
        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("flags: review-needed");
    }

    [Fact]
    public void EntryFlag_JsonOutput_ReturnsJson()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "entry", "flag", TestEntryId, "important");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"modified\":");
        result.StdOut.Should().Contain("\"flags\":");
    }

    [Fact]
    public void EntryFlag_Idempotent_DoesNotDuplicate()
    {
        CreateEntryWithId(TestEntryId);

        // First flag
        var result1 = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", TestEntryId, "starred");
        result1.ExitCode.Should().Be(0);

        // Second flag with same type -- should not duplicate
        var result2 = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", TestEntryId, "starred");
        result2.ExitCode.Should().Be(0);
        result2.StdOut.Should().Contain("already has flag");

        // Verify only one occurrence of "starred" in the flags field
        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        var flagsLine = content.Split('\n').FirstOrDefault(l => l.Trim().StartsWith("flags:"));
        flagsLine.Should().NotBeNull();
        // Count occurrences of "starred" in the flags line
        var count = flagsLine!.Split("starred").Length - 1;
        count.Should().Be(1, "flag should appear exactly once");
    }

    [Fact]
    public void EntryFlag_HyphenatedFlag_Accepted()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "entry", "flag", TestEntryId, "needs-review");

        result.ExitCode.Should().Be(0);

        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("flags: needs-review");
    }
}
