namespace LogMark.Models;

public enum ClusterContextSource
{
    Manual,
    Detected,
    DigestArticle
}

public record ClusterContext(
    string Id,
    string Name,
    ClusterContextSource Source,
    IReadOnlyList<string> MemberTags,
    string? ContextNote,
    float? Confidence,
    IReadOnlyList<Entry> Entries,
    DateTime? LastUsed
);
