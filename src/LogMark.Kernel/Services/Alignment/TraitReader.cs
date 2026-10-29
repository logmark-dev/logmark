using LogMark.Services.EntryCache;

namespace LogMark.Services.Alignment;

public static class TraitReader
{
    public static IReadOnlyList<LearnedTrait> ReadAll(IEnumerable<IndexedEntry> selfRouteEntries) =>
        selfRouteEntries
            .Where(e => NotationParser.ExtractTags(e.Entry.Content).Contains(TraitFormat.Tag, StringComparer.OrdinalIgnoreCase))
            .Select(e => TraitFormat.Parse(e.Entry.ShortId, e.Entry.Content, e.Entry.ExtendedMetadata))
            .OfType<LearnedTrait>()
            .ToList();

    public static IReadOnlyList<LearnedTrait> ReadConfirmed(IEnumerable<IndexedEntry> selfRouteEntries, string scope) =>
        ReadAll(selfRouteEntries)
            .Where(t => t.Status == TraitStatus.Confirmed && string.Equals(t.Scope, scope, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
