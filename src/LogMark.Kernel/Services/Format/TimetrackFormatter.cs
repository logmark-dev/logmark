using System;
using System.Linq;
using System.Text;
using LogMark.Models;

namespace LogMark.Services.Timetrack;

public static class TimetrackFormatter
{
    public static string FormatDuration(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        if (hours == 0)
            return $"{minutes}m";

        return $"{hours}h {minutes}m";
    }

    public static string FormatSegment(TimeSegment segment)
    {
        var start = segment.Start.ToString("HH:mm");

        if (segment.IsOpen)
            return $"- {start}-???";

        var end = segment.End.ToString("HH:mm");
        var duration = FormatDuration(segment.Duration);
        return $"- {start}-{end} ({duration})";
    }

    public static string FormatEntry(TimetrackEntry entry, DateTime startTime)
    {
        // Vault content uses LF, not Environment.NewLine -- markdown files are
        // shared cross-platform via Obsidian Sync / git, so line endings stay
        // consistent regardless of the writer's OS. TimetrackRepository
        // separates entries with "\n\n", which only works if the formatter
        // matches.
        var sb = new StringBuilder();

        sb.Append("## ").Append(startTime.ToString("yyyy-MM-dd HH:mm")).Append('\n');
        sb.Append('\n');

        // Description line with tags and duration
        // Note: timetrack: prefix omitted (implicit from _timetrack/ folder)
        // Note: +route omitted (implicit from file path)
        sb.Append(entry.Description);

        if (entry.Tags.Count > 0)
        {
            foreach (var tag in entry.Tags)
                sb.Append(" #").Append(tag);
        }

        var isRunning = entry.Timeline.Any(s => s.IsOpen);
        if (isRunning)
            sb.Append(" (running)");
        else
            sb.Append(" (").Append(FormatDuration(entry.TotalDuration)).Append(')');

        sb.Append('\n');
        sb.Append('\n');

        sb.Append("timeline:\n");
        foreach (var segment in entry.Timeline)
            sb.Append(FormatSegment(segment)).Append('\n');

        return sb.ToString().TrimEnd();
    }
}
