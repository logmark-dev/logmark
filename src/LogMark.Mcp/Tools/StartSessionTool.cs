using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to explicitly start a new agent work session.
/// Sessions are auto-created on first mutation, so this is optional.
/// Use this to explicitly set route/agent or to start a new session.
/// </summary>
public class StartSessionTool : ITool
{
    private readonly VaultServices _services;

    public StartSessionTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "start_session";

    public ToolDefinition Definition => new()
    {
        Name = "start_session",
        Description = "Start a new agent work session. Optional - sessions are auto-created on first mutation. Use this to explicitly set route/agent or start fresh. Find this tool when: begin tracked work period, clock in for attribution.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Focus route for this session (e.g., '+logmark' or 'logmark')."
                },
                ["agent"] = new()
                {
                    Type = "string",
                    Description = "Agent identifier (e.g., 'claude-code', 'cursor'). Default: inherited from server startup."
                }
            },
            Required = new List<string> { "route" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var route = McpToolHelpers.GetStringArg(arguments, "route");
        if (string.IsNullOrWhiteSpace(route))
        {
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: route"));
        }

        var agent = McpToolHelpers.GetStringArg(arguments, "agent");

        try
        {
            // Check if there's already an active materialized session
            var existingSession = _services.SessionContext!.GetActiveSession();
            var wasMaterialized = _services.SessionContext!.IsMaterialized;

            // Start new session (ends any existing one)
            var session = _services.SessionContext!.StartNewSession(route, agent);

            var result = new StartSessionResult
            {
                SessionId = session.SessionId,
                Started = session.Started.ToString("O"),
                Route = session.Route,
                Agent = session.Agent
            };

            if (wasMaterialized && existingSession != null)
            {
                result.PreviousSession = new PreviousSessionInfo
                {
                    SessionId = existingSession.SessionId,
                    EntriesCreated = existingSession.EntriesCreated.Count
                };
            }

            return Task.FromResult(McpToolHelpers.SuccessResult(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to start session: {ex.Message}"));
        }
    }

    private class StartSessionResult
    {
        public string SessionId { get; set; } = "";
        public string Started { get; set; } = "";
        public string Route { get; set; } = "";
        public string Agent { get; set; } = "";
        public PreviousSessionInfo? PreviousSession { get; set; }
    }

    private class PreviousSessionInfo
    {
        public string SessionId { get; set; } = "";
        public int EntriesCreated { get; set; }
    }
}
