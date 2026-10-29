using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using EntryCacheService = LogMark.Services.EntryCache.EntryCache;

namespace LogMark.Services.Search;

/// <summary>
/// Adapts the file-watched <see cref="EntryCacheService"/> to <see cref="IDocumentSource"/>.
/// Unlike the document-family sources (concept, feed card, chat, ...), the entry cache is
/// already warmed and kept fresh by the vault's file watcher, so this source is read live on
/// every query rather than mtime-cached by the composing index. <see cref="NewestMtime"/>
/// always returns <see cref="DateTime.MinValue"/> and is not used to gate a rescan.
/// </summary>
public class EntryDocumentSource : IDocumentSource
{
    private readonly EntryCacheService _cache;

    public EntryDocumentSource(EntryCacheService cache)
    {
        _cache = cache;
    }

    public SearchFamily Family => SearchFamily.Entry;

    public DateTime NewestMtime() => DateTime.MinValue;

    public IEnumerable<SearchIndexRecord> GetRecords()
    {
        foreach (var ie in _cache.GetAllEntries())
        {
            yield return RecordFor(ie.Entry, ie.FilePath, ie.Route);
        }
    }

    internal static string FirstLine(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t;
        }
        return content.Trim();
    }

    /// <summary>
    /// Builds the search index record for a single entry, including the id fallback
    /// (short id when present, else file+line). Shared with <see cref="InMemorySearchIndex"/>'s
    /// semantic tier so the fallback cannot drift between the two callers - HitMerger dedup
    /// depends on both producing the same id for the same entry.
    /// </summary>
    internal static SearchIndexRecord RecordFor(ParsedEntry entry, string filePath, string? route)
    {
        var id = !string.IsNullOrEmpty(entry.ShortId) ? entry.ShortId! : $"entry:{filePath}#{entry.StartLine}";
        return new SearchIndexRecord(id, SearchFamily.Entry, FirstLine(entry.Content), filePath, route,
            NotationParser.ExtractTags(entry.Content), entry.Timestamp, entry.Content, entry.Timestamp, entry);
    }
}
