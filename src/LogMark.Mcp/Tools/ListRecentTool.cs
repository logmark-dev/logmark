using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to list recent entries from the vault.
/// </summary>
public class ListRecentTool : ITool
{
    private readonly VaultServices _services;

    public ListRecentTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "list_recent";

    public ToolDefinition Definition => new()
    {
        Name = "list_recent",
        Description = "List recent entries from the LogMark vault. Returns entries from the past N days with optional filtering by route, type, and tags. Results are concise (id, type, timestamp, truncated content); fetch full detail with read_entry(id). Find this tool when: latest captures, what happened lately, newest notes.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Filter by route (e.g., 'logmark' or '+logmark')."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Filter by scope within route."
                },
                ["entry_types"] = new()
                {
                    Type = "array",
                    Description = "Filter by entry types.",
                    Items = new JsonSchemaProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "task", "idea", "block", "decision", "log", "workflow", "question", "gap", "analogy" }
                    }
                },
                ["tags"] = new()
                {
                    Type = "array",
                    Description = "Filter by tags (entries must have ALL specified tags).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of entries to return.",
                    Default = 20
                },
                ["days"] = new()
                {
                    Type = "integer",
                    Description = "Time window in days.",
                    Default = 7
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
        var entryTypes = McpToolHelpers.GetStringArrayArg(arguments, "entry_types");
        var tags = McpToolHelpers.GetStringArrayArg(arguments, "tags");
        var limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 20;
        var days = McpToolHelpers.GetIntArg(arguments, "days") ?? 7;

        try
        {
            var filter = new SearchQuery
            {
                Route = route,
                Scope = scope,
                Types = ParseEntryTypes(entryTypes),
                RequiredTags = tags,
                Limit = limit
            };

            var result = _services.SearchService!.GetRecent(days, filter);
            // List(200) truncates Content in place; Preview(200) added a
            // ContentPreview field ALONGSIDE the still-full Content, making
            // every result strictly larger. Concise shape drops per-entry
            // metadata; read_entry(id) returns the full record (bead lmk-1zld).
            var entries = ConciseEntry.FromMany(
                _services.EntryResolver!.ResolveMany(result.Entries, ResolveOptions.List(200)));

            return Task.FromResult(McpToolHelpers.SuccessResult(new
            {
                Entries = entries,
                TotalCount = result.TotalCount,
                Breakdown = ResultShaping.Breakdown(result),
                QueryParams = new { route, scope, entryTypes, tags, limit, days }
            }));
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"MCP list_recent failed: {ex.Message}");
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to list entries: {ex.Message}"));
        }
    }

    private static List<EntryType>? ParseEntryTypes(List<string>? typeStrings)
    {
        if (typeStrings == null || typeStrings.Count == 0) return null;

        var types = new List<EntryType>();
        foreach (var t in typeStrings)
        {
            if (Enum.TryParse<EntryType>(t, ignoreCase: true, out var et))
                types.Add(et);
        }
        return types.Count > 0 ? types : null;
    }
}
