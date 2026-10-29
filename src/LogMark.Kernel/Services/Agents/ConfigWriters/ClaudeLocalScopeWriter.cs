using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Claude Code local scope: ~/.claude.json -> projects["&lt;normalized dir&gt;"].mcpServers.logmark.
/// Local beats user scope in Claude Code and does not inherit into subfolders,
/// so the key must be the exact session start directory. The configKey
/// parameter of each override is ignored: the entry always lives under
/// projects[key].mcpServers, never under the root key a user-scope writer uses.
/// </summary>
public sealed class ClaudeLocalScopeWriter : JsonMcpServersWriter
{
    private readonly string _projectKey;

    public ClaudeLocalScopeWriter(string projectDir)
    {
        _projectKey = ClaudeProjectKey.Normalize(projectDir);
    }

    /// <summary>
    /// This directory's keys under projects: the canonical key first, then any
    /// case variant Claude Code already created (one directory, several
    /// session-start spellings; see ClaudeProjectKey).
    /// </summary>
    private List<string> ProjectKeys(JsonObject projects)
    {
        var keys = new List<string> { _projectKey };
        keys.AddRange(projects.Select(p => p.Key)
            .Where(k => k != _projectKey && ClaudeProjectKey.SameDirectory(k, _projectKey)));
        return keys;
    }

    private static JsonObject ServersOf(JsonObject projects, string key, bool create)
    {
        if (projects[key] is not JsonObject project)
        {
            if (!create) return new JsonObject();
            project = new JsonObject();
            projects[key] = project;
        }
        if (project["mcpServers"] is not JsonObject servers)
        {
            if (!create) return new JsonObject();
            servers = new JsonObject();
            project["mcpServers"] = servers;
        }
        return servers;
    }

    /// <summary>The first of this directory's keys that holds a logmark entry.</summary>
    private JsonNode? FindEntry(JsonObject root) =>
        root["projects"] is JsonObject projects
            ? ProjectKeys(projects).Select(k => ServersOf(projects, k, create: false)["logmark"]).FirstOrDefault(e => e is not null)
            : null;

    public override void WriteLogmarkEntry(string configPath, string configKey, string mcpExePath,
        string bootstrapPath, string agentSlug, IReadOnlyList<string>? argsPrefix = null)
    {
        var root = ReadOrCreateRoot(configPath);
        if (root["projects"] is not JsonObject projects)
        {
            projects = new JsonObject();
            root["projects"] = projects;
        }
        // The canonical key always; each variant only where Claude Code made it.
        foreach (var key in ProjectKeys(projects))
            ServersOf(projects, key, create: key == _projectKey || projects[key] is JsonObject)["logmark"] =
                BuildLogmarkEntry(mcpExePath, bootstrapPath, agentSlug, argsPrefix);
        WriteWithBackup(configPath, root);
    }

    public override bool HasLogmarkEntry(string configPath, string configKey)
    {
        var root = TryReadRootObject(configPath);
        return root is not null && FindEntry(root) is not null;
    }

    public override void RemoveLogmarkEntry(string configPath, string configKey)
    {
        var root = TryReadRootObject(configPath);
        if (root?["projects"] is not JsonObject projects) return;
        var changed = false;
        foreach (var key in ProjectKeys(projects))
        {
            if (projects[key] is not JsonObject project || project["mcpServers"] is not JsonObject servers
                || servers["logmark"] is null) continue;
            servers.Remove("logmark");
            changed = true;
            // A project object holding nothing but the emptied mcpServers is
            // what this writer created; Claude Code's own objects carry more.
            if (servers.Count == 0 && project.Count == 1) projects.Remove(key);
        }
        if (changed) WriteWithBackup(configPath, root);
    }

    /// <summary>
    /// The scoped text used by InboundWirer.GetState/Diagnose: only this
    /// project's entry, so a user-scope entry never masquerades as local.
    /// </summary>
    public override string? ReadLogmarkEntryText(string configPath, string configKey)
    {
        var root = TryReadRootObject(configPath);
        return root is null ? null : FindEntry(root)?.ToJsonString(EntryTextOptions);
    }
}
