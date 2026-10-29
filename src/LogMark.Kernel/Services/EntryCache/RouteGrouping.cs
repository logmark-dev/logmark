namespace LogMark.Services.EntryCache;

/// <summary>
/// Groups indexed entries by route name with optional year sub-divisions.
/// Pure: takes a routeResolver delegate so it has no EntryCache dependency.
/// </summary>
public static class RouteGrouping
{
    public const string InboxKey = "(inbox)";
    public const string UnroutedKey = "(unrouted)";

    public sealed record YearGroup(string Year, IReadOnlyList<IndexedEntry> Entries);

    public sealed record RouteGroup(
        string RouteName,
        IReadOnlyList<YearGroup> YearGroups,
        int TotalCount,
        bool HasMultipleYears);

    /// <summary>
    /// Groups entries into route buckets, ordered by count desc with sentinel
    /// groups (<see cref="InboxKey"/>, <see cref="UnroutedKey"/>) sorted last.
    /// Inside each route, entries are bucketed by year (from Entry.Timestamp)
    /// then sorted newest-first.
    /// </summary>
    public static List<RouteGroup> GroupByRoute(
        IReadOnlyList<IndexedEntry> entries,
        Func<string, string?> getRouteForPath,
        string logmarkPath)
    {
        if (entries.Count == 0) return new List<RouteGroup>();

        var inboxPrefix = logmarkPath.Replace('\\', '/').TrimEnd('/') + "/_inbox/";

        var byRoute = entries
            .GroupBy(ie => ResolveRouteKey(ie.FilePath, getRouteForPath, inboxPrefix))
            .Select(g => BuildRouteGroup(g.Key, g.ToList()))
            .ToList();

        return byRoute
            .OrderBy(g => IsSentinel(g.RouteName) ? 1 : 0)
            .ThenByDescending(g => g.TotalCount)
            .ThenBy(g => g.RouteName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveRouteKey(
        string filePath,
        Func<string, string?> getRouteForPath,
        string normalisedInboxPrefix)
    {
        var route = getRouteForPath(filePath);
        if (!string.IsNullOrEmpty(route)) return route;

        var normalised = filePath.Replace('\\', '/');
        if (normalised.StartsWith(normalisedInboxPrefix, StringComparison.OrdinalIgnoreCase))
            return InboxKey;

        return UnroutedKey;
    }

    private static RouteGroup BuildRouteGroup(string routeName, IReadOnlyList<IndexedEntry> entries)
    {
        var yearGroups = entries
            .GroupBy(ie => ie.Entry.Timestamp.Year.ToString())
            .Select(g => new YearGroup(
                g.Key,
                g.OrderByDescending(ie => ie.Entry.Timestamp).ToList()))
            .OrderByDescending(yg => yg.Year, StringComparer.Ordinal)
            .ToList();

        return new RouteGroup(
            RouteName: routeName,
            YearGroups: yearGroups,
            TotalCount: entries.Count,
            HasMultipleYears: yearGroups.Count > 1);
    }

    private static bool IsSentinel(string routeName) =>
        routeName == InboxKey || routeName == UnroutedKey;
}
