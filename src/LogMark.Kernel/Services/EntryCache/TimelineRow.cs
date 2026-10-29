namespace LogMark.Services.EntryCache;

/// <summary>
/// Discriminated row type for the Timeline lens flat list. The grouper emits
/// these interleaved (day header, route header, entries, route header, entries,
/// day header, ...) and the view's DataTemplateSelector renders each subtype.
/// </summary>
public abstract record TimelineRow;

public sealed record DayHeaderRow(
    DateOnly Date,
    string Label,
    int EntryCount) : TimelineRow;

public sealed record RouteHeaderRow(
    string RouteName,
    bool IsSentinel) : TimelineRow;

public sealed record EntryRowItem(
    IndexedEntry Entry) : TimelineRow;
