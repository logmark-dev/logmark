using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to query open blockers.
/// </summary>
public class GetBlocksTool : ITool
{
    private readonly VaultServices _services;

    public GetBlocksTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_blocks";

    public ToolDefinition Definition => new()
    {
        Name = "get_blocks",
        Description = "Query open blockers for a route, optionally filtered by scope and age. Results are concise (id, timestamp, truncated content, age); fetch full detail with read_entry(id). Find this tool when: stuck points, obstacles, what is blocking progress.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Optional route to filter by (e.g., 'logmark')."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Optional scope within route (e.g., 'architecture')."
                },
                ["age_days"] = new()
                {
                    Type = "integer",
                    Description = "Filter to blocks older than N days."
                },
                ["include_resolved"] = new()
                {
                    Type = "boolean",
                    Description = "Include resolved and dismissed blocks (marked with #resolved/#dismissed or strikethrough). Default: false, which excludes both closed states.",
                    Default = false
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of blocks to return. Default: 25. TotalCount reports the full open count.",
                    Default = 25
                }
            }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var route = McpToolHelpers.GetStringArg(arguments, "route")?.TrimStart('+');
        var scope = McpToolHelpers.GetStringArg(arguments, "scope");
        var ageDays = McpToolHelpers.GetIntArg(arguments, "age_days");
        var includeResolved = McpToolHelpers.GetBoolArg(arguments, "include_resolved") ?? false;
        var limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 25;

        var now = DateTime.Now;

        try
        {
            var query = new SearchQuery
            {
                Types = new[] { EntryType.Block },
                Route = route,
                Scope = scope,
                // For age_days: blocks OLDER than N days means Before = now - ageDays
                Before = ageDays.HasValue ? now.AddDays(-ageDays.Value) : null,
                Lifecycle = includeResolved ? null : LifecycleFilter.Open,
                // No longer over-collects: closure is applied before the limit.
                Limit = limit,
                Sort = SearchSort.TimestampDesc
            };

            var result = _services.SearchService!.Search(query);
            // List(200) truncates content; ConciseEntry projection happens
            // after filtering (bead lmk-1zld).
            var resolved = _services.EntryResolver!.ResolveMany(result.Entries, ResolveOptions.List(200));

            // Newest first. The previous OrderByDescending(AgeDays) answered
            // "what has been stuck longest", which put a January test artifact
            // ahead of a blocker raised this morning.
            var totalOpen = result.TotalCount;

            // Oldest across the full open set, not the returned page.
            // AllMatches is every match in sort order ignoring Limit (see
            // SearchResult's doc comment on AllMatches) - aggregating over
            // Entries/resolved here would describe only the newest page while
            // this figure is labelled alongside the full-set TotalCount, so
            // OldestDays would silently understate once open count exceeds
            // limit. Reads the raw timestamp instead of going through
            // EntryResolver, so a large open set is not fully resolved just
            // to read one number.
            var oldestDays = result.AllMatches.Count > 0
                ? result.AllMatches.Max(ie => (int)(now - ie.Entry.Timestamp).TotalDays)
                : 0;

            return Task.FromResult(McpToolHelpers.SuccessResult(new
            {
                Blocks = ConciseEntry.FromMany(resolved),
                // Open blocks surviving the filter, before the page limit, so
                // the agent knows more exist beyond the returned page.
                TotalCount = totalOpen,
                OldestDays = oldestDays,
                Breakdown = ResultShaping.Breakdown(result)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to get blocks: {ex.Message}"));
        }
    }
}
