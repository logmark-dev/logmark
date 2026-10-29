using LogMark.Services.EntryParser;

namespace LogMark.Models;

public enum TagSortKey
{
    Frequency,
    Recency,
    Alphabetical
}

public enum ClusterSource
{
    Manual,
    Detected
}

public sealed record TagListItem(
    string Tag,
    int EntryCount,
    DateTime? LastUsed
);

public sealed record RelatedTag(
    string Tag,
    int CoOccurrenceCount
);

public sealed record ClusterRef(
    string Name,
    ClusterSource Source,
    IReadOnlyList<string> MemberTags,
    double? Confidence,               // null for manual; 0.3-1.0 for detected
    string? Context,                  // optional description from tags.md
    DateTime? LastUsed = null         // most recent timestamp across spanning entries
);

public sealed record ManualCluster(
    string Name,
    IReadOnlyList<string> Tags,
    string? Context,
    string? CanonicalTag              // optional; canonical variant within cluster
);

public sealed record TagSummary(
    string Tag,
    int EntryCount,
    DateTime? LastUsed,
    IReadOnlyList<ParsedEntry> RecentEntries,
    IReadOnlyList<RelatedTag> RelatedTags,
    IReadOnlyList<ClusterRef> Clusters
);

/// <summary>
/// A single value within a facet family, with the number of entries carrying
/// that value (counted by FullPath, i.e. key/value together).
/// </summary>
public sealed record FacetValueCount(
    string Value,
    int EntryCount
);

/// <summary>
/// All values observed for one facet key (e.g. "status"), sorted by
/// EntryCount desc. Total is the number of DISTINCT entries carrying at
/// least one value in this family -- not the sum of per-value counts, so an
/// entry carrying two values of the same family (#aspect/light and
/// #aspect/dark) still contributes 1 to Total.
/// </summary>
public sealed record FacetFamily(
    string Key,
    IReadOnlyList<FacetValueCount> Values,
    int Total
);
