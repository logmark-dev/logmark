using System.Text.Json;
using FluentAssertions;
using LogMark.Interfaces;
using LogMark.Mcp.Tools;
using LogMark.Models;
using LogMark.Services;
using Xunit;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

/// <summary>
/// End-to-end repro mirroring the 2026-04-29 quant-trading-platform capture
/// that motivated the vault-write hardening sprint. Confirms all five sprint
/// defects are structurally impossible after the fixes:
///   1. No invented `_log/` folder (T1+T2 type validation)
///   2. Page lands at canonical `_decisions/pages/`
///   3. Em-dashes normalized to `--` on disk (T8 HouseStyleNormalizer)
///   4. No YAML frontmatter on the page (T3 frontmatter param removed)
///   5. Diary entry contains a wiki-link to the subpage (T6 atomicity)
///
/// T9's encoding fix is exercised by McpServerEncodingTests; not duplicated here.
/// Routes-dedup (T10/T11/T12) is tested directly in route-specific test files.
/// </summary>
public class KakushadzeRepro : IDisposable
{
    private readonly string _vault;
    private readonly CreateEntryWithSubpageTool _tool;

    public KakushadzeRepro()
    {
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-kakushadze-{Guid.NewGuid()}");
        Directory.CreateDirectory(_vault);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_vault, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Create routes file with the +foo project route used in the repro.
        var routesPath = Path.Combine(_vault, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath,
            "# Routes\n\n## Projects\n\n### foo\n- path: projects/foo\n\n## Domains\n");

        var notationParser = new NotationParser();
        var systemLogger = new SystemLogger(_vault);
        var vaultWriter = new VaultWriter();
        var vaultService = new VaultService(_vault, notationParser, vaultWriter: vaultWriter);

        var routeService = new RouteService(_vault, systemLogger, new TestRoutePromptService(), vaultWriter: vaultWriter);
        routeService.LoadRoutes();

        var sessionService = new SessionService(_vault, systemLogger, vaultWriter: vaultWriter);
        var sessionContext = new McpSessionContext(sessionService, systemLogger, "test-agent", "general");

        var subpageWriter = new SubpageWriter(_vault, vaultWriter: vaultWriter);
        var entryPageLinker = new EntryPageLinker(_vault, subpageWriter, vaultWriter);

        var services = new VaultServices
        {
            VaultPath = _vault,
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

    [Fact]
    public async Task KakushadzeFlow_ProducesLinkedNonOrphanedPages_NoLogFolder_NoFrontmatter()
    {
        var args = new Dictionary<string, object?>
        {
            ["entry_content"] = JsonDocument.Parse("\"+foo log: Phase 2 cross-sectional alpha validation complete. See brief and results.\"").RootElement,
            ["subpage"] = JsonDocument.Parse(@"{
                ""type"": ""decisions"",
                ""filename"": ""2026-04-30-kakushadze-phase2-brief.md"",
                ""content"": ""# Brief body\nPhase 2 — cross-sectional"",
                ""display_text"": ""Phase 2 Brief""
            }").RootElement
        };

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: "the kakushadze flow should succeed end-to-end");

        // 1. _log/ folder MUST NOT exist anywhere in the vault.
        Directory.EnumerateDirectories(_vault, "_log", SearchOption.AllDirectories).Should().BeEmpty(
            because: "T1+T2 should have rejected the legacy _log folder name");

        // 2. The page exists under _decisions/pages/ with normalized em-dash.
        var pageFile = Directory.EnumerateFiles(_vault, "*kakushadze*brief*.md", SearchOption.AllDirectories).Single();
        pageFile.Should().Contain("_decisions" + Path.DirectorySeparatorChar + "pages",
            because: "the canonical decisions page folder is _decisions/pages/");
        var pageContent = File.ReadAllText(pageFile);
        pageContent.Should().Contain("Phase 2 -- cross-sectional",
            because: "T8 HouseStyleNormalizer should have replaced the em-dash with --");
        pageContent.Should().NotContain("—",
            because: "no em-dashes should survive the normalizer");

        // 3. No YAML frontmatter.
        pageContent.Should().NotStartWith("---\n",
            because: "T3 dropped the frontmatter param; pages must not start with YAML frontmatter");

        // 4. Diary entry contains the wikilink with the display text.
        var diaryFile = Directory.EnumerateFiles(_vault, "*.md", SearchOption.AllDirectories)
            .First(f => f.Contains("_diary"));
        var diaryContent = File.ReadAllText(diaryFile);
        diaryContent.Should().Contain("|Phase 2 Brief]]",
            because: "T6 atomicity should have inserted the wikilink with the supplied display text");
        diaryContent.Should().Contain("kakushadze-phase2-brief",
            because: "the wikilink target should be the subpage path");
    }

    public void Dispose()
    {
        if (Directory.Exists(_vault))
            Directory.Delete(_vault, recursive: true);
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
