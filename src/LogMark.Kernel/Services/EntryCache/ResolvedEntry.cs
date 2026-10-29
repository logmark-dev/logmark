using System.Text.Json.Serialization;
using LogMark.Models;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Unified entry representation for both MCP tools and App viewers.
/// Produced by EntryResolver from IndexedEntry or ParsedEntry.
/// </summary>
public class ResolvedEntry
{
    // Identity
    public string EntryId { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string EntryType { get; init; } = "";

    // Content
    public string Content { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentPreview { get; init; }

    // Location
    public string Path { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Route { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }

    public int StartLine { get; init; }
    public int EndLine { get; init; }

    // Classification
    public List<string> Tags { get; init; } = [];
    public bool IsCompleted { get; init; }

    // Attribution
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Session { get; init; }

    /// <summary>
    /// Distill provenance breadcrumbs, raw: this entry's turn indices plus the
    /// session's commits, changed files and evidence page. Surfaced so an agent
    /// reading an entry can follow the trail - git show a sha, grep a filename -
    /// without parsing vault markdown itself, which is the point of recording it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Evidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortId { get; init; }

    /// <summary>
    /// True when the entry carries no persisted id anchor, so EntryId is a
    /// computed hash rather than a stored one. Such an entry can be read but
    /// not mutated: the lookup layer indexes ShortId only, and the write layer
    /// finds an entry by matching its "_id:"/"_task:" line in file text.
    /// Completing, retyping, rerouting, and retagging all fail on it.
    ///
    /// Emitted only when true, so healthy entries pay nothing. It exists
    /// because the signal was previously implicit - a missing "shortId" field -
    /// and two grooming runs each burned about 19 approved actions discovering
    /// it the hard way. The remedy is "lm entry mint-anchors --apply", which
    /// anchors the id already served here, so ids a caller holds keep resolving
    /// (only an id colliding with another entry's is replaced).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Unaddressable { get; init; }

    // Task-specific (null for non-tasks)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Deadline { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PostponedCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resolution { get; init; }

    // Block-specific (null for non-blocks)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AgeDays { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsResolved { get; init; }

    /// <summary>
    /// Closure state as a single honest value: "open", "completed",
    /// "dismissed" or "resolved". IsResolved deliberately does NOT fold in
    /// dismissal - a dismissed item is closed but was never resolved - so this
    /// is the field a consumer reads to ask "is this still live".
    /// </summary>
    public string? Lifecycle { get; init; }

    // Relationships (controlled by ResolveOptions)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ResolvedLink> Links { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<ResolvedBacklink> Backlinks { get; init; } = [];
}

/// <summary>
/// A resolved link from an entry (wiki link or metadata ref). Wiki links are
/// "light file objects": the target is resolved the way the viewer resolves a
/// click (.md appended, vault-relative vs file-relative), validated for
/// existence, and classified so an agent can decide how to follow it -
/// entries-file/page targets via read_entry, external targets via its own
/// file tools.
/// </summary>
public class ResolvedLink
{
    public string Label { get; init; } = "";
    public string LinkType { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetPath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetShortId { get; init; }

    public bool IsResolved { get; init; }

    // Light-file-object fields, wiki links only (null on metadata refs).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Exists { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Anchor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SizeBytes { get; init; }
}

/// <summary>
/// A resolved backlink referencing this entry.
/// </summary>
public class ResolvedBacklink
{
    public string SourcePath { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string EntryType { get; init; } = "";
    public string ContentPreview { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryId { get; init; }
}
