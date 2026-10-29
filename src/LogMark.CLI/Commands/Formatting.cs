using System.Text.Json;
using LogMark.Models;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

/// <summary>
/// Shared entry formatting for plain text and JSON output.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Format a single entry as a plain text line.
    /// </summary>
    public static string FormatEntryLine(IndexedEntry ie)
    {
        var ts = ie.Entry.Timestamp.ToString("yyyy-MM-dd HH:mm");
        var type = ie.Entry.Type.ToString().ToLowerInvariant().PadRight(9);
        var route = ie.Route != null ? $"+{ie.Route}" : "";
        var routeCol = route.PadRight(12);
        var content = TruncateContent(ie.Entry.Content, 80);
        return $"{ts}  {type} {routeCol} {content}";
    }

    /// <summary>
    /// Write a list of entries to stdout as plain text.
    /// </summary>
    public static void WriteEntries(CliContext context, IReadOnlyList<IndexedEntry> entries, int totalCount)
    {
        foreach (var entry in entries)
            context.WriteResult(FormatEntryLine(entry));

        if (totalCount > entries.Count)
            context.WriteInfo($"Showing {entries.Count} of {totalCount} results.");
    }

    /// <summary>
    /// Write a list of entries as JSON to stdout.
    /// </summary>
    public static void WriteEntriesJson(CliContext context, IReadOnlyList<IndexedEntry> entries, int totalCount)
    {
        var result = new
        {
            entries = entries.Select(ie => new
            {
                timestamp = ie.Entry.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
                type = ie.Entry.Type.ToString().ToLowerInvariant(),
                route = ie.Route,
                content = ie.Entry.Content,
                shortId = ie.Entry.ShortId
            }),
            total = totalCount
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        context.WriteResult(json);
    }

    /// <summary>
    /// Write entries in either plain or JSON format depending on context.
    /// </summary>
    public static void WriteResults(CliContext context, SearchResult result)
    {
        if (context.JsonOutput)
            WriteEntriesJson(context, result.Entries, result.TotalCount);
        else
            WriteEntries(context, result.Entries, result.TotalCount);
    }

    private static string TruncateContent(string content, int maxLength)
    {
        // Take first line only
        var firstLine = content.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= maxLength)
            return firstLine;
        return firstLine[..(maxLength - 3)] + "...";
    }
}
