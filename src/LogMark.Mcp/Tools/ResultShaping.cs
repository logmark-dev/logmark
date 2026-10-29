using System.Globalization;
using LogMark.Mcp.Protocol;
using LogMark.Services.EntryCache;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Shared shaping for the list tools: the truncation Breakdown and the
/// after/before date bounds. Instead of a cursor, a truncated result says how
/// its full match set splits (route, scope, type, date span) so the agent
/// narrows the query; the date bounds are the fallback for a large leaf with
/// nothing left to split on.
/// </summary>
public static class ResultShaping
{
    public const string AfterDescription =
        "Only entries at or after this date or datetime (ISO 8601, e.g. '2026-09-01' or '2026-09-01T14:30'). Inclusive.";

    public const string BeforeDescription =
        "Only entries at or before this date or datetime (ISO 8601). Inclusive: a date covers that whole day, " +
        "a datetime covers that minute. To read past a truncated result, pass the oldest timestamp you received " +
        "as before and drop ids you already have (entries can share a minute).";

    public static JsonSchemaProperty AfterProperty => new() { Type = "string", Description = AfterDescription };

    public static JsonSchemaProperty BeforeProperty => new() { Type = "string", Description = BeforeDescription };

    /// <summary>
    /// Counts over every match when the page was cut short; null otherwise, so
    /// a complete answer stays small (nulls are dropped on serialization).
    /// </summary>
    public static ResultBreakdown? Breakdown(SearchResult result)
    {
        var all = result.AllMatches;
        if (result.TotalCount <= result.Entries.Count || all.Count == 0)
            return null;

        var scopes = CountDesc(all
            .Where(ie => !string.IsNullOrEmpty(ie.Scope))
            .Select(ie => $"{ie.Route ?? "(none)"}/{ie.Scope}"));

        return new ResultBreakdown
        {
            Routes = CountDesc(all.Select(ie => ie.Route ?? "(none)")),
            Scopes = scopes.Count > 0 ? scopes : null,
            Types = CountDesc(all.Select(ie => ie.Entry.Type.ToString().ToLowerInvariant())),
            Oldest = all.Min(ie => ie.Entry.Timestamp),
            Newest = all.Max(ie => ie.Entry.Timestamp),
        };
    }

    /// <summary>
    /// Parse after/before into SearchQuery bounds. SearchQuery.Before is
    /// exclusive; the agent-facing before is inclusive, so a date moves to the
    /// next midnight and a datetime to the next minute. Error names the bad
    /// argument; null when both parse (or are absent).
    /// </summary>
    public static string? TryParseBounds(
        Dictionary<string, object?>? arguments, out DateTime? after, out DateTime? before)
    {
        after = null;
        before = null;

        var afterRaw = McpToolHelpers.GetStringArg(arguments, "after");
        if (!string.IsNullOrWhiteSpace(afterRaw))
        {
            if (!TryParse(afterRaw, out var value, out _))
                return $"Invalid after: '{afterRaw}'. Use an ISO date or datetime, e.g. 2026-09-01 or 2026-09-01T14:30.";
            after = value;
        }

        var beforeRaw = McpToolHelpers.GetStringArg(arguments, "before");
        if (!string.IsNullOrWhiteSpace(beforeRaw))
        {
            if (!TryParse(beforeRaw, out var value, out var dateOnly))
                return $"Invalid before: '{beforeRaw}'. Use an ISO date or datetime, e.g. 2026-09-01 or 2026-09-01T14:30.";
            before = dateOnly ? value.AddDays(1) : TruncateToMinute(value).AddMinutes(1);
        }

        return null;
    }

    private static bool TryParse(string raw, out DateTime value, out bool dateOnly)
    {
        raw = raw.Trim();
        dateOnly = DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
        if (dateOnly)
        {
            value = d.ToDateTime(TimeOnly.MinValue);
            return true;
        }
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static DateTime TruncateToMinute(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);

    private static Dictionary<string, int> CountDesc(IEnumerable<string> keys) =>
        keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());
}

public class ResultBreakdown
{
    public Dictionary<string, int> Routes { get; init; } = new();
    public Dictionary<string, int>? Scopes { get; init; }
    public Dictionary<string, int> Types { get; init; } = new();
    public DateTime Oldest { get; init; }
    public DateTime Newest { get; init; }
}
