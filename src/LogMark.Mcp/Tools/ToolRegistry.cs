using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Registry for MCP tools. The open registry has no tier concept: it exposes
/// every registered tool. Which tools are registered is the source-level gate -
/// the open build registers only the kernel tool set; the shipped (closed) build
/// additionally registers the Pro/closed tools and gates their execution per-call
/// via <see cref="IMcpServerHooks"/> (design 4.1: gating is registration-time and
/// per-call, never a list-time filter inside this open registry).
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    /// <summary>
    /// Tool list of every registered tool (no tier filter).
    /// </summary>
    public ToolsListResult GetToolList()
    {
        return new ToolsListResult
        {
            Tools = _tools.Values.Select(t => t.Definition).ToList()
        };
    }

    public IEnumerable<ITool> GetAllTools() => _tools.Values;
}
