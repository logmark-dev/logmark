using System.Collections.Concurrent;
using LogMark.Interfaces;
using LogMark.Services;
using LogMark.Services.EntryCache;
using EntryCacheService = LogMark.Services.EntryCache.EntryCache;

namespace LogMark.Services.Search;

public class InMemorySearchIndex : ISearchIndex
{
    private const double SemanticFloor = 0.35;
    private static readonly TimeSpan FreshnessTtl = TimeSpan.FromSeconds(2);
    private readonly EntryCacheService _cache;
    private readonly ISemanticSearchService? _semantic;
    private readonly ITimeProvider? _time;
    private readonly List<IDocumentSource> _sources;
    private readonly ConcurrentDictionary<IDocumentSource, (DateTime CheckedAt, DateTime Mtime, List<SearchIndexRecord> Records)> _sourceCache = new();

    public InMemorySearchIndex(EntryCacheService cache, ISemanticSearchService? semantic = null)
        : this(cache, Array.Empty<IDocumentSource>(), semantic)
    {
    }

    public InMemorySearchIndex(EntryCacheService cache, IEnumerable<IDocumentSource> documentSources, ISemanticSearchService? semantic = null, ITimeProvider? time = null)
    {
        _cache = cache;
        _semantic = semantic;
        _time = time;
        _sources = new List<IDocumentSource> { new EntryDocumentSource(cache) };
        _sources.AddRange(documentSources);
    }

    public async Task<IReadOnlyList<SearchHit>> QueryAsync(SearchRequest request, CancellationToken ct = default)
    {
        var q = QueryParser.Parse(request.Query);
        var routeFilter = request.ScopeRoute ?? (q.Routes.Count > 0 ? q.Routes[0] : null);
        var candidates = new List<SearchHit>();
        var routes = _cache.Routes;

        foreach (var record in AllRecords())
        {
            // Parent-inclusive: a filter on a route also matches routes nested in it.
            if (routeFilter != null && !routes.IsWithin(record.Route, routeFilter))
                continue;

            // Route-only browse (DESK169): a filter query with no free-text terms and no
            // #tag tokens, but a route filter, returns everything under that route. Tag-only
            // and tag+route queries keep flowing through TagMatcher below unchanged.
            if (q.Terms.Count == 0 && q.Tags.Count == 0 && routeFilter != null)
            {
                candidates.Add(new SearchHit(record, MatchReason.Browse, 1.0));
                continue;
            }

            var lex = LexicalScorer.Score(record.SearchText, q);
            if (lex > 0) candidates.Add(new SearchHit(record, MatchReason.Verbatim, lex));

            var tag = TagMatcher.Score(record.Tags, q);
            if (tag > 0) candidates.Add(new SearchHit(record, MatchReason.Tag, tag));
        }

        await AddSemanticAsync(q, routeFilter, candidates, ct);
        return HitMerger.Merge(candidates, request.Limit);
    }

    private IEnumerable<SearchIndexRecord> AllRecords()
    {
        foreach (var source in _sources)
        {
            foreach (var record in RecordsFor(source))
                yield return record;
        }
    }

    // The entry source (Family == Entry) is backed by the already-warmed, file-watched
    // EntryCache, so it is read fresh on every query rather than mtime-cached. Every other
    // document source is cached by GetRecords() and only rebuilt when NewestMtime() advances
    // past what was last observed (or there is no cache yet). NewestMtime() can be a full
    // recursive vault walk (e.g. SubpageDocumentSource), so the freshness check itself is
    // gated behind a short TTL: back-to-back searches within the window skip the walk and
    // reuse whatever was cached last, instead of calling NewestMtime() every query.
    private IEnumerable<SearchIndexRecord> RecordsFor(IDocumentSource source)
    {
        if (source.Family == SearchFamily.Entry) return source.GetRecords();

        var now = _time?.Now ?? DateTime.Now;
        if (_sourceCache.TryGetValue(source, out var cached) && now - cached.CheckedAt < FreshnessTtl)
            return cached.Records;   // within TTL: skip the walk entirely

        var newest = source.NewestMtime();
        if (_sourceCache.TryGetValue(source, out cached) && cached.Mtime >= newest)
        {
            _sourceCache[source] = (now, cached.Mtime, cached.Records);  // refresh check time only
            return cached.Records;
        }

        var records = source.GetRecords().ToList();
        _sourceCache[source] = (now, newest, records);
        return records;
    }

    private async Task AddSemanticAsync(ParsedQuery q, string? routeFilter, List<SearchHit> candidates, CancellationToken ct)
    {
        if (_semantic == null || q.Phrase == null) return;

        IReadOnlyList<SemanticSearchResult> results;
        try { results = await _semantic.SearchAsync(q.Phrase, topK: 20, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return; }

        foreach (var r in results)
        {
            if (r.Score < SemanticFloor) continue;
            var resolved = _cache.ResolveShortId(r.EntryId);
            if (resolved == null) continue;
            var (filePath, entry) = resolved.Value;
            var route = _cache.GetRouteForPath(filePath);
            if (routeFilter != null && !_cache.Routes.IsWithin(route, routeFilter))
                continue;

            candidates.Add(new SearchHit(EntryDocumentSource.RecordFor(entry, filePath, route), MatchReason.Related, 0.9 * r.Score));
        }
    }
}
