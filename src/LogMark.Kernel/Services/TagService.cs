using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services;

// Lightweight projection of an entry for tag work; insulates TagService from EntryCache types.
public sealed record TagBearingEntry(
    IReadOnlyList<string> Tags,
    DateTime Timestamp,
    string Content,
    string FilePath,
    ParsedEntry Entry);

public interface IEntrySource
{
    IReadOnlyList<TagBearingEntry> EnumerateEntries();
}

public sealed record DetectedCluster(string Id, IReadOnlyList<string> Tags, double Confidence);

public interface IClusterSource
{
    IReadOnlyList<DetectedCluster> GetDetectedClusters();
}

public interface IManualClusterSource
{
    IReadOnlyList<ManualCluster> GetClusters();
}

public class TagService : ITagService
{
    private readonly IEntrySource _entrySource;
    private readonly IClusterSource _clusterSource;
    private readonly IManualClusterSource _manualClusters;

    public TagService(IEntrySource entrySource, IClusterSource clusterSource, IManualClusterSource manualClusters)
    {
        _entrySource = entrySource;
        _clusterSource = clusterSource;
        _manualClusters = manualClusters;
    }

    // Canonical collapses a tag to its leaf: delegates to NotationParser.LeafTag (the
    // single source of truth for the ':'->'/' normalization + last-segment rule).
    // Defensive trim and TrimStart('#') handle raw colon separators from older vaults
    // or test doubles where parser output has not already produced clean leaf tokens.
    private static string Canonical(string tag)
        => NotationParser.LeafTag(tag.Trim().TrimStart('#')).ToLowerInvariant();

    // Fix round (inc3-final-review R1/R4): full-path detection shared by every
    // method that resolves a query tag against entries -- originally added to
    // GetTagSummaryAsync alone (I1 fix), now shared so a facet page's entry
    // list, type breakdown, related tags, and clusters cannot disagree about
    // which entries the page is about. A tag containing a path separator ('/'
    // after normalizing ':' to '/') scopes matching to that exact facet path
    // (e.g. "phase/done" excludes entries carrying only "status/done"); a bare
    // leaf keeps the pre-existing leaf-collapsed aggregate-across-everything
    // behavior (that leaf reconciliation is deliberate elsewhere, e.g.
    // SearchService, and untouched by this).
    private static string NormalizedPath(string tag)
        => tag.Trim().TrimStart('#').Replace(':', '/').ToLowerInvariant();

    private static (string Canonical, bool IsFullPath) NormalizeQuery(string tag)
    {
        var normalized = NormalizedPath(tag);
        var slashIdx = normalized.IndexOf('/');
        var isFullPath = slashIdx > 0 && slashIdx < normalized.Length - 1;
        var canonical = isFullPath ? normalized : Canonical(tag);
        return (canonical, isFullPath);
    }

    private IReadOnlyList<TagBearingEntry> MatchingEntries(string canonical, bool isFullPath)
    {
        var entries = _entrySource.EnumerateEntries();
        return isFullPath
            ? entries
                .Where(e => NotationParser.ExtractTagPaths(e.Content)
                    .Any(p => string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : entries.Where(e => e.Tags.Any(t => Canonical(t) == canonical)).ToList();
    }

    // Leaf-collision fix (increment 3 Task 1): facet-derived tags (#key/value)
    // are listed by their full path instead of their leaf, so two facet keys
    // sharing a leaf value (#status/done, #phase/done) stay distinct rows
    // instead of silently collapsing into one leaf-keyed "done" row. Flat
    // tags (no separator) keep the original leaf-keyed behavior unchanged.
    // A leaf that a facet already contributed for this entry is skipped in
    // the flat pass, since e.Tags is already leaf-collapsed and cannot tell
    // a flat #done apart from a facet-derived #status/done sharing the same
    // leaf within one entry.
    //
    // This is deliberately scoped to the all-tags LISTING only. Leaf-based
    // reconciliation elsewhere (SearchService's flat-filter matching,
    // GetTagCount/GetTagCountsSince, co-occurrence) is untouched -- those
    // are separate concerns increment 1 and increment 3 Task 2 own.
    private static IEnumerable<string> TagKeysForListing(TagBearingEntry e)
    {
        var facets = NotationParser.ExtractFacets(e.Content);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in facets)
        {
            var path = f.FullPath.ToLowerInvariant();
            if (emitted.Add(path))
                yield return path;
        }

        // Fix round (inc3-final-review I2): a leaf whose only source on this
        // entry is a facet must still be skipped here -- the facet pass above
        // already emitted it as a full path -- but a leaf that ALSO has a
        // genuine flat occurrence on the same entry (both #done and
        // #status/done present) must survive, or the flat tag silently
        // vanishes from the Tags lens. e.Tags is already leaf-collapsed and
        // cannot tell the two apart by itself. Reuse
        // NotationParser.ExcludeFacetOnlyLeaves (Task 2, commit d38913f9),
        // which already draws exactly this per-entry distinction by
        // re-reading content through ExtractTagPaths, instead of
        // re-deriving a second, looser discrimination here.
        var flatEligible = NotationParser.ExcludeFacetOnlyLeaves(e.Tags.Select(Canonical), e.Content);
        foreach (var leaf in flatEligible)
        {
            if (emitted.Add(leaf))
                yield return leaf;
        }
    }

    public Task<IReadOnlyList<TagListItem>> GetAllTagsAsync(TagSortKey sort, CancellationToken ct)
    {
        var entries = _entrySource.EnumerateEntries();
        var groups = entries
            .SelectMany(e => TagKeysForListing(e).Select(t => new { Tag = t, e.Timestamp }))
            .GroupBy(x => x.Tag)
            .Select(g => new TagListItem(
                Tag: g.Key,
                EntryCount: g.Count(),
                LastUsed: g.Max(x => (DateTime?)x.Timestamp)
            ));

        IReadOnlyList<TagListItem> result = sort switch
        {
            TagSortKey.Frequency => groups
                .OrderByDescending(t => t.EntryCount)
                .ThenBy(t => t.Tag)
                .ToList(),
            TagSortKey.Recency => groups
                .OrderByDescending(t => t.LastUsed ?? DateTime.MinValue)
                .ThenBy(t => t.Tag)
                .ToList(),
            TagSortKey.Alphabetical => groups
                .OrderBy(t => t.Tag, StringComparer.Ordinal)
                .ToList(),
            _ => groups.ToList()
        };

        return Task.FromResult(result);
    }

    public int GetTagCount()
    {
        // Fix round minor: shares TagKeysForListing with GetAllTagsAsync so
        // this count and the Tags tab total agree (both full-path for
        // facet-derived tags, leaf for flat tags), instead of this staying
        // purely leaf-keyed while the tab went full-path-aware in increment
        // 3 Task 1. For a vault with no facet notation this is byte-identical
        // to the old raw-Canonical(entry.Tags) pass.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _entrySource.EnumerateEntries())
        {
            foreach (var key in TagKeysForListing(entry))
            {
                seen.Add(key);
            }
        }
        return seen.Count;
    }

    public (int Total, int AddedSince) GetTagCountsSince(DateTime lastSeenUtc)
    {
        // Track the earliest occurrence per canonical tag in a single pass. A
        // tag counts toward AddedSince iff its earliest occurrence is strictly
        // after lastSeen; that is, the tag did not exist in the vault at
        // last-visit time. Entry timestamps are local (FeedItem-style); the
        // threshold is converted to local for apples-to-apples comparison.
        //
        // Fix round minor: keys come from TagKeysForListing (see GetTagCount),
        // so Total agrees with the Tags tab header instead of disagreeing
        // whenever two facet keys share a leaf value.
        var lastSeenLocal = lastSeenUtc.Kind == DateTimeKind.Utc
            ? lastSeenUtc.ToLocalTime()
            : lastSeenUtc;

        var firstSeen = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var entry in _entrySource.EnumerateEntries())
        {
            foreach (var key in TagKeysForListing(entry))
            {
                if (firstSeen.TryGetValue(key, out var existing))
                {
                    if (entry.Timestamp < existing) firstSeen[key] = entry.Timestamp;
                }
                else
                {
                    firstSeen[key] = entry.Timestamp;
                }
            }
        }

        var addedSince = 0;
        foreach (var ts in firstSeen.Values)
        {
            if (ts > lastSeenLocal) addedSince++;
        }
        return (firstSeen.Count, addedSince);
    }

    public async Task<TagSummary?> GetTagSummaryAsync(string tag, int recentLimit, int relatedLimit, CancellationToken ct)
    {
        // Fix round (inc3-final-review I1): facet chips and full-path All-tags
        // rows navigate with the full path ("phase/done"), but this used to
        // always leaf-collapse via Canonical(tag), so #phase/done and
        // #status/done opened the byte-identical leaf-keyed "done" page. A
        // full-path query (contains a separator) now scopes matching to that
        // exact facet path via ExtractTagPaths; a bare-leaf query (no
        // separator) keeps the pre-existing aggregate-across-everything
        // behavior untouched -- that leaf reconciliation is deliberate
        // elsewhere (SearchService) and this method's own bare-leaf callers
        // (e.g. a genuinely flat tag chip) still depend on it.
        var (canonical, isFullPath) = NormalizeQuery(tag);
        var matching = MatchingEntries(canonical, isFullPath);

        if (matching.Count == 0)
            return null;

        var lastUsed = matching.Max(e => (DateTime?)e.Timestamp);
        var recent = matching
            .OrderByDescending(e => e.Timestamp)
            .Take(recentLimit)
            .Select(e => e.Entry)
            .ToList();

        var related = await GetCoOccurringTagsAsync(canonical, relatedLimit, ct);
        var clusters = await GetClustersContainingAsync(canonical, ct);

        return new TagSummary(
            Tag: canonical,
            EntryCount: matching.Count,
            LastUsed: lastUsed,
            RecentEntries: recent,
            RelatedTags: related,
            Clusters: clusters
        );
    }

    public Task<IReadOnlyList<RelatedTag>> GetCoOccurringTagsAsync(string tag, int top, CancellationToken ct)
    {
        var (canonical, isFullPath) = NormalizeQuery(tag);
        var entries = _entrySource.EnumerateEntries();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        if (isFullPath)
        {
            // Fix round (inc3-final-review R4): a facet page's Related strip
            // must reflect co-occurrence within the SAME entries the page
            // lists -- not a leaf-collapsed aggregate that can pull in
            // entries the page never shows (a flat "#done" entry) while
            // missing tags on the entries it does show (the facet-only leaf
            // was previously excluded from consideration entirely).
            // TagKeysForListing already emits full-path keys for facets and
            // flat-eligible leaves for genuine flat tags on the same entry --
            // exactly what "what else does this entry carry" means here.
            foreach (var entry in entries)
            {
                if (!NotationParser.ExtractTagPaths(entry.Content)
                        .Any(p => string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var other in TagKeysForListing(entry))
                {
                    if (string.Equals(other, canonical, StringComparison.Ordinal))
                        continue;
                    counts[other] = counts.TryGetValue(other, out var v) ? v + 1 : 1;
                }
            }
        }
        else
        {
            foreach (var entry in entries)
            {
                // Increment 3 Task 2: facet-derived leaves must not create
                // co-occurrence evidence -- facets are declared structure,
                // co-occurrence is emergent. Excluded per entry (see
                // NotationParser.ExcludeFacetOnlyLeaves), so a flat tag on a
                // different entry sharing the same leaf is unaffected.
                var entryCanonicals = NotationParser
                    .ExcludeFacetOnlyLeaves(entry.Tags.Select(Canonical), entry.Content)
                    .ToHashSet(StringComparer.Ordinal);
                if (!entryCanonicals.Contains(canonical))
                    continue;

                foreach (var other in entryCanonicals)
                {
                    if (other == canonical)
                        continue;
                    counts[other] = counts.TryGetValue(other, out var v) ? v + 1 : 1;
                }
            }
        }

        IReadOnlyList<RelatedTag> result = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(kv => new RelatedTag(kv.Key, kv.Value))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<FacetFamily>> GetFacetFamiliesAsync(CancellationToken ct)
    {
        var entries = _entrySource.EnumerateEntries();

        // key -> (value -> entry count).
        var valueCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        // key -> distinct entry count (family total). Tracked separately from
        // the sum of value counts so an entry carrying two values of the same
        // family contributes 1, not 2.
        var familyTotals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var facets = NotationParser.ExtractFacets(entry.Content);
            if (facets.Count == 0)
                continue;

            foreach (var keyGroup in facets.GroupBy(f => f.Key.ToLowerInvariant(), StringComparer.Ordinal))
            {
                var key = keyGroup.Key;
                if (!valueCounts.TryGetValue(key, out var values))
                {
                    values = new Dictionary<string, int>(StringComparer.Ordinal);
                    valueCounts[key] = values;
                }

                foreach (var value in keyGroup.Select(f => f.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
                {
                    values[value] = values.TryGetValue(value, out var v) ? v + 1 : 1;
                }

                familyTotals[key] = familyTotals.TryGetValue(key, out var t) ? t + 1 : 1;
            }
        }

        IReadOnlyList<FacetFamily> result = valueCounts
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FacetFamily(
                Key: kv.Key,
                Values: kv.Value
                    .OrderByDescending(vc => vc.Value)
                    .ThenBy(vc => vc.Key, StringComparer.Ordinal)
                    .Select(vc => new FacetValueCount(vc.Key, vc.Value))
                    .ToList(),
                Total: familyTotals[kv.Key]
            ))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<EntryType, int>> GetTypeBreakdownAsync(string tag, CancellationToken ct)
    {
        // Fix round (inc3-final-review R1): shares NormalizeQuery/MatchingEntries
        // with GetTagSummaryAsync so a full-path facet query ("phase/done")
        // scopes the type-breakdown bar to the same entries the page's own
        // header and entry list are built from, instead of leaf-collapsing
        // back to "done" and summing every sibling facet sharing that leaf.
        var (canonical, isFullPath) = NormalizeQuery(tag);
        var matching = MatchingEntries(canonical, isFullPath);

        var breakdown = matching
            .GroupBy(e => e.Entry.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult<IReadOnlyDictionary<EntryType, int>>(breakdown);
    }

    public Task<IReadOnlyList<ClusterRef>> GetClustersContainingAsync(string tag, CancellationToken ct)
    {
        var (canonical, isFullPath) = NormalizeQuery(tag);
        var entries = _entrySource.EnumerateEntries();

        // Fix round (inc3-final-review R4): a full-path query only matches a
        // cluster member tag that is itself that exact facet path -- a bare
        // leaf member ("done") is ambiguous as to which facet it means and
        // must not match a specific facet-path query. Bare-leaf queries keep
        // the pre-existing leaf-collapsed comparison unchanged. In practice
        // detected clusters never carry facet-derived-only leaves as members
        // (ClusterDetectorSource excludes them, same as co-occurrence), so a
        // full-path query legitimately returns no detected clusters; that is
        // correct-by-construction, not a bug -- clustering is emergent
        // structure and facets are declared structure (see
        // NotationParser.ExcludeFacetOnlyLeaves doc comment).
        bool MemberMatches(string memberTag) => isFullPath
            ? string.Equals(NormalizedPath(memberTag), canonical, StringComparison.Ordinal)
            : Canonical(memberTag) == canonical;

        var manual = _manualClusters.GetClusters()
            .Where(c => c.Tags.Any(MemberMatches))
            .Select(c =>
            {
                var spanning = EntriesSpanningAny(entries, c.Tags);
                var lastUsed = spanning.Count > 0 ? spanning.Max(e => (DateTime?)e.Timestamp) : null;
                return new ClusterRef(
                    Name: c.Name,
                    Source: ClusterSource.Manual,
                    MemberTags: c.Tags,
                    Confidence: null,
                    Context: c.Context,
                    LastUsed: lastUsed
                );
            });

        var detected = _clusterSource.GetDetectedClusters()
            .Where(c => c.Tags.Any(MemberMatches))
            .Select(c =>
            {
                var spanning = EntriesSpanningAny(entries, c.Tags);
                var lastUsed = spanning.Count > 0 ? spanning.Max(e => (DateTime?)e.Timestamp) : null;
                return new ClusterRef(
                    Name: Adapters.DetectedClusterAdapter.BuildSynthesizedName(c.Tags),
                    Source: ClusterSource.Detected,
                    MemberTags: c.Tags,
                    Confidence: c.Confidence,
                    Context: null,
                    LastUsed: lastUsed
                );
            });

        IReadOnlyList<ClusterRef> result = manual.Concat(detected).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ClusterListItem>> GetAllClustersAsync(
        ClusterListSortKey sort,
        CancellationToken ct)
    {
        var entries = _entrySource.EnumerateEntries();
        var items = new List<ClusterListItem>();

        foreach (var m in _manualClusters.GetClusters())
        {
            var leaves = ClusterMemberNormalizer.DistinctLeaves(m.Tags);
            if (leaves.Count < 2) continue;
            var spanning = EntriesSpanningAny(entries, m.Tags);
            items.Add(new ClusterListItem(
                Id: $"manual:{m.Name}",
                Name: m.Name,
                Source: ClusterContextSource.Manual,
                MemberTagCount: leaves.Count,
                EntryCount: spanning.Count,
                LastUsed: spanning.Count > 0 ? spanning.Max(e => (DateTime?)e.Timestamp) : null,
                Confidence: null,
                PreviewTags: leaves.Take(6).ToList()));
        }

        foreach (var d in _clusterSource.GetDetectedClusters())
        {
            var leaves = ClusterMemberNormalizer.DistinctLeaves(d.Tags);
            if (leaves.Count < 2) continue;
            var spanning = EntriesSpanningAny(entries, d.Tags);
            items.Add(new ClusterListItem(
                Id: "detected:" + Adapters.DetectedClusterAdapter.ComputeMemberHash(d.Tags),
                Name: Adapters.DetectedClusterAdapter.BuildSynthesizedName(d.Tags),
                Source: ClusterContextSource.Detected,
                MemberTagCount: leaves.Count,
                EntryCount: spanning.Count,
                LastUsed: spanning.Count > 0 ? spanning.Max(e => (DateTime?)e.Timestamp) : null,
                Confidence: (float?)d.Confidence,
                PreviewTags: leaves.Take(6).ToList()));
        }

        IReadOnlyList<ClusterListItem> result = sort switch
        {
            ClusterListSortKey.Frequency => items
                .OrderByDescending(i => i.EntryCount)
                .ThenBy(i => i.Name, StringComparer.Ordinal)
                .ToList(),
            ClusterListSortKey.Recency => items
                .OrderByDescending(i => i.LastUsed ?? DateTime.MinValue)
                .ThenBy(i => i.Name, StringComparer.Ordinal)
                .ToList(),
            ClusterListSortKey.Alphabetical => items
                .OrderBy(i => i.Name, StringComparer.Ordinal)
                .ToList(),
            _ => items
        };

        return Task.FromResult(result);
    }

    public Task<ClusterContext?> GetClusterContextAsync(string id, CancellationToken ct)
    {
        ClusterContext? result = null;

        if (id.StartsWith("manual:", StringComparison.Ordinal))
        {
            var name = id["manual:".Length..];
            var manual = _manualClusters.GetClusters().FirstOrDefault(m => m.Name == name);
            if (manual != null)
            {
                var entries = _entrySource.EnumerateEntries();
                var spanning = EntriesSpanningAny(entries, manual.Tags);
                var lastUsed = spanning.Count > 0
                    ? spanning.Max(e => (DateTime?)e.Timestamp)
                    : null;
                result = Adapters.ManualClusterAdapter.ToContext(manual, ProjectToEntries(spanning), lastUsed);
            }
        }
        else if (id.StartsWith("detected:", StringComparison.Ordinal))
        {
            var hash = id["detected:".Length..];
            var detected = _clusterSource.GetDetectedClusters()
                .FirstOrDefault(d => Adapters.DetectedClusterAdapter.ComputeMemberHash(d.Tags) == hash);
            if (detected != null)
            {
                var entries = _entrySource.EnumerateEntries();
                var spanning = EntriesSpanningAny(entries, detected.Tags);
                var lastUsed = spanning.Count > 0
                    ? spanning.Max(e => (DateTime?)e.Timestamp)
                    : null;
                result = Adapters.DetectedClusterAdapter.ToContext(
                    detected.Tags,
                    (float)detected.Confidence,
                    ProjectToEntries(spanning),
                    lastUsed);
            }
        }
        // digest:* and any other prefix fall through; result stays null.

        return Task.FromResult(result);
    }

    private static List<TagBearingEntry> EntriesSpanningAny(IReadOnlyList<TagBearingEntry> entries, IReadOnlyList<string> memberTags)
    {
        var canonicalSet = memberTags.Select(Canonical).ToHashSet(StringComparer.Ordinal);
        return entries
            .Where(e => e.Tags.Any(t => canonicalSet.Contains(Canonical(t))))
            .ToList();
    }

    private static IReadOnlyList<Entry> ProjectToEntries(IEnumerable<TagBearingEntry> entries) =>
        entries.Select(tbe => new Entry
        {
            Timestamp = tbe.Timestamp,
            RawContent = tbe.Content,
            ExpandedContent = tbe.Content,
            Tags = tbe.Tags.ToList(),
            Projects = NotationParser.ExtractProjects(tbe.Content),
            VaultPath = tbe.FilePath
        }).ToList();

}
