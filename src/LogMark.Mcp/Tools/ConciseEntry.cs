using System.Text.Json.Serialization;
using LogMark.Services.EntryCache;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Trimmed per-entry shape for list-returning MCP tools.
///
/// A full ResolvedEntry costs ~525 bytes of metadata per entry (session GUID,
/// vault path, line numbers, EntryId and ShortId carrying the same value)
/// before any content. Across a 50-item list that metadata dominated a
/// measured 36KB get_project_context response and pushed it past real client
/// caps: Codex CLI silently middle-truncates tool results at ~10KB, grok
/// defaults to 20,000 bytes, Claude Code warns at 10,000 tokens. Survey:
/// docs/notes/2026-07-31-mcp-tool-result-size-research.md (bead lmk-1zld).
///
/// Lists carry this shape; read_entry(id) returns the full record. Id is the
/// short id when the entry has one - the form every mutating tool accepts -
/// falling back to the computed hash id for unaddressable entries.
/// </summary>
public class ConciseEntry
{
    public string Id { get; init; } = "";
    public string EntryType { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string Content { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Route { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; init; }

    // Emitted only when true, i.e. only in listings that include closed items.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCompleted { get; init; }

    // Task-specific (null for non-tasks)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Deadline { get; init; }

    // Closed-item semantics: only listings that opt into completed/resolved
    // items (include_completed, include_resolved) ever populate these, so
    // default open-item listings pay nothing.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resolution { get; init; }

    // Block-specific (null for non-blocks)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AgeDays { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsResolved { get; init; }

    // Closure state. Emitted always, unlike Resolution and IsResolved which
    // only appear in listings that opt into closed items: a consumer must be
    // able to tell open from closed without a second call.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lifecycle { get; init; }

    /// <summary>See ResolvedEntry.Unaddressable - read-only entry, id cannot mutate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Unaddressable { get; init; }

    public static ConciseEntry From(ResolvedEntry e) => new()
    {
        Id = e.ShortId ?? e.EntryId,
        EntryType = e.EntryType,
        Timestamp = e.Timestamp,
        Content = e.Content,
        Route = e.Route,
        Scope = e.Scope,
        IsCompleted = e.IsCompleted,
        Deadline = e.Deadline,
        Resolution = e.Resolution,
        AgeDays = e.AgeDays,
        IsResolved = e.IsResolved,
        Lifecycle = e.Lifecycle,
        Unaddressable = e.Unaddressable,
    };

    public static List<ConciseEntry> FromMany(IEnumerable<ResolvedEntry> entries)
        => entries.Select(From).ToList();
}
