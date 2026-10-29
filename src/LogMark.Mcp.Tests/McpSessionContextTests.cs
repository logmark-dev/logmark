using FluentAssertions;
using LogMark.Models;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

public class McpSessionContextTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly SessionService _sessionService;
    private readonly SystemLogger _systemLogger;

    public McpSessionContextTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-context-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        _systemLogger = new SystemLogger(_testVaultPath);
        _sessionService = new SessionService(_testVaultPath, _systemLogger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    #region Initialization Tests

    [Fact]
    public void Constructor_CreatesPendingSession()
    {
        // Act
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "logmark");

        // Assert
        context.IsMaterialized.Should().BeFalse();
        var session = context.GetActiveSession();
        session.Should().NotBeNull();
        session!.Agent.Should().Be("test-agent");
        session.Route.Should().Be("logmark");
    }

    [Fact]
    public void Constructor_UsesDefaultAgent()
    {
        // Act
        var context = new McpSessionContext(_sessionService, _systemLogger);

        // Assert
        var session = context.GetActiveSession();
        session.Should().NotBeNull();
        session!.Agent.Should().Be("mcp");
    }

    [Fact]
    public void Constructor_DefaultRouteIsGeneral()
    {
        // Act
        var context = new McpSessionContext(_sessionService, _systemLogger);

        // Assert
        var session = context.GetActiveSession();
        session.Should().NotBeNull();
        session!.Route.Should().Be("general");
    }

    [Fact]
    public void Constructor_PendingSessionNotWrittenToDisk()
    {
        // Act
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");

        // Assert - Session file should NOT exist yet
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        if (Directory.Exists(sessionsDir))
        {
            Directory.GetFiles(sessionsDir, "*.md").Should().BeEmpty();
        }
    }

    #endregion

    #region Lazy Materialization Tests

    [Fact]
    public void EnsureMaterialized_WritesSessionToDisk()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");

        // Act
        var session = context.EnsureMaterialized();

        // Assert
        context.IsMaterialized.Should().BeTrue();
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        var sessionFile = Path.Combine(sessionsDir, $"{session.SessionId}.md");
        File.Exists(sessionFile).Should().BeTrue();
    }

    [Fact]
    public void EnsureMaterialized_IsIdempotent()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");

        // Act
        var session1 = context.EnsureMaterialized();
        var session2 = context.EnsureMaterialized();
        var session3 = context.EnsureMaterialized();

        // Assert - All return the same session
        session1.SessionId.Should().Be(session2.SessionId);
        session2.SessionId.Should().Be(session3.SessionId);
    }

    [Fact]
    public void EnsureMaterialized_WithRoute_UsesProvidedRoute()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", null);

        // Act
        var session = context.EnsureMaterialized("custom-route");

        // Assert
        session.Route.Should().Be("custom-route");
    }

    [Fact]
    public void RecordEntry_MaterializesSession()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.IsMaterialized.Should().BeFalse();

        // Act
        context.RecordEntry("logmark/_tasks/2026.md", "abc12345", "logmark");

        // Assert
        context.IsMaterialized.Should().BeTrue();
    }

    [Fact]
    public void RecordEntry_TracksEntryInSession()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");

        // Act
        context.RecordEntry("logmark/_tasks/2026.md", "abc123");
        context.RecordEntry("logmark/_ideas/2026.md", "def456");

        // Assert
        var info = context.GetActiveSessionInfo();
        info.Should().NotBeNull();
        info!.EntriesCreated.Should().Be(2);
    }

    #endregion

    #region StartNewSession Tests

    [Fact]
    public void StartNewSession_MaterializesImmediately()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");

        // Act
        var session = context.StartNewSession("project-x", "claude-code");

        // Assert
        context.IsMaterialized.Should().BeTrue();
        session.Route.Should().Be("project-x");
        session.Agent.Should().Be("claude-code");
    }

    [Fact]
    public void StartNewSession_EndsPreviousMaterializedSession()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        var session1 = context.StartNewSession("first", "test");
        var sessionId1 = session1.SessionId;

        // Act
        var session2 = context.StartNewSession("second", "test");

        // Assert
        session2.SessionId.Should().NotBe(sessionId1);

        // First session should be ended in vault
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        var sessionFile = Path.Combine(sessionsDir, $"{sessionId1}.md");
        var content = File.ReadAllText(sessionFile);
        content.Should().Contain("status: completed");
    }

    [Fact]
    public void StartNewSession_InheritsDefaultAgent()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "default-agent", "logmark");

        // Act
        var session = context.StartNewSession("project-x"); // No agent specified

        // Assert
        session.Agent.Should().Be("default-agent");
    }

    #endregion

    #region EndSession Tests

    [Fact]
    public void EndSession_PendingSession_ReturnsSessionWithoutWriting()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        var pendingId = context.GetActiveSession()!.SessionId;

        // Act
        var session = context.EndSession();

        // Assert
        session.Should().NotBeNull();
        session!.SessionId.Should().Be(pendingId);

        // Session should NOT be written to disk (was never materialized)
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        if (Directory.Exists(sessionsDir))
        {
            Directory.GetFiles(sessionsDir, $"{pendingId}.md").Should().BeEmpty();
        }
    }

    [Fact]
    public void EndSession_MaterializedSession_EndsAndPersists()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.EnsureMaterialized();
        var sessionId = context.GetActiveSession()!.SessionId;

        // Act
        var session = context.EndSession();

        // Assert
        session.Should().NotBeNull();
        session!.SessionId.Should().Be(sessionId);

        // Session should be marked completed in file
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        var sessionFile = Path.Combine(sessionsDir, $"{sessionId}.md");
        var content = File.ReadAllText(sessionFile);
        content.Should().Contain("status: completed");
    }

    [Fact]
    public void EndSession_CreatesNewPendingSession()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.EnsureMaterialized();
        var sessionId1 = context.GetActiveSession()!.SessionId;

        // Act
        context.EndSession();

        // Assert - New pending session should exist
        context.IsMaterialized.Should().BeFalse();
        var newSession = context.GetActiveSession();
        newSession.Should().NotBeNull();
        newSession!.SessionId.Should().NotBe(sessionId1);
    }

    #endregion

    #region Shutdown Tests

    [Fact]
    public void Shutdown_MaterializedSession_MarksCompletedOnDisk()
    {
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "general");
        context.RecordEntry("logmark/_tasks/2026.md", "abc12345");
        var sessionId = context.GetActiveSession()!.SessionId;

        var closed = context.Shutdown();

        closed.Should().NotBeNull();
        closed!.SessionId.Should().Be(sessionId);
        _sessionService.GetSession(sessionId)!.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void Shutdown_UnmaterializedSession_WritesNoFile()
    {
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "general");
        var pendingId = context.GetActiveSession()!.SessionId;

        var closed = context.Shutdown();

        closed.Should().BeNull();
        _sessionService.GetSession(pendingId).Should().BeNull();
    }

    [Fact]
    public void EndSession_NeverMaterialized_LeavesAUsablePendingSession()
    {
        // Every other clearing path either creates a replacement or deliberately
        // latches closed at shutdown. This branch used to do neither, so the context
        // was left with no pending session at all: GetActiveSession returned null,
        // both idle-timeout branches were guarded off, and mutation responses carried
        // no session_info until the next write happened to materialize a new one.
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "general");
        var endedId = context.GetActiveSession()!.SessionId;

        var ended = context.EndSession();

        ended!.SessionId.Should().Be(endedId);
        var replacement = context.GetActiveSession();
        replacement.Should().NotBeNull("ending an unused session must not leave the context blind");
        replacement!.SessionId.Should().NotBe(endedId);
        context.IsMaterialized.Should().BeFalse("the replacement is pending, not persisted");
        _sessionService.GetSession(replacement.SessionId).Should().BeNull("nothing was written to disk");
    }

    [Fact]
    public void Shutdown_CalledTwice_ClosesOnce()
    {
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "general");
        context.RecordEntry("logmark/_tasks/2026.md", "abc12345");

        var first = context.Shutdown();
        var second = context.Shutdown();

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void EnsureMaterialized_AfterShutdown_Throws()
    {
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "general");
        context.Shutdown();

        var act = () => context.EnsureMaterialized("general");

        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region Session Timeout Tests

    [Fact]
    public void GetActiveSession_TimedOutPending_CreatesNewPendingSession()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.SessionTimeout = TimeSpan.FromMilliseconds(1); // Very short timeout
        var originalId = context.GetActiveSession()!.SessionId;

        // Wait for timeout
        Thread.Sleep(10);

        // Act
        var session = context.GetActiveSession();

        // Assert - Should have new pending session
        session.Should().NotBeNull();
        session!.SessionId.Should().NotBe(originalId);
        context.IsMaterialized.Should().BeFalse();
    }

    [Fact]
    public void GetActiveSession_TimedOutMaterialized_EndsAndCreatesNew()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.SessionTimeout = TimeSpan.FromMilliseconds(50); // Slightly longer to ensure file ops complete
        context.EnsureMaterialized();
        var originalId = context.GetActiveSession()!.SessionId;

        // Verify session file was created
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        var sessionFile = Path.Combine(sessionsDir, $"{originalId}.md");
        File.Exists(sessionFile).Should().BeTrue("materialized session should create file");

        // Wait for timeout
        Thread.Sleep(100);

        // Act
        var session = context.GetActiveSession();

        // Assert
        session.Should().NotBeNull();
        session!.SessionId.Should().NotBe(originalId);
        context.IsMaterialized.Should().BeFalse(); // New session is pending

        // Original session should be ended - verify via SessionService API
        var endedSession = _sessionService.GetSession(originalId);
        endedSession.Should().NotBeNull();
        endedSession!.Status.Should().Be(LogMark.Models.SessionStatus.Completed);
    }

    [Fact]
    public void EnsureMaterialized_UpdatesLastActivity()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test", "logmark");
        context.SessionTimeout = TimeSpan.FromMilliseconds(50);
        context.EnsureMaterialized();
        var sessionId = context.GetActiveSession()!.SessionId;

        // Keep session alive by calling EnsureMaterialized
        for (int i = 0; i < 5; i++)
        {
            Thread.Sleep(20); // Less than timeout
            context.EnsureMaterialized(); // This should reset the timeout
        }

        // Act - Get session (should still be same because we kept it alive)
        var session = context.GetActiveSession();

        // Assert
        session.Should().NotBeNull();
        session!.SessionId.Should().Be(sessionId);
    }

    #endregion

    #region GetActiveSessionInfo Tests

    [Fact]
    public void GetActiveSessionInfo_PendingSession_ReturnInfo()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "test-route");

        // Act
        var info = context.GetActiveSessionInfo();

        // Assert
        info.Should().NotBeNull();
        info!.Agent.Should().Be("test-agent");
        info.Route.Should().Be("test-route");
        info.IsMaterialized.Should().BeFalse();
        info.EntriesCreated.Should().Be(0);
    }

    [Fact]
    public void GetActiveSessionInfo_MaterializedSession_ReturnsUpdatedInfo()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger, "test-agent", "logmark");
        context.EnsureMaterialized();
        context.RecordEntry("logmark/_tasks/2026.md", "abc");

        // Act
        var info = context.GetActiveSessionInfo();

        // Assert
        info.Should().NotBeNull();
        info!.IsMaterialized.Should().BeTrue();
        info.EntriesCreated.Should().Be(1);
    }

    [Fact]
    public void GetActiveSessionInfo_SessionIdFormat()
    {
        // Arrange
        var context = new McpSessionContext(_sessionService, _systemLogger);

        // Act
        var info = context.GetActiveSessionInfo();

        // Assert
        info.Should().NotBeNull();
        info!.SessionId.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}-[a-f0-9]{6}$");
    }

    #endregion
}
