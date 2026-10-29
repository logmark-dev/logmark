using LogMark.Services.EntryParser;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Entry with file location metadata for navigation and aggregation.
/// </summary>
public class IndexedEntry
{
    public ParsedEntry Entry { get; }
    public string FilePath { get; }
    public string? Route { get; }
    public string? Scope { get; }

    public IndexedEntry(ParsedEntry entry, string filePath, string? route, string? scope = null)
    {
        Entry = entry;
        FilePath = filePath;
        Route = route;
        Scope = scope;
    }
}
