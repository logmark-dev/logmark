using System.Security.Cryptography;
using System.Text;
using LogMark.Models;

namespace LogMark.Services.Adapters;

public static class DetectedClusterAdapter
{
    public static ClusterContext ToContext(
        IReadOnlyList<string> memberTags,
        float confidence,
        IReadOnlyList<Entry> entries,
        DateTime? lastUsed)
    {
        return new ClusterContext(
            Id: "detected:" + ComputeMemberHash(memberTags),
            Name: BuildSynthesizedName(memberTags),
            Source: ClusterContextSource.Detected,
            MemberTags: memberTags,
            ContextNote: null,
            Confidence: confidence,
            Entries: entries,
            LastUsed: lastUsed);
    }

    public static string BuildSynthesizedName(IReadOnlyList<string> memberTags)
        => string.Join(" + ", memberTags.Take(2))
           + (memberTags.Count > 2 ? $" +{memberTags.Count - 2}" : "");

    public static string ComputeMemberHash(IEnumerable<string> memberTags)
    {
        var sorted = memberTags
            .Select(t => t.ToLowerInvariant())
            .OrderBy(t => t, StringComparer.Ordinal);
        var input = string.Join("|", sorted);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
