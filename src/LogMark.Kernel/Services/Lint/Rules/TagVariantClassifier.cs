using System.Text;

namespace LogMark.Services.Lint.Rules;

/// <summary>
/// Single source of truth for tag-variant equivalence: case, plural (-s/-es/-ies),
/// and punctuation. The five tag lint rules and ClusterMemberNormalizer both consume
/// this so "variant" means exactly one thing across the codebase.
///
/// Each method takes a set of tag spellings (leaf form, with or without case noise)
/// and returns groups of spellings that are mutually variant. Singletons are not
/// returned. Logic ported verbatim from the original per-rule grouping; the only
/// behavioral contract is "same groups the rules produced before extraction".
/// </summary>
public static class TagVariantClassifier
{
    private const int MinPluralSStemLength = 4;
    private const int MinPluralIesStemLength = 3;

    public static List<List<string>> GroupCaseVariants(IEnumerable<string> tags)
    {
        return tags
            .Distinct(StringComparer.Ordinal)
            .GroupBy(t => t.ToLowerInvariant())
            .Where(g => g.Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(g => g.ToList())
            .ToList();
    }

    public static List<List<string>> GroupPluralSVariants(IEnumerable<string> tags)
    {
        return GroupPlural(tags, minStemLength: MinPluralSStemLength,
            stemPredicate: stem => !EndsInEsTrigger(stem), transform: stem => stem + "s");
    }

    public static List<List<string>> GroupPluralEsVariants(IEnumerable<string> tags)
    {
        // -es trigger suffixes (s/x/z/ch/sh) are self-filtering; no length guard needed.
        return GroupPlural(tags, minStemLength: 0,
            stemPredicate: EndsInEsTrigger, transform: stem => stem + "es");
    }

    public static List<List<string>> GroupPluralIesVariants(IEnumerable<string> tags)
    {
        return GroupPlural(tags, minStemLength: MinPluralIesStemLength,
            stemPredicate: stem => stem.EndsWith("y", StringComparison.Ordinal),
            transform: stem => stem[..^1] + "ies");
    }

    public static List<List<string>> GroupPunctVariants(IEnumerable<string> tags)
    {
        return tags
            .Distinct(StringComparer.Ordinal)
            .GroupBy(t => StripSeparators(t.ToLowerInvariant()))
            .Where(g => g.Select(t => t.ToLowerInvariant())
                         .Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(g => g.ToList())
            .ToList();
    }

    private static List<List<string>> GroupPlural(
        IEnumerable<string> tags,
        int minStemLength,
        Func<string, bool> stemPredicate,
        Func<string, string> transform)
    {
        var byCaseFolded = tags
            .Distinct(StringComparer.Ordinal)
            .GroupBy(t => t.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var set = byCaseFolded.Keys.ToHashSet(StringComparer.Ordinal);
        var issued = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<List<string>>();

        foreach (var stem in set)
        {
            if (stem.Length < minStemLength) continue;
            if (!stemPredicate(stem)) continue;
            var plural = transform(stem);
            if (!set.Contains(plural)) continue;
            if (issued.Contains(stem) || issued.Contains(plural)) continue;

            groups.Add(byCaseFolded[stem].Concat(byCaseFolded[plural]).ToList());
            issued.Add(stem);
            issued.Add(plural);
        }
        return groups;
    }

    private static bool EndsInEsTrigger(string s)
        => s.EndsWith("s", StringComparison.Ordinal)
        || s.EndsWith("x", StringComparison.Ordinal)
        || s.EndsWith("z", StringComparison.Ordinal)
        || s.EndsWith("ch", StringComparison.Ordinal)
        || s.EndsWith("sh", StringComparison.Ordinal);

    private static string StripSeparators(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (c != '.' && c != '_') sb.Append(c);
        return sb.ToString();
    }
}
