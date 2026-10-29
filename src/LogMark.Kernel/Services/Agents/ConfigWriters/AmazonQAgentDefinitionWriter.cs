using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

public class AmazonQAgentDefinitionWriter : IAgentConfigWriter
{
    public void WriteLogmarkEntry(string configPath, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        var root = ReadOrCreateAgent(configPath);

        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        servers["logmark"] = BuildEntry(mcpExePath, bootstrapPath, agentSlug, argsPrefix);
        WriteWithBackup(configPath, root);
    }

    public void RemoveLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return;
        var root = ReadRoot(configPath);
        if (root?["mcpServers"] is JsonObject servers)
        {
            servers.Remove("logmark");
            WriteWithBackup(configPath, root);
        }
    }

    public bool HasLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return false;
        try
        {
            // Detect by command path first (binary name is unique to LogMark
            // and survives any key the user chose), then fall back to the
            // canonical "logmark" entry key.
            var content = File.ReadAllText(configPath);
            if (content.Contains("logmark-mcp", StringComparison.OrdinalIgnoreCase))
                return true;
            var root = ReadRoot(configPath);
            return root?["mcpServers"] is JsonObject servers && servers.ContainsKey("logmark");
        }
        catch { return false; }
    }

    // Shares the JSON writers' parse policy: an existing file that is not
    // valid JSON throws rather than being replaced by a fresh default agent.
    private static JsonObject ReadOrCreateAgent(string configPath)
        => JsonMcpServersWriter.ReadExistingRootForWrite(configPath) ?? new JsonObject
        {
            ["name"] = JsonValue.Create("default"),
            ["description"] = JsonValue.Create("Default agent"),
            ["mcpServers"] = new JsonObject(),
            ["useLegacyMcpJson"] = JsonValue.Create(false)
        };

    private static JsonObject? ReadRoot(string configPath) => JsonMcpServersWriter.TryReadRootObject(configPath);

    private static JsonObject BuildEntry(string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix)
    {
        var args = new JsonArray();
        if (argsPrefix != null)
            foreach (var a in argsPrefix)
                args.Add(JsonValue.Create(a));
        args.Add(JsonValue.Create("--bootstrap-path"));
        args.Add(JsonValue.Create(bootstrapPath));
        args.Add(JsonValue.Create("--agent"));
        args.Add(JsonValue.Create(agentSlug));
        return new JsonObject
        {
            ["command"] = JsonValue.Create(mcpExePath),
            ["args"] = args
        };
    }

    private static void WriteWithBackup(string configPath, JsonObject root)
    {
        if (File.Exists(configPath))
            File.Copy(configPath, configPath + ".bak", overwrite: true);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(configPath, root.ToJsonString(options));
    }
}
