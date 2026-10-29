using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Parses seed.md files into a list of SeedEntry objects.
///
/// Expected format:
/// ## 2025-11-15 09:00 | +route | type
/// content line 1
/// content line 2
/// #tag1 #tag2
/// </summary>
public partial class SeedParser
{
    // Header pattern: ## DATE | +ROUTE | TYPE
    // Route is optional (can be empty between pipes)
    // Examples:
    //   ## 2025-11-15 09:00 | +logmark | task
    //   ## 2025-11-15 09:00 | | diary   (no route)
    [GeneratedRegex(@"^##\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})\s*\|\s*\+?(\w*)\s*\|\s*(\w+)\s*$", RegexOptions.Compiled)]
    private static partial Regex HeaderPattern();

    // Tag pattern: #tag
    [GeneratedRegex(@"#([a-zA-Z0-9_-]+)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    // Relative-date token: {{T HH:mm}} or {{T-Nd HH:mm}}
    //   {{T 09:00}}      -> install-time today at 09:00
    //   {{T-7d 14:30}}   -> install-time minus 7 days at 14:30
    //   {{T-15d 09:00}}  -> install-time minus 15 days at 09:00
    // v0.18 QA H2: template seed.md files used literal dates (2026-01-10 etc.)
    // which appeared as "112 days dormant" entries on every fresh install made
    // months after the dates were written, triggering Staleness on day one.
    [GeneratedRegex(@"\{\{T(?:-(\d+)d)?\s+(\d{2}:\d{2})\}\}", RegexOptions.Compiled)]
    private static partial Regex RelativeDateTokenPattern();

    // Entry type mapping
    private static readonly Dictionary<string, EntryType> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["task"] = EntryType.Task,
        ["idea"] = EntryType.Idea,
        ["block"] = EntryType.Block,
        ["decision"] = EntryType.Decision,
        ["diary"] = EntryType.Log,
        ["log"] = EntryType.Log,
        ["workflow"] = EntryType.Timetrack
    };

    /// <summary>
    /// Parse seed markdown content into a list of SeedEntry objects.
    /// Relative-date tokens (<c>{{T-Nd HH:mm}}</c>) are resolved against
    /// <c>DateTime.Now</c>.
    /// </summary>
    public IReadOnlyList<SeedEntry> Parse(string markdown)
        => Parse(markdown, DateTime.Now);

    /// <summary>
    /// Parse seed markdown content with a specific anchor date. Used by tests
    /// and any caller that wants deterministic offsets. v0.18 QA H2.
    /// </summary>
    public IReadOnlyList<SeedEntry> Parse(string markdown, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<SeedEntry>();

        // Substitute {{T-Nd HH:mm}} tokens into yyyy-MM-dd HH:mm so the
        // HeaderPattern regex matches the same way it always has.
        markdown = SubstituteRelativeDates(markdown, now);

        var entries = new List<SeedEntry>();
        var lines = markdown.Split('\n');

        DateTime? currentDate = null;
        string? currentRoute = null;
        EntryType? currentType = null;
        var contentLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            var headerMatch = HeaderPattern().Match(line);
            if (headerMatch.Success)
            {
                // Save previous entry if exists
                if (currentDate.HasValue && currentType.HasValue)
                {
                    var entry = CreateEntry(currentDate.Value, currentType.Value, currentRoute, contentLines);
                    entries.Add(entry);
                }

                // Parse new header
                var dateStr = headerMatch.Groups[1].Value;
                var route = headerMatch.Groups[2].Value;
                var typeStr = headerMatch.Groups[3].Value;

                currentDate = DateTime.ParseExact(dateStr, "yyyy-MM-dd HH:mm", null);
                currentRoute = string.IsNullOrWhiteSpace(route) ? null : route;
                currentType = TypeMap.TryGetValue(typeStr, out var type) ? type : EntryType.Log;
                contentLines.Clear();
            }
            else if (currentDate.HasValue)
            {
                // Skip comment lines (start with #) that aren't tags
                // A tag line has tags, a comment line is just text starting with #
                if (!string.IsNullOrWhiteSpace(line))
                {
                    contentLines.Add(line);
                }
            }
        }

        // Save last entry
        if (currentDate.HasValue && currentType.HasValue)
        {
            var entry = CreateEntry(currentDate.Value, currentType.Value, currentRoute, contentLines);
            entries.Add(entry);
        }

        return entries;
    }

    private SeedEntry CreateEntry(DateTime date, EntryType type, string? route, List<string> contentLines)
    {
        var content = string.Join("\n", contentLines).Trim();
        var tags = ExtractTags(content);

        return new SeedEntry(date, type, content, route, tags);
    }

    private static IReadOnlyList<string> ExtractTags(string content)
    {
        return TagPattern().Matches(content)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string SubstituteRelativeDates(string markdown, DateTime now)
    {
        return RelativeDateTokenPattern().Replace(markdown, match =>
        {
            // group 1 = optional offset days (empty for {{T HH:mm}})
            // group 2 = HH:mm
            var offsetStr = match.Groups[1].Value;
            var timeStr = match.Groups[2].Value;
            var offsetDays = string.IsNullOrEmpty(offsetStr) ? 0 : int.Parse(offsetStr);
            var hh = int.Parse(timeStr.Substring(0, 2));
            var mm = int.Parse(timeStr.Substring(3, 2));
            var resolved = new DateTime(now.Year, now.Month, now.Day, hh, mm, 0).AddDays(-offsetDays);
            return resolved.ToString("yyyy-MM-dd HH:mm");
        });
    }
}
