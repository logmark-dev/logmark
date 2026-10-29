using LogMark.Models;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Agents;

/// <summary>One handoff entry: what the previous stretch of work left behind.</summary>
public sealed record Handoff(string ShortId, string Content, DateTime Timestamp);

/// <summary>
/// Answers "which entry is the handoff for this window".
///
/// Recency-anchored by design decision, not by accident: the transcript uuid a
/// hook carries and the session record id an MCP write carries only meet
/// through AgentCaptureCommand.LinkTranscript, which refuses to link in the
/// ambiguous cases. Anchoring on identity today would return nothing on a
/// measurable share of real sessions.
///
/// This type is where handoff resolution lives, and it is the only place it
/// lives. lmk-continuation-anchor-by-session-identity-nhwh will change how
/// the answer is computed, from recency to session identity, keeping
/// recency as the documented fallback when identity resolution returns
/// nothing. That swap needs a new parameter carrying the session identity
/// and a one-line edit at each call site to pass it - both call sites
/// already have the id in scope, so this is a small mechanical change, not
/// a redesign.
/// </summary>
public static class HandoffResolver
{
    private const string HandoffTag = "handoff";
    private const string DiaryRefType = "diary";

    public static Handoff? Resolve(SearchService search, string? route, DateTime since)
    {
        var candidates = search.Search(new SearchQuery
        {
            Types = new[] { EntryType.Log },
            Route = route,
            After = since,
            Limit = 25,
            Sort = SearchSort.TimestampDesc
        }).Entries;

        if (candidates.Count == 0) return null;

        // The vault constitution defines #handoff as the checkpoint marker, so
        // an explicit tag always beats a newer untagged note. Falling straight
        // through to "newest" would let an incidental log written after the
        // checkpoint displace the checkpoint itself. An id-less entry is not
        // addressable and so cannot be returned even when tagged; it falls
        // through to the scan below rather than suppressing the answer.
        var tagged = candidates.FirstOrDefault(ie =>
            !string.IsNullOrEmpty(ie.Entry.ShortId)
            && NotationParser.ExtractTags(ie.Entry.Content)
                .Contains(HandoffTag, StringComparer.OrdinalIgnoreCase));
        if (tagged is not null) return ToHandoff(tagged.Entry);

        // Fallback scan, newest first. THE hook-only path: with no skill pack
        // installed nothing ever writes #handoff, so this branch is the only
        // branch that runs - and at a compaction boundary the newest Log in
        // route and window is deterministically the checkpoint stub PreCompact
        // wrote seconds earlier. Returning it made "Where you left off" a turn
        // count in exactly the path the design calls primary.
        //
        // Fixing it HERE and not at the payload call site is what breaks the
        // loop: the stub's own refs: line is resolved through this same method
        // at capture time, so an unfixed resolver would chain one compaction's
        // stub to the previous compaction's stub across a long session.
        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;

            if (AgentStubEntry.IsStub(entry.Content))
            {
                // The stub is a pointer, not a dead end: section 3 records the
                // handoff for that boundary on it. Following the ref can reach
                // a handoff OLDER than `since`, and that is correct - the ref
                // was resolved against the real checkpoint window, which is
                // tighter and better-founded than this method's fallback
                // window.
                var referenced = FollowRef(search, entry);
                if (referenced is not null) return referenced;
                continue;
            }

            if (string.IsNullOrEmpty(entry.ShortId)) continue;
            return ToHandoff(entry);
        }

        return null;
    }

    /// <summary>
    /// The handoff a checkpoint stub recorded on itself, or null.
    ///
    /// Never recurses. A ref written by a build that predates the fallback fix
    /// above can point at another stub; taking it would put the turn count back
    /// in the payload one indirection later.
    /// </summary>
    private static Handoff? FollowRef(SearchService search, ParsedEntry stub)
    {
        foreach (var reference in stub.Refs)
        {
            if (!string.Equals(reference.Type, DiaryRefType, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = search.GetCache().ResolveShortId(reference.Id);
            if (target is null) continue;

            var entry = target.Value.entry;
            if (AgentStubEntry.IsStub(entry.Content)) continue;
            if (string.IsNullOrEmpty(entry.ShortId)) continue;

            return ToHandoff(entry);
        }

        return null;
    }

    private static Handoff ToHandoff(ParsedEntry entry) =>
        new(entry.ShortId!, entry.Content, entry.Timestamp);
}
