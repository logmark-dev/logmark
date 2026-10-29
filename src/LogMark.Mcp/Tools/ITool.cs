using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Interface for MCP tools.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Tool name as exposed via MCP.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Tool definition including input schema.
    /// </summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Execute the tool with given arguments.
    /// </summary>
    Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct);
}
