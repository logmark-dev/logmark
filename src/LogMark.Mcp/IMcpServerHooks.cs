using LogMark.Mcp.Protocol;

namespace LogMark.Mcp;

/// <summary>
/// Closed-side per-call hooks for <see cref="McpServer"/>. The open build leaves
/// this null: every registered tool is available and no usage is recorded. The
/// closed LogMark.Mcp.Producers assembly supplies an implementation backed by
/// the entitlement and usage-tracking services (Core types the open substrate
/// never sees), so the open server holds no reference to those service or
/// model types at all.
/// </summary>
public interface IMcpServerHooks
{
    /// <summary>
    /// Called before a tool executes. Returns a structured gating result when the
    /// caller's license does not permit the named tool (the server returns it
    /// verbatim and does not run the tool), or null to allow execution.
    /// </summary>
    Task<ToolCallResult?> CheckToolAccessAsync(string toolName);

    /// <summary>
    /// Records a write-shaped tool invocation for analytics. Best-effort; the
    /// server ignores exceptions. Never consulted for gating.
    /// </summary>
    void RecordWrite(string toolName);
}
