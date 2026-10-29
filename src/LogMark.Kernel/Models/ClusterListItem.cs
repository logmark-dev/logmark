namespace LogMark.Models;

public enum ClusterListSortKey
{
    Frequency,
    Recency,
    Alphabetical
}

public record ClusterListItem(
    string Id,
    string Name,
    ClusterContextSource Source,
    int MemberTagCount,
    int EntryCount,
    DateTime? LastUsed,
    float? Confidence,
    // Preview of the first N member tags (canonical form, no leading #),
    // ordered as the source presents them. Used by the S-08 Clusters tab
    // ClusterCard preview chip row (C-12). Empty by default for back-compat.
    IReadOnlyList<string> PreviewTags = null!
)
{
    public IReadOnlyList<string> PreviewTags { get; init; } =
        PreviewTags ?? Array.Empty<string>();
}
