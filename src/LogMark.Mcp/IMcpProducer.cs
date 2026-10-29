using LogMark.Mcp.Prompts;
using LogMark.Mcp.Tools;

namespace LogMark.Mcp;

/// <summary>
/// Arguments describing the vault to (re)initialize. Passed to the producer's
/// vault-services initializer at startup and on every vault switch.
/// </summary>
public sealed record McpVaultInit(
    string VaultPath,
    string AgentId,
    string? DefaultRoute,
    int SessionTimeoutMinutes,
    int? HostPid = null);

/// <summary>
/// Closed composition seam for the MCP server. The open build leaves
/// <see cref="Program.Producer"/> null and ships the kernel tool set with no
/// license gating, no cloud inference, and no closed (Core-backed) services. The
/// closed LogMark.Mcp.Producers assembly supplies an implementation that wires
/// license/usage, sensitivity, cloud inference, semantic search, and the
/// Pro/closed tools. There is no assembly scanning: the closed entry point
/// (<c>ClosedProgram</c>) assigns <see cref="Program.Producer"/> explicitly, and
/// the open tree compiles with LogMark.Mcp.Producers simply absent.
/// </summary>
public interface IMcpProducer
{
    /// <summary>
    /// Create the vault-services holder. The closed build returns a subclass
    /// carrying the Core-typed accessors (image assets, feed, protocols, views).
    /// </summary>
    VaultServices CreateServices();

    /// <summary>
    /// Build the per-call gating + write-telemetry hooks (backed by the license
    /// and usage services). Reads the installation token from the bootstrap.
    /// Returns null if no gating is required. Called once at startup.
    /// </summary>
    IMcpServerHooks? BuildServerHooks(string bootstrapPath);

    /// <summary>
    /// Wire the full vault service graph (kernel + closed sensitivity/feed/
    /// protocol/view/semantic) onto <paramref name="services"/>. When a producer
    /// is present this fully replaces the open kernel-only initializer. Called at
    /// startup and on every vault switch.
    /// </summary>
    void InitializeVaultServices(VaultServices services, McpVaultInit init);

    /// <summary>
    /// Register the closed tools (protocols, behaviors, views, save_asset,
    /// enrich_entry, get_agent_scorecard, and the DEBUG thinker import) onto the
    /// registry. Called once at startup, after the open tools are registered.
    /// </summary>
    void RegisterClosedTools(ToolRegistry registry, VaultServices services, string bootstrapPath);

    /// <summary>
    /// Register the closed prompts (DEBUG thinker study prompt) onto the registry.
    /// Called once at startup.
    /// </summary>
    void RegisterClosedPrompts(PromptRegistry prompts, ToolRegistry registry);
}
