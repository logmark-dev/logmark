using FluentAssertions;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Pins the SessionService.RecordEntry/RecordModification contract: in-memory and
/// on-disk references are stored as "{entryPath}#{shortId}" so the SessionEntryGrouper
/// can resolve entries via EntryCache's short-id index. Without this format every
/// entry shows as "missing" in the SessionDetailView even though the entries exist.
/// </summary>
public class SessionServiceShortIdTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly SessionService _sessionService;

    public SessionServiceShortIdTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-session-shortid-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        var systemLogger = new SystemLogger(_testVaultPath);
        _sessionService = new SessionService(_testVaultPath, systemLogger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    [Fact]
    public void RecordEntry_StoresPathHashShortIdFormat_InMemory()
    {
        var session = _sessionService.StartSession("logmark", "test-agent");

        _sessionService.RecordEntry(session.SessionId, "logmark/_tasks/2026.md", "aaaa1111");

        var refreshed = _sessionService.GetSession(session.SessionId);
        refreshed.Should().NotBeNull();
        refreshed!.EntriesCreated.Should().ContainSingle()
            .Which.Should().Be("logmark/_tasks/2026.md#aaaa1111");
    }

    [Fact]
    public void RecordEntry_RoundtripsThroughDisk_PreservesShortIdSuffix()
    {
        var session = _sessionService.StartSession("logmark", "test-agent");
        _sessionService.RecordEntry(session.SessionId, "logmark/_decisions/2026.md", "bbbb2222");

        // Force a fresh read from disk by going through GetSessions (skips in-memory cache).
        // GetSession returns the cached active session; we need to verify the on-disk format.
        var sessions = _sessionService.GetSessions(new LogMark.Models.SessionQuery { Limit = 10 });
        sessions.Should().ContainSingle(s => s.SessionId == session.SessionId);
        sessions[0].EntriesCreated.Should().Be(1);

        // Now end the session and re-parse to verify the wiki link format on disk parses back to path#shortid.
        _sessionService.EndSession(session.SessionId);

        // Use a fresh SessionService so the in-memory cache is bypassed and the file is reparsed.
        var freshLogger = new SystemLogger(_testVaultPath);
        var freshService = new SessionService(_testVaultPath, freshLogger);
        var reparsed = freshService.GetSession(session.SessionId);
        reparsed.Should().NotBeNull();
        reparsed!.EntriesCreated.Should().ContainSingle()
            .Which.Should().EndWith("#bbbb2222",
                "the #shortid suffix must survive the disk roundtrip so SessionEntryGrouper can resolve via EntryCache");
        // Wiki-link normalization strips the .md extension on write (matches Obsidian convention).
        // The grouper only consumes the substring after '#', so this is functionally equivalent.
        reparsed.EntriesCreated[0].Should().Be("logmark/_decisions/2026#bbbb2222");
    }

    [Fact]
    public void RecordEntry_AppliesNeedsReview_WithShortIdFormat()
    {
        var session = _sessionService.StartSession("logmark", "test-agent");

        _sessionService.RecordEntry(session.SessionId, "logmark/_blocks/2026.md", "cccc3333", needsReview: true);

        var refreshed = _sessionService.GetSession(session.SessionId);
        refreshed!.EntriesCreated.Should().ContainSingle()
            .Which.Should().Be("logmark/_blocks/2026.md#cccc3333");
        refreshed.PendingReview.Should().ContainSingle()
            .Which.Should().Be("logmark/_blocks/2026.md#cccc3333");
    }

    [Fact]
    public void RecordEntry_ThrowsWhenShortIdIsEmpty()
    {
        var session = _sessionService.StartSession("logmark", "test-agent");

        Action act = () => _sessionService.RecordEntry(session.SessionId, "logmark/_tasks/2026.md", "");

        act.Should().Throw<ArgumentException>().WithParameterName("shortId");
    }

    [Fact]
    public void RecordModification_WithShortId_StoresBlockGranularReference()
    {
        var session = _sessionService.StartSession("logmark", "test-agent");

        _sessionService.RecordModification(session.SessionId, "logmark/_tasks/2026.md", "dddd4444");

        var refreshed = _sessionService.GetSession(session.SessionId);
        refreshed!.EntriesModified.Should().ContainSingle()
            .Which.Should().Be("logmark/_tasks/2026.md#dddd4444");
    }

    [Fact]
    public void RecordModification_WithoutShortId_StoresPlainPath_ForSystemFiles()
    {
        // System files like routes.md are file-level modifications; they have no
        // shortId because they're not entries. Keep plain-path storage for these.
        var session = _sessionService.StartSession("logmark", "test-agent");

        _sessionService.RecordModification(session.SessionId, "logmark/_system/routes.md");

        var refreshed = _sessionService.GetSession(session.SessionId);
        refreshed!.EntriesModified.Should().ContainSingle()
            .Which.Should().Be("logmark/_system/routes.md");
    }

    [Fact]
    public void LoadSession_PlainPathLegacyEntries_DoNotCrash()
    {
        // Back-compat: a session file written by an older build whose "## Entries Created"
        // lines are plain wiki-links without a #shortid suffix must still parse. The grouper
        // is responsible for counting these as "missing" via its existing path.
        var sessionsDir = Path.Combine(_testVaultPath, "logmark", "_sessions");
        Directory.CreateDirectory(sessionsDir);
        var sessionFile = Path.Combine(sessionsDir, "2026-05-25-abcdef.md");
        var content = @"# Session: 2026-05-25-abcdef

started: 2026-05-25T10:00:00.0000000+00:00
ended: 2026-05-25T11:00:00.0000000+00:00
agent: legacy-build
route: +logmark
status: completed

## Entries Created

- [[logmark/_tasks/2026]]
- [[logmark/_decisions/2026]]
";
        File.WriteAllText(sessionFile, content);

        var session = _sessionService.GetSession("2026-05-25-abcdef");

        session.Should().NotBeNull();
        session!.EntriesCreated.Should().HaveCount(2);
        session.EntriesCreated[0].Should().Be("logmark/_tasks/2026");
        session.EntriesCreated[1].Should().Be("logmark/_decisions/2026");
    }
}
