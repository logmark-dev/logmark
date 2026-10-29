using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool returning a capped vault index for agent context.
/// Designed to be called once per session to orient an agent on vault contents.
/// </summary>
public class VaultSummaryTool : ITool
{
    private readonly VaultServices _services;

    public VaultSummaryTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "vault_summary";

    public ToolDefinition Definition => new()
    {
        Name = "vault_summary",
        Description = "Get a compact vault summary for orientation: route list, entry counts by type, top tags, recent activity, and active session. Call once at start of a session to understand vault contents before querying specific entries. Find this tool when: orientation overview, what is in this knowledge base.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["recent_days"] = new()
                {
                    Type = "integer",
                    Description = "Days of recent activity to summarize.",
                    Default = 7
                },
                ["top_tags_limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of tags to return in the tag cloud.",
                    Default = 20
                }
            }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
        Meta = new Dictionary<string, object?> { ["anthropic/alwaysLoad"] = true },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var recentDays = McpToolHelpers.GetIntArg(arguments, "recent_days") ?? 7;
        var topTagsLimit = McpToolHelpers.GetIntArg(arguments, "top_tags_limit") ?? 20;

        try
        {
            // Entry counts by type
            var allEntries = _services.EntryCache!.GetAllEntries();
            var countsByType = new Dictionary<string, int>();
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var indexed in allEntries)
            {
                var typeName = indexed.Entry.Type.ToString().ToLowerInvariant();
                if (countsByType.ContainsKey(typeName))
                    countsByType[typeName]++;
                else
                    countsByType[typeName] = 1;

                var entryTags = NotationParser.ExtractTags(indexed.Entry.Content);
                foreach (var tag in entryTags)
                {
                    if (tagCounts.ContainsKey(tag))
                        tagCounts[tag]++;
                    else
                        tagCounts[tag] = 1;
                }
            }

            // Top tags by frequency
            var topTags = tagCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(topTagsLimit)
                .Select(kvp => new TagInfo { Name = kvp.Key, Count = kvp.Value })
                .ToList();

            // Routes with entry counts
            var routes = _services.RouteService!.GetRoutes();
            var routeSummaries = new List<RouteSummary>();
            foreach (var route in routes)
            {
                if (route.Status == "archived") continue;

                var routeEntries = allEntries.Where(e =>
                    e.Route != null && e.Route.Equals(route.Name, StringComparison.OrdinalIgnoreCase)).ToList();

                routeSummaries.Add(new RouteSummary
                {
                    Name = route.Name,
                    Description = route.Description,
                    EntryCount = routeEntries.Count,
                    Status = route.Status
                });
            }

            // Recent activity
            var cutoff = DateTime.Now.AddDays(-recentDays);
            var recentEntries = allEntries.Where(e => e.Entry.Timestamp >= cutoff).ToList();
            var recentByType = new Dictionary<string, int>();
            foreach (var entry in recentEntries)
            {
                var typeName = entry.Entry.Type.ToString().ToLowerInvariant();
                if (recentByType.ContainsKey(typeName))
                    recentByType[typeName]++;
                else
                    recentByType[typeName] = 1;
            }

            // Pending tasks
            var pendingTasks = _services.EntryCache.GetPendingTasks();

            // Active session
            var activeSession = _services.SessionContext?.GetActiveSessionInfo();

            var result = new VaultSummaryResult
            {
                TotalEntries = allEntries.Count,
                CountsByType = countsByType,
                Routes = routeSummaries,
                TopTags = topTags,
                RecentActivity = new RecentActivityInfo
                {
                    Days = recentDays,
                    TotalEntries = recentEntries.Count,
                    CountsByType = recentByType
                },
                PendingTaskCount = pendingTasks.Count,
                ActiveSession = activeSession
            };

            return Task.FromResult(McpToolHelpers.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _services.Logger?.Error($"MCP vault_summary failed: {ex.Message}");
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to generate vault summary: {ex.Message}"));
        }
    }

    private class VaultSummaryResult
    {
        public int TotalEntries { get; set; }
        public Dictionary<string, int> CountsByType { get; set; } = new();
        public List<RouteSummary> Routes { get; set; } = new();
        public List<TagInfo> TopTags { get; set; } = new();
        public RecentActivityInfo RecentActivity { get; set; } = new();
        public int PendingTaskCount { get; set; }
        public ActiveSessionInfo? ActiveSession { get; set; }
    }

    private class RouteSummary
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int EntryCount { get; set; }
        public string? Status { get; set; }
    }

    private class TagInfo
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    private class RecentActivityInfo
    {
        public int Days { get; set; }
        public int TotalEntries { get; set; }
        public Dictionary<string, int> CountsByType { get; set; } = new();
    }
}
