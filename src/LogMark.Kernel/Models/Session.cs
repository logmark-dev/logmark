namespace LogMark.Models;

/// <summary>
/// Represents an agent work session that groups related entries.
/// Sessions enable humans to understand what an agent did and review outputs.
/// </summary>
public class Session
{
    /// <summary>
    /// Unique session identifier (format: YYYY-MM-DD-{hash}).
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// When the session started.
    /// </summary>
    public DateTime Started { get; set; }

    /// <summary>
    /// When the session ended (null if still active).
    /// </summary>
    public DateTime? Ended { get; set; }

    /// <summary>
    /// Agent identifier (e.g., "claude-code", "cursor").
    /// </summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>
    /// Focus route for this session (e.g., "+logmark").
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Session status.
    /// </summary>
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    /// <summary>
    /// Host transcript identifier (the Claude Code / Codex session uuid), or null
    /// when nothing linked it. The distill pipeline keys everything by this id
    /// while session records key by their own YYYY-MM-DD-hash; without the link
    /// the two id spaces never meet.
    /// </summary>
    public string? TranscriptId { get; set; }

    /// <summary>
    /// How this session's boundary was established, or null for a legacy record
    /// written before close provenance existed. Values: "agent" (explicit
    /// end_session), "host-exit" (MCP server shutdown, either path),
    /// "hook-exit" (the agent host's session-end hook, via agent-capture),
    /// "idle-timeout" (the in-process 30-minute check), "superseded"
    /// (start_session replacing it), "vault-switch" (the MCP vault watcher),
    /// "swept" (the sweeper).
    ///
    /// Worth distinguishing because only some of these observed the end. The
    /// idle-timeout path stamps detection time, not last activity, so its
    /// `ended` value is an inference wearing the same shape as an observation.
    /// </summary>
    public string? ClosedBy { get; set; }

    /// <summary>
    /// The process that owns this record, as "{machine}:{pid}:{start-utc-iso}".
    /// See <see cref="LogMark.Services.SessionOwner"/>. Null on legacy records.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// PID of the agent host that launched the owning MCP process (the Claude Code
    /// or Claude Desktop process). Null on legacy records, and on platforms where
    /// the parent cannot be resolved - ParentProcess is Windows-only. Stamped for
    /// the follow-on hook fix, which needs to match a record to the host whose
    /// SessionEnd hook is firing.
    /// </summary>
    public int? HostPid { get; set; }

    /// <summary>
    /// When this record was last written to. The sweeper's fallback signal for
    /// records it cannot check for liveness. File mtime is deliberately not used:
    /// the vault syncs across machines and sync rewrites mtimes.
    /// </summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Paths to entries created during this session.
    /// </summary>
    public List<string> EntriesCreated { get; set; } = new();

    /// <summary>
    /// Paths to entries modified during this session (e.g., task completions).
    /// </summary>
    public List<string> EntriesModified { get; set; } = new();

    /// <summary>
    /// Entry paths pending human review.
    /// </summary>
    public List<string> PendingReview { get; set; } = new();

    /// <summary>
    /// Context loaded at session start (decisions, tasks, etc.).
    /// </summary>
    public SessionContextSummary? ContextLoaded { get; set; }
}

/// <summary>
/// Session status enumeration.
/// </summary>
public enum SessionStatus
{
    Active,
    Completed,

    /// <summary>
    /// No process ever reported this session ending: its owner died without notice
    /// and the sweeper marked it. Carries no `ended` value, because none was
    /// observed. Distinct from Completed, which always had a witness.
    /// </summary>
    Abandoned
}

/// <summary>
/// Summary of context loaded at session start.
/// </summary>
public class SessionContextSummary
{
    public int DecisionsLoaded { get; set; }
    public int TasksLoaded { get; set; }
    public int BlocksLoaded { get; set; }
}

/// <summary>
/// Lightweight session summary for listing.
/// </summary>
public class SessionSummary
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
    public string Agent { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public SessionStatus Status { get; set; }
    public int EntriesCreated { get; set; }
    public int EntriesModified { get; set; }
    public int PendingReview { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>
/// Query parameters for listing sessions.
/// </summary>
public class SessionQuery
{
    public string? Route { get; set; }
    public string? Date { get; set; } // YYYY-MM-DD
    public string? Agent { get; set; }
    public SessionStatus? Status { get; set; }
    public int Limit { get; set; } = 50;
}
