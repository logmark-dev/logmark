using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Claude Code user scope: ~/.claude.json root mcpServers.logmark. The same
/// file holds local-scope entries under projects["&lt;dir&gt;"].mcpServers, and
/// Claude Code writes "projects" ahead of the root "mcpServers" (observed on
/// the live config 2026-10-15). Whole-file detection and text scoping would
/// then read a local entry as the user one: report a local-only install as
/// user-installed, diagnose a local entry's dead binary as a broken user
/// entry, and let HealUserScopeEntry rewrite the user entry from a local
/// entry's flags. Reads here drop the projects subtree first. Writes and
/// removes are the base class's, which already touch only the root key.
/// </summary>
public sealed class ClaudeUserScopeWriter : JsonMcpServersWriter
{
    /// <summary>The config root without "projects"; null when the file is
    /// absent or not a JSON object, so callers keep the base behavior.</summary>
    private static JsonObject? ReadUserScopeRoot(string configPath)
    {
        var root = TryReadRootObject(configPath);
        root?.Remove("projects");
        return root;
    }

    public override bool HasLogmarkEntry(string configPath, string configKey)
    {
        var root = ReadUserScopeRoot(configPath);
        return root is null
            ? base.HasLogmarkEntry(configPath, configKey)
            : HasLogmarkEntryInText(root.ToJsonString(EntryTextOptions), configKey);
    }

    /// <summary>
    /// The root "logmark" entry when present; otherwise the whole root minus
    /// projects, which is what text scoping would fall back to for an entry
    /// under another key (e.g. "logmark-mcp" from `claude mcp add`).
    /// </summary>
    public override string? ReadLogmarkEntryText(string configPath, string configKey)
    {
        var root = ReadUserScopeRoot(configPath);
        if (root is null) return null;
        return (root[configKey] as JsonObject)?["logmark"]?.ToJsonString(EntryTextOptions)
            ?? root.ToJsonString(EntryTextOptions);
    }

    /// <summary>Whether the user scope holds an entry under the canonical
    /// "logmark" key (not merely one that runs a LogMark binary).</summary>
    public bool HasCanonicalEntry(string configPath, string configKey)
        => (ReadUserScopeRoot(configPath)?[configKey] as JsonObject)?["logmark"] is not null;
}
