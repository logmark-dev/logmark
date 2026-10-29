using LogMark.Interfaces;
using LogMark.Services.EntryCache;
using EntryCacheService = LogMark.Services.EntryCache.EntryCache;

namespace LogMark.Services.Search;

/// <summary>
/// Centralizes composition of the app's live search index: the entry cache plus
/// every Slice 2 document-family source (concepts, sessions, digests, chats,
/// subpages, feed cards). Startup previously built a bare
/// <c>InMemorySearchIndex(cache, semantic)</c> using the entry-only 2-arg
/// constructor, which left the non-entry families unsearchable in the running app.
/// </summary>
public static class SearchIndexFactory
{
    public static InMemorySearchIndex Create(
        EntryCacheService cache, string vaultPath, IFileAccessor files,
        ISemanticSearchService? semantic = null, ITimeProvider? time = null)
    {
        var logmarkPath = Path.Combine(vaultPath, "logmark");
        var sources = new IDocumentSource[]
        {
            new ConceptDocumentSource(files, logmarkPath),
            new SessionDocumentSource(files, logmarkPath),
            new DigestDocumentSource(files, logmarkPath),
            new ChatDocumentSource(files, logmarkPath),
            new SubpageDocumentSource(files, logmarkPath, () => cache.Routes),
            new FeedCardDocumentSource(files, logmarkPath),
        };

        return new InMemorySearchIndex(cache, sources, semantic, time);
    }
}
