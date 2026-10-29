using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Represents a cached file with its parsed entries and metadata.
/// </summary>
public class CachedFile
{
    public DateTime LastModified { get; }
    public IReadOnlyList<ParsedEntry> Entries { get; }
    public EntryType? TypeHint { get; }

    public CachedFile(DateTime lastModified, IReadOnlyList<ParsedEntry> entries, EntryType? typeHint)
    {
        LastModified = lastModified;
        Entries = entries;
        TypeHint = typeHint;
    }
}
