namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Writes MCP server entries into TOML config files (e.g. Codex CLI config.toml).
/// Uses string-based TOML manipulation to preserve existing content.
/// </summary>
public class TomlMcpServersWriter : IAgentConfigWriter
{
    private const string LogmarkKey = "logmark";

    public void WriteLogmarkEntry(string configPath, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        BackupIfExists(configPath);
        var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
        var updated = UpsertLogmarkSection(existing, configKey, mcpExePath, bootstrapPath, agentSlug, argsPrefix);
        File.WriteAllText(configPath, updated);
    }

    public void RemoveLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return;
        BackupIfExists(configPath);
        var content = File.ReadAllText(configPath);
        var updated = RemoveLogmarkSection(content, configKey);
        File.WriteAllText(configPath, updated);
    }

    public bool HasLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath)) return false;
        try
        {
            var content = File.ReadAllText(configPath);
            // Detect by command path first (binary name is unique to LogMark
            // and survives any key the user chose), then fall back to the
            // canonical section header.
            if (content.Contains("logmark-mcp", StringComparison.OrdinalIgnoreCase))
                return true;
            return content.Contains($"[{configKey}.{LogmarkKey}]");
        }
        catch { return false; }
    }

    private static string UpsertLogmarkSection(string content, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug, IReadOnlyList<string>? argsPrefix)
    {
        // Remove any existing logmark section first
        content = RemoveLogmarkSection(content, configKey);

        // Escape backslashes for TOML strings
        var escapedExe = mcpExePath.Replace("\\", "\\\\");
        var escapedBootstrap = bootstrapPath.Replace("\\", "\\\\");

        var prefixTokens = argsPrefix == null || argsPrefix.Count == 0
            ? ""
            : string.Join(", ", argsPrefix.Select(a => $"\"{a.Replace("\\", "\\\\")}\"")) + ", ";

        var section = $"\n[{configKey}.{LogmarkKey}]\n" +
                      $"command = \"{escapedExe}\"\n" +
                      $"args = [{prefixTokens}\"--bootstrap-path\", \"{escapedBootstrap}\", \"--agent\", \"{agentSlug}\"]\n";

        return content.TrimEnd() + "\n" + section;
    }

    private static string RemoveLogmarkSection(string content, string configKey)
    {
        var sectionHeader = $"[{configKey}.{LogmarkKey}]";
        var startIdx = content.IndexOf(sectionHeader, StringComparison.Ordinal);
        if (startIdx < 0) return content;

        // Find the end of this section (next top-level section header or end of file)
        var searchFrom = startIdx + sectionHeader.Length;
        var nextSection = FindNextSectionIndex(content, searchFrom);

        if (nextSection < 0)
        {
            // Remove to end of file, also trim trailing blank lines before the section
            return content[..startIdx].TrimEnd() + "\n";
        }
        else
        {
            // Remove just this section block
            var before = content[..startIdx].TrimEnd();
            var after = content[nextSection..];
            return before + "\n\n" + after;
        }
    }

    private static int FindNextSectionIndex(string content, int from)
    {
        var idx = from;
        while (idx < content.Length)
        {
            var newline = content.IndexOf('\n', idx);
            if (newline < 0) break;
            var lineStart = newline + 1;
            if (lineStart >= content.Length) break;
            // Check if next non-empty line starts with '['
            var trimmed = content[lineStart..].TrimStart('\r', '\n', ' ', '\t');
            if (trimmed.StartsWith('['))
            {
                // Find the actual position of '[' in the original content
                return content.IndexOf('[', lineStart);
            }
            idx = lineStart;
        }
        return -1;
    }

    private static void BackupIfExists(string configPath)
    {
        if (File.Exists(configPath))
            File.Copy(configPath, configPath + ".bak", overwrite: true);
    }
}
