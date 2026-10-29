using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Models;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

public class SessionToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly SessionService _sessionService;
    private readonly McpSessionContext _sessionContext;
    private readonly StartSessionTool _startSessionTool;
    private readonly EndSessionTool _endSessionTool;
    private readonly GetSessionsTool _getSessionsTool;

    public SessionToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-session-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        var systemLogger = new SystemLogger(_testVaultPath);
        _sessionService = new SessionService(_testVaultPath, systemLogger);
        _sessionContext = new McpSessionContext(_sessionService, systemLogger, "test-agent", "general");
        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            SessionService = _sessionService,
            SessionContext = _sessionContext
        };
        _startSessionTool = new StartSessionTool(services);
        _endSessionTool = new EndSessionTool(services);
        _getSessionsTool = new GetSessionsTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    #region StartSessionTool Tests

    [Fact]
    public async Task StartSession_CreatesNewSession()
    {
        // Arrange
        var args = CreateArgs(new { route = "+logmark", agent = "claude-code" });

        // Act
        var result = await _startSessionTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("\"SessionId\":");
        json.ShouldHaveJsonProperty("Route", "\"logmark\"");
        json.ShouldHaveJsonProperty("Agent", "\"claude-code\"");

        // Verify session ID format (YYYY-MM-DD-hash)
        using var doc = JsonDocument.Parse(json!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();
        sessionId.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}-[a-f0-9]{6}$");
    }

    [Fact]
    public async Task StartSession_ClosesExistingAndCreatesNew()
    {
        // Arrange - Start first session
        var args1 = CreateArgs(new { route = "logmark", agent = "test" });
        var result1 = await _startSessionTool.ExecuteAsync(args1, CancellationToken.None);
        using var doc1 = JsonDocument.Parse(result1.Content[0].Text!);
        var sessionId1 = doc1.RootElement.GetProperty("SessionId").GetString();

        // Act - Start second session (closes first)
        var args2 = CreateArgs(new { route = "other", agent = "test2" });
        var result2 = await _startSessionTool.ExecuteAsync(args2, CancellationToken.None);

        // Assert - Should create new session with PreviousSession info
        result2.IsError.Should().BeFalse();
        var json = result2.Content[0].Text;
        json.ShouldHaveJsonProperty("Route", "\"other\"");
        json.ShouldHaveJsonProperty("Agent", "\"test2\"");
        // Should reference previous session that was closed
        json.Should().Contain("\"PreviousSession\":");
        json.ShouldHaveJsonProperty("SessionId", $"\"{sessionId1}\"");
    }

    [Fact]
    public async Task StartSession_InheritsDefaultAgentFromContext()
    {
        // Arrange - context was created with "test-agent" as default
        var args = CreateArgs(new { route = "logmark" }); // No agent specified

        // Act
        var result = await _startSessionTool.ExecuteAsync(args, CancellationToken.None);

        // Assert - should inherit from context (test-agent)
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Agent", "\"test-agent\"");
    }

    [Fact]
    public async Task StartSession_MissingRoute_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new { agent = "test" }); // Missing route

        // Act
        var result = await _startSessionTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("route");
    }

    #endregion

    #region EndSessionTool Tests

    [Fact]
    public async Task EndSession_AfterStartSession_CompletesSession()
    {
        // Verify initial state - not materialized
        _sessionContext.IsMaterialized.Should().BeFalse("context should start with pending session");

        // Arrange - Create session
        var startArgs = CreateArgs(new { route = "logmark", agent = "test" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var doc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();

        // Verify session was materialized
        _sessionContext.IsMaterialized.Should().BeTrue("StartSession should materialize the session");

        // Act
        var endArgs = CreateArgs(new { });
        var result = await _endSessionTool.ExecuteAsync(endArgs, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("SessionId", $"\"{sessionId}\"");
        json.ShouldHaveJsonProperty("WasMaterialized", "true");
        json.Should().Contain("\"Ended\":");
        json.Should().Contain("\"DurationMinutes\":");
    }

    [Fact]
    public async Task EndSession_PendingSession_ReturnsNotMaterialized()
    {
        // Arrange - Context always has pending session, but it's never used
        var args = CreateArgs(new { });

        // Act
        var result = await _endSessionTool.ExecuteAsync(args, CancellationToken.None);

        // Assert - Should succeed but indicate session was not materialized
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("WasMaterialized", "false");
    }

    [Fact]
    public async Task EndSession_MaterializedSession_IncludesStats()
    {
        // Arrange - Start a session explicitly to materialize it
        var startArgs = CreateArgs(new { route = "logmark", agent = "test" });
        await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);

        // Act
        var args = CreateArgs(new { });
        var result = await _endSessionTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("WasMaterialized", "true");
        json.Should().Contain("\"Ended\":");
        json.Should().Contain("\"DurationMinutes\":");
    }

    #endregion

    #region GetSessions Single-Session Detail Tests (formerly GetSessionTool)

    [Fact]
    public async Task GetSession_BySessionId_ReturnsSession()
    {
        // Arrange - Create session
        var startArgs = CreateArgs(new { route = "logmark", agent = "claude-code" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var doc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();

        // Act - use get_sessions with session_id mode
        var args = CreateArgs(new { session_id = sessionId });
        var result = await _getSessionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Found", "true");
        json.ShouldHaveJsonProperty("SessionId", $"\"{sessionId}\"");
        json.ShouldHaveJsonProperty("Route", "\"logmark\"");
        json.ShouldHaveJsonProperty("Agent", "\"claude-code\"");
        json.ShouldHaveJsonProperty("Status", "\"active\"");
    }

    [Fact]
    public async Task GetSession_ActiveOnly_ReturnsActiveSession()
    {
        // Arrange - Create session
        var startArgs = CreateArgs(new { route = "testproject", agent = "test" });
        await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);

        // Act - use get_sessions with active_only mode
        var args = CreateArgs(new { active_only = true });
        var result = await _getSessionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Found", "true");
        json.ShouldHaveJsonProperty("Route", "\"testproject\"");
    }

    [Fact]
    public async Task GetSession_ActiveOnly_NoActiveSession_ReturnsNotFound()
    {
        // Arrange - No session
        var args = CreateArgs(new { active_only = true });

        // Act
        var result = await _getSessionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Found", "false");
        json.Should().Contain("No active session");
    }

    [Fact]
    public async Task GetSession_InvalidSessionId_ReturnsNotFound()
    {
        // Arrange
        var args = CreateArgs(new { session_id = "2026-01-01-notfound" });

        // Act
        var result = await _getSessionsTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Found", "false");
        json.Should().Contain("not found");
    }

    #endregion

    #region GetSessionsTool Mode Tests

    [Fact]
    public async Task GetSessions_WithSessionId_ReturnsSingleDetail()
    {
        // start a session so there is one to fetch by id
        var start = await _startSessionTool.ExecuteAsync(
            CreateArgs(new { agent = "test", route = "logmark" }), CancellationToken.None);
        var id = JsonDocument.Parse(start.Content[0].Text!)
            .RootElement.GetProperty("SessionId").GetString();

        var result = await _getSessionsTool.ExecuteAsync(
            CreateArgs(new { session_id = id }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text!;
        json.ShouldHaveJsonProperty("Found", "true");
        json.Should().Contain(id!);
    }

    [Fact]
    public async Task GetSessions_ActiveOnly_ReturnsActiveDetail()
    {
        await _startSessionTool.ExecuteAsync(
            CreateArgs(new { agent = "test", route = "logmark" }), CancellationToken.None);

        var result = await _getSessionsTool.ExecuteAsync(
            CreateArgs(new { active_only = true }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text!.ShouldHaveJsonProperty("Found", "true");
    }

    [Fact]
    public async Task GetSessions_NoModeArgs_StillReturnsList()
    {
        var result = await _getSessionsTool.ExecuteAsync(
            CreateArgs(new { }), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text!.Should().Contain("TotalCount");
    }

    #endregion

    #region GetSessionsTool Tests

    [Fact]
    public async Task GetSessions_ReturnsAllSessions()
    {
        // Arrange - Create and end multiple sessions
        var args1 = CreateArgs(new { route = "project1", agent = "test" });
        await _startSessionTool.ExecuteAsync(args1, CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        var args2 = CreateArgs(new { route = "project2", agent = "test" });
        await _startSessionTool.ExecuteAsync(args2, CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        // Act
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "2");
        json.Should().Contain("project1");
        json.Should().Contain("project2");
    }

    [Fact]
    public async Task GetSessions_FilterByRoute()
    {
        // Arrange - Create sessions for different routes
        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "alpha", agent = "test" }), CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "beta", agent = "test" }), CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        // Act
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { route = "alpha" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "1");
        json.ShouldHaveJsonProperty("Route", "\"alpha\"");
        json.ShouldNotHaveJsonProperty("Route", "\"beta\"");
    }

    [Fact]
    public async Task GetSessions_FilterByStatus()
    {
        // Arrange - Create one completed, one active
        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "completed", agent = "test" }), CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "active", agent = "test" }), CancellationToken.None);
        // Don't end the second session

        // Act - Filter for completed only
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { status = "completed" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "1");
        json.ShouldHaveJsonProperty("Route", "\"completed\"");
        json.ShouldNotHaveJsonProperty("Route", "\"active\"");
    }

    [Fact]
    public async Task GetSessions_FilterByAgent()
    {
        // Arrange
        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "test1", agent = "claude-code" }), CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        await _startSessionTool.ExecuteAsync(CreateArgs(new { route = "test2", agent = "cursor" }), CancellationToken.None);
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        // Act
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { agent = "cursor" }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "1");
        json.ShouldHaveJsonProperty("Agent", "\"cursor\"");
    }

    [Fact]
    public async Task GetSessions_RespectLimit()
    {
        // Arrange - Create 5 sessions
        for (int i = 0; i < 5; i++)
        {
            await _startSessionTool.ExecuteAsync(CreateArgs(new { route = $"project{i}", agent = "test" }), CancellationToken.None);
            await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);
        }

        // Act
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { limit = 3 }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("TotalCount", "3");
    }

    #endregion

    #region GetSessions Active-Only Detail Tests (formerly GetActiveSessionTool)

    [Fact]
    public async Task GetActiveSession_ReturnsSessionInfo()
    {
        // Arrange - Start a session to materialize it
        var startArgs = CreateArgs(new { route = "logmark", agent = "claude-code" });
        await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);

        // Act - use get_sessions active_only mode
        var result = await _getSessionsTool.ExecuteAsync(CreateArgs(new { active_only = true }), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Found", "true");
        json.ShouldHaveJsonProperty("Route", "\"logmark\"");
        json.ShouldHaveJsonProperty("Agent", "\"claude-code\"");
        json.ShouldHaveJsonProperty("Status", "\"active\"");
    }

    [Fact]
    public async Task GetActiveSession_NoActiveSession_ReturnsNotFound()
    {
        // Arrange - No materialized session in service (pending sessions are not in SessionService)
        // End any implicit session so the service has no active session
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        // To get Found: false we need no active session in SessionService at all.
        // Fresh vault with no start_session call has nothing in SessionService.
        var freshVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-fresh-{Guid.NewGuid()}");
        Directory.CreateDirectory(freshVaultPath);
        Directory.CreateDirectory(Path.Combine(freshVaultPath, "logmark", "_system"));
        var freshLogger = new SystemLogger(freshVaultPath);
        var freshSessionService = new SessionService(freshVaultPath, freshLogger);
        var freshServices = new VaultServices
        {
            VaultPath = freshVaultPath,
            SessionService = freshSessionService,
            SessionContext = new McpSessionContext(freshSessionService, freshLogger, "t", "g")
        };
        var freshTool = new GetSessionsTool(freshServices);

        try
        {
            var result = await freshTool.ExecuteAsync(CreateArgs(new { active_only = true }), CancellationToken.None);

            result.IsError.Should().BeFalse();
            var json = result.Content[0].Text;
            json.ShouldHaveJsonProperty("Found", "false");
            json.Should().Contain("No active session");
        }
        finally
        {
            if (Directory.Exists(freshVaultPath)) Directory.Delete(freshVaultPath, true);
        }
    }

    [Fact]
    public async Task EndSession_RecordUnreadable_DoesNotClaimThereWasNoSession()
    {
        // A materialized session whose file was truncated by a crash. SessionService
        // correctly refuses to rewrite it - a close is a whole-file rewrite, so acting
        // would destroy the record to mark it finished. The tool must not then report
        // "no active session to end": one existed, it could not be closed, and it is
        // still Active on disk waiting for the sweeper.
        _sessionContext.RecordEntry("logmark/_tasks/2026.md", "abc12345");
        var sessionId = _sessionContext.GetActiveSession()!.SessionId;
        var recordPath = Path.Combine(_testVaultPath, "logmark", "_sessions", $"{sessionId}.md");
        File.Exists(recordPath).Should().BeTrue("the session must be materialized before truncating it");
        File.WriteAllText(recordPath, "");

        var result = await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        result.IsError.Should().BeTrue();
        var message = result.Content[0].Text!;
        message.Should().NotContain("No active session to end");
        message.Should().Contain(sessionId, "a human needs the id to go find the damaged file");
    }

    [Fact]
    public async Task EndSession_RecordUnreadable_StillResumesTrackingOnTheNextWrite()
    {
        // Dropping ownership of an unwritable record is deliberate: continuing to point
        // at it would mean later entries are tracked nowhere. A fresh record resumes
        // tracking and the sweeper reclaims the orphan.
        _sessionContext.RecordEntry("logmark/_tasks/2026.md", "abc12345");
        var abandonedId = _sessionContext.GetActiveSession()!.SessionId;
        File.WriteAllText(Path.Combine(_testVaultPath, "logmark", "_sessions", $"{abandonedId}.md"), "");
        await _endSessionTool.ExecuteAsync(CreateArgs(new { }), CancellationToken.None);

        _sessionContext.RecordEntry("logmark/_tasks/2026.md", "def67890");

        var resumed = _sessionContext.GetActiveSession()!;
        resumed.SessionId.Should().NotBe(abandonedId);
        _sessionService.GetSession(resumed.SessionId)!.Status.Should().Be(SessionStatus.Active);
    }

    #endregion

    #region SessionService Integration Tests

    [Fact]
    public async Task Session_FileIsWrittenCorrectly()
    {
        // Arrange & Act
        var startArgs = CreateArgs(new { route = "logmark", agent = "claude-code" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var doc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();

        // Assert - Check file exists
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        var sessionFile = Path.Combine(sessionsDir, $"{sessionId}.md");
        File.Exists(sessionFile).Should().BeTrue();

        // Check file contents
        var content = File.ReadAllText(sessionFile);
        content.Should().Contain($"# Session: {sessionId}");
        content.Should().Contain("agent: claude-code");
        content.Should().Contain("route: +logmark");
        content.Should().Contain("status: active");
    }

    [Fact]
    public async Task Session_RecordEntry_UpdatesSession()
    {
        // Arrange
        var startArgs = CreateArgs(new { route = "logmark", agent = "test" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var doc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();

        // Act - Record an entry (shortId required so the detail-view grouper can resolve via EntryCache)
        _sessionService.RecordEntry(sessionId!, "logmark/_diary/2026.md", "abc12345", needsReview: true);

        // Assert
        var getArgs = CreateArgs(new { session_id = sessionId });
        var result = await _getSessionsTool.ExecuteAsync(getArgs, CancellationToken.None);
        var json = result.Content[0].Text;
        json.Should().Contain("logmark/_diary/2026.md#abc12345");
        json.Should().Contain("\"PendingReview\"");
    }

    [Fact]
    public async Task Session_RecordContextLoaded_UpdatesSession()
    {
        // Arrange
        var startArgs = CreateArgs(new { route = "logmark", agent = "test" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var doc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = doc.RootElement.GetProperty("SessionId").GetString();

        // Act - Record context loaded
        _sessionService.RecordContextLoaded(sessionId!, decisions: 5, tasks: 3, blocks: 1);

        // Assert
        var getArgs = CreateArgs(new { session_id = sessionId });
        var result = await _getSessionsTool.ExecuteAsync(getArgs, CancellationToken.None);
        var json = result.Content[0].Text;
        json.Should().Contain("\"ContextLoaded\"");
        json.ShouldHaveJsonProperty("Decisions", "5");
        json.ShouldHaveJsonProperty("Tasks", "3");
        json.ShouldHaveJsonProperty("Blocks", "1");
    }

    [Fact]
    public async Task CreateEntryWithSession_RecordsEntryInSession()
    {
        // Arrange - Create services and tools
        var routesPath = Path.Combine(_testVaultPath, "logmark", "_system", "routes.md");
        File.WriteAllText(routesPath, "# Routes\n\n## Projects\n\n## Domains\n");

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_testVaultPath, notationParser);
        var systemLogger = new SystemLogger(_testVaultPath);
        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService());
        routeService.LoadRoutes();

        var inlineServices = new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultService = vaultService,
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = _sessionService,
            SessionContext = _sessionContext
        };
        var createEntryTool = new CreateEntryTool(inlineServices);

        // Start a session
        var startArgs = CreateArgs(new { route = "logmark", agent = "test-agent" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var startDoc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = startDoc.RootElement.GetProperty("SessionId").GetString()!;

        // Act - Create entry with session
        var createArgs = CreateArgs(new { content = "i: test idea", session = sessionId });
        var createResult = await createEntryTool.ExecuteAsync(createArgs, CancellationToken.None);
        createResult.IsError.Should().BeFalse();

        // End session
        var endArgs = CreateArgs(new { session_id = sessionId });
        var endResult = await _endSessionTool.ExecuteAsync(endArgs, CancellationToken.None);
        using var endDoc = JsonDocument.Parse(endResult.Content[0].Text!);

        // Assert - Session should have 1 entry recorded
        var entriesCreated = endDoc.RootElement.GetProperty("EntriesCreated").GetInt32();
        entriesCreated.Should().Be(1);
    }

    [Fact]
    public async Task CreateMultipleEntriesWithSession_CountsAllEntries()
    {
        // Arrange
        var routesPath = Path.Combine(_testVaultPath, "logmark", "_system", "routes.md");
        File.WriteAllText(routesPath, "# Routes\n\n## Projects\n\n## Domains\n");

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_testVaultPath, notationParser);
        var systemLogger = new SystemLogger(_testVaultPath);
        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService());
        routeService.LoadRoutes();

        var inlineServices = new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultService = vaultService,
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = _sessionService,
            SessionContext = _sessionContext
        };
        var createEntryTool = new CreateEntryTool(inlineServices);

        // Start a session
        var startArgs = CreateArgs(new { route = "logmark", agent = "test-agent" });
        var startResult = await _startSessionTool.ExecuteAsync(startArgs, CancellationToken.None);
        using var startDoc = JsonDocument.Parse(startResult.Content[0].Text!);
        var sessionId = startDoc.RootElement.GetProperty("SessionId").GetString()!;

        // Act - Create 3 entries with session
        for (int i = 1; i <= 3; i++)
        {
            var createArgs = CreateArgs(new { content = $"i: test idea {i}", session = sessionId });
            await createEntryTool.ExecuteAsync(createArgs, CancellationToken.None);
        }

        // End session
        var endArgs = CreateArgs(new { session_id = sessionId });
        var endResult = await _endSessionTool.ExecuteAsync(endArgs, CancellationToken.None);
        using var endDoc = JsonDocument.Parse(endResult.Content[0].Text!);

        // Assert - Session should have 3 entries recorded
        var entriesCreated = endDoc.RootElement.GetProperty("EntriesCreated").GetInt32();
        entriesCreated.Should().Be(3);
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

    private class TestRoutePromptService : IRoutePromptService
    {
        public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
        {
            var path = options.DefaultPath ?? $"projects/{routeName}";
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
        }
    }
}
