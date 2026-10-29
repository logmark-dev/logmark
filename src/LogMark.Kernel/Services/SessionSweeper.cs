using System.Diagnostics;
using LogMark.Interfaces;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Real process lookup. A missing process, an access failure, or a start-time
/// mismatch all answer "not alive": the sweeper's job is to reclaim records whose
/// owner is gone, and an unanswerable query is not evidence of life.
/// </summary>
public sealed class SystemProcessLiveness : IProcessLiveness
{
    // ISO round-tripping loses sub-tick precision, and Process.StartTime is not
    // exact to the tick across queries. Compare with a tolerance rather than for
    // equality.
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    public bool IsAlive(int pid, DateTime startUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var actualStartUtc = process.StartTime.ToUniversalTime();
            return (actualStartUtc - startUtc).Duration() <= StartTimeTolerance;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Reclaims session records whose owning process died without closing them.
///
/// This exists only for the residue. The MCP server closes its own session on both
/// shutdown paths, so the ordinary case never reaches here; what does reach here is
/// an owner that was SIGKILLed or lost to a power cut, plus every record written
/// before owner stamping existed.
///
/// Run by whoever holds the vault-host role, so exactly one process sweeps a vault.
///
/// Only `started` is validated before acting, and that is a decision rather than an
/// oversight. It is the one field that is always written (first, right under the
/// title) and whose default is distinguishable from every legitimate value, so it is
/// the one guard that can block a fabricated timestamp. `agent` and `route` have
/// legitimate empty values, so absence proves nothing about them. A missing `status:`
/// line is the known residual: the parser maps anything it does not recognize to
/// Active, so a record truncated after `started` but before `status` still reaches
/// the staleness rule. That case loses nothing new - a write that dies partway
/// leaves a prefix, and `status` is written before the entry sections, so a record
/// missing its status line had already lost those sections; the rewrite re-emits what
/// the parse captured and adds only `status: abandoned` plus `closed: swept`, with no
/// invented `ended` and no invented `started`. Closing that residual would mean making
/// the parser report header presence, which ripples into GetActiveSession, GetSessions
/// and every UI and HTTP consumer, against its documented skip-unknown-lines
/// tolerance. Not worth it for a case that destroys nothing.
/// </summary>
public sealed class SessionSweeper
{
    private readonly SessionService _sessions;
    private readonly SystemLogger _logger;
    private readonly IProcessLiveness _liveness;
    private readonly TimeSpan _staleAfter;

    public SessionSweeper(
        SessionService sessions,
        SystemLogger logger,
        IProcessLiveness liveness,
        TimeSpan staleAfter)
    {
        _sessions = sessions;
        _logger = logger;
        _liveness = liveness;
        _staleAfter = staleAfter;
    }

    /// <summary>
    /// Mark every unreclaimed active record abandoned. Returns how many were marked.
    /// </summary>
    public int Sweep(DateTime now)
    {
        var active = _sessions.GetSessions(new SessionQuery
        {
            Status = SessionStatus.Active,
            Limit = int.MaxValue
        });

        var swept = 0;
        foreach (var summary in active)
        {
            var session = _sessions.GetSession(summary.SessionId);
            if (session == null || session.Status != SessionStatus.Active)
                continue;

            // A record with no readable `started` is not an old session, it is an
            // unreadable file - and the residue this sweeper exists to reclaim is
            // exactly that: a power cut mid-write leaves a truncated record. Parsing
            // one yields Started = 0001-01-01, which the staleness rule reads as
            // enormously old, so without this guard the sweep would rewrite the file
            // from the empty parse, discard whatever ## Entries Created survived, and
            // stamp a `started` that reads as fabricated rather than absent. The
            // predicate is shared with SessionService, which applies the same refusal
            // to every mutating path for the same reason.
            if (!SessionService.IsReadableRecord(session))
            {
                _logger.Warn(
                    $"Session sweep: skipped {session.SessionId} - no readable 'started' " +
                    "header (truncated or malformed record). Left untouched.");
                continue;
            }

            if (!ShouldAbandon(session, now))
                continue;

            _sessions.MarkAbandoned(session.SessionId);
            swept++;
        }

        if (swept > 0)
            _logger.Info($"Session sweep: {swept} record(s) marked abandoned");

        return swept;
    }

    private bool ShouldAbandon(Session session, DateTime now)
    {
        // A stamped owner on this machine gives a precise answer, so use it and do
        // not fall through to the time heuristic either way.
        if (SessionOwner.TryParse(session.Owner, out var machine, out var pid, out var startUtc)
            && string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return !_liveness.IsAlive(pid, startUtc);
        }

        // Everything else - another machine's record, or a legacy record with no
        // stamp at all - can only be judged on elapsed time. LastActivity when we
        // have it, started when we do not; never file mtime, which sync rewrites.
        var lastSignal = session.LastActivity ?? session.Started;
        return now - lastSignal > _staleAfter;
    }
}
