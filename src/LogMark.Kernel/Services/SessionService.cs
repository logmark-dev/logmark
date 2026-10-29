using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Interfaces;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Manages agent work sessions. Sessions group related entries for review.
/// Storage location: _sessions/{session_id}.md
/// </summary>
public partial class SessionService
{
    private readonly string _vaultPath;
    private readonly SystemLogger _logger;
    private readonly IVaultFileWriter? _vaultWriter;

    // In-memory cache of active session
    private Session? _activeSession;

    public SessionService(string vaultPath, SystemLogger logger, IVaultFileWriter? vaultWriter = null)
    {
        _vaultPath = vaultPath;
        _logger = logger;
        _vaultWriter = vaultWriter;
    }

    /// <summary>
    /// Start a new agent work session.
    /// </summary>
    public Session StartSession(string route, string agent, int? hostPid = null)
    {
        var sessionId = GenerateSessionId();
        var session = new Session
        {
            SessionId = sessionId,
            Started = DateTime.Now,
            Agent = agent,
            Route = route.TrimStart('+'),
            Status = SessionStatus.Active,
            Owner = SessionOwner.Current(),
            HostPid = hostPid,
            LastActivity = DateTime.Now
        };

        // Write initial session file
        WriteSessionFile(session);
        _activeSession = session;

        _logger.Info($"Session started: {sessionId} ({agent} on +{session.Route})");

        return session;
    }

    /// <summary>
    /// End an active session.
    /// </summary>
    public Session? EndSession(string sessionId, string? closedBy = null)
    {
        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // Refusing to close is the safe degrade: a close is a whole-file rewrite,
            // so the alternative is destroying the record to mark it finished. It
            // stays Active on disk, which the sweeper also refuses to touch, so the
            // file survives for a human to look at.
            WarnUnreadable(sessionId, "close");
            return null;
        }

        if (session == null)
        {
            _logger.Warn($"Cannot end session: {sessionId} not found");
            return null;
        }

        if (session.Status != SessionStatus.Active)
        {
            _logger.Warn($"Cannot end session: {sessionId} already completed");
            return session;
        }

        session.Ended = DateTime.Now;
        session.Status = SessionStatus.Completed;
        if (!string.IsNullOrEmpty(closedBy))
        {
            session.ClosedBy = closedBy;
        }

        WriteSessionFile(session);

        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = null;
        }

        var durationMinutes = (int)(session.Ended.Value - session.Started).TotalMinutes;
        _logger.Info($"Session ended: {sessionId} ({durationMinutes}m, {session.EntriesCreated.Count} entries)");

        return session;
    }

    /// <summary>
    /// Mark a session abandoned: its owning process died without ever reporting an
    /// end. Deliberately writes no `ended` value - none was observed, and inventing
    /// one would make a guess indistinguishable from a witnessed close.
    /// </summary>
    public Session? MarkAbandoned(string sessionId)
    {
        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // The sweeper skips unreadable records before it ever gets here, so this
            // is the guard for anyone else who calls the method directly. Same
            // reasoning: an unreadable file is evidence of a crashed write, not of an
            // old session, and marking it abandoned would rewrite it from nothing.
            WarnUnreadable(sessionId, "abandon");
            return null;
        }

        if (session == null || session.Status != SessionStatus.Active)
            return session;

        session.Status = SessionStatus.Abandoned;
        session.ClosedBy = "swept";

        WriteSessionFile(session);

        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = null;
        }

        _logger.Info($"Session abandoned: {sessionId} (owner gone, no close reported)");

        return session;
    }

    /// <summary>
    /// Get the currently active session, if any.
    /// </summary>
    public Session? GetActiveSession()
    {
        if (_activeSession != null && _activeSession.Status == SessionStatus.Active)
        {
            // Verify against file - session may have been ended externally (MCP, another process)
            var fileSession = ParseSessionFile(GetSessionFilePath(_activeSession.SessionId));
            if (fileSession?.Status == SessionStatus.Active)
            {
                return _activeSession;
            }
            // Session was ended externally, clear cache
            _activeSession = null;
        }

        // Search for active session file
        var sessionsDir = GetSessionsDirectory();
        if (!Directory.Exists(sessionsDir))
            return null;

        var files = Directory.GetFiles(sessionsDir, "*.md")
            .OrderByDescending(f => f)
            .Take(10); // Check recent sessions only

        foreach (var file in files)
        {
            var session = ParseSessionFile(file);
            if (session?.Status == SessionStatus.Active)
            {
                _activeSession = session;
                return session;
            }
        }

        return null;
    }

    /// <summary>
    /// Get a session by ID. A read path: answers from the in-memory cache when the
    /// id matches, which is fine for reading but never acceptable as the source for
    /// a whole-file rewrite. Mutating paths use <see cref="ReadForMutation"/>.
    /// </summary>
    public Session? GetSession(string sessionId)
    {
        if (_activeSession?.SessionId == sessionId)
            return _activeSession;

        var filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
            return null;

        return ParseSessionFile(filePath);
    }

    /// <summary>
    /// Whether a parsed record can be vouched for as a real session.
    ///
    /// <see cref="ParseSessionFile"/> does not validate: it returns null only when it
    /// throws, so a truncated or header-less file parses SUCCESSFULLY into a record
    /// with Started = default, Status = Active (a missing `status:` line falls to the
    /// default arm) and empty entry lists. Nothing about that shape distinguishes it
    /// from a genuine record downstream, which is why every consumer that acts on a
    /// record has to ask this question explicitly.
    ///
    /// `started` is the one field that can decide it: always written, written first,
    /// and its default is distinguishable from every legitimate value. `agent` and
    /// `route` have legitimate empty values, so their absence proves nothing.
    /// </summary>
    public static bool IsReadableRecord(Session session) => session.Started != default;

    /// <inheritdoc cref="IsReadableRecord(Session)"/>
    public static bool IsReadableRecord(SessionSummary summary) => summary.Started != default;

    /// <summary>
    /// How <see cref="ReadForMutation"/> answered: a record to rewrite from, nothing
    /// at all, or a file that exists but cannot be read as a session.
    /// </summary>
    private enum MutationRead
    {
        Ok,
        Missing,
        Unreadable
    }

    /// <summary>
    /// The record as it currently exists on disk, for the paths that rewrite the
    /// whole file from a <see cref="Session"/> object.
    ///
    /// Deliberately not <see cref="GetSession"/>. That returns the cached
    /// `_activeSession` by reference when the id matches, and rewriting from a
    /// cached model silently erases every header another process wrote since this
    /// one last looked. The case that motivated this: at `/exit` the CLI hook
    /// process writes a `transcript:` header, then the MCP process's shutdown close
    /// rewrites the file from its own cached object whose TranscriptId is null, and
    /// the link is gone. The defect is general - any field, any writer - so the rule
    /// is read, apply, write.
    ///
    /// Falls back to the cache only when there is no file at all. That preserves
    /// GetSession's behaviour for the missing-file case rather than inventing a new
    /// failure mode for it.
    ///
    /// Refuses outright when the file exists but is not a readable record
    /// (<see cref="IsReadableRecord(Session)"/>). Rewriting from that parse would
    /// discard the `## Entries Created` list - the only account of what the session
    /// did - and stamp `started: 0001-01-01T00:00:00`, a value that reads as
    /// fabricated rather than absent. The cache is NOT used as a substitute here: the
    /// file existing means some writer owns it, and rewriting it from this process's
    /// model is exactly the stale-cache defect this method was introduced to fix. A
    /// truncated file is also the only forensic trace of what was lost. So the read
    /// is refused, the caller degrades, and the file is left for a human. The sweeper
    /// carries the same guard for the same reason.
    /// </summary>
    private Session? ReadForMutation(string sessionId, out MutationRead outcome)
    {
        var fromDisk = ParseSessionFile(GetSessionFilePath(sessionId));
        if (fromDisk != null)
        {
            if (!IsReadableRecord(fromDisk))
            {
                outcome = MutationRead.Unreadable;
                return null;
            }

            outcome = MutationRead.Ok;
            return fromDisk;
        }

        var cached = _activeSession?.SessionId == sessionId ? _activeSession : null;
        outcome = cached is null ? MutationRead.Missing : MutationRead.Ok;
        return cached;
    }

    /// <summary>
    /// A refused mutation must not be silently invisible: the write did not happen,
    /// and the fact it was asked for is the only remaining record of it. Single site
    /// so every caller reports the refusal in the same words, naming what it was
    /// trying to do.
    /// </summary>
    private void WarnUnreadable(string sessionId, string operation)
    {
        _logger.Warn(
            $"Session {sessionId}: refused {operation} - the record on disk has no readable " +
            "'started' header (truncated or malformed). File left untouched.");
    }

    /// <summary>
    /// List sessions matching query.
    /// </summary>
    public List<SessionSummary> GetSessions(SessionQuery query)
    {
        var sessionsDir = GetSessionsDirectory();
        if (!Directory.Exists(sessionsDir))
            return new List<SessionSummary>();

        var files = Directory.GetFiles(sessionsDir, "*.md");
        var sessions = new List<SessionSummary>();

        foreach (var file in files)
        {
            var session = ParseSessionFile(file);
            if (session == null)
                continue;

            // Apply filters
            if (query.Status.HasValue && session.Status != query.Status.Value)
                continue;

            if (!string.IsNullOrEmpty(query.Route) && !session.Route.Equals(query.Route.TrimStart('+'), StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(query.Agent) && !session.Agent.Equals(query.Agent, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(query.Date))
            {
                var sessionDate = session.Started.ToString("yyyy-MM-dd");
                if (sessionDate != query.Date)
                    continue;
            }

            var durationMinutes = session.Ended.HasValue
                ? (int)(session.Ended.Value - session.Started).TotalMinutes
                : session.Status == SessionStatus.Abandoned
                    // No observed end. Report the span to the last write when we have
                    // one, else zero. Never started -> now: that number would climb
                    // forever on a record that has been dead for months.
                    ? (int)Math.Max(0, ((session.LastActivity ?? session.Started) - session.Started).TotalMinutes)
                    : (int)(DateTime.Now - session.Started).TotalMinutes;

            sessions.Add(new SessionSummary
            {
                SessionId = session.SessionId,
                Started = session.Started,
                Ended = session.Ended,
                Agent = session.Agent,
                Route = session.Route,
                Status = session.Status,
                EntriesCreated = session.EntriesCreated.Count,
                EntriesModified = session.EntriesModified.Count,
                PendingReview = session.PendingReview.Count,
                DurationMinutes = durationMinutes
            });
        }

        return sessions
            .OrderByDescending(s => s.Started)
            .Take(query.Limit)
            .ToList();
    }

    /// <summary>
    /// Record an entry created during the session. Stored as "{entryPath}#{shortId}"
    /// so the SessionEntryGrouper can resolve the entry through EntryCache's short-id
    /// index when rendering the detail view. The shortId is required: every entry
    /// created via MCP carries a ShortId (NotationParser assigns one on save), and
    /// without it the grouper has no way to identify the entry within a year-file.
    /// </summary>
    public void RecordEntry(string sessionId, string entryPath, string shortId, bool needsReview = false)
    {
        if (string.IsNullOrEmpty(shortId))
            throw new ArgumentException("shortId is required to record a session entry", nameof(shortId));

        var entryRef = $"{entryPath}#{shortId}";

        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // Hot path: this runs right after an agent created an entry. The entry
            // itself is already saved in the vault by the time we get here, so what
            // is at stake is session bookkeeping, not the entry. Skip the write and
            // name the reference in the log: the bookkeeping gap stays recoverable,
            // while rewriting would destroy the whole record to add one line to it.
            // Throwing is worse still - it would fail create_entry AFTER the entry
            // was durably written, reporting a failure that did not happen.
            WarnUnreadable(sessionId, $"recording created entry {entryRef}");
            return;
        }

        if (session == null)
            return;

        if (!ContainsRef(session.EntriesCreated, entryRef))
        {
            session.EntriesCreated.Add(entryRef);
        }

        if (needsReview && !ContainsRef(session.PendingReview, entryRef))
        {
            session.PendingReview.Add(entryRef);
        }

        session.LastActivity = DateTime.Now;
        WriteSessionFile(session);

        // Update cache
        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = session;
        }
    }

    /// <summary>
    /// Record an entry modified during the session (e.g., task completion).
    /// When <paramref name="shortId"/> is non-null, the reference is stored as
    /// "{entryPath}#{shortId}" for block-granular resolution (matches RecordEntry's
    /// shape). When null, the reference is stored as a plain path -- used for
    /// system-file modifications (e.g., routes.md) that are not entries and have
    /// no shortId.
    /// </summary>
    public void RecordModification(string sessionId, string entryPath, string? shortId = null)
    {
        var entryRef = string.IsNullOrEmpty(shortId) ? entryPath : $"{entryPath}#{shortId}";

        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // Same trade as RecordEntry: the modification itself already landed in
            // the vault, only the session's account of it is lost, and the log keeps
            // that recoverable.
            WarnUnreadable(sessionId, $"recording modified entry {entryRef}");
            return;
        }

        if (session == null)
            return;

        if (!ContainsRef(session.EntriesModified, entryRef))
        {
            session.EntriesModified.Add(entryRef);
        }

        session.LastActivity = DateTime.Now;
        WriteSessionFile(session);

        // Update cache
        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = session;
        }
    }

    /// <summary>
    /// Record context loaded at session start.
    /// </summary>
    public void RecordContextLoaded(string sessionId, int decisions, int tasks, int blocks)
    {
        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // Purely descriptive metadata about what the session started with. Never
            // worth a whole-file rewrite from an empty parse.
            WarnUnreadable(sessionId, "recording loaded context");
            return;
        }

        if (session == null)
            return;

        session.ContextLoaded = new SessionContextSummary
        {
            DecisionsLoaded = decisions,
            TasksLoaded = tasks,
            BlocksLoaded = blocks
        };

        WriteSessionFile(session);

        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = session;
        }
    }

    /// <summary>
    /// Best-effort link from the hook side. First writer wins: a session belongs to
    /// the transcript that claimed it, and a later hook fire from a different
    /// transcript must not steal it. An unknown session is a no-op, because the hook
    /// runs whether or not the agent ever called start_session.
    /// </summary>
    public void LinkTranscript(string sessionId, string transcriptId)
    {
        if (string.IsNullOrEmpty(transcriptId)) return;

        var session = ReadForMutation(sessionId, out var read);
        if (read == MutationRead.Unreadable)
        {
            // A link is provenance. Writing one into a record whose own `started` is
            // unreadable would attach a true transcript id to a record that cannot
            // support any claim, and would cost the file to do it.
            WarnUnreadable(sessionId, $"linking transcript {transcriptId}");
            return;
        }

        if (session is null) return;
        if (!string.IsNullOrEmpty(session.TranscriptId)) return;

        session.TranscriptId = transcriptId;
        WriteSessionFile(session);

        // Refresh the read cache. It used to stay coherent by accident: the mutation
        // ran against the very object the cache held. Now that the write is built
        // from a fresh disk read, the cache has to be pointed at the new object
        // explicitly, exactly as RecordEntry and RecordModification do.
        if (_activeSession?.SessionId == sessionId)
        {
            _activeSession = session;
        }

        _logger.Info($"Session {sessionId} linked to transcript {transcriptId}");
    }

    /// <summary>
    /// Find the session that claimed an agent transcript, or null when no session
    /// linked it. Hook-written entries record the transcript id (not a session id)
    /// in their `session:` metadata, so this is the only way back from such an
    /// entry to its session record.
    /// </summary>
    public Session? FindByTranscriptId(string transcriptId)
    {
        var file = FindFileByTranscriptId(GetSessionsDirectory(), transcriptId);
        return file == null ? null : ParseSessionFile(file);
    }

    /// <summary>
    /// Resolve a session reference to its file on disk, accepting either id space
    /// that reaches the `session:` field: a LogMark session id (yyyy-MM-dd-hash,
    /// written by the MCP path) or an agent transcript id (written by the hook
    /// path, resolvable only once a session claimed it). Returns null when the
    /// reference names nothing - callers must not fabricate a path from the raw
    /// reference, because for a transcript id there is no such file.
    /// </summary>
    public static string? ResolveSessionFilePath(string vaultPath, string sessionRef)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || string.IsNullOrWhiteSpace(sessionRef))
            return null;

        // A session reference is vault content: keep it to a bare file name so it
        // cannot address anything outside _sessions/.
        if (sessionRef.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        var sessionsDir = Path.Combine(vaultPath, "logmark", "_sessions");

        var direct = Path.Combine(sessionsDir, $"{sessionRef}.md");
        if (File.Exists(direct))
            return direct;

        return FindFileByTranscriptId(sessionsDir, sessionRef);
    }

    /// <summary>
    /// Scan session files for a `transcript:` header matching the given id. Reads
    /// only the header block (stops at the first `## ` section) rather than parsing
    /// whole files, since this runs on the UI render path.
    /// </summary>
    private static string? FindFileByTranscriptId(string sessionsDir, string transcriptId)
    {
        if (string.IsNullOrWhiteSpace(transcriptId) || !Directory.Exists(sessionsDir))
            return null;

        foreach (var file in Directory.GetFiles(sessionsDir, "*.md"))
        {
            try
            {
                foreach (var rawLine in File.ReadLines(file))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith("## ", StringComparison.Ordinal))
                        break;
                    if (!line.StartsWith("transcript:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // A file carries at most one transcript header, so this line
                    // decides the file either way - match, or move to the next.
                    if (string.Equals(line.Substring(11).Trim(), transcriptId, StringComparison.OrdinalIgnoreCase))
                        return file;
                    break;
                }
            }
            catch (IOException)
            {
                // A session file being rewritten mid-scan is not worth failing the
                // whole lookup over.
            }
        }

        return null;
    }

    private string GenerateSessionId()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var randomBytes = RandomNumberGenerator.GetBytes(4);
        var hash = Convert.ToHexString(randomBytes).ToLowerInvariant().Substring(0, 6);
        return $"{date}-{hash}";
    }

    private string GetSessionsDirectory()
    {
        return Path.Combine(_vaultPath, "logmark", "_sessions");
    }

    private string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(GetSessionsDirectory(), $"{sessionId}.md");
    }

    private void WriteSessionFile(Session session)
    {
        var sessionsDir = GetSessionsDirectory();
        Directory.CreateDirectory(sessionsDir);

        var filePath = GetSessionFilePath(session.SessionId);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# Session: {session.SessionId}");
        sb.AppendLine();

        // Metadata
        sb.AppendLine($"started: {session.Started:O}");
        if (session.Ended.HasValue)
        {
            sb.AppendLine($"ended: {session.Ended:O}");
        }
        sb.AppendLine($"agent: {session.Agent}");
        if (!string.IsNullOrEmpty(session.TranscriptId))
        {
            sb.AppendLine($"transcript: {session.TranscriptId}");
        }
        if (!string.IsNullOrEmpty(session.ClosedBy))
        {
            sb.AppendLine($"closed: {session.ClosedBy}");
        }
        if (!string.IsNullOrEmpty(session.Owner))
        {
            sb.AppendLine($"owner: {session.Owner}");
        }
        if (session.HostPid.HasValue)
        {
            sb.AppendLine($"host-pid: {session.HostPid.Value}");
        }
        if (session.LastActivity.HasValue)
        {
            sb.AppendLine($"last-activity: {session.LastActivity.Value:O}");
        }
        sb.AppendLine($"route: +{session.Route}");
        sb.AppendLine($"status: {session.Status.ToString().ToLowerInvariant()}");
        sb.AppendLine();

        // Context loaded
        if (session.ContextLoaded != null)
        {
            sb.AppendLine("## Context Loaded");
            sb.AppendLine();
            sb.AppendLine($"- decisions: {session.ContextLoaded.DecisionsLoaded}");
            sb.AppendLine($"- tasks: {session.ContextLoaded.TasksLoaded}");
            sb.AppendLine($"- blocks: {session.ContextLoaded.BlocksLoaded}");
            sb.AppendLine();
        }

        // Entries created
        if (session.EntriesCreated.Count > 0)
        {
            sb.AppendLine("## Entries Created");
            sb.AppendLine();
            foreach (var entry in session.EntriesCreated)
            {
                sb.AppendLine($"- [[{NormalizeObsidianLink(entry)}]]");
            }
            sb.AppendLine();
        }

        // Entries modified
        if (session.EntriesModified.Count > 0)
        {
            sb.AppendLine("## Entries Modified");
            sb.AppendLine();
            foreach (var entry in session.EntriesModified)
            {
                sb.AppendLine($"- [[{NormalizeObsidianLink(entry)}]]");
            }
            sb.AppendLine();
        }

        // Pending review
        if (session.PendingReview.Count > 0)
        {
            sb.AppendLine("## Pending Review");
            sb.AppendLine();
            foreach (var entry in session.PendingReview)
            {
                sb.AppendLine($"- [[{NormalizeObsidianLink(entry)}]]");
            }
            sb.AppendLine();
        }

        if (_vaultWriter != null)
            _vaultWriter.WriteFile(filePath, sb.ToString());
        else
            File.WriteAllText(filePath, sb.ToString());
    }

    /// <summary>
    /// Whether a reference is already recorded, compared in the normalized space the
    /// file round-trip produces.
    ///
    /// Required because the mutating paths now read from disk: they hand in a raw
    /// ref ("logmark/_system/routes.md") while a parsed list holds the wiki-link form
    /// the writer emitted ("logmark/_system/routes"). A raw List.Contains would miss
    /// on every repeat call and append a duplicate line - UpdateRoutesTool records
    /// the same path on each routes edit, so that is the ordinary case, not an edge.
    /// </summary>
    private static bool ContainsRef(List<string> recorded, string entryRef)
    {
        var target = NormalizeObsidianLink(entryRef);
        return recorded.Any(r => string.Equals(NormalizeObsidianLink(r), target, StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalize path for Obsidian wiki links (forward slashes, no .md extension).
    /// </summary>
    private static string NormalizeObsidianLink(string path)
    {
        // Replace backslashes with forward slashes
        var normalized = path.Replace('\\', '/');

        // Handle block reference (path#id)
        var hashIndex = normalized.IndexOf('#');
        if (hashIndex > 0)
        {
            var filePart = normalized.Substring(0, hashIndex);
            var blockId = normalized.Substring(hashIndex);

            // Remove .md extension from file part
            if (filePart.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                filePart = filePart.Substring(0, filePart.Length - 3);
            }

            return filePart + blockId;
        }

        // No block reference - just remove .md extension
        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 3);
        }

        return normalized;
    }

    private Session? ParseSessionFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var content = File.ReadAllText(filePath);
            var lines = content.Split('\n');

            var session = new Session();

            // Parse session ID from filename
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            session.SessionId = fileName;

            var currentSection = "";

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim('\r').Trim();

                // Section headers
                if (line.StartsWith("## "))
                {
                    currentSection = line.Substring(3).Trim().ToLowerInvariant();
                    continue;
                }

                // Metadata lines
                if (line.StartsWith("started:", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParse(line.Substring(8).Trim(), out var started))
                        session.Started = started;
                    continue;
                }

                if (line.StartsWith("ended:", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParse(line.Substring(6).Trim(), out var ended))
                        session.Ended = ended;
                    continue;
                }

                if (line.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
                {
                    session.Agent = line.Substring(6).Trim();
                    continue;
                }

                if (line.StartsWith("transcript:", StringComparison.OrdinalIgnoreCase))
                {
                    session.TranscriptId = line.Substring(11).Trim();
                    continue;
                }

                if (line.StartsWith("closed:", StringComparison.OrdinalIgnoreCase))
                {
                    session.ClosedBy = line.Substring(7).Trim();
                    continue;
                }

                if (line.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
                {
                    session.Owner = line.Substring(6).Trim();
                    continue;
                }

                if (line.StartsWith("host-pid:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring(9).Trim(), out var hostPid))
                        session.HostPid = hostPid;
                    continue;
                }

                if (line.StartsWith("last-activity:", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParse(line.Substring(14).Trim(), out var lastActivity))
                        session.LastActivity = lastActivity;
                    continue;
                }

                if (line.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
                {
                    session.Route = line.Substring(6).Trim().TrimStart('+');
                    continue;
                }

                if (line.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                {
                    var statusStr = line.Substring(7).Trim();
                    session.Status = statusStr.ToLowerInvariant() switch
                    {
                        "completed" => SessionStatus.Completed,
                        "abandoned" => SessionStatus.Abandoned,
                        _ => SessionStatus.Active
                    };
                    continue;
                }

                // Section content
                if (line.StartsWith("- ") && line.Contains("[["))
                {
                    var linkMatch = ObsidianLinkPattern().Match(line);
                    if (linkMatch.Success)
                    {
                        var path = linkMatch.Groups[1].Value;
                        if (currentSection == "entries created")
                        {
                            session.EntriesCreated.Add(path);
                        }
                        else if (currentSection == "entries modified")
                        {
                            session.EntriesModified.Add(path);
                        }
                        else if (currentSection == "pending review")
                        {
                            session.PendingReview.Add(path);
                        }
                    }
                    continue;
                }

                // Context loaded section
                if (currentSection == "context loaded" && line.StartsWith("- "))
                {
                    session.ContextLoaded ??= new SessionContextSummary();
                    var contextMatch = ContextLinePattern().Match(line);
                    if (contextMatch.Success)
                    {
                        var key = contextMatch.Groups[1].Value.ToLowerInvariant();
                        if (int.TryParse(contextMatch.Groups[2].Value, out var count))
                        {
                            if (key == "decisions")
                                session.ContextLoaded.DecisionsLoaded = count;
                            else if (key == "tasks")
                                session.ContextLoaded.TasksLoaded = count;
                            else if (key == "blocks")
                                session.ContextLoaded.BlocksLoaded = count;
                        }
                    }
                }
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to parse session file {filePath}: {ex.Message}");
            return null;
        }
    }

    // Match [[path]] or [[path|display]]
    [GeneratedRegex(@"\[\[([^\]|]+)")]
    private static partial Regex ObsidianLinkPattern();

    // Match "- key: value" in context loaded section
    [GeneratedRegex(@"^- (\w+):\s*(\d+)")]
    private static partial Regex ContextLinePattern();
}
