namespace LogMark.Services.Agents;

/// <summary>
/// Overrides InboundWirer's default "resolve a separate logmark-mcp.exe"
/// behavior with "launch the currently running binary's own MCP verb"
/// instead. Set only by the unified `logmark` binary (LogMark.Daemon), which
/// runs the MCP server as its own `mcp` verb rather than a sidecar exe -
/// <c>Command</c> is the running process's own path and <c>PrefixArgs</c> is
/// <c>["mcp"]</c>, prepended to the normal wiring args so the written config
/// invokes `&lt;logmark&gt; mcp --agent ...` instead of `logmark-mcp.exe --agent ...`.
/// Absent (null) on lm.exe and desktop, which keep the McpExeResolver path
/// byte-for-byte unchanged.
/// </summary>
public sealed record McpLaunchSpec(string Command, IReadOnlyList<string> PrefixArgs);
