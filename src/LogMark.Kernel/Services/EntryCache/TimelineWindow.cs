using System;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Time windows offered by the Timeline lens. Each window maps to an
/// (after, before) range matching the SearchQuery.After / SearchQuery.Before
/// half-open semantics (After is inclusive lower bound; Before is exclusive
/// upper bound -- entry.Timestamp >= After AND entry.Timestamp < Before).
/// </summary>
public enum TimelineWindow
{
    All,
    Today,
    Last7Days,
    Last30Days,
}

public static class TimelineWindowExtensions
{
    /// <summary>
    /// Map a window to an (after, before) date range relative to a reference day.
    /// Reference day is injected for testability and to keep the type pure.
    /// </summary>
    public static (DateTime? After, DateTime? Before) ToRange(this TimelineWindow window, DateTime today)
    {
        var startOfToday = today.Date;
        var startOfTomorrow = startOfToday.AddDays(1);

        return window switch
        {
            TimelineWindow.All        => (null, null),
            TimelineWindow.Today      => (startOfToday, startOfTomorrow),
            TimelineWindow.Last7Days  => (startOfToday.AddDays(-6), startOfTomorrow),
            TimelineWindow.Last30Days => (startOfToday.AddDays(-29), startOfTomorrow),
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, null),
        };
    }
}
