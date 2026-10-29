using System;
using System.Collections.Generic;
using System.Linq;

namespace LogMark.Services.Capture;

/// <summary>One vocabulary entry (a route or a tag) with its usage count.</summary>
public sealed record VocabItem(string Name, int Count);

/// <summary>Routes and tags the desktop publishes to the host for extension autocomplete.</summary>
public sealed class VaultVocabulary
{
    public IReadOnlyList<VocabItem> Routes { get; init; } = Array.Empty<VocabItem>();
    public IReadOnlyList<VocabItem> Tags { get; init; } = Array.Empty<VocabItem>();
}

/// <summary>The vocabulary-relevant projection of one cached entry.</summary>
public sealed record EntryVocabFacts(string? Route, IReadOnlyList<string> Tags);

/// <summary>
/// Pure builder: turns the configured route names and the cached entries into a
/// VaultVocabulary. Route count = entries routed there; tag count = entries carrying
/// the tag. Configured routes appear even with zero use; used-but-unconfigured routes
/// are included too. Both lists are ordered by count desc then name asc.
/// </summary>
public static class VaultVocabularyBuilder
{
    public static VaultVocabulary Build(
        IReadOnlyList<string> configuredRoutes,
        IReadOnlyList<EntryVocabFacts> entries)
    {
        var routeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in configuredRoutes)
            routeCounts.TryAdd(name, 0);

        var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.Route))
                routeCounts[e.Route!] = routeCounts.GetValueOrDefault(e.Route!) + 1;
            foreach (var tag in e.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
            }
        }

        return new VaultVocabulary
        {
            Routes = Order(routeCounts),
            Tags = Order(tagCounts),
        };
    }

    private static IReadOnlyList<VocabItem> Order(Dictionary<string, int> counts) =>
        counts
            .Select(kv => new VocabItem(kv.Key, kv.Value))
            .OrderByDescending(i => i.Count)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ToList();
}
