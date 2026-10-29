using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using Xunit;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

public class WritePageToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly WritePageTool _tool;

    public WritePageToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        var subpageWriter = new SubpageWriter(_testVaultPath);
        var entryPageLinker = new EntryPageLinker(_testVaultPath, subpageWriter);

        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            SubpageWriter = subpageWriter,
            EntryPageLinker = entryPageLinker
        };
        _tool = new WritePageTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    [Fact]
    public async Task Execute_BasicPage_CreatesFileAtCorrectPath()
    {
        // Arrange
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-my-first-idea.md";
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"ideas\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# My First Idea\\n\\nSome content here.\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        var expectedPath = Path.Combine(_testVaultPath, "logmark", "_inbox", "_ideas", "pages", filename);
        File.Exists(expectedPath).Should().BeTrue($"expected file at {expectedPath}");

        var fileContent = File.ReadAllText(expectedPath);
        fileContent.Should().Contain("# My First Idea");
        fileContent.Should().Contain("Some content here.");
    }

    [Fact]
    public async Task Execute_PageWithRoute_CreatesFileAtRoutedPath()
    {
        // Arrange
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-research-notes.md";
        var args = new Dictionary<string, object?>
        {
            ["route"] = JsonDocument.Parse("\"ai-tools\"").RootElement,
            ["type"] = JsonDocument.Parse("\"diary\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# Research Notes\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        var expectedPath = Path.Combine(_testVaultPath, "logmark", "ai-tools", "_diary", "pages", filename);
        File.Exists(expectedPath).Should().BeTrue($"expected file at {expectedPath}");
    }

    [Fact]
    public async Task Execute_ReturnsWikiPathInResult()
    {
        // Arrange
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-fix-bug.md";
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"tasks\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# Fix Bug\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text!;

        json.Should().Contain("path");
        json.Should().Contain($"logmark/_inbox/_tasks/pages/{today}-fix-bug");
    }

    [Fact]
    public async Task Execute_ReturnsFilenameInResult()
    {
        // Arrange
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-fix-bug.md";
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"tasks\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# Fix Bug\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text!;
        json.Should().Contain("filename");
        json.Should().Contain(filename);
    }

    [Fact]
    public async Task Execute_MissingTypeName_ReturnsError()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            ["filename"] = JsonDocument.Parse("\"my-file.md\"").RootElement,
            ["content"] = JsonDocument.Parse("\"Some content\"").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("type");
    }

    [Fact]
    public async Task Execute_MissingFilename_ReturnsError()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"ideas\"").RootElement,
            ["content"] = JsonDocument.Parse("\"Some content\"").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("filename");
    }

    [Fact]
    public async Task Execute_MissingContent_ReturnsError()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"ideas\"").RootElement,
            ["filename"] = JsonDocument.Parse("\"my-file.md\"").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("content");
    }

    [Fact]
    public async Task Execute_NullArguments_ReturnsError()
    {
        // Act
        var result = await _tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_InvalidType_ReturnsError()
    {
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"bogus\"").RootElement,
            ["filename"] = JsonDocument.Parse("\"x.md\"").RootElement,
            ["content"] = JsonDocument.Parse("\"hi\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("bogus");
    }

    [Fact]
    public async Task Execute_TypeLog_WritesToDiaryFolder()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-x.md";
        var args = new Dictionary<string, object?>
        {
            ["route"] = JsonDocument.Parse("\"projects/foo\"").RootElement,
            ["type"] = JsonDocument.Parse("\"log\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# x\"").RootElement,
            ["unlinked"] = JsonDocument.Parse("true").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var expected = Path.Combine(_testVaultPath, "logmark", "projects", "foo", "_diary", "pages", filename);
        File.Exists(expected).Should().BeTrue($"expected at {expected}");
    }

    [Fact]
    public async Task Execute_NoEntryIdAndNotUnlinked_ReturnsError()
    {
        var args = new Dictionary<string, object?>
        {
            ["type"] = JsonDocument.Parse("\"decisions\"").RootElement,
            ["filename"] = JsonDocument.Parse("\"x.md\"").RootElement,
            ["content"] = JsonDocument.Parse("\"hi\"").RootElement
            // no entry_id, no unlinked
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("entry_id"); // error must mention either of the two paths
    }

    [Fact]
    public async Task Execute_WithEntryId_LinksToExistingEntry()
    {
        // Seed an entry the tool can find.
        var diary = Path.Combine(_testVaultPath, "logmark", "projects", "foo", "_diary");
        Directory.CreateDirectory(diary);
        File.WriteAllText(Path.Combine(diary, "2026.md"),
            "## 2026-04-30 10:00 | MCP\nSee brief.\n_id: cafef00d ^cafef00d\n");

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-brief.md";
        var args = new Dictionary<string, object?>
        {
            ["route"] = JsonDocument.Parse("\"projects/foo\"").RootElement,
            ["type"] = JsonDocument.Parse("\"decisions\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# Brief body\"").RootElement,
            ["entry_id"] = JsonDocument.Parse("\"cafef00d\"").RootElement,
            ["display_text"] = JsonDocument.Parse("\"Phase 2 Brief\"").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var diaryContent = File.ReadAllText(Path.Combine(diary, "2026.md"));
        diaryContent.Should().Contain("|Phase 2 Brief]]");
    }

    [Fact]
    public async Task Execute_WithUnknownEntryId_ReturnsErrorAndDoesNotWritePage()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-orphan.md";
        var args = new Dictionary<string, object?>
        {
            ["route"] = JsonDocument.Parse("\"projects/foo\"").RootElement,
            ["type"] = JsonDocument.Parse("\"decisions\"").RootElement,
            ["filename"] = JsonDocument.Parse($"\"{filename}\"").RootElement,
            ["content"] = JsonDocument.Parse("\"# x\"").RootElement,
            ["entry_id"] = JsonDocument.Parse("\"deadbeef\"").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("deadbeef");
        var pagePath = Path.Combine(_testVaultPath, "logmark", "projects", "foo", "_decisions", "pages", filename);
        File.Exists(pagePath).Should().BeFalse("page must not be written when target entry does not exist");
    }
}
