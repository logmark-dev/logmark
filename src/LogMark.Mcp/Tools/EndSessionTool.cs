using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to end an active agent work session.
/// </summary>
public class EndSessionTool : ITool
{
    private readonly VaultServices _services;

    public EndSessionTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "end_session";

    public ToolDefinition Definition => new()
    {
        Name = "end_session",
        Description = "End this connection's work session - the one whose id appears as session_info on your own mutation responses. Each MCP connection owns exactly one session and can only end that one; this never touches sessions owned by other connections or other agents. Calculates session statistics and marks as completed. Find this tool when: finish tracked work period, clock out.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>()
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        try
        {
            // Check materialization before ending (EndSession creates new pending session after)
            var wasMaterialized = _services.SessionContext!.IsMaterialized;

            // Captured before the call, because the context clears its state either way.
            var sessionId = _services.SessionContext!.GetActiveSession()?.SessionId;

            var session = _services.SessionContext!.EndSession();
            if (session == null)
            {
                // Two different situations reach a null here, and they must not share a
                // message. Not materialized means there was genuinely nothing to close.
                // Materialized means a real record existed and SessionService refused to
                // close it, because it could not read the record back for a safe rewrite -
                // a close rewrites the whole file, so acting would destroy whatever
                // survived. Saying "no active session" there would report a false fact
                // about the vault, which is the one thing this subsystem must not do.
                if (!wasMaterialized)
                {
                    return Task.FromResult(McpToolHelpers.ErrorResult("No active session to end"));
                }

                return Task.FromResult(McpToolHelpers.ErrorResult(
                    $"Session {sessionId} could not be closed: its record could not be read back for a safe rewrite, " +
                    "so it was left untouched and is still active on disk. Nothing was lost. The sweeper will reclaim " +
                    "it, and your next write starts a fresh session."));
            }

            // If session was never materialized, report that
            if (!wasMaterialized)
            {
                return Task.FromResult(McpToolHelpers.SuccessResult(new EndSessionResult
                {
                    SessionId = session.SessionId,
                    WasMaterialized = false,
                    Message = "Session ended without any mutations (was never persisted)"
                }));
            }

            var durationMinutes = session.Ended.HasValue
                ? (int)(session.Ended.Value - session.Started).TotalMinutes
                : 0;

            return Task.FromResult(McpToolHelpers.SuccessResult(new EndSessionResult
            {
                SessionId = session.SessionId,
                Ended = session.Ended?.ToString("O") ?? "",
                DurationMinutes = durationMinutes,
                EntriesCreated = session.EntriesCreated.Count,
                EntriesModified = session.EntriesModified.Count,
                PendingReview = session.PendingReview.Count,
                WasMaterialized = true
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to end session: {ex.Message}"));
        }
    }

    private class EndSessionResult
    {
        public string SessionId { get; set; } = "";
        public string? Ended { get; set; }
        public int DurationMinutes { get; set; }
        public int EntriesCreated { get; set; }
        public int EntriesModified { get; set; }
        public int PendingReview { get; set; }
        public bool WasMaterialized { get; set; }
        public string? Message { get; set; }
    }
}
