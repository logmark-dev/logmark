using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to list sessions with filters, or retrieve a single session's detail.
/// Modes:
///   session_id set   -> single-session detail (same shape as the former get_session tool)
///   active_only true -> active session detail (same shape)
///   neither          -> list of session summaries (original behavior)
/// </summary>
public class GetSessionsTool : ITool
{
    private readonly VaultServices _services;

    public GetSessionsTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_sessions";

    public ToolDefinition Definition => new()
    {
        Name = "get_sessions",
        Description = "List sessions with optional filters, or retrieve a single session's detail. Pass session_id for detail of one session; pass active_only: true for the current active session; omit both for a summary list. Find this tool when: history of tracked work periods, inspect one session by id, currently active session detail.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["session_id"] = new()
                {
                    Type = "string",
                    Description = "Session ID to retrieve detail for. Returns the full detail shape for that session."
                },
                ["active_only"] = new()
                {
                    Type = "boolean",
                    Description = "When true (and no session_id given), returns the active session's full detail. Default: false."
                },
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Filter by route (e.g., 'logmark' or '+logmark'). Applies to list mode only."
                },
                ["date"] = new()
                {
                    Type = "string",
                    Description = "Filter by date (format: YYYY-MM-DD). Applies to list mode only."
                },
                ["agent"] = new()
                {
                    Type = "string",
                    Description = "Filter by agent identifier. Applies to list mode only."
                },
                ["status"] = new()
                {
                    Type = "string",
                    Description = "Filter by status. Applies to list mode only.",
                    Enum = new List<string> { "active", "completed", "abandoned" }
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of sessions to return. Default: 50. Applies to list mode only.",
                    Default = 50
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
        try
        {
            var sessionId = McpToolHelpers.GetStringArg(arguments, "session_id");
            var activeOnly = McpToolHelpers.GetBoolArg(arguments, "active_only") ?? false;

            // Mode: single-session detail
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var session = _services.SessionService!.GetSession(sessionId);
                if (session == null)
                {
                    return Task.FromResult(McpToolHelpers.SuccessResult(new GetSessionResult
                    {
                        Found = false,
                        Message = $"Session not found: {sessionId}"
                    }));
                }
                return Task.FromResult(McpToolHelpers.SuccessResult(BuildSessionDetail(session)));
            }

            // Mode: active-session detail
            if (activeOnly)
            {
                var session = _services.SessionService!.GetActiveSession();
                if (session == null)
                {
                    return Task.FromResult(McpToolHelpers.SuccessResult(new GetSessionResult
                    {
                        Found = false,
                        Message = "No active session"
                    }));
                }
                return Task.FromResult(McpToolHelpers.SuccessResult(BuildSessionDetail(session)));
            }

            // Mode: list (original behavior)
            var query = new SessionQuery
            {
                Route = McpToolHelpers.GetStringArg(arguments, "route"),
                Date = McpToolHelpers.GetStringArg(arguments, "date"),
                Agent = McpToolHelpers.GetStringArg(arguments, "agent"),
                Limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 50
            };

            var statusStr = McpToolHelpers.GetStringArg(arguments, "status");
            if (!string.IsNullOrEmpty(statusStr))
            {
                query.Status = statusStr.ToLowerInvariant() switch
                {
                    "completed" => SessionStatus.Completed,
                    "abandoned" => SessionStatus.Abandoned,
                    _ => SessionStatus.Active
                };
            }

            var sessions = _services.SessionService!.GetSessions(query);

            var listResult = new GetSessionsResult
            {
                Sessions = sessions.Select(s => new SessionSummaryDto
                {
                    SessionId = s.SessionId,
                    Started = s.Started.ToString("O"),
                    Ended = s.Ended?.ToString("O"),
                    Agent = s.Agent,
                    Route = s.Route,
                    Status = s.Status.ToString().ToLowerInvariant(),
                    EntriesCreated = s.EntriesCreated,
                    EntriesModified = s.EntriesModified,
                    PendingReview = s.PendingReview,
                    DurationMinutes = s.DurationMinutes
                }).ToList(),
                TotalCount = sessions.Count
            };

            return Task.FromResult(McpToolHelpers.SuccessResult(listResult));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to list sessions: {ex.Message}"));
        }
    }

    private static GetSessionResult BuildSessionDetail(Session session)
    {
        var durationMinutes = session.Ended.HasValue
            ? (int)(session.Ended.Value - session.Started).TotalMinutes
            : (int)(DateTime.Now - session.Started).TotalMinutes;

        return new GetSessionResult
        {
            Found = true,
            Session = new SessionDetails
            {
                SessionId = session.SessionId,
                Started = session.Started.ToString("O"),
                Ended = session.Ended?.ToString("O"),
                Agent = session.Agent,
                Route = session.Route,
                Status = session.Status.ToString().ToLowerInvariant(),
                DurationMinutes = durationMinutes,
                ContextLoaded = session.ContextLoaded != null
                    ? new ContextLoadedDetails
                    {
                        Decisions = session.ContextLoaded.DecisionsLoaded,
                        Tasks = session.ContextLoaded.TasksLoaded,
                        Blocks = session.ContextLoaded.BlocksLoaded
                    }
                    : null,
                EntriesCreated = session.EntriesCreated,
                EntriesModified = session.EntriesModified,
                PendingReview = session.PendingReview
            }
        };
    }

    private class GetSessionResult
    {
        public bool Found { get; set; }
        public SessionDetails? Session { get; set; }
        public string? Message { get; set; }
    }

    private class SessionDetails
    {
        public string SessionId { get; set; } = "";
        public string Started { get; set; } = "";
        public string? Ended { get; set; }
        public string Agent { get; set; } = "";
        public string Route { get; set; } = "";
        public string Status { get; set; } = "";
        public int DurationMinutes { get; set; }
        public ContextLoadedDetails? ContextLoaded { get; set; }
        public List<string> EntriesCreated { get; set; } = new();
        public List<string> EntriesModified { get; set; } = new();
        public List<string> PendingReview { get; set; } = new();
    }

    private class ContextLoadedDetails
    {
        public int Decisions { get; set; }
        public int Tasks { get; set; }
        public int Blocks { get; set; }
    }

    private class GetSessionsResult
    {
        public List<SessionSummaryDto> Sessions { get; set; } = new();
        public int TotalCount { get; set; }
    }

    private class SessionSummaryDto
    {
        public string SessionId { get; set; } = "";
        public string Started { get; set; } = "";
        public string? Ended { get; set; }
        public string Agent { get; set; } = "";
        public string Route { get; set; } = "";
        public string Status { get; set; } = "";
        public int EntriesCreated { get; set; }
        public int EntriesModified { get; set; }
        public int PendingReview { get; set; }
        public int DurationMinutes { get; set; }
    }
}
