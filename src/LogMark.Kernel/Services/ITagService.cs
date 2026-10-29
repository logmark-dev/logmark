using LogMark.Models;

namespace LogMark.Services;

public interface ITagService
{
    /// <summary>
    /// Returns full summary for a single tag, or null if the tag has zero entries.
    /// Tag name must NOT include the leading '#'.
    /// A tag containing a path separator ('/' or ':') scopes matching to that
    /// exact facet path (e.g. "phase/done" excludes entries carrying only
    /// "status/done"); a bare leaf keeps the pre-existing aggregate-across-all
    /// behavior (facet-derived and flat occurrences of that leaf together).
    /// </summary>
    Task<TagSummary?> GetTagSummaryAsync(string tag, int recentLimit, int relatedLimit, CancellationToken ct);

    /// <summary>
    /// Returns all tags appearing in the vault, sorted by the requested key.
    /// </summary>
    Task<IReadOnlyList<TagListItem>> GetAllTagsAsync(TagSortKey sort, CancellationToken ct);

    /// <summary>
    /// Returns the number of distinct tags appearing in the vault, counted with
    /// the same accounting as GetAllTagsAsync (full path for facet-derived
    /// tags, e.g. "status/done" and "phase/done" count as two; leaf for flat
    /// tags), so this agrees with the Tags tab total instead of the raw leaf
    /// count. Cheap, synchronous: one pass over the entry source with no
    /// allocation of the full TagListItem set. Used by the sidebar lens badge.
    /// </summary>
    int GetTagCount();

    /// <summary>
    /// Returns (Total, AddedSince) for the sidebar Tags-unread badge.
    ///   Total = distinct tag count (matches GetTagCount and the Tags tab
    ///           header total -- full path for facet-derived tags, leaf for
    ///           flat tags).
    ///   AddedSince = distinct tags whose earliest entry timestamp is strictly
    ///                after <paramref name="lastSeenUtc"/>.
    /// One pass over the entry source; no per-tag list allocation. The two
    /// numbers count the same unit, so the "513 (+2)" badge reads coherently.
    /// Adding entries with already-known tags does NOT move AddedSince;
    /// introducing a brand-new tag does.
    /// </summary>
    (int Total, int AddedSince) GetTagCountsSince(DateTime lastSeenUtc);

    /// <summary>
    /// Returns tags that frequently co-occur with the given tag, sorted by
    /// co-occurrence count desc. Same full-path-vs-leaf scoping as
    /// GetTagSummaryAsync: a full-path query ("phase/done") counts only tags
    /// found on entries carrying that exact facet path (facet full paths and
    /// flat leaves alike); a bare leaf keeps the pre-existing
    /// facet-derived-leaf-excluded aggregate behavior.
    /// </summary>
    Task<IReadOnlyList<RelatedTag>> GetCoOccurringTagsAsync(string tag, int top, CancellationToken ct);

    /// <summary>
    /// Returns facet families: facet-derived tags (#key/value) grouped by
    /// key, each carrying its distinct values with per-value entry counts
    /// and a family total, counted by FullPath so two keys sharing a leaf
    /// value (#status/done, #phase/done) never merge. Flat (non-faceted)
    /// tags never appear here -- see GetAllTagsAsync for those. Sorted by
    /// key ascending; values within a family sorted by EntryCount desc.
    /// Empty vault and vaults with no facets both return an empty list.
    /// </summary>
    Task<IReadOnlyList<FacetFamily>> GetFacetFamiliesAsync(CancellationToken ct);

    /// <summary>
    /// Returns clusters (manual from tags.md + detected from ClusterDetector)
    /// containing the given tag. A full-path query ("phase/done") only
    /// matches a cluster member that is itself that exact facet path; since
    /// clustering deliberately excludes facet-derived-only leaves as members
    /// (see NotationParser.ExcludeFacetOnlyLeaves), this legitimately returns
    /// no detected clusters for most full-path queries -- correct-by-construction,
    /// not a gap. A bare leaf keeps the pre-existing leaf-collapsed comparison.
    /// </summary>
    Task<IReadOnlyList<ClusterRef>> GetClustersContainingAsync(string tag, CancellationToken ct);

    /// <summary>
    /// Returns the breakdown of entry types for entries tagged with the given tag.
    /// Used by the C-16 TypeDistributionBar on the single tag detail surface. Keys
    /// are the EntryType enum values present in the result set; values are entry
    /// counts. Types with zero matching entries are absent from the dictionary.
    /// Same full-path-vs-leaf scoping as GetTagSummaryAsync, so the bar this
    /// feeds always sums to the entry count on the same page (inc3-final-review R1).
    /// </summary>
    Task<IReadOnlyDictionary<EntryType, int>> GetTypeBreakdownAsync(string tag, CancellationToken ct);

    /// <summary>
    /// Returns all manual + detected clusters, sorted by the requested key.
    /// Digest article-clusters are excluded (surfaced via DigestView).
    /// </summary>
    Task<IReadOnlyList<ClusterListItem>> GetAllClustersAsync(
        ClusterListSortKey sort,
        CancellationToken ct);

    /// <summary>
    /// Resolves a cluster ID (manual:* or detected:*) to a fully-loaded ClusterContext.
    /// Returns null for unknown or malformed IDs. Digest IDs (digest:*) are NOT resolved here
    /// -- those flow through the digest pipeline directly to ShowCluster(ArticleCluster).
    /// </summary>
    Task<ClusterContext?> GetClusterContextAsync(string id, CancellationToken ct);
}
