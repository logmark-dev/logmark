using LogMark.Models;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Composable query for searching cached entries.
/// All filter properties are optional -- null/empty means "no filter".
/// </summary>
public record SearchQuery
{
    /// <summary>Free-text search (matches content, context, ShortId, source, session).</summary>
    public string? Text { get; init; }

    /// <summary>Filter to specific entry types.</summary>
    public IReadOnlyList<EntryType>? Types { get; init; }

    /// <summary>Filter to a specific route name.</summary>
    public string? Route { get; init; }

    /// <summary>Filter to a specific scope within a route.</summary>
    public string? Scope { get; init; }

    /// <summary>Entries must have ALL of these tags.</summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }

    /// <summary>Exclude entries that have ANY of these tags.</summary>
    public IReadOnlyList<string>? ExcludedTags { get; init; }

    /// <summary>Only entries after this timestamp (inclusive).</summary>
    public DateTime? After { get; init; }

    /// <summary>Only entries before this timestamp (exclusive).</summary>
    public DateTime? Before { get; init; }

    /// <summary>Filter by completion state. Null = no filter.</summary>
    public bool? IsCompleted { get; init; }

    /// <summary>
    /// Lifecycle filter. Null (the default) preserves the pre-2026-08-19
    /// behaviour: only IsCompleted is consulted, so a dismissed entry reads as
    /// open. Open additionally excludes the tag-driven closed states.
    /// </summary>
    public LifecycleFilter? Lifecycle { get; init; }

    /// <summary>Filter by source attribution (e.g. "agent:mcp").</summary>
    public string? Source { get; init; }

    /// <summary>Filter by session ID.</summary>
    public string? Session { get; init; }

    /// <summary>Maximum results to return.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Sort order for results.</summary>
    public SearchSort Sort { get; init; } = SearchSort.TimestampDesc;
}

public enum SearchSort
{
    TimestampDesc,
    TimestampAsc,
    DeadlineAsc
}

/// <summary>
/// What "still open" means to a reader.
///
/// Completion is a strikethrough the parser sets on IsCompleted. Dismissal and
/// resolution are lifecycle TAGS with no strikethrough, deliberately so - a
/// dismissed task is not a completed one (lmk-task-dismiss-exit-xz5j). A reader
/// that filters on IsCompleted alone therefore returns closed work as open,
/// which is what put a January test artifact at the top of every agent's
/// briefing (lmk-nnd5).
/// </summary>
public enum LifecycleFilter
{
    /// <summary>Not completed, not #dismissed, and for blocks not #resolved.</summary>
    Open
}
