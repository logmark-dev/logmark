using System.Linq;
using LogMark.Models;

namespace LogMark.Services.EntryParser;

/// <summary>
/// A parsed entry from a markdown file.
/// </summary>
public class ParsedEntry
{
    public DateTime Timestamp { get; init; }
    public string? Context { get; init; }           // From "| context" in header
    public string Content { get; init; } = string.Empty; // Raw content lines
    public int StartLine { get; init; }              // For editing/navigation (1-based)
    public int EndLine { get; init; }                // Inclusive
    public EntryType Type { get; init; }             // From file path or metadata
    public bool IsCompleted { get; init; }           // Strikethrough detection
    public bool HasParseError { get; init; }         // Malformed entry flag
    public string? ParseErrorMessage { get; init; }

    // Task-specific (null for non-tasks)
    public TaskMetadata? TaskMeta { get; init; }

    /// <summary>
    /// Resolution note text from a "resolved: ..." trailer line, populated for
    /// ANY entry type that carries one (the parser recognizes the line
    /// unconditionally - see EntryParser). For tasks this duplicates
    /// TaskMeta.Resolution (kept for backward compatibility with existing
    /// consumers); for blocks this is the only place the note surfaces, since
    /// blocks have no TaskMetadata. Null when no such line is present.
    /// </summary>
    public string? Resolution { get; init; }

    // Timetrack-specific (null for non-timetrack entries)
    public WorkflowMetadata? WorkflowMeta { get; init; }

    // Entry attribution (null = human-created, "agent:claude-code" = agent-created)
    public string? Source { get; init; }

    // Session grouping (format: "2026-02-04-abc123")
    public string? Session { get; init; }

    // Cross-entry references (format: ["task:b123bbe1", "decision:xyz123"])
    public IReadOnlyList<EntryRef> Refs { get; init; } = [];

    // Extended reference tokens (wiki-links, URLs, and entry refs)
    public IReadOnlyList<RefToken> RefTokens { get; init; } = [];

    /// <summary>
    /// Unique identifier for the entry. For tasks, comes from TaskMeta.ShortId.
    /// For other entry types, comes from _id: metadata line.
    /// May be null for legacy entries without stored IDs.
    /// </summary>
    public string? ShortId { get; init; }

    /// <summary>
    /// Catch-all for unrecognized key: value metadata lines (e.g. origin, via, rating).
    /// Only populated for lines appearing after the first content line.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtendedMetadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Distill provenance breadcrumbs, raw: turn indices for this entry plus
    /// the session's commits, mutated files, and evidence page. Null when the
    /// entry carries no evidence line.
    ///
    /// A named view over <see cref="ExtendedMetadata"/> rather than a parser
    /// branch of its own. The catch-all already consumes `evidence:` (its
    /// pattern is ^([a-z_]+): (.+)$) and consuming is what matters: an
    /// unconsumed metadata line falls through to the content body, and evidence
    /// in the body reaches distill prompts as closure-candidate text. Adding a
    /// dedicated branch would have re-implemented a guard that already holds.
    ///
    /// Raw, never parsed and reserialized. The vault text is the source of
    /// truth; a lossy round trip would let a rendering change silently rewrite
    /// provenance that was already recorded.
    /// </summary>
    public string? Evidence =>
        ExtendedMetadata.TryGetValue("evidence", out var v) ? v : null;

    /// <summary>
    /// True when the entry has an attached wiki body (subpage paste flow created
    /// a file at _&lt;type&gt;/pages/&lt;id&gt;.md and the entry content links to it).
    /// Used by EntryCard's wiki pill (RightRail) and the "Continues at wiki" card
    /// in EntryDetailPanel. Detects [[...pages/...]] links in Content, not refs:
    /// block anchors -- subpage-paste entries carry the wiki link inline in their
    /// content, not as a structured refs: token.
    /// </summary>
    public bool HasWiki => ContentParser.HasPageLinks(Content);
}

/// <summary>
/// Task-specific metadata extracted from _task: lines.
/// </summary>
public class TaskMetadata
{
    public DateTime? Deadline { get; init; }
    public int PostponedCount { get; init; }
    public string ShortId { get; init; } = string.Empty;
    public string? Resolution { get; init; }
}

/// <summary>
/// Workflow-specific metadata extracted from workflow entries.
/// </summary>
public class WorkflowMetadata
{
    public string Description { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public TimeSpan TotalDuration { get; init; }
    public bool IsRunning { get; init; }
    public List<TimeSegment> Timeline { get; init; } = new();
}
