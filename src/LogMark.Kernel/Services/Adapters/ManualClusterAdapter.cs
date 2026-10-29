using LogMark.Models;

namespace LogMark.Services.Adapters;

public static class ManualClusterAdapter
{
    public static ClusterContext ToContext(
        ManualCluster manual,
        IReadOnlyList<Entry> entries,
        DateTime? lastUsed)
    {
        return new ClusterContext(
            Id: $"manual:{manual.Name}",
            Name: manual.Name,
            Source: ClusterContextSource.Manual,
            MemberTags: manual.Tags,
            ContextNote: manual.Context,
            Confidence: null,
            Entries: entries,
            LastUsed: lastUsed);
    }
}
