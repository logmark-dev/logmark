using LogMark.Models;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Represents a backlink from one entry to another.
/// Used by BacklinkIndex to track which entries reference a given page or entry.
/// </summary>
public record BacklinkEntry(
    /// <summary>
    /// Full path to the file containing the reference.
    /// </summary>
    string SourceFilePath,

    /// <summary>
    /// Timestamp of the entry containing the reference.
    /// </summary>
    DateTime Timestamp,

    /// <summary>
    /// Entry type for display badge.
    /// </summary>
    EntryType Type,

    /// <summary>
    /// First ~60 chars of entry content for preview.
    /// </summary>
    string ContentPreview,

    /// <summary>
    /// Id of the entry holding the reference, when it has one: what an agent
    /// passes to read_entry to follow the backlink (bead lmk-gr8f).
    /// </summary>
    string? ShortId = null
);
