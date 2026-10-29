using LogMark.Services;

namespace LogMark.Services.Search;

public static class TagMatcher
{
    public static double Score(IReadOnlyList<string> entryTags, ParsedQuery q)
    {
        if (entryTags.Count == 0)
            return 0.0;

        var leaves = entryTags
            .Select(t => NotationParser.LeafTag(t.TrimStart('#')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in q.Tags)
            if (leaves.Contains(NotationParser.LeafTag(tag)))
                return 1.0;

        // Bare terms joined by '-' (query "local llm" -> "local-llm") match a tag leaf.
        if (q.Terms.Count > 0)
        {
            var joined = string.Join('-', q.Terms);
            if (leaves.Contains(joined))
                return 1.0;

            foreach (var term in q.Terms)
                if (leaves.Contains(term))
                    return 1.0;
        }

        return 0.0;
    }
}
