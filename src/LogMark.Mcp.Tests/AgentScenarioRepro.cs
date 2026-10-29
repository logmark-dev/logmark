using System.Text.Json;
using FluentAssertions;
using LogMark.Interfaces;
using LogMark.Mcp.Tools;
using LogMark.Models;
using LogMark.Services;
using Xunit;
using Xunit.Abstractions;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Repro for the 2026-05-02 agent capture in fin-inv/trade-ideas:
/// agent fired 3 MCP calls (1x create_entry_with_subpage + 2x create_entry),
/// vault ended with 6 entries, each appearing in BOTH the scoped folder
/// (domains/finance/investing/trade-ideas/_*) and the parent folder
/// (domains/finance/investing/_*).
///
/// Goal: isolate which call/path produces the duplication so the actual
/// fix can be scoped tightly.
/// </summary>
public class AgentScenarioRepro : IDisposable
{
    private readonly string _vault;
    private readonly CreateEntryTool _createEntry;
    private readonly CreateEntryWithSubpageTool _composite;
    private readonly ITestOutputHelper _output;

    public AgentScenarioRepro(ITestOutputHelper output)
    {
        _output = output;
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-agentscenario-{Guid.NewGuid()}");
        Directory.CreateDirectory(_vault);

        var logmarkDir = Path.Combine(_vault, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Mirror routes.md: alias `fin-inv` -> path `domains/finance/investing`
        // with `trade-ideas` scope listed multiple times to mirror real vault.
        var routesPath = Path.Combine(_vault, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath,
            "# Routes\n\n## Domains\n\n" +
            "### fin-inv\n" +
            "- path: domains/finance/investing\n" +
            "- description: Investment analysis, market research\n" +
            "- scopes:\n" +
            "  - market-analysis:\n" +
            "  - trade-ideas:\n" +
            "  - trade-ideas:\n" +
            "  - market-analysis:\n");

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
        _createEntry = new CreateEntryTool(services);
        _composite = new CreateEntryWithSubpageTool(services);
    }

    [Fact]
    public async Task SimpleCreateEntry_WithRouteAndScope_WritesExactlyOneFile()
    {
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"Reconciliation tension between SaaS counter-trade narrative and crisis-cycle deployment\"").RootElement,
            ["route"] = JsonDocument.Parse("\"fin-inv\"").RootElement,
            ["scope"] = JsonDocument.Parse("\"trade-ideas\"").RootElement,
            ["entry_type"] = JsonDocument.Parse("\"gap\"").RootElement
        };

        var result = await _createEntry.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        DumpVaultTree("After single create_entry");

        var allYearMd = Directory.EnumerateFiles(_vault, "2026.md", SearchOption.AllDirectories).ToList();
        allYearMd.Should().HaveCount(1, because: "exactly one entry-rolling-log file should exist after one create_entry call");
    }

    [Fact]
    public async Task CreateEntry_RouteAndScopeParams_PlusBareRouteInContent_DoublewritesScopedAndParent()
    {
        // Reproduces 2026-05-02 production: agent passed route="fin-inv", scope="trade-ideas"
        // AND included a bare "+fin-inv" reference inside the entry_content. The contains-check
        // in CreateEntryTool only looks for the exact merged string, so it prepends
        // "+fin-inv/trade-ideas" alongside the bare "+fin-inv" already in content.
        // entry.Projects then has BOTH and RouteEntryAsync writes twice.
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"g: Reconciliation tension. cross-ref +fin-inv standalone mention.\"").RootElement,
            ["route"] = JsonDocument.Parse("\"fin-inv\"").RootElement,
            ["scope"] = JsonDocument.Parse("\"trade-ideas\"").RootElement
        };

        var result = await _createEntry.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        DumpVaultTree("After create_entry with route+scope+bare-route-in-content");

        var allGapsYear = Directory.EnumerateFiles(_vault, "2026.md", SearchOption.AllDirectories)
            .Where(p => p.Contains("_gaps")).ToList();
        allGapsYear.Should().HaveCount(1,
            because: "exactly one rolling-log file should exist; agents must not double-write to scoped+parent when route+scope params are provided");
        allGapsYear.Single().Replace('\\', '/').Should().Contain("trade-ideas/_gaps",
            because: "the surviving destination must be the most-specific (scoped) one, not the parent route folder");
    }

    [Fact]
    public async Task SimpleCreateEntry_IdeaPrefix_WithGapTokenInBody_RoutesToIdeasNotGaps()
    {
        // Mirror the actual problematic content: starts with "i:" but mentions "gap:b9477bf7" later.
        var content = "i: SaaS narrative-arb watchlist seed -- top three picks. Refs gap:b9477bf7 for unresolved framework-tension.";

        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse(JsonSerializer.Serialize(content)).RootElement,
            ["route"] = JsonDocument.Parse("\"fin-inv\"").RootElement,
            ["scope"] = JsonDocument.Parse("\"trade-ideas\"").RootElement
        };

        var result = await _createEntry.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        DumpVaultTree("After i: with gap:ref body");

        var ideasFiles = Directory.EnumerateFiles(_vault, "*.md", SearchOption.AllDirectories)
            .Where(f => f.Contains("_ideas")).ToList();
        var gapsFiles = Directory.EnumerateFiles(_vault, "*.md", SearchOption.AllDirectories)
            .Where(f => f.Contains("_gaps")).ToList();

        gapsFiles.Should().BeEmpty(because: "an entry starting with 'i:' must not be classified as a gap due to a later 'gap:b9477bf7' reference token");
        ideasFiles.Should().HaveCount(1, because: "the entry should land in _ideas/ exactly once");
    }

    [Fact]
    public async Task CompositeCreateEntryWithSubpage_PageLandsInSameRouteSubtreeAsEntry()
    {
        var args = new Dictionary<string, object?>
        {
            ["entry_content"] = JsonDocument.Parse("\"log: SaaS counter-trade thesis review complete\"").RootElement,
            ["route"] = JsonDocument.Parse("\"fin-inv\"").RootElement,
            ["scope"] = JsonDocument.Parse("\"trade-ideas\"").RootElement,
            ["subpage"] = JsonDocument.Parse(@"{
                ""type"": ""diary"",
                ""filename"": ""2026-05-02-thesis-evaluation.md"",
                ""content"": ""# Thesis Evaluation\n\nBody."",
                ""display_text"": ""Thesis Evaluation""
            }").RootElement
        };

        var result = await _composite.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        DumpVaultTree("After composite create_entry_with_subpage");

        var entryFiles = Directory.EnumerateFiles(_vault, "2026.md", SearchOption.AllDirectories).ToList();
        var pageFiles = Directory.EnumerateFiles(_vault, "*thesis-evaluation*.md", SearchOption.AllDirectories).ToList();

        entryFiles.Should().HaveCount(1, because: "exactly one rolling-log file");
        pageFiles.Should().HaveCount(1, because: "exactly one page file");

        // The page must live under the same route subtree as the entry.
        // Entry under domains/finance/investing/... -> page must too.
        var entryPath = entryFiles.Single();
        var pagePath = pageFiles.Single();
        var entryUnderInvesting = entryPath.Replace('\\', '/').Contains("domains/finance/investing/");
        var pageUnderInvesting = pagePath.Replace('\\', '/').Contains("domains/finance/investing/");

        entryUnderInvesting.Should().BeTrue($"entry should be under resolved route path; actual: {entryPath}");
        pageUnderInvesting.Should().BeTrue($"page should be under resolved route path (not the alias name); actual: {pagePath}");
    }

    private void DumpVaultTree(string label)
    {
        _output.WriteLine($"=== {label} ===");
        foreach (var f in Directory.EnumerateFiles(_vault, "*.md", SearchOption.AllDirectories).OrderBy(s => s))
        {
            var rel = Path.GetRelativePath(_vault, f);
            _output.WriteLine($"  {rel}");
        }
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
