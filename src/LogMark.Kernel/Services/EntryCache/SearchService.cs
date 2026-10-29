using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Services.EntryParser;
using LogMark.Services.Routes;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Query layer over EntryCache. Provides composable search, filtering, and
/// convenience methods that replace scattered file-enumeration code in App and MCP.
/// </summary>
public class SearchService
{
    private static readonly Regex ShortIdPattern = new(@"^[0-9a-fA-F]{8}$", RegexOptions.Compiled);

    private readonly EntryCache _cache;

    public SearchService(EntryCache cache)
    {
        _cache = cache;
    }

    /// <summary>The underlying cache, for folder-scoped queries that bypass the query DSL.</summary>
    public EntryCache GetCache() => _cache;

    /// <summary>
    /// Execute a composable search query against the cache.
    /// </summary>
    public SearchResult Search(SearchQuery query)
    {
        // Fast path: if query text looks like a ShortId, try direct lookup first
        if (!string.IsNullOrEmpty(query.Text) && ShortIdPattern.IsMatch(query.Text))
        {
            var resolved = _cache.ResolveShortId(query.Text);
            if (resolved != null)
            {
                var (filePath, entry) = resolved.Value;
                var route = ExtractRouteFromCache(filePath);
                var scope = ExtractScopeFromCache(filePath, route);
                var indexed = new IndexedEntry(entry, filePath, route, scope);

                // Still apply other filters (type, route, completion, etc.)
                if (PassesFilters(indexed, query, _cache.Routes))
                {
                    return new SearchResult(new[] { indexed }, 1);
                }
            }
        }

        // Full path: enumerate all entries, filter, sort, limit
        var all = _cache.GetAllEntries();
        var routes = _cache.Routes;
        var filtered = all.Where(ie => PassesFilters(ie, query, routes));

        if (!string.IsNullOrEmpty(query.Text))
        {
            filtered = filtered.Where(ie => MatchesText(ie, query.Text));
        }

        // Materialize for count before sorting/limiting
        var materialised = filtered.ToList();
        var totalCount = materialised.Count;

        // Sort once, eagerly. ApplySort returns a lazy OrderedEnumerable, and
        // the sorted set is handed out as AllMatches as well as being paged -
        // enumerating it twice would re-sort.
        var sorted = ApplySort(materialised, query.Sort).ToList();
        var limited = sorted.Count <= query.Limit ? sorted : sorted.Take(query.Limit).ToList();

        return new SearchResult(limited, totalCount, sorted);
    }

    /// <summary>
    /// Get entries from today, with optional additional filtering.
    /// </summary>
    public SearchResult GetToday(SearchQuery? filter = null)
    {
        var today = DateTime.Today;
        var q = (filter ?? new SearchQuery()) with
        {
            After = today,
            Before = today.AddDays(1)
        };
        return Search(q);
    }

    /// <summary>
    /// Get entries from the last N days, with optional additional filtering.
    /// </summary>
    public SearchResult GetRecent(int days = 7, SearchQuery? filter = null)
    {
        var cutoff = DateTime.Today.AddDays(-days);
        var q = (filter ?? new SearchQuery()) with
        {
            After = cutoff
        };
        return Search(q);
    }

    /// <summary>
    /// Get entries for a TimelineWindow preset. Thin wrapper that maps the
    /// window enum to After/Before bounds on a SearchQuery and delegates to Search.
    /// All -> unbounded; Today -> today's 24h; Last7Days/Last30Days -> from
    /// (today - N + 1) inclusive to tomorrow exclusive.
    /// </summary>
    public SearchResult GetWindow(TimelineWindow window)
    {
        var (after, before) = window.ToRange(DateTime.Today);
        var query = new SearchQuery
        {
            After = after,
            Before = before,
            Limit = int.MaxValue,
        };
        return Search(query);
    }

    #region Filter helpers

    private static bool PassesFilters(IndexedEntry ie, SearchQuery query, RouteLocator? routes)
    {
        var entry = ie.Entry;

        // Type filter
        if (query.Types is { Count: > 0 } && !query.Types.Contains(entry.Type))
            return false;

        // Route filter. Parent-inclusive: a route nested in the filter route's
        // folder (fin-inv inside fin) matches too; each entry keeps its exact route.
        if (!string.IsNullOrEmpty(query.Route) &&
            !(routes ?? RouteLocator.Empty).IsWithin(ie.Route, query.Route))
            return false;

        // Scope filter. A scope belongs to one route, so with a scope the route
        // match is exact: route fin + scope tax-au never reaches into fin-inv.
        if (!string.IsNullOrEmpty(query.Scope))
        {
            if (!string.Equals(ie.Scope, query.Scope, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(query.Route) &&
                !string.Equals(ie.Route, query.Route, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Date range
        if (query.After.HasValue && entry.Timestamp < query.After.Value)
            return false;
        if (query.Before.HasValue && entry.Timestamp >= query.Before.Value)
            return false;

        // Completion filter
        if (query.IsCompleted.HasValue && entry.IsCompleted != query.IsCompleted.Value)
            return false;

        // Lifecycle filter. Deliberately AFTER the IsCompleted check and
        // BEFORE the limit is applied by the caller: TotalCount is taken from
        // the materialised filtered set, so putting closure here fixes the
        // rows, the page size and the count in one place. The three
        // pre-existing per-tool post-filters could fix none of the last two.
        if (query.Lifecycle == LifecycleFilter.Open)
        {
            if (entry.IsCompleted)
                return false;

            // Single source of truth for what dismissal means. EntryCache.IsDismissed
            // is the definition every other consumer already reads; duplicating its
            // body here would let the two diverge with no compiler signal.
            if (EntryCache.IsDismissed(entry))
                return false;

            // Blocks alone treat #resolved as closed. On a decision, #resolved is not
            // a closure tag, and folding it in here would hide live decisions from
            // every reader at once. Tags are extracted only on this branch: blocks are
            // a small minority of entries, so the second pass over content that
            // IsDismissed already made costs nothing on the common path.
            if (entry.Type == EntryType.Block &&
                NotationParser.ExtractTags(entry.Content)
                    .Contains("resolved", StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // Source filter
        if (!string.IsNullOrEmpty(query.Source) &&
            !string.Equals(entry.Source, query.Source, StringComparison.OrdinalIgnoreCase))
            return false;

        // Session filter
        if (!string.IsNullOrEmpty(query.Session) &&
            !string.Equals(entry.Session, query.Session, StringComparison.OrdinalIgnoreCase))
            return false;

        // Tag filters
        if (query.RequiredTags is { Count: > 0 } || query.ExcludedTags is { Count: > 0 })
        {
            var entryTags = NotationParser.ExtractTags(entry.Content);

            // Facet extraction re-runs a regex over entry.Content, so only pay for it
            // when at least one filter term is a facet path (contains '/' or ':').
            // Flat-only queries (the common case) never touch ExtractFacets.
            var needsFacets = (query.RequiredTags?.Any(IsFacetPathFilter) ?? false) ||
                               (query.ExcludedTags?.Any(IsFacetPathFilter) ?? false);
            var entryFacets = needsFacets
                ? NotationParser.ExtractFacets(entry.Content)
                : Array.Empty<TagFacet>();

            if (query.RequiredTags is { Count: > 0 } &&
                !query.RequiredTags.All(t => MatchesTagFilter(t, entryFacets, entryTags)))
                return false;

            if (query.ExcludedTags is { Count: > 0 } &&
                query.ExcludedTags.Any(t => MatchesTagFilter(t, entryFacets, entryTags)))
                return false;
        }

        return true;
    }

    // A filter term is a facet path (as opposed to a flat leaf filter) when it
    // contains a separator ('/' or ':') after stripping the leading '#'.
    private static bool IsFacetPathFilter(string filter) =>
        filter.TrimStart('#').Replace(':', '/').Contains('/');

    // Match a tag filter against an entry's precomputed facets and leaf tags.
    // Flat filters canonicalize both sides to the leaf (final segment), so #mcp
    // and #surface/mcp reconcile. Filters containing a separator ('/' or ':')
    // are facet paths and match the entry's facet FullPaths exactly -
    // #aspect/light does not match #status/light or a flat #light. Path filters
    // are MORE specific than leaf filters, not different.
    private static bool MatchesTagFilter(string filter, IReadOnlyList<TagFacet> entryFacets, IReadOnlyList<string> entryTags)
    {
        var raw = filter.TrimStart('#').Replace(':', '/');
        if (IsFacetPathFilter(filter))
        {
            return entryFacets.Any(f => f.FullPath.Equals(raw, StringComparison.OrdinalIgnoreCase));
        }
        var needle = NotationParser.LeafTag(raw);
        return entryTags.Contains(needle, StringComparer.OrdinalIgnoreCase);
    }

    // Whitespace-separated terms must all match, each in any field. A whole
    // query as one substring missed entries whose words are present but not
    // adjacent ("Handoff mining" against "Handoff 2026-09-28 (mining").
    // Single-term queries behave exactly as before.
    private static bool MatchesText(IndexedEntry ie, string text)
    {
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return terms.Length <= 1
            ? MatchesTerm(ie, text)
            : terms.All(term => MatchesTerm(ie, term));
    }

    private static bool MatchesTerm(IndexedEntry ie, string text)
    {
        var entry = ie.Entry;

        if (entry.Content.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        if (entry.Context != null && entry.Context.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        if (entry.ShortId != null && entry.ShortId.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        if (entry.Source != null && entry.Source.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        if (entry.Session != null && entry.Session.Contains(text, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static IEnumerable<IndexedEntry> ApplySort(IEnumerable<IndexedEntry> entries, SearchSort sort)
    {
        return sort switch
        {
            SearchSort.TimestampDesc => entries.OrderByDescending(ie => ie.Entry.Timestamp),
            SearchSort.TimestampAsc => entries.OrderBy(ie => ie.Entry.Timestamp),
            SearchSort.DeadlineAsc => entries
                .OrderBy(ie => ie.Entry.TaskMeta?.Deadline ?? DateTime.MaxValue)
                .ThenByDescending(ie => ie.Entry.Timestamp),
            _ => entries.OrderByDescending(ie => ie.Entry.Timestamp)
        };
    }

    #endregion

    #region Cache route/scope extraction

    // Delegates to the cache rather than re-deriving from the path: a routes.md
    // stanza can name a route differently from its folder, and private copies
    // here reported the folder name and lost the scope.
    private string? ExtractRouteFromCache(string filePath) => _cache.GetRouteForPath(filePath);

    private string? ExtractScopeFromCache(string filePath, string? route) => _cache.LocatePath(filePath).Scope;

    #endregion
}

/// <summary>
/// Result of a search query. Contains the matching entries and total count before limiting.
/// </summary>
public class SearchResult
{
    /// <summary>The page of results, capped by <see cref="SearchQuery.Limit"/>.</summary>
    public IReadOnlyList<IndexedEntry> Entries { get; }

    /// <summary>How many entries matched, before Limit was applied.</summary>
    public int TotalCount { get; }

    /// <summary>
    /// Every matching entry in sort order, ignoring Limit - the set
    /// <see cref="TotalCount"/> counts. Callers that summarize a query
    /// (distributions, facet counts, per-day buckets) MUST aggregate over this
    /// rather than over <see cref="Entries"/>, or their figures describe only
    /// the newest page while being labelled with the full TotalCount. Holding
    /// it costs one reference per match - the IndexedEntry instances are the
    /// cache's own and are not copied.
    /// Defaults to <see cref="Entries"/> for the ShortId fast path and for the
    /// many hand-built SearchResults in tests, where page == match set.
    /// </summary>
    public IReadOnlyList<IndexedEntry> AllMatches { get; }

    public SearchResult(
        IReadOnlyList<IndexedEntry> entries,
        int totalCount,
        IReadOnlyList<IndexedEntry>? allMatches = null)
    {
        Entries = entries;
        TotalCount = totalCount;
        AllMatches = allMatches ?? entries;
    }
}
