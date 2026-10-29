using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class PageCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    private const string TestEntryId = "abcd1234";

    public PageCommandTests()
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
    /// Uses _inbox structure since that's the current vault layout.
    /// </summary>
    private void CreateEntryWithId(string id, string content = "i: test idea about caching")
    {
        var entryContent = $"---\n\n## 2026-03-16 07:00\n\n{content}\n_id: {id}\nsource: cli\n";
        _vault.WithFile("_inbox/_ideas/2026.md", entryContent);
    }

    [Fact]
    public void PageWrite_NoArgs_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("type");
    }

    [Fact]
    public void PageWrite_MissingSlug_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write", "ideas");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("slug");
    }

    [Fact]
    public void PageWrite_WithStdinContent_CreatesPage()
    {
        var content = "# My Page\n\nSome content here.";
        var result = CliRunner.RunWithStdin(content,
            "--vault", _vault.VaultPath, "page", "write", "ideas", "my-test-page");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("my-test-page");
    }

    [Fact]
    public void PageWrite_WithFileContent_CreatesPage()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "# File Content\n\nFrom a file.");
            var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write",
                "--file", tempFile, "ideas", "file-page");

            result.ExitCode.Should().Be(0);
            result.StdOut.Should().Contain("file-page");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void PageWrite_WithRoute_CreatesRoutedPage()
    {
        var content = "# Routed Page\n\nContent.";
        var result = CliRunner.RunWithStdin(content,
            "--vault", _vault.VaultPath, "page", "write", "--route", "projects/backend", "tasks", "api-design");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("api-design");
    }

    [Fact]
    public void PageWrite_JsonOutput_ReturnsJson()
    {
        var content = "# JSON Test\n\nContent.";
        var result = CliRunner.RunWithStdin(content,
            "--json", "--vault", _vault.VaultPath, "page", "write", "ideas", "json-test");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"wiki_path\":");
    }

    [Fact]
    public void PageWrite_NoContent_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write", "ideas", "no-content");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("content");
    }

    [Fact]
    public void PageWrite_FileNotFound_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write",
            "--file", "/nonexistent/file.md", "ideas", "missing-file");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("not found");
    }

    [Fact]
    public void PageWrite_VerifyFileCreated()
    {
        var content = "# Verify Page\n\nVerification content.";
        var result = CliRunner.RunWithStdin(content,
            "--vault", _vault.VaultPath, "page", "write", "ideas", "verify-page");

        result.ExitCode.Should().Be(0);

        // Check the file was actually written to the vault
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var expectedDir = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "pages");
        var expectedFile = Path.Combine(expectedDir, $"{today}-verify-page.md");
        File.Exists(expectedFile).Should().BeTrue($"page file should exist at {expectedFile}");
        File.ReadAllText(expectedFile).Should().Contain("Verification content");
    }

    [Fact]
    public void Page_NoSubcommand_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page");

        result.ExitCode.Should().Be(1);
        result.StdOut.Should().Contain("write");
    }

    [Fact]
    public void Page_UnknownSubcommand_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "unknown");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown");
    }

    // --- PageLinkCommand tests ---

    [Fact]
    public void PageLink_NoArgs_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "link");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("entry-id");
    }

    [Fact]
    public void PageLink_MissingWikiPath_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "link", "abcd1234");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("wiki-path");
    }

    [Fact]
    public void PageLink_EntryNotFound_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "link",
            "00000000", "logmark/_inbox/_ideas/pages/2026-03-16-test");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("not found");
    }

    [Fact]
    public void PageLink_InsertsWikiLink()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "link",
            TestEntryId, "logmark/_inbox/_ideas/pages/2026-03-16-test-page", "--text", "Test Page");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Linked");

        // Verify the wiki link was actually inserted
        var filePath = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "2026.md");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[[logmark/_inbox/_ideas/pages/2026-03-16-test-page|Test Page]]");
    }

    [Fact]
    public void PageLink_JsonOutput_ReturnsJson()
    {
        CreateEntryWithId(TestEntryId);

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "page", "link",
            TestEntryId, "logmark/_inbox/_ideas/pages/2026-03-16-json-test");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"modified\":");
        result.StdOut.Should().Contain("\"wiki_path\":");
    }

    [Fact]
    public void PageLink_Idempotent_DoesNotDuplicate()
    {
        CreateEntryWithId(TestEntryId);
        var wikiPath = "logmark/_inbox/_ideas/pages/2026-03-16-idem-test";

        // First link
        var result1 = CliRunner.Run("--vault", _vault.VaultPath, "page", "link",
            TestEntryId, wikiPath, "--text", "Idem Test");
        result1.ExitCode.Should().Be(0);

        // Second link to same path -- should not duplicate
        var result2 = CliRunner.Run("--vault", _vault.VaultPath, "page", "link",
            TestEntryId, wikiPath, "--text", "Idem Test");
        result2.ExitCode.Should().Be(0);
        result2.StdOut.Should().Contain("already links");
    }
}
