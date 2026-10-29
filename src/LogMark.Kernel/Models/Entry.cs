namespace LogMark.Models;

/// <summary>
/// A captured entry with parsed notation.
/// </summary>
public class Entry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string RawContent { get; set; } = string.Empty;
    public string ExpandedContent { get; set; } = string.Empty;

    // Parsed notation
    public EntryType? Type { get; set; }
    public string? CustomTypePrefix { get; set; } // For custom entry types
    public List<string> Tags { get; set; } = [];
    public List<string> Projects { get; set; } = [];

    // +words a surface decided are prose, not routing ("ASX +Security
    // Description"). Kept verbatim when notation is stripped on write.
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> ProseRouteTokens { get; set; } = [];

    // Temporal
    public Deadline? Deadline { get; set; }

    // Task tracking (populated during save for tasks with deadlines)
    public string? ShortId { get; set; }

    // Context
    public string? Context { get; set; }
    public string? VaultPath { get; set; }

    // Paste attribution
    public PasteContext? PasteContext { get; set; }

    // Entry attribution (for AI agent workflow)
    // null = human-created, "agent:claude-code" = agent-created
    public string? Source { get; set; }

    // Session grouping (for AI agent workflow)
    // Format: "2026-02-04-abc123" (date + short hash)
    public string? Session { get; set; }

    // Cross-entry references (for AI agent workflow)
    // Format: ["task:b123bbe1", "decision:xyz123"]
    public List<EntryRef> Refs { get; set; } = [];

    // Distill provenance breadcrumbs, written as an "evidence: " metadata line.
    // Format: "turns:237,643; commits:LogMark@e1f8d03; files:A.cs +10; page:[[...]]"
    //
    // Write-side twin of ParsedEntry.Evidence, which reads the same line back
    // out of ExtendedMetadata. Stored raw rather than as a parsed structure:
    // the vault text is the source of truth, and a parse-then-reserialize round
    // trip would let a rendering change silently rewrite recorded provenance.
    public string? Evidence { get; set; }
}
