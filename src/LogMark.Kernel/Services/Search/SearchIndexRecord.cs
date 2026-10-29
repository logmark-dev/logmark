using LogMark.Services.EntryParser;

namespace LogMark.Services.Search;

public record SearchIndexRecord(
    string Id,
    SearchFamily Family,
    string Title,
    string FilePath,
    string? Route,
    IReadOnlyList<string> Tags,
    DateTime Timestamp,
    string SearchText,
    DateTime? EntryTimestamp,
    ParsedEntry? SourceEntry = null);
