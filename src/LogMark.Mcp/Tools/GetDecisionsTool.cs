using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to query frozen decisions for context loading.
/// Primary use case: Load project constitution at session start.
/// </summary>
public class GetDecisionsTool : ITool
{
    private readonly VaultServices _services;

    public GetDecisionsTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_decisions";

    public ToolDefinition Definition => new()
    {
        Name = "get_decisions",
        Description = "Query decisions for a route, optionally filtered by scope and status tags. Primary use: Load frozen architecture decisions at session start. Find this tool when: choices made, rationale history, what was settled.",
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
                    Description = "Required route to query (e.g., 'logmark' or '+logmark')."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Optional scope within route (e.g., 'architecture', 'ux')."
                },
                ["status_tags"] = new()
                {
                    Type = "array",
                    Description = "Filter by status tags (e.g., ['#frozen']). Default: all decisions.",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["exclude_tags"] = new()
                {
                    Type = "array",
                    Description = "Exclude entries with these tags (e.g., ['#obsolete']).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of decisions to return. Default: 25. TotalCount reports the full match count.",
                    Default = 25
                }
            },
            Required = new List<string> { "route" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var route = McpToolHelpers.GetStringArg(arguments, "route")?.TrimStart('+');
        if (string.IsNullOrWhiteSpace(route))
        {
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: route"));
        }

        var scope = McpToolHelpers.GetStringArg(arguments, "scope");
        var statusTags = McpToolHelpers.GetStringArrayArg(arguments, "status_tags");
        var excludeTags = McpToolHelpers.GetStringArrayArg(arguments, "exclude_tags");
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
                Types = new[] { EntryType.Decision },
                Route = route,
                Scope = scope,
                RequiredTags = statusTags,
                ExcludedTags = excludeTags,
                After = after,
                Before = before,
                Limit = limit,
                Sort = SearchSort.TimestampAsc // Chronological for decisions
            };

            var result = _services.SearchService!.Search(query);
            // Query, not List: decision text is this tool's semantic payload,
            // never truncated. The ConciseEntry projection trims only the
            // per-entry metadata (session GUID, path, line numbers) that
            // dominated oversized list responses (bead lmk-1zld).
            var decisions = ConciseEntry.FromMany(
                _services.EntryResolver!.ResolveMany(result.Entries, ResolveOptions.Query));

            return Task.FromResult(McpToolHelpers.SuccessResult(new
            {
                Route = route,
                Scope = scope,
                Decisions = decisions,
                TotalCount = result.TotalCount,
                Breakdown = ResultShaping.Breakdown(result)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to get decisions: {ex.Message}"));
        }
    }
}
