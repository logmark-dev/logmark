namespace LogMark.Models;

/// <summary>
/// A seed entry parsed from a seed.md file for vault initialization.
/// Used by BackfillEngine to create historical entries.
/// </summary>
public record SeedEntry(
    DateTime Date,
    EntryType Type,
    string Content,
    string? Route,
    IReadOnlyList<string> Tags,
    string? Source = null,
    string? Context = null
);
