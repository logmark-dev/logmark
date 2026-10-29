using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents;

public record OutboundAgentConfig(
    string Slug, string Command, string[] Args, string[] Capabilities);

public class OutboundWirer
{
    public WiringResult Connect(AgentDefinition agent, string vaultPath,
        string command, string[] capabilities)
    {
        try
        {
            var agentsJsonPath = GetAgentsJsonPath(vaultPath);
            var root = LoadOrCreate(agentsJsonPath);
            if (root["outbound"] is not JsonObject outbound)
            {
                outbound = new JsonObject();
                root["outbound"] = outbound;
            }
            outbound[agent.Slug] = new JsonObject
            {
                ["command"] = command,
                ["args"] = new JsonArray(agent.CliArgs?.Select(a => (JsonNode)a!).ToArray()
                    ?? Array.Empty<JsonNode>()),
                ["capabilities"] = new JsonArray(capabilities.Select(c => (JsonNode)c!).ToArray())
            };
            WriteJson(agentsJsonPath, root);
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to write agents.json: {ex.Message}");
        }
    }

    public WiringResult Disconnect(string slug, string vaultPath)
    {
        try
        {
            var agentsJsonPath = GetAgentsJsonPath(vaultPath);
            if (!File.Exists(agentsJsonPath)) return new WiringResult(true);
            var root = LoadOrCreate(agentsJsonPath);
            var outbound = root["outbound"]?.AsObject();
            outbound?.Remove(slug);
            WriteJson(agentsJsonPath, root);
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to update agents.json: {ex.Message}");
        }
    }

    public List<OutboundAgentConfig> GetOutboundAgents(string vaultPath)
    {
        var agentsJsonPath = GetAgentsJsonPath(vaultPath);
        if (!File.Exists(agentsJsonPath)) return [];
        try
        {
            var json = File.ReadAllText(agentsJsonPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("outbound", out var outbound)) return [];
            var result = new List<OutboundAgentConfig>();
            foreach (var prop in outbound.EnumerateObject())
            {
                var command = prop.Value.GetProperty("command").GetString() ?? "";
                var args = prop.Value.TryGetProperty("args", out var argsEl)
                    ? argsEl.EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
                    : [];
                var caps = prop.Value.TryGetProperty("capabilities", out var capsEl)
                    ? capsEl.EnumerateArray().Select(c => c.GetString() ?? "").ToArray()
                    : [];
                result.Add(new OutboundAgentConfig(prop.Name, command, args, caps));
            }
            return result;
        }
        catch { return []; }
    }

    private static string GetAgentsJsonPath(string vaultPath) =>
        Path.Combine(vaultPath, "logmark", "_system", "agents.json");

    private static JsonObject LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        return new JsonObject();
    }

    private static void WriteJson(string path, JsonObject root)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
