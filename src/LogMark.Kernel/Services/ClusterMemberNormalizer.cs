using LogMark.Services.Lint.Rules;

namespace LogMark.Services;

/// <summary>
/// Reduces a cluster's member tag set to a canonical form, using one shared
/// definition of degeneracy across the view layer, the write paths, and the
/// cleanup lint.
///
/// Two tiers:
///   DistinctLeaves   -- cheap: leaf-collapse + lowercase + dedup. Merges case and
///                       slash-facet variants. Used by the view guard (hide only).
///   CollapseVariants -- full: DistinctLeaves, then fold plural/punct variants via
///                       TagVariantClassifier and pick one canonical per group.
///                       Used by write paths and the cleanup lint (can rename/drop).
/// A cluster is degenerate when the chosen tier yields fewer than 2 members.
/// </summary>
public static class ClusterMemberNormalizer
{
    public static IReadOnlyList<string> DistinctLeaves(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var leaf = LeafLower(t);
            if (leaf.Length == 0) continue;
            if (seen.Add(leaf)) result.Add(leaf);
        }
        return result;
    }

    public static IReadOnlyList<string> CollapseVariants(
        IEnumerable<string> tags, string? preferredCanonical = null)
    {
        var leaves = DistinctLeaves(tags).ToList();
        if (leaves.Count <= 1) return leaves;

        // Union-find over the leaf set; tiny N, so a plain map is fine.
        var parent = leaves.ToDictionary(l => l, l => l, StringComparer.Ordinal);
        string Find(string x)
        {
            while (!StringComparer.Ordinal.Equals(parent[x], x))
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
        // Union direction (ra -> rb) is arbitrary; the canonical is chosen independently below by ordinal-min, so Find()'s representative is never used as the canonical.
        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (!StringComparer.Ordinal.Equals(ra, rb)) parent[ra] = rb;
        }

        // Case variants are already merged by DistinctLeaves (lowercased); only plural/punct folding remains.
        var groupings = new[]
        {
            TagVariantClassifier.GroupPluralSVariants(leaves),
            TagVariantClassifier.GroupPluralEsVariants(leaves),
            TagVariantClassifier.GroupPluralIesVariants(leaves),
            TagVariantClassifier.GroupPunctVariants(leaves),
        };

        foreach (var grouping in groupings)
            foreach (var group in grouping)
                for (int i = 1; i < group.Count; i++)
                    Union(group[0], group[i]);

        var preferLeaf = string.IsNullOrWhiteSpace(preferredCanonical)
            ? null : LeafLower(preferredCanonical!);

        var canonicals = leaves
            .GroupBy(Find)
            .Select(g =>
            {
                var members = g.ToList();
                if (preferLeaf != null && members.Contains(preferLeaf, StringComparer.Ordinal))
                    return preferLeaf;
                return members.OrderBy(m => m, StringComparer.Ordinal).First();
            })
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        return canonicals;
    }

    private static string LeafLower(string tag)
        => NotationParser.LeafTag(tag.Trim().TrimStart('#')).ToLowerInvariant();
}
