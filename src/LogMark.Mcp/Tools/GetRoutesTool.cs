using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to get available routes from the vault.
/// </summary>
public class GetRoutesTool : ITool
{
    private readonly VaultServices _services;

    public GetRoutesTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_routes";

    public ToolDefinition Definition => new()
    {
        Name = "get_routes",
        Description = "Get available routes (projects, domains) from the LogMark vault. Use this to discover where entries can be routed. Find this tool when: project folders, organization structure, where notes file.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["include_archived"] = new()
                {
                    Type = "boolean",
                    Description = "Include archived routes.",
                    Default = false
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
        var includeArchived = GetBoolArg(arguments, "include_archived") ?? false;

        var routes = _services.RouteService!.GetRoutes();
        var routeInfos = new List<RouteInfo>();

        foreach (var route in routes)
        {
            // Skip archived if not requested
            if (!includeArchived && route.Status == "archived")
                continue;

            var routeType = DetermineRouteType(route);

            routeInfos.Add(new RouteInfo
            {
                Name = route.Name,
                Type = routeType,
                Path = route.Path ?? "",
                Description = route.Description,
                Section = route.Section,
                SectionNotes = route.SectionNotes,
                Status = route.Status,
                Tags = route.Tags.Count > 0 ? route.Tags : null
            });
        }

        var result = new GetRoutesResult
        {
            Routes = routeInfos
        };

        return Task.FromResult(new ToolCallResult
        {
            Content = new List<ToolContent>
            {
                new() { Type = "text", Text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) }
            }
        });
    }

    private static string DetermineRouteType(Route route)
    {
        if (route.Path == null)
            return "reference";

        if (route.Path.StartsWith("projects/", StringComparison.OrdinalIgnoreCase))
            return "project";
        if (route.Path.StartsWith("domains/", StringComparison.OrdinalIgnoreCase))
            return "domain";
        if (route.Path.StartsWith("clients/", StringComparison.OrdinalIgnoreCase))
            return "client";
        if (route.Path.StartsWith("areas/", StringComparison.OrdinalIgnoreCase))
            return "area";

        return "project"; // Default
    }

    private static List<string>? ParseTags(string tagsStr)
    {
        if (string.IsNullOrWhiteSpace(tagsStr))
            return null;

        return tagsStr
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().TrimStart('#'))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    private static bool? GetBoolArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null) return null;
        if (args.TryGetValue(key, out var value) && value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private class GetRoutesResult
    {
        public List<RouteInfo> Routes { get; set; } = new();
    }

    private class RouteInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Description { get; set; }
        public string? Section { get; set; }
        public string? SectionNotes { get; set; }
        public string? Status { get; set; }
        public List<string>? Tags { get; set; }
    }
}
