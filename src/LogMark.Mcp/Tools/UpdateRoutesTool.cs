using LogMark.Mcp.Protocol;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to replace routes.md with new content.
/// Validates, backs up, writes, and reloads.
/// </summary>
public class UpdateRoutesTool : ITool
{
    private readonly VaultServices _services;

    public UpdateRoutesTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "update_routes";

    public ToolDefinition Definition => new()
    {
        Name = "update_routes",
        Description = "Replace the vault's routes.md with new content. Validates the markdown, backs up the previous version, writes the new content, and reloads the route cache. Use get_routes first to understand current structure. Find this tool when: add or rename project folders, reorganize filing.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Full markdown content for routes.md. Must follow the routes format: # Routes header, ## sections, ### route names with - path:, - description:, - scopes: properties."
                }
            },
            Required = new List<string> { "content" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = true, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var content = McpToolHelpers.GetStringArg(arguments, "content");
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(McpToolHelpers.ErrorResult("'content' is required and must contain routes.md markdown"));

        try
        {
            var result = _services.RouteService!.UpdateRoutes(content);

            // Track in session
            _services.SessionContext!.RecordModification("logmark/_system/routes.md");

            var response = new UpdateRoutesResponse
            {
                RouteCount = result.RouteCount,
                Sections = result.Sections,
                RouteNames = result.RouteNames,
                VaultNotes = result.VaultNotes,
                Warnings = result.Warnings.Count > 0 ? result.Warnings : null,
                Session = _services.SessionContext!.GetActiveSessionInfo()
            };

            return Task.FromResult(McpToolHelpers.SuccessResult(response));
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"update_routes failed: {ex.Message}");
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to update routes: {ex.Message}"));
        }
    }

    private class UpdateRoutesResponse
    {
        public int RouteCount { get; set; }
        public List<string> Sections { get; set; } = new();
        public List<string> RouteNames { get; set; } = new();
        public string? VaultNotes { get; set; }
        public List<string>? Warnings { get; set; }
        public ActiveSessionInfo? Session { get; set; }
    }
}
