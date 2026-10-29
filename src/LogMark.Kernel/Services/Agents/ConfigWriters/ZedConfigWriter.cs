using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

// Zed requires a "source": "custom" field in each MCP server entry.
public class ZedConfigWriter : JsonMcpServersWriter
{
    protected override JsonObject BuildLogmarkEntry(string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        var entry = base.BuildLogmarkEntry(mcpExePath, bootstrapPath, agentSlug, argsPrefix);
        entry["source"] = JsonValue.Create("custom");
        return entry;
    }
}
