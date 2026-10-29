namespace LogMark.Services.EntryCache;

/// <summary>
/// Controls what gets resolved when projecting an entry.
/// </summary>
public class ResolveOptions
{
    /// <summary>
    /// Resolve wiki links and metadata refs into Links list.
    /// </summary>
    public bool IncludeLinks { get; init; }

    /// <summary>
    /// Fetch backlinks from cache into Backlinks list.
    /// </summary>
    public bool IncludeBacklinks { get; init; }

    /// <summary>
    /// Truncate content to this length for ContentPreview. Null = no preview (full content only).
    /// </summary>
    public int? ContentPreviewLength { get; init; }

    /// <summary>
    /// Truncate Content ITSELF to this length. Null = full content.
    ///
    /// Distinct from <see cref="ContentPreviewLength"/>, which ADDS a
    /// ContentPreview field alongside the still-full Content and therefore
    /// makes a payload larger, not smaller. This one shrinks the payload.
    ///
    /// Added for bead lmk-1zld: get_project_context returned 51KB for a busy
    /// route, 75% of it full task bodies, which blew grok's 20_000-byte MCP
    /// tool-result cap. The client then spilled the payload to disk and the
    /// model wrote scripts to read back data it had just fetched. Entry ids
    /// are always returned, and read_entry(id) exists for drill-down, so a
    /// list result does not need whole bodies.
    /// </summary>
    public int? ContentMaxLength { get; init; }

    /// <summary>
    /// For search/list results: truncated content, no relationship resolution.
    /// </summary>
    public static ResolveOptions Preview(int length = 200) => new()
    {
        ContentPreviewLength = length
    };

    /// <summary>
    /// For typed queries (get_tasks, get_blocks): full content, no relationship resolution.
    /// </summary>
    public static ResolveOptions Query => new();

    /// <summary>
    /// For list-shaped tool results that carry many entries at once
    /// (get_project_context's ActiveTasks / OpenBlocks / RecentEntries):
    /// Content truncated in place, no relationship resolution. Callers that
    /// need a whole body follow the returned id with read_entry.
    /// </summary>
    public static ResolveOptions List(int contentMax = 200) => new()
    {
        ContentMaxLength = contentMax
    };

    /// <summary>
    /// For detail views (read_entry, detail panel): full content + links + backlinks.
    /// </summary>
    public static ResolveOptions Full => new()
    {
        IncludeLinks = true,
        IncludeBacklinks = true
    };
}
