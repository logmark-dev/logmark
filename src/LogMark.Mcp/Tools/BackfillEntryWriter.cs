using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Exceptions;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Shared backfill machinery for BackfillEntryTool and BackfillBatchTool:
/// content mutation (tags/route), entry-type override mapping, destination
/// resolution with the private-route guard, and chronological insertion into
/// yearly files.
/// </summary>
internal sealed partial class BackfillEntryWriter
{
    private readonly VaultServices _services;

    [GeneratedRegex(@"^## (\d{4}-\d{2}-\d{2} \d{2}:\d{2})", RegexOptions.Multiline)]
    private static partial Regex EntryHeaderPattern();

    internal BackfillEntryWriter(VaultServices services)
    {
        _services = services;
    }

    /// <summary>
    /// Appends the given tags to the content, skipping tags already present.
    /// Tags are normalized to the Obsidian-portable '/' facet separator.
    /// </summary>
    internal static string ApplyTags(string content, List<string>? tags)
    {
        if (tags == null || tags.Count == 0)
            return content;

        var existingTags = NotationParser.ExtractTags(content);
        var newTags = tags
            .Select(t => t.TrimStart('#').Replace(':', '/'))  // normalize before dedup
            .Where(t => !existingTags.Contains(NotationParser.LeafTag(t), StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (newTags.Any())
        {
            content = content.TrimEnd() + " " + string.Join(" ", newTags.Select(t => $"#{t}"));
        }
        return content;
    }

    /// <summary>Prefixes the content with +route unless it is already routed there.</summary>
    internal static string ApplyRoute(string content, string? route)
    {
        if (string.IsNullOrEmpty(route))
            return content;

        var existingProjects = NotationParser.ExtractProjects(content);
        var routeName = route.TrimStart('+');
        if (!existingProjects.Contains(routeName, StringComparer.OrdinalIgnoreCase))
        {
            content = $"+{routeName} {content}";
        }
        return content;
    }

    /// <summary>
    /// Headless +token policy for a parsed entry: only the explicit route (and
    /// its /scopes), or without one only declared routes, stay in Projects; every
    /// other +word moves to ProseRouteTokens and is written verbatim. Returns the
    /// prose tokens so the caller can report them.
    /// </summary>
    internal static List<string> ApplyHeadlessRouteTokens(Entry entry, RouteService routeService, string? route)
    {
        var (routed, prose) = routeService.PartitionHeadlessRouteTokens(entry.Projects, route);
        entry.Projects = routed;
        entry.ProseRouteTokens = prose;
        return prose;
    }

    /// <summary>
    /// Applies an explicit entry_type argument onto a parsed entry. Only
    /// overrides when the notation parser saw no typing prefix (Log).
    /// </summary>
    internal static void ApplyEntryTypeOverride(Entry entry, string? entryTypeStr)
    {
        if (string.IsNullOrEmpty(entryTypeStr) || entry.Type != EntryType.Log)
            return;

        entry.Type = entryTypeStr.ToLowerInvariant() switch
        {
            "task" => EntryType.Task,
            "idea" => EntryType.Idea,
            "block" => EntryType.Block,
            "decision" => EntryType.Decision,
            "question" => EntryType.Question,
            "gap" => EntryType.Gap,
            "analogy" => EntryType.Analogy,
            _ => EntryType.Log
        };
    }

    /// <summary>
    /// Resolves the yearly-file destination for the entry. Throws
    /// RoutePrivateDesktopOnlyException when the target route is marked
    /// private -- backfill bypasses FileRouter so the masking gate is
    /// enforced here directly (MCP cannot mask content).
    /// </summary>
    internal (string fullPath, string yearFile) GetDestinationPath(Entry entry)
    {
        var typeName = EntryTypeMapper.GetFolderName(entry.Type);
        var year = entry.Timestamp.Year;
        var yearFile = $"{year}.md";

        if (entry.Projects.Count > 0)
        {
            var fullRouteName = entry.Projects[0];

            // Handle two-level routing: "logmark/ux" -> route "logmark" + scope "ux"
            var routeParts = fullRouteName.Split('/');
            var routeName = routeParts[0];
            var scope = routeParts.Length > 1 ? string.Join("/", routeParts.Skip(1)) : null;

            var routes = _services.RouteService!.GetRoutes();
            var route = routes.FirstOrDefault(r => r.Name.Equals(routeName, StringComparison.OrdinalIgnoreCase));

            if (route != null && !string.IsNullOrEmpty(route.Path))
            {
                if (route.Private is not null)
                    throw new RoutePrivateDesktopOnlyException(route.Name);

                // Build path: route.Path + scope (if any)
                var routePath = scope != null
                    ? Path.Combine(route.Path, scope)
                    : route.Path;

                var relativePath = PathConstants.GetRoutedFilePath(routePath, typeName, year);
                return (Path.Combine(_services.VaultPath!, relativePath), yearFile);
            }
        }

        // Unrouted - goes to _inbox
        var unroutedPath = PathConstants.GetUnroutedFilePath(typeName, year);
        return (Path.Combine(_services.VaultPath!, unroutedPath), yearFile);
    }

    /// <summary>
    /// Inserts the entry into the yearly file at its chronological position.
    /// BuildEntryText assigns entry.ShortId (the persisted _id:/_task: anchor)
    /// as a side effect. Returns the 1-based insert position.
    /// </summary>
    internal async Task<int> InsertEntryChronologicallyAsync(string filePath, Entry entry)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var existingContent = File.Exists(filePath) ? await File.ReadAllTextAsync(filePath) : "";
        existingContent = existingContent.TrimStart('\uFEFF'); // Remove BOM if present

        var entryText = BuildEntryText(entry);

        if (string.IsNullOrWhiteSpace(existingContent))
        {
            await _services.VaultWriter!.WriteFileAsync(filePath, entryText.TrimStart('\n'));
            return 1;
        }

        var entries = ParseEntryPositions(existingContent);
        var insertIndex = entries.FindIndex(e => e.Timestamp > entry.Timestamp);

        if (insertIndex == -1)
        {
            await _services.VaultWriter!.WriteFileAsync(filePath, existingContent + entryText);
            return entries.Count + 1;
        }

        var insertPosition = entries[insertIndex].StartPosition;
        var newContent = existingContent.Substring(0, insertPosition) + entryText.TrimStart('\n') + "\n" + existingContent.Substring(insertPosition);
        await _services.VaultWriter!.WriteFileAsync(filePath, newContent);
        return insertIndex + 1;
    }

    private static List<(DateTime Timestamp, int StartPosition)> ParseEntryPositions(string content)
    {
        var result = new List<(DateTime, int)>();
        var matches = EntryHeaderPattern().Matches(content);

        foreach (Match match in matches)
        {
            if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                // Find the start of this entry (look for preceding blank line or start of file)
                var position = match.Index;
                while (position > 0 && content[position - 1] == '\n')
                    position--;
                if (position > 0)
                    position++; // Keep one newline before

                result.Add((timestamp, position));
            }
        }

        return result;
    }

    private static string BuildEntryText(Entry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine(); // Blank line before entry

        // Header with inline context
        var header = $"## {entry.Timestamp:yyyy-MM-dd HH:mm}";
        if (!string.IsNullOrWhiteSpace(entry.Context))
        {
            header += $" | {TruncateContext(entry.Context)}";
        }
        sb.AppendLine(header);

        // Content with notation stripped
        var content = FileRouter.StripNotation(entry.RawContent, entry.Type, entry.ProseRouteTokens);
        sb.AppendLine(content);

        // Persist a ShortId anchor exactly as FileRouter/VaultService do, so the
        // entry is addressable on disk (EntryResolver reads _id:/_task: back as the
        // entry id). entry.ShortId is set as a side effect for the caller to return.
        var shortId = TaskTracker.GenerateShortId();
        entry.ShortId = shortId;
        var blockIdSuffix = $" ^{shortId}";

        if (entry.Type == EntryType.Task)
        {
            if (entry.Deadline != null)
            {
                var isoFormat = entry.Deadline.DateTime.ToString("yyyy-MM-dd, HH:mm");
                sb.AppendLine($"_task: {isoFormat} | postponed: 0 | {shortId}{blockIdSuffix}");
            }
            else
            {
                sb.AppendLine($"_task: none | postponed: 0 | {shortId}{blockIdSuffix}");
            }
        }
        else
        {
            sb.AppendLine($"_id: {shortId}{blockIdSuffix}");
        }

        // Source attribution
        if (!string.IsNullOrEmpty(entry.Source))
        {
            sb.AppendLine($"source: {entry.Source}");
        }

        // Session reference
        if (!string.IsNullOrEmpty(entry.Session))
        {
            sb.AppendLine($"session: {entry.Session}");
        }

        return sb.ToString();
    }

    private static string TruncateContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context) || context.Length <= 75)
            return context;

        var truncated = context.Substring(0, 75);
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > 50)
            truncated = truncated.Substring(0, lastSpace);
        return truncated.TrimEnd('.', ',', ' ') + "...";
    }
}
