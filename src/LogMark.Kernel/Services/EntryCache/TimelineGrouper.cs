using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Pure function that flattens a list of IndexedEntry into the interleaved
/// row sequence the Timeline lens renders. No EntryCache dependency: takes a
/// route resolver delegate (same pattern as RouteGrouping).
/// </summary>
public static class TimelineGrouper
{
    public static List<TimelineRow> Flatten(
        IReadOnlyList<IndexedEntry> entries,
        Func<string, string?> getRouteForPath,
        string logmarkPath,
        DateOnly today)
    {
        if (entries.Count == 0) return new List<TimelineRow>();

        var rows = new List<TimelineRow>();

        var byDay = entries
            .GroupBy(ie => DateOnly.FromDateTime(ie.Entry.Timestamp))
            .OrderByDescending(g => g.Key);

        foreach (var dayGroup in byDay)
        {
            var dayEntries = dayGroup.ToList();
            rows.Add(new DayHeaderRow(
                Date: dayGroup.Key,
                Label: FormatDayLabel(dayGroup.Key, today),
                EntryCount: dayEntries.Count));

            var routeGroups = RouteGrouping.GroupByRoute(
                dayEntries,
                getRouteForPath,
                logmarkPath);

            foreach (var routeGroup in routeGroups)
            {
                rows.Add(new RouteHeaderRow(
                    RouteName: routeGroup.RouteName,
                    IsSentinel: routeGroup.RouteName == RouteGrouping.InboxKey
                             || routeGroup.RouteName == RouteGrouping.UnroutedKey));

                // Within a day, RouteGrouping returns year-grouped buckets; flatten
                // them. All entries in a single day share the same year so this is
                // a single bucket in practice.
                foreach (var yearGroup in routeGroup.YearGroups)
                {
                    foreach (var entry in yearGroup.Entries)
                    {
                        rows.Add(new EntryRowItem(entry));
                    }
                }
            }
        }

        return rows;
    }

    private static string FormatDayLabel(DateOnly date, DateOnly today)
    {
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";

        return date.Year == today.Year
            ? date.ToString("dddd, MMMM d", CultureInfo.InvariantCulture)
            : date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
