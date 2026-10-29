using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

public class ContextToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly ReadEntryTool _readEntryTool;
    private readonly GetDecisionsTool _getDecisionsTool;
    private readonly GetTasksTool _getTasksTool;
    private readonly GetBlocksTool _getBlocksTool;
    private readonly GetProjectContextTool _getProjectContextTool;
    private readonly EntryCache _cache;

    public ContextToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-context-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Create routes file with test project
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath,
            "# Routes\n\n## Projects\n\n### testproject\n- path: projects/testproject\n\n" +
            "### untaggedproject\n- path: projects/untaggedproject\n\n" +
            "### manydecisions\n- path: projects/manydecisions\n\n## Domains\n");

        // Create test data
        SetupTestData();

        // Create entry cache and search service for the tools
        var entryCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        entryCache.StartWarmup();
        // Wait for warmup to complete so tests have data
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!entryCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);
        var searchService = new SearchService(entryCache);
        var entryResolver = new EntryResolver(entryCache, _testVaultPath);
        _cache = entryCache;

        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            SearchService = searchService,
            EntryResolver = entryResolver
        };
        _readEntryTool = new ReadEntryTool(services);
        _getDecisionsTool = new GetDecisionsTool(services);
        _getTasksTool = new GetTasksTool(services);
        _getBlocksTool = new GetBlocksTool(services);
        _getProjectContextTool = new GetProjectContextTool(services);
    }

    private void SetupTestData()
    {
        // Create decisions
        var decisionsDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_decisions");
        Directory.CreateDirectory(decisionsDir);
        File.WriteAllText(Path.Combine(decisionsDir, "2025.md"), """
            ## 2025-06-15 10:00 | Architecture
            Using WPF for Windows-first experience #frozen #architecture
            source: backfill:CLAUDE.md

            ## 2025-07-01 14:00 | Core
            Using SQLite for local storage #frozen #database
            source: backfill:CLAUDE.md

            ## 2025-08-01 09:00 | UX
            Debug hotkey is Ctrl+Backspace #active
            source: manual
            """);

        // Create decisions with scope
        var scopeDecisionsDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "ux", "_decisions");
        Directory.CreateDirectory(scopeDecisionsDir);
        File.WriteAllText(Path.Combine(scopeDecisionsDir, "2025.md"), """
            ## 2025-06-20 11:00 | UX Scope
            Dark theme by default #frozen #ux
            source: backfill:philosophy.md
            """);

        // Create tasks
        var tasksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_tasks");
        Directory.CreateDirectory(tasksDir);
        File.WriteAllText(Path.Combine(tasksDir, "2026.md"), """
            ## 2026-02-01 09:00 | Development
            Implement MCP server
            _task: 2026-02-15, 12:00 | postponed: 0 | a1b2c3d4 ^a1b2c3d4

            ## 2026-02-02 10:00 | Testing
            ~~Write unit tests~~
            ~~_task: none | postponed: 0 | b2c3d4e5 ^b2c3d4e5~~

            ## 2026-02-03 11:00 | Superseded
            Replaced by a corrected task
            _task: none | postponed: 0 | c3d4e5f6 ^c3d4e5f6
            resolved: Superseded, not completed.

            #dismissed
            """);

        // Create blocks
        var blocksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_blocks");
        Directory.CreateDirectory(blocksDir);
        File.WriteAllText(Path.Combine(blocksDir, "2026.md"), """
            ## 2026-01-15 16:00 | Blocker
            Rate limiting implementation unclear #needs-research

            ## 2026-01-20 11:00 | Resolved
            Build failing on CI #resolved

            ## 2026-01-22 09:00 | SecondBlocker
            API contract still undecided for the sync endpoint

            ## 2026-01-25 09:00 | Dismissed
            Duplicate of an earlier block

            #dismissed
            """);

        // Decisions with no status tag at all (lmk-distill-decisions-carry-no-status-tag-0bs6):
        // simulates a route whose decisions were all written by session-distill,
        // which never tags. Exercises GetProjectContextTool's untagged-decisions
        // fallback (Task 7 - only two rows, so a tag-filtered read returns none).
        var untaggedDecisionsDir = Path.Combine(_testVaultPath, "logmark", "projects", "untaggedproject", "_decisions");
        Directory.CreateDirectory(untaggedDecisionsDir);
        File.WriteAllText(Path.Combine(untaggedDecisionsDir, "2026.md"), """
            ## 2026-03-01 09:00 | Parser
            Chose the streaming parser over the batch one
            source: agent:session-distill

            ## 2026-03-02 10:00 | Storage
            Chose flat files over a database for the cache
            source: agent:session-distill
            """);

        // The live-vault shape the fallback actually meets: projects/LogMark
        // holds 165 untagged decisions across 66KB, all root-scoped. Two rows
        // (above) cannot show whether the fallback caps or dumps. Fourteen
        // long-bodied rows can: the cap, the newest-N selection, the
        // ascending presentation order and the List() content truncation are
        // all only observable past the cap.
        var manyDecisionsDir = Path.Combine(_testVaultPath, "logmark", "projects", "manydecisions", "_decisions");
        Directory.CreateDirectory(manyDecisionsDir);
        var manyBody = new StringBuilder();
        for (var i = 1; i <= 14; i++)
        {
            manyBody.Append($"## 2026-04-{i:00} 09:00 | Choice{i:00}\n");
            manyBody.Append($"Decision number {i:00} taken by session-distill. ");
            manyBody.Append(new string('y', 240));
            manyBody.Append(" TAILMARKER\n");
            manyBody.Append("source: agent:session-distill\n\n");
        }
        File.WriteAllText(Path.Combine(manyDecisionsDir, "2026.md"), manyBody.ToString());

        // Create diary entries with a recent timestamp (within last 7 days)
        // so that the GetProjectContext recent_days filter always finds them
        var recentDate = DateTime.Today.AddDays(-2);
        var diaryDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_diary");
        Directory.CreateDirectory(diaryDir);
        File.WriteAllText(Path.Combine(diaryDir, $"{recentDate.Year}.md"),
            $"""
            ## {recentDate:yyyy-MM-dd} 14:00 | Session
            Working on Phase D implementation
            source: agent:claude-code
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    #region ReadEntryTool Tests

    [Fact]
    public async Task ReadEntry_ReturnsAllEntriesInFile()
    {
        // Arrange
        var args = CreateArgs(new { path = "logmark/projects/testproject/_decisions/2025.md" });

        // Act
        var result = await _readEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "3");
        json.Should().Contain("Using WPF");
        json.Should().Contain("Using SQLite");
        json.Should().Contain("Debug hotkey");
    }

    [Fact]
    public async Task ReadEntry_ByTimestamp_ReturnsSingleEntry()
    {
        // Arrange
        var args = CreateArgs(new
        {
            path = "logmark/projects/testproject/_decisions/2025.md",
            timestamp = "2025-07-01T14:00:00"
        });

        // Act
        var result = await _readEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Success", "true");
        json.Should().Contain("Using SQLite");
        json.Should().NotContain("Using WPF");
    }

    [Fact]
    public async Task ReadEntry_NonExistentFile_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new { path = "logmark/nonexistent/file.md" });

        // Act
        var result = await _readEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("File not found");
    }

    #endregion

    #region GetDecisionsTool Tests

    [Fact]
    public async Task GetDecisions_ReturnsDecisionsForRoute()
    {
        // Arrange
        var args = CreateArgs(new { route = "+testproject" });

        // Act
        var result = await _getDecisionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Route", "\"testproject\"");
        json.Should().Contain("Using WPF");
    }

    [Fact]
    public async Task GetDecisions_WithFrozenTag_FiltersCorrectly()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            status_tags = new[] { "#frozen" }
        });

        // Act
        var result = await _getDecisionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Using WPF");
        json.Should().Contain("Using SQLite");
        json.Should().NotContain("Debug hotkey"); // Not frozen
    }

    [Fact]
    public async Task GetDecisions_WithScope_FiltersToScope()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            scope = "ux"
        });

        // Act
        var result = await _getDecisionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Dark theme");
        json.Should().NotContain("Using WPF"); // Not in ux scope
    }

    [Fact]
    public async Task GetDecisions_MissingRoute_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new { scope = "ux" }); // Missing route

        // Act
        var result = await _getDecisionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("route");
    }

    #endregion

    #region GetTasksTool Tests

    [Fact]
    public async Task GetTasks_ReturnsActiveTasks()
    {
        // Arrange
        var args = CreateArgs(new { route = "testproject" });

        // Act
        var result = await _getTasksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Implement MCP server");
        json.Should().NotContain("Write unit tests"); // Completed
        json.Should().NotContain("Replaced by a corrected task"); // Dismissed
    }

    [Fact]
    public async Task GetTasks_IncludeCompleted_ReturnsAllTasks()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            include_completed = true
        });

        // Act
        var result = await _getTasksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Implement MCP server");
        json.Should().Contain("Write unit tests");
    }

    [Fact]
    public async Task GetTasks_IncludesDeadlineInfo()
    {
        // Arrange
        var args = CreateArgs(new { route = "testproject" });

        // Act
        var result = await _getTasksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("\"Deadline\"");
        json.Should().Contain("2026-02-15");
    }

    #endregion

    #region GetBlocksTool Tests

    [Fact]
    public async Task GetBlocks_ReturnsOpenBlocks()
    {
        // Arrange
        var args = CreateArgs(new { route = "testproject" });

        // Act
        var result = await _getBlocksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Rate limiting");
        json.Should().NotContain("Build failing"); // Resolved
    }

    [Fact]
    public async Task GetBlocks_IncludeResolved_ReturnsAllBlocks()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            include_resolved = true
        });

        // Act
        var result = await _getBlocksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("Rate limiting");
        json.Should().Contain("Build failing");
    }

    [Fact]
    public async Task GetBlocks_IncludesAgeDays()
    {
        // Arrange
        var args = CreateArgs(new { route = "testproject" });

        // Act
        var result = await _getBlocksTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("\"AgeDays\":");
        json.Should().Contain("\"OldestDays\":");
    }

    [Fact]
    public async Task GetBlocks_ExcludesDismissedByDefault()
    {
        // Arrange: SetupTestData's fixture blocks don't include a dismissed one,
        // so seed a dedicated file. The default listing must exclude #dismissed
        // the same way it already excludes #resolved - a dismissed block is
        // closed, not open.
        var blocksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_blocks");
        var filePath = Path.Combine(blocksDir, "2026-dismissed.md");
        File.WriteAllText(filePath, """
            ## 2026-02-01 08:00 | Abandoned
            Old blocker no longer applies #dismissed
            """);
        _cache.InvalidateFile(filePath);

        // Act
        var defaultResult = await _getBlocksTool.ExecuteAsync(
            CreateArgs(new { route = "testproject" }), CancellationToken.None);
        var includedResult = await _getBlocksTool.ExecuteAsync(
            CreateArgs(new { route = "testproject", include_resolved = true }), CancellationToken.None);

        // Assert
        defaultResult.IsError.Should().BeFalse();
        defaultResult.Content[0].Text.Should().NotContain("Old blocker no longer applies");

        includedResult.IsError.Should().BeFalse();
        includedResult.Content[0].Text.Should().Contain("Old blocker no longer applies");
    }

    [Fact]
    public async Task GetBlocks_IncludeResolved_ReturnsResolutionNoteVerbatim_AfterServiceResolve()
    {
        // Arrange: seed a block with an explicit "_id:" line so DecisionApprovalService
        // can locate it by shortId, then resolve it with a note through the service -
        // the same backing update_entry and the resolve_block verb use. This pins the
        // round trip: service-level resolve-with-note -> get_blocks(include_resolved)
        // surfaces the note verbatim.
        var blocksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_blocks");
        var filePath = Path.Combine(blocksDir, "2026-roundtrip.md");
        File.WriteAllText(filePath, """
            ## 2026-03-01 09:00 | RoundTrip
            Waiting on vendor API key
            _id: cafef00d ^cafef00d
            """);
        _cache.InvalidateFile(filePath);

        var approvals = new DecisionApprovalService(_cache, new VaultWriter(), new FileAccessor());
        var ok = await approvals.ResolveBlockAsync(filePath, "cafef00d", "vendor sent the key on slack");
        ok.Should().BeTrue();

        // Act
        var result = await _getBlocksTool.ExecuteAsync(
            CreateArgs(new { route = "testproject", include_resolved = true }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Content[0].Text.ShouldHaveJsonProperty("Resolution", "\"vendor sent the key on slack\"");
    }

    [Fact]
    public async Task GetBlocks_OldestDays_ReflectsFullOpenSetNotJustReturnedPage()
    {
        // Arrange: seed an open block older than everything else in the
        // fixture, then request a page of 1. Blocks sort newest-first, so
        // this ancient block is excluded from the returned page - but
        // OldestDays is documented (SearchResult.AllMatches) as a full-set
        // aggregate, not a property of whatever page came back. A regression
        // that goes back to computing OldestDays from the returned page would
        // report the age of "Rate limiting implementation unclear" (the
        // fixture's other open block, newer, and the one actually returned)
        // instead of this one.
        var blocksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "_blocks");
        var filePath = Path.Combine(blocksDir, "2026-ancient.md");
        var ancientTimestamp = new DateTime(2026, 1, 1, 8, 0, 0);
        File.WriteAllText(filePath, """
            ## 2026-01-01 08:00 | Ancient
            The oldest open block in the fixture
            """);
        _cache.InvalidateFile(filePath);

        var expectedOldestDays = (int)(DateTime.Now - ancientTimestamp).TotalDays;

        // Act
        var result = await _getBlocksTool.ExecuteAsync(
            CreateArgs(new { route = "testproject", limit = 1 }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text!;
        json.Should().NotContain("The oldest open block in the fixture"); // paged out
        json.ShouldHaveJsonProperty("OldestDays", expectedOldestDays.ToString());
    }

    #endregion

    #region GetProjectContextTool Tests

    [Fact]
    public async Task GetProjectContext_ReturnsAggregatedContext()
    {
        // Arrange
        var args = CreateArgs(new { route = "+testproject" });

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Route", "\"testproject\"");
        json.Should().Contain("\"Summary\"");
        json.Should().Contain("\"TotalDecisions\"");
        json.Should().Contain("\"FrozenDecisions\"");
        json.Should().Contain("\"ActiveTasks\"");
        json.Should().Contain("\"OpenBlocks\"");
    }

    [Fact]
    public async Task GetProjectContext_DiscoversScopesAutomatically()
    {
        // Arrange
        var args = CreateArgs(new { route = "testproject" });

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Scope", "\"ux\"");
    }

    [Fact]
    public async Task GetProjectContext_IncludesRecentEntries()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            include_recent = true,
            recent_days = 30
        });

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("\"RecentEntries\"");
        json.Should().Contain("Working on Phase D");
    }

    [Fact]
    public async Task GetProjectContext_FiltersByStatusTags()
    {
        // Arrange
        var args = CreateArgs(new
        {
            route = "testproject",
            status_tags = new[] { "#frozen" }
        });

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        // Only frozen decisions should be included
        json.Should().Contain("Using WPF");
        json.Should().NotContain("Debug hotkey"); // Not frozen
    }

    [Fact]
    public async Task GetProjectContext_MissingRoute_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new { scopes = new[] { "ux" } }); // Missing route

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("route");
    }

    [Fact]
    public async Task GetProjectContext_CustomSectionPath_DiscoversScopesCorrectly()
    {
        // Arrange - Create route with custom path (areas/health instead of projects/health)
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath, """
            # Routes

            ## Areas

            ### health
            - path: areas/health
            - description: Health and wellness tracking

            ## Projects
            """);

        // Create a scope folder under the custom path
        var scopePath = Path.Combine(_testVaultPath, "logmark", "areas", "health", "fitness", "_decisions");
        Directory.CreateDirectory(scopePath);
        File.WriteAllText(Path.Combine(scopePath, "2026.md"), """
            ## 2026-01-10 10:00 | Fitness
            Track workouts weekly #frozen
            """);

        // Re-create cache/service after modifying routes.md and adding files
        var freshCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        freshCache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!freshCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);
        var freshSearch = new SearchService(freshCache);
        var freshResolver = new EntryResolver(freshCache, _testVaultPath);
        var freshServices = new VaultServices
        {
            VaultPath = _testVaultPath,
            SearchService = freshSearch,
            EntryResolver = freshResolver
        };
        var freshTool = new GetProjectContextTool(freshServices);

        var args = CreateArgs(new { route = "health" });

        // Act
        var result = await freshTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Scope", "\"fitness\"");
        json.Should().Contain("Track workouts");
    }

    [Fact]
    public async Task GetProjectContext_ConfiguredScopes_PrefersConfiguredOverDiscovered()
    {
        // Arrange - Create route with configured scopes
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath, """
            # Routes

            ## Projects

            ### configuredproject
            - path: projects/configuredproject
            - scopes:
              - api: API design and contracts
              - infra: Infrastructure decisions
            """);

        // Create the route folder but with different subfolders
        var projectPath = Path.Combine(_testVaultPath, "logmark", "projects", "configuredproject");
        Directory.CreateDirectory(Path.Combine(projectPath, "api", "_decisions"));
        Directory.CreateDirectory(Path.Combine(projectPath, "random-folder")); // This should not appear

        // Re-create tool with fresh cache (routes.md was modified after constructor warmup)
        var freshCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        freshCache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!freshCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);
        var freshSearch = new SearchService(freshCache);
        var freshResolver = new EntryResolver(freshCache, _testVaultPath);
        var freshServices = new VaultServices
        {
            VaultPath = _testVaultPath,
            SearchService = freshSearch,
            EntryResolver = freshResolver
        };
        var freshTool = new GetProjectContextTool(freshServices);

        var args = CreateArgs(new { route = "configuredproject" });

        // Act
        var result = await freshTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        // Should use configured scopes (api, infra) not discovered ones (api, random-folder)
        json.ShouldHaveJsonProperty("Scope", "\"api\"");
        json.ShouldHaveJsonProperty("Scope", "\"infra\"");
        json.Should().NotContain("random-folder");
    }

    [Fact]
    public async Task GetProjectContext_ExcludesDismissedTasksAndBlocks()
    {
        var args = CreateArgs(new { route = "+testproject" });

        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        var json = result.Content[0].Text;
        json.Should().NotContain("c3d4e5f6");
        json.Should().NotContain("Duplicate of an earlier block");
    }

    [Fact]
    public async Task GetProjectContext_AgreesWithGetBlocks()
    {
        // lmk-nnd5 observed get_blocks returning 6 for +logmark where
        // get_project_context returned 9. The two must return the same set.
        var contextResult = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+testproject" }), CancellationToken.None);
        var blocksResult = await _getBlocksTool.ExecuteAsync(
            CreateArgs(new { route = "+testproject" }), CancellationToken.None);

        var contextJson = contextResult.Content[0].Text!;
        var blocksJson = blocksResult.Content[0].Text!;

        // Both genuinely open blocks appear in both readers; neither closed
        // block appears in either.
        contextJson.Should().Contain("Rate limiting implementation unclear");
        blocksJson.Should().Contain("Rate limiting implementation unclear");
        contextJson.Should().Contain("API contract still undecided");
        blocksJson.Should().Contain("API contract still undecided");
        contextJson.Should().NotContain("Build failing on CI");
        blocksJson.Should().NotContain("Build failing on CI");
    }

    [Fact]
    public async Task GetProjectContext_BlocksAreNewestFirst()
    {
        // Arrange: the fixture carries two genuinely open blocks -
        // "Rate limiting implementation unclear" (2026-01-15) and
        // "API contract still undecided..." (2026-01-22, newer). Asserting
        // presence alone cannot distinguish ascending from descending sort;
        // this pins actual array position.
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+testproject" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var openBlockContents = doc.RootElement.GetProperty("OpenBlocks").EnumerateArray()
            .Select(b => b.GetProperty("Content").GetString())
            .ToList();

        var newerIndex = openBlockContents.FindIndex(c => c!.Contains("API contract still undecided"));
        var olderIndex = openBlockContents.FindIndex(c => c!.Contains("Rate limiting implementation unclear"));

        newerIndex.Should().BeGreaterOrEqualTo(0);
        olderIndex.Should().BeGreaterOrEqualTo(0);
        newerIndex.Should().BeLessThan(olderIndex,
            "newest-first sort must place the 2026-01-22 block before the 2026-01-15 block");
    }

    [Fact]
    public async Task GetProjectContext_ScopeHasBlocksExcludesDismissed()
    {
        // Arrange: the ux scope's only block is dismissed. HasBlocks must
        // read false for it - before this fix the existence probe checked
        // only IsCompleted=false, so a scope holding nothing but a dismissed
        // block still reported HasBlocks: true while OpenBlocks (correctly)
        // omitted it, an inconsistency inside a single response.
        var uxBlocksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "ux", "_blocks");
        Directory.CreateDirectory(uxBlocksDir);
        var filePath = Path.Combine(uxBlocksDir, "2026.md");
        File.WriteAllText(filePath, """
            ## 2026-02-05 10:00 | UX Blocker
            Dismissed ux-scoped block

            #dismissed
            """);
        _cache.InvalidateFile(filePath);

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+testproject" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var uxScope = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Single(s => s.GetProperty("Scope").GetString() == "ux");
        uxScope.GetProperty("HasBlocks").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetProjectContext_ScopeHasActiveTasksExcludesDismissed()
    {
        // Arrange: the ux scope's only task is dismissed. HasActiveTasks must
        // read false for it - mirrors GetProjectContext_ScopeHasBlocksExcludesDismissed
        // for the sibling probe; before this fix it checked only
        // IsCompleted=false, so a scope holding nothing but a dismissed task
        // still reported HasActiveTasks: true.
        var uxTasksDir = Path.Combine(_testVaultPath, "logmark", "projects", "testproject", "ux", "_tasks");
        Directory.CreateDirectory(uxTasksDir);
        var filePath = Path.Combine(uxTasksDir, "2026.md");
        File.WriteAllText(filePath, """
            ## 2026-02-06 10:00 | UX Task
            Dismissed ux-scoped task
            _task: none | postponed: 0 | d4e5f6a7 ^d4e5f6a7
            resolved: Superseded, not completed.

            #dismissed
            """);
        _cache.InvalidateFile(filePath);

        // Act
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+testproject" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var uxScope = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Single(s => s.GetProperty("Scope").GetString() == "ux");
        uxScope.GetProperty("HasActiveTasks").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetProjectContext_FallsBackToUntaggedDecisions_WhenTaggedSetIsNearEmpty()
    {
        // Live vault: 164 of 165 decisions were written by session-distill with
        // no status tag, so the tag-filtered read returns one row
        // (lmk-distill-decisions-carry-no-status-tag-0bs6).
        var args = CreateArgs(new { route = "+untaggedproject" });

        var result = await _getProjectContextTool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text!;
        json.Should().Contain("Chose the streaming parser");

        using var doc = JsonDocument.Parse(json);
        var rootScope = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Single(s => s.GetProperty("Scope").GetString() == "(root)");
        rootScope.GetProperty("DecisionsUnconfirmed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetProjectContext_FallbackTakesTheNewestN_RatherThanTheWholeFile()
    {
        // The spec said "falls back to recent decisions"; the code took all of
        // them. On projects/LogMark that is 165 entries / 66KB of decision
        // bodies plus per-entry metadata, in the tool lm-orient calls on every
        // orient.
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+manydecisions" }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var rootScope = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Single(s => s.GetProperty("Scope").GetString() == "(root)");
        var decisions = rootScope.GetProperty("Decisions").EnumerateArray().ToList();

        decisions.Should().HaveCount(10, "the fallback emits the newest 10, not the whole file");

        // Selection is by recency: the four oldest of the fourteen are gone.
        var contents = decisions.Select(d => d.GetProperty("Content").GetString()!).ToList();
        contents.Should().NotContain(c => c.Contains("Decision number 01"));
        contents.Should().NotContain(c => c.Contains("Decision number 04"));
        contents.Should().Contain(c => c.Contains("Decision number 05"));
        contents.Should().Contain(c => c.Contains("Decision number 14"));

        // Presentation is chronological, matching the tagged path's
        // TimestampAsc. Selecting newest-first and emitting newest-first would
        // flip this section's order depending on which path fired.
        contents[0].Should().Contain("Decision number 05");
        contents[^1].Should().Contain("Decision number 14");
    }

    [Fact]
    public async Task GetProjectContext_FallbackDecisions_AreListShaped_NotFullBodies()
    {
        // Siblings (ActiveTasks / OpenBlocks / RecentEntries) moved to List()
        // after the measured 36KB response in lmk-1zld. The fallback set is the
        // same shape of problem - many rows at once - so it resolves the same
        // way. The tagged set stays on Query: frozen decisions are this tool's
        // durable semantic payload.
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+manydecisions" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var rootScope = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Single(s => s.GetProperty("Scope").GetString() == "(root)");

        foreach (var d in rootScope.GetProperty("Decisions").EnumerateArray())
        {
            var content = d.GetProperty("Content").GetString()!;
            content.Should().NotContain("TAILMARKER", "a fallback body is truncated, not shipped whole");
            content.Length.Should().BeLessThan(220);
        }
    }

    [Fact]
    public async Task GetProjectContext_SummaryDecisionCount_MatchesTheDecisionsItReturns()
    {
        // lmk-nnd5's defect shape: a count describing a different set than the
        // array beside it. TotalDecisions counted the tagged set while
        // Scopes[].DecisionCount reported the fallback set.
        var result = await _getProjectContextTool.ExecuteAsync(
            CreateArgs(new { route = "+manydecisions" }), CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var emitted = doc.RootElement.GetProperty("Scopes").EnumerateArray()
            .Sum(s => s.GetProperty("Decisions").GetArrayLength());

        emitted.Should().Be(10, "guards against a vacuous pass on an empty response");
        doc.RootElement.GetProperty("Summary").GetProperty("TotalDecisions").GetInt32()
            .Should().Be(emitted);
    }

    [Fact]
    public async Task GetProjectContext_FallbackDoesNotPullScopedDecisionsIntoRoot()
    {
        // Arrange: a decision that is correctly scoped AND already tagged
        // #frozen - a real durable fact - must never be pulled into "(root)"
        // and relabelled unconfirmed just because the route's root-level
        // tagged set is empty. Regression for the untagged fallback query
        // originally missing the Scope == null filter that
        // decisionsResult.Entries.Where(...) applies to build the tagged
        // root set (GetProjectContextTool.cs:165-166) - without it, this
        // fitness-style fixture (zero root decisions, one scoped-and-tagged
        // decision) pulled the scoped decision into "(root)" and marked it
        // decisions_unconfirmed.
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath, """
            # Routes

            ## Projects

            ### scopedonly
            - path: projects/scopedonly
            """);

        var scopedDecisionsDir = Path.Combine(_testVaultPath, "logmark", "projects", "scopedonly", "api", "_decisions");
        Directory.CreateDirectory(scopedDecisionsDir);
        File.WriteAllText(Path.Combine(scopedDecisionsDir, "2026.md"), """
            ## 2026-04-01 09:00 | API
            Use REST over GraphQL #frozen
            """);

        var freshCache = new LogMark.Services.EntryCache.EntryCache(_testVaultPath);
        freshCache.StartWarmup();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!freshCache.IsReady && DateTime.UtcNow < timeout)
            Thread.Sleep(10);
        var freshSearch = new SearchService(freshCache);
        var freshResolver = new EntryResolver(freshCache, _testVaultPath);
        var freshServices = new VaultServices
        {
            VaultPath = _testVaultPath,
            SearchService = freshSearch,
            EntryResolver = freshResolver
        };
        var freshTool = new GetProjectContextTool(freshServices);

        var args = CreateArgs(new { route = "scopedonly" });

        // Act
        var result = await freshTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var scopes = doc.RootElement.GetProperty("Scopes").EnumerateArray().ToList();

        // The scoped decision appears once, under its real scope, still
        // frozen - never duplicated under "(root)" and relabelled.
        scopes.Should().NotContain(s => s.GetProperty("Scope").GetString() == "(root)");
        var apiScope = scopes.Single(s => s.GetProperty("Scope").GetString() == "api");
        apiScope.GetProperty("Decisions").EnumerateArray()
            .Should().ContainSingle(d => d.GetProperty("Content").GetString()!.Contains("Use REST over GraphQL"));
    }

    #endregion

    private static Dictionary<string, object?> CreateArgs(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }

        return result;
    }
}
