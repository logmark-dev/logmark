using FluentAssertions;
using LogMark.Models;
using LogMark.Services.EntryCache;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class PipeCaptureTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    public PipeCaptureTests()
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
    public void Pipe_LargeContent_CreatesSubpage()
    {
        var pipedContent = "Line 1: First point\nLine 2: Second point\nLine 3: Third point\nLine 4: Fourth point";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture", "d: architecture review notes", "-");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved decision");
        result.StdOut.Should().Contain("with linked page");
    }

    [Fact]
    public void Pipe_LargeContent_SubpageFileExists()
    {
        var pipedContent = "Meeting started at 10am\nDiscussed caching strategy\nAgreed on Redis approach\nAction items below";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture", "d: meeting notes", "-");

        result.ExitCode.Should().Be(0);

        // Verify subpage file was created in pages/ folder (unrouted = _inbox)
        var pagesDir = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_decisions", "pages");
        Directory.Exists(pagesDir).Should().BeTrue("subpage directory should be created");

        var subpageFiles = Directory.GetFiles(pagesDir, "*.md");
        subpageFiles.Should().HaveCount(1);

        // Subpage should contain the piped content
        var subpageContent = File.ReadAllText(subpageFiles[0]);
        subpageContent.Should().Contain("Meeting started at 10am");
        subpageContent.Should().Contain("Agreed on Redis approach");
    }

    [Fact]
    public void Pipe_ShortContent_StaysInline()
    {
        // 2 lines or fewer -- no subpage
        var pipedContent = "small note\nsecond line";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture", "quick thought", "-");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");
        result.StdOut.Should().NotContain("linked page");

        // No pages directory should be created
        var pagesDir = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_diary", "pages");
        Directory.Exists(pagesDir).Should().BeFalse("short content should stay inline");
    }

    [Fact]
    public void Pipe_OnlyStdin_NoInlineArgs_UsesFirstLineAsEntry()
    {
        var pipedContent = "Architecture Decision Record\nWe decided to use PostgreSQL\nFor the following reasons\nScalability and reliability";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");
        result.StdOut.Should().Contain("with linked page");
    }

    [Fact]
    public void Pipe_WithTypeFlag_SetsCorrectType()
    {
        var pipedContent = "Line 1\nLine 2\nLine 3\nLine 4";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture", "--type", "idea", "caching strategy", "-");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved idea");
        result.StdOut.Should().Contain("with linked page");

        // Subpage should be in ideas folder (unrouted = _inbox)
        var pagesDir = Path.Combine(_vault.VaultPath, "logmark", "_inbox", "_ideas", "pages");
        Directory.Exists(pagesDir).Should().BeTrue();
    }

    [Fact]
    public void Pipe_JsonOutput_IncludesSubpageFlag()
    {
        var pipedContent = "Line 1\nLine 2\nLine 3\nLine 4";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--json", "--vault", _vault.VaultPath, "capture", "d: test json", "-");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"hasSubpage\":");
        result.StdOut.Should().Contain("true");
    }

    [Fact]
    public void Pipe_EmptyStdin_NoInlineArgs_ShowsUsage()
    {
        var result = CliRunner.RunWithStdin("",
            "--vault", _vault.VaultPath, "capture");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("No content");
    }

    [Fact]
    public void Pipe_EmptyStdin_WithInlineArgs_UsesInlineOnly()
    {
        var result = CliRunner.RunWithStdin("",
            "--vault", _vault.VaultPath, "capture", "t: inline task only");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
        result.StdOut.Should().NotContain("linked page");
    }

    [Fact]
    public void File_LargeContent_CreatesSubpage()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Item 1: Database migration\nItem 2: API refactor\nItem 3: Frontend update\nItem 4: Testing");
            var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "--file", tempFile, "t: sprint backlog");

            result.ExitCode.Should().Be(0);
            result.StdOut.Should().Contain("Saved task");
            result.StdOut.Should().Contain("with linked page");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void File_NonexistentPath_ShowsError()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "--file",
            Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".txt"), "test");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("File not found");
    }

    [Fact]
    public void File_ShortContent_StaysInline()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Just one line");
            var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "--file", tempFile, "quick note");

            result.ExitCode.Should().Be(0);
            result.StdOut.Should().NotContain("linked page");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Pipe_LargeContent_EntryHasWikiLink()
    {
        var pipedContent = "Heading line\nDetail one\nDetail two\nDetail three";
        var result = CliRunner.RunWithStdin(pipedContent,
            "--vault", _vault.VaultPath, "capture", "d: design doc", "-");

        result.ExitCode.Should().Be(0);

        // Verify entry in cache contains wiki-link
        var cache = new EntryCache(_vault.VaultPath);
        cache.StartWarmup();
        if (cache.WarmupTask != null) await cache.WarmupTask;
        var searchResult = new SearchService(cache).Search(new SearchQuery { Types = new[] { EntryType.Decision } });
        searchResult.Entries.Should().NotBeEmpty();
        searchResult.Entries[0].Entry.Content.Should().Contain("[[");
    }
}
