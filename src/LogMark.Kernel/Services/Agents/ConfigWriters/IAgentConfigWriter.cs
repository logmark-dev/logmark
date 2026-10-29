namespace LogMark.Services.Agents.ConfigWriters;

public interface IAgentConfigWriter
{
    /// <summary>
    /// Writes/updates the managed "logmark" MCP server entry. <paramref name="argsPrefix"/>
    /// (null in the ordinary case) is prepended ahead of the standard
    /// "--bootstrap-path"/"--agent" args - used by the unified `logmark` binary's
    /// McpLaunchSpec override so the written entry invokes "&lt;logmark&gt; mcp ..."
    /// instead of "logmark-mcp.exe ...".
    /// </summary>
    void WriteLogmarkEntry(string configPath, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null);
    void RemoveLogmarkEntry(string configPath, string configKey);
    bool HasLogmarkEntry(string configPath, string configKey);

    /// <summary>
    /// The managed "logmark" entry's own text when the writer can isolate it
    /// structurally, else null (the default), in which case InboundWirer
    /// falls back to scoping the whole config file by text search. A writer
    /// whose file holds several same-named entries in different scopes
    /// (ClaudeLocalScopeWriter) must return its own entry here, or a
    /// user-scope entry earlier in the file would be read in its place.
    /// </summary>
    string? ReadLogmarkEntryText(string configPath, string configKey) => null;
}
