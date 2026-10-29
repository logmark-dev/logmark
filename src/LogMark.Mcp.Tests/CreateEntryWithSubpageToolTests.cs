using System.Text.Json;
using FluentAssertions;
using LogMark.Interfaces;
using LogMark.Mcp.Tools;
using LogMark.Models;
using LogMark.Services;
using Xunit;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

public class CreateEntryWithSubpageToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly CreateEntryWithSubpageTool _tool;

    public CreateEntryWithSubpageToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-create-with-subpage-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Create routes file with one project route used in tests
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath,
            "# Routes\n\n## Projects\n\n### foo\n- path: projects/foo\n\n## Domains\n");

        var notationParser = new NotationParser();
        var systemLogger = new SystemLogger(_testVaultPath);
        var vaultWriter = new VaultWriter();
        var vaultService = new VaultService(_testVaultPath, notationParser, vaultWriter: vaultWriter);

        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService(), vaultWriter: vaultWriter);
        routeService.LoadRoutes();

        var sessionService = new SessionService(_testVaultPath, systemLogger, vaultWriter: vaultWriter);
        var sessionContext = new McpSessionContext(sessionService, systemLogger, "test-agent", "general");

        var subpageWriter = new SubpageWriter(_testVaultPath, vaultWriter: vaultWriter);
        var entryPageLinker = new EntryPageLinker(_testVaultPath, subpageWriter, vaultWriter);

        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultWriter = vaultWriter,
            VaultService = vaultService,
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = sessionService,
            SessionContext = sessionContext,
            SubpageWriter = subpageWriter,
            EntryPageLinker = entryPageLinker
        };
        _tool = new CreateEntryWithSubpageTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    [Fact]
    public async Task Execute_ProducesEntry_Subpage_AndLink_Atomically()
    {
        var args = new Dictionary<string, object?>
        {
            ["entry_content"] = JsonDocument.Parse("\"+foo Phase 2 alpha validation complete. See brief and results.\"").RootElement,
            ["subpage"] = JsonDocument.Parse(@"{
                ""type"": ""decisions"",
                ""filename"": ""2026-04-30-brief.md"",
                ""content"": ""# Brief body"",
                ""display_text"": ""Phase 2 Brief""
            }").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();

        var pageFile = Directory.EnumerateFiles(_testVaultPath, "2026-04-30-brief.md", SearchOption.AllDirectories).FirstOrDefault();
        pageFile.Should().NotBeNull();

        // Diary entry contains the wiki link
        var diaryFiles = Directory.EnumerateFiles(_testVaultPath, "*.md", SearchOption.AllDirectories)
            .Where(f => f.Contains("_diary"));
        var diaryContent = string.Concat(diaryFiles.Select(File.ReadAllText));
        diaryContent.Should().Contain("|Phase 2 Brief]]");
    }

    [Fact]
    public async Task Execute_InvalidSubpageType_ReturnsErrorAndCreatesNothing()
    {
        var args = new Dictionary<string, object?>
        {
            ["entry_content"] = JsonDocument.Parse("\"+foo a thing\"").RootElement,
            ["subpage"] = JsonDocument.Parse(@"{
                ""type"": ""bogus"",
                ""filename"": ""x.md"",
                ""content"": ""hi""
            }").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("bogus");

        // No entry or page produced. Filter out system bookkeeping files (routes.md, _system/log.md)
        // that exist before the call -- the assertion is "the call wrote nothing".
        var logmarkDir = Path.Combine(_testVaultPath, "logmark");
        var mdFiles = Directory.Exists(logmarkDir)
            ? Directory.EnumerateFiles(logmarkDir, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_system" + Path.DirectorySeparatorChar))
            : Enumerable.Empty<string>();
        mdFiles.Should().BeEmpty(
            "no entry should be created when the subpage spec is invalid");
    }

    [Fact]
    public async Task Execute_MissingSubpage_ReturnsError()
    {
        var args = new Dictionary<string, object?>
        {
            ["entry_content"] = JsonDocument.Parse("\"+foo a thing\"").RootElement
            // no subpage
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("subpage");
    }

    private class TestRoutePromptService : IRoutePromptService
    {
        public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
        {
            var path = options.DefaultPath ?? $"projects/{routeName}";
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
        }
    }
}
