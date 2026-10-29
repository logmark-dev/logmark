using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp;

/// <summary>
/// Manages MCP server session state with lazy materialization.
/// Sessions are created in memory and only written to disk on first mutation.
/// </summary>
public class McpSessionContext
{
    private readonly SessionService _sessionService;
    private readonly SystemLogger _logger;
    private readonly string _defaultAgent;
    private readonly string? _defaultRoute;
    private readonly int? _hostPid;
    private readonly object _lock = new();

    private Session? _pendingSession;
    private bool _materialized;
    private bool _closed;
    private DateTime _lastActivity;

    /// <summary>
    /// Default timeout for session inactivity (30 minutes).
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The agent identity this server was launched with (--agent; "mcp" when
    /// none). Tools use it as the writer name when a call passes no
    /// source_agent, so the vault records which agent wrote an entry.
    /// </summary>
    public string DefaultAgent => _defaultAgent;

    public McpSessionContext(
        SessionService sessionService,
        SystemLogger logger,
        string defaultAgent = "mcp",
        string? defaultRoute = null,
        int? hostPid = null)
    {
        _sessionService = sessionService;
        _logger = logger;
        _defaultAgent = defaultAgent;
        _defaultRoute = defaultRoute;
        _hostPid = hostPid;
        _lastActivity = DateTime.Now;

        // Create pending session immediately
        CreatePendingSession();
    }

    /// <summary>
    /// Get or create the active session. Returns pending session if not yet materialized.
    /// </summary>
    public Session? GetActiveSession()
    {
        lock (_lock)
        {
            // Check for timeout
            if (_pendingSession != null && !_materialized)
            {
                if (DateTime.Now - _lastActivity > SessionTimeout)
                {
                    // Pending session timed out without ever being used - create new
                    CreatePendingSession();
                }
            }

            // Check for materialized session timeout
            if (_materialized && _pendingSession != null)
            {
                if (DateTime.Now - _lastActivity > SessionTimeout)
                {
                    // End current session due to timeout
                    _sessionService.EndSession(_pendingSession.SessionId, "idle-timeout");
                    _logger.Info($"Session {_pendingSession.SessionId} auto-ended due to inactivity");
                    CreatePendingSession();
                }
            }

            return _pendingSession;
        }
    }

    /// <summary>
    /// Ensure session is materialized (written to disk). Call before any mutation.
    /// Returns the active session ID.
    /// </summary>
    public Session EnsureMaterialized(string? route = null)
    {
        lock (_lock)
        {
            if (_closed)
            {
                // A tool call that arrives after shutdown began must not create a
                // record: the process is exiting, so the record would be orphaned
                // the moment it was written, and the caller's response cannot be
                // delivered anyway because the pipe is gone. Mutation tools wrap
                // execution in catch (Exception) and surface this as an error result.
                throw new InvalidOperationException(
                    "MCP session context is shut down; no new session can be materialized.");
            }

            _lastActivity = DateTime.Now;

            if (_materialized && _pendingSession != null)
            {
                return _pendingSession;
            }

            // Materialize the pending session
            var sessionRoute = route ?? _pendingSession?.Route ?? _defaultRoute ?? "general";
            var agent = _pendingSession?.Agent ?? _defaultAgent;

            var session = _sessionService.StartSession(sessionRoute, agent, _hostPid);
            _pendingSession = session;
            _materialized = true;

            _logger.Info($"Session materialized: {session.SessionId} (route: +{session.Route})");

            return session;
        }
    }

    /// <summary>
    /// Record an entry in the active session.
    /// Automatically materializes the session if needed.
    /// The shortId is required so the session file stores "{entryPath}#{shortId}",
    /// which SessionEntryGrouper resolves via EntryCache's short-id index when
    /// rendering the session detail view.
    /// </summary>
    public void RecordEntry(string entryPath, string shortId, string? route = null, bool needsReview = false)
    {
        var session = EnsureMaterialized(route);
        _sessionService.RecordEntry(session.SessionId, entryPath, shortId, needsReview);

        lock (_lock)
        {
            _lastActivity = DateTime.Now;
            RefreshPendingFromService(session.SessionId);
        }
    }

    /// <summary>
    /// Record an entry modification in the active session (e.g., task completion).
    /// Automatically materializes the session if needed.
    /// When <paramref name="shortId"/> is non-null, the reference is stored block-
    /// granular as "{entryPath}#{shortId}". When null, the reference is stored as
    /// a plain path -- used for system-file modifications (e.g., routes.md) that
    /// are not entries and have no shortId.
    /// </summary>
    public void RecordModification(string entryPath, string? shortId = null, string? route = null)
    {
        var session = EnsureMaterialized(route);
        _sessionService.RecordModification(session.SessionId, entryPath, shortId);

        lock (_lock)
        {
            _lastActivity = DateTime.Now;
            RefreshPendingFromService(session.SessionId);
        }
    }

    /// <summary>
    /// Re-point this context's copy of the record at the service's current view.
    ///
    /// The service used to mutate the very object it handed back, so this copy
    /// updated itself by aliasing. It now rewrites session files from a fresh disk
    /// read - so another process's headers survive a close - which means the alias
    /// is gone and ActiveSessionInfo would report zero entries forever without this.
    /// Caller holds _lock.
    /// </summary>
    private void RefreshPendingFromService(string sessionId)
    {
        if (_pendingSession?.SessionId != sessionId)
            return;

        _pendingSession = _sessionService.GetSession(sessionId) ?? _pendingSession;
    }

    /// <summary>
    /// Explicitly start a new session, ending any active session.
    /// </summary>
    public Session StartNewSession(string route, string? agent = null)
    {
        lock (_lock)
        {
            // End existing materialized session
            if (_materialized && _pendingSession != null)
            {
                _sessionService.EndSession(_pendingSession.SessionId, "superseded");
            }

            // Create new session directly (not pending)
            var session = _sessionService.StartSession(route, agent ?? _defaultAgent, _hostPid);
            _pendingSession = session;
            _materialized = true;
            _lastActivity = DateTime.Now;

            return session;
        }
    }

    /// <summary>
    /// End the active session.
    /// </summary>
    /// <param name="closedBy">
    /// How this boundary was established. Defaults to "agent" because the
    /// end_session tool is the caller that passes nothing.
    /// </param>
    public Session? EndSession(string? closedBy = "agent")
    {
        lock (_lock)
        {
            if (_pendingSession == null)
                return null;

            if (!_materialized)
            {
                // Session was never used - nothing on disk to close. Hand the caller
                // the record it ended and put a fresh pending session in its place.
                // Without the replacement this branch was the one clearing path that
                // left the context with no pending session at all: GetActiveSession
                // returned null, both idle-timeout branches are guarded on non-null so
                // they stopped running, and mutation responses carried no session_info
                // until the next write happened to materialize one.
                var pending = _pendingSession;
                CreatePendingSession();
                return pending;
            }

            var session = _sessionService.EndSession(_pendingSession.SessionId, closedBy);
            _pendingSession = null;
            _materialized = false;

            // Create new pending session for future use
            CreatePendingSession();

            return session;
        }
    }

    /// <summary>
    /// Close this process's session because the server is shutting down, and latch
    /// the context so no further session can be materialized.
    ///
    /// Distinct from <see cref="EndSession"/>, which creates a replacement pending
    /// session for continued use. At shutdown there is no continued use, and a
    /// replacement would only emit a misleading "pending session created" log line
    /// on the way out.
    ///
    /// Idempotent: both shutdown call sites in Program.cs may fire (stdin EOF can
    /// race the parent-process watchdog), and only the first does the work.
    /// </summary>
    public Session? Shutdown()
    {
        lock (_lock)
        {
            if (_closed)
                return null;

            _closed = true;

            if (_pendingSession == null || !_materialized)
            {
                // Never materialized: nothing on disk, so nothing to close. An agent
                // that only read the vault does not acquire a record.
                _pendingSession = null;
                return null;
            }

            var session = _sessionService.EndSession(_pendingSession.SessionId, "host-exit");
            _pendingSession = null;
            _materialized = false;
            return session;
        }
    }

    /// <summary>
    /// Check if session has been materialized (written to disk).
    /// </summary>
    public bool IsMaterialized
    {
        get
        {
            lock (_lock)
            {
                return _materialized;
            }
        }
    }

    /// <summary>
    /// Get summary of active session for inclusion in mutation responses.
    /// </summary>
    public ActiveSessionInfo? GetActiveSessionInfo()
    {
        lock (_lock)
        {
            if (_pendingSession == null)
                return null;

            return new ActiveSessionInfo
            {
                SessionId = _pendingSession.SessionId,
                Agent = _pendingSession.Agent,
                Route = _pendingSession.Route,
                StartedAt = _pendingSession.Started.ToString("O"),
                EntriesCreated = _pendingSession.EntriesCreated.Count,
                EntriesModified = _pendingSession.EntriesModified.Count,
                IsMaterialized = _materialized
            };
        }
    }

    private void CreatePendingSession()
    {
        // Create session object in memory without writing to disk
        var sessionId = GenerateSessionId();
        _pendingSession = new Session
        {
            SessionId = sessionId,
            Started = DateTime.Now,
            Agent = _defaultAgent,
            Route = _defaultRoute?.TrimStart('+') ?? "general",
            Status = SessionStatus.Active
        };
        _materialized = false;
        _lastActivity = DateTime.Now;

        _logger.Info($"Pending session created: {sessionId} (will materialize on first mutation)");
    }

    private static string GenerateSessionId()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(4);
        var hash = Convert.ToHexString(randomBytes).ToLowerInvariant()[..6];
        return $"{date}-{hash}";
    }
}

/// <summary>
/// Session info included in mutation responses.
/// </summary>
public class ActiveSessionInfo
{
    public string SessionId { get; set; } = "";
    public string Agent { get; set; } = "";
    public string Route { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public int EntriesCreated { get; set; }
    public int EntriesModified { get; set; }
    public bool IsMaterialized { get; set; }
}
