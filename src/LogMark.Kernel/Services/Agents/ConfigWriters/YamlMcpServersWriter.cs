using YamlDotNet.RepresentationModel;

namespace LogMark.Services.Agents.ConfigWriters;

public class YamlMcpServersWriter : IAgentConfigWriter
{
    public void WriteLogmarkEntry(string configPath, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        BackupIfExists(configPath);
        var yaml = new YamlStream();
        if (File.Exists(configPath))
        {
            using var reader = new StreamReader(configPath);
            yaml.Load(reader);
        }
        var root = yaml.Documents.Count > 0
            ? (YamlMappingNode)yaml.Documents[0].RootNode
            : new YamlMappingNode();
        if (yaml.Documents.Count == 0)
            yaml.Add(new YamlDocument(root));

        var key = new YamlScalarNode(configKey);
        if (!root.Children.ContainsKey(key))
            root.Children[key] = new YamlSequenceNode();
        var servers = (YamlSequenceNode)root.Children[key];

        // Remove existing logmark entry if present
        var existing = servers.Children.OfType<YamlMappingNode>()
            .FirstOrDefault(n => n.Children.ContainsKey(new YamlScalarNode("name"))
                && ((YamlScalarNode)n.Children[new YamlScalarNode("name")]).Value == "logmark");
        if (existing != null) servers.Children.Remove(existing);

        var argsSeq = new YamlSequenceNode();
        if (argsPrefix != null)
            foreach (var a in argsPrefix)
                argsSeq.Add(new YamlScalarNode(a));
        argsSeq.Add(new YamlScalarNode("--bootstrap-path"));
        argsSeq.Add(new YamlScalarNode(bootstrapPath));
        argsSeq.Add(new YamlScalarNode("--agent"));
        argsSeq.Add(new YamlScalarNode(agentSlug));

        var entry = new YamlMappingNode
        {
            { "name", "logmark" },
            { "command", mcpExePath },
            { "args", argsSeq }
        };
        servers.Children.Add(entry);

        using var writer = new StreamWriter(configPath);
        yaml.Save(writer, assignAnchors: false);
    }

    public void RemoveLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return;
        BackupIfExists(configPath);
        var yaml = new YamlStream();
        using (var reader = new StreamReader(configPath))
            yaml.Load(reader);
        if (yaml.Documents.Count == 0) return;
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var key = new YamlScalarNode(configKey);
        if (!root.Children.ContainsKey(key)) return;
        var servers = (YamlSequenceNode)root.Children[key];
        var existing = servers.Children.OfType<YamlMappingNode>()
            .FirstOrDefault(n => n.Children.ContainsKey(new YamlScalarNode("name"))
                && ((YamlScalarNode)n.Children[new YamlScalarNode("name")]).Value == "logmark");
        if (existing != null) servers.Children.Remove(existing);
        using var writer = new StreamWriter(configPath);
        yaml.Save(writer, assignAnchors: false);
    }

    public bool HasLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return false;
        try
        {
            var content = File.ReadAllText(configPath);
            // Detect by command path first (binary name is unique to LogMark
            // and survives any name the user chose), then fall back to the
            // canonical "name: logmark" marker.
            if (content.Contains("logmark-mcp", StringComparison.OrdinalIgnoreCase))
                return true;
            return content.Contains("name: logmark") || content.Contains("name: \"logmark\"");
        }
        catch { return false; }
    }

    private static void BackupIfExists(string configPath)
    {
        if (File.Exists(configPath))
            File.Copy(configPath, configPath + ".bak", overwrite: true);
    }
}
