using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to query tasks with status filters.
/// </summary>
public class GetTasksTool : ITool
{
    private readonly VaultServices _services;

    public GetTasksTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_tasks";

    public ToolDefinition Definition => new()
    {
        Name = "get_tasks",
        Description = "Query tasks for a route, optionally filtered by scope, status tags, and completion state. Results are concise (id, timestamp, truncated content, deadline); fetch full detail with read_entry(id). Find this tool when: open todos, pending action items, work remaining.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["after"] = ResultShaping.AfterProperty,
                ["before"] = ResultShaping.BeforeProperty,
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
                ["status_tags"] = new()
                {
                    Type = "array",
                    Description = "Filter by status tags (e.g., ['#active']).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["include_completed"] = new()
                {
                    Type = "boolean",
                    Description = "Include closed tasks - both completed and dismissed. Default: false, which excludes both.",
                    Default = false
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of tasks to return. Default: 25. TotalCount reports the full match count.",
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
        var statusTags = McpToolHelpers.GetStringArrayArg(arguments, "status_tags");
        var includeCompleted = McpToolHelpers.GetBoolArg(arguments, "include_completed") ?? false;
        var limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 25;
        var boundsError = ResultShaping.TryParseBounds(arguments, out var after, out var before);
        if (boundsError != null)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult(boundsError));
        }

        try
        {
            var query = new SearchQuery
            {
                Types = new[] { EntryType.Task },
                Route = route,
                Scope = scope,
                RequiredTags = statusTags,
                After = after,
                Before = before,
                IsCompleted = includeCompleted ? null : false,
                // Closure now travels in the query, so TotalCount below counts
                // exactly the rows returned and the page is not shortened by a
                // filter running after Take(limit).
                Lifecycle = includeCompleted ? null : LifecycleFilter.Open,
                Limit = limit,
                Sort = SearchSort.DeadlineAsc
            };

            var result = _services.SearchService!.Search(query);
            // List(200) + ConciseEntry: truncated content, trimmed metadata.
            // read_entry(id) returns the full record (bead lmk-1zld).
            var resolved = _services.EntryResolver!.ResolveMany(result.Entries, ResolveOptions.List(200));
            var tasks = ConciseEntry.FromMany(resolved);

            return Task.FromResult(McpToolHelpers.SuccessResult(new
            {
                Tasks = tasks,
                // Pre-limit total from the search layer, so the agent knows
                // more exist beyond the returned page.
                TotalCount = result.TotalCount,
                Breakdown = ResultShaping.Breakdown(result)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to get tasks: {ex.Message}"));
        }
    }
}
