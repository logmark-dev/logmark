namespace LogMark.Services.Agents;

using LogMark.Interfaces;

[Flags]
public enum AgentCapabilities
{
    None = 0,
    Inbound = 1,   // Agent can read/write vault via MCP ("Reads your vault")
    Outbound = 2   // LogMark can call agent for inference ("Enriches your vault")
}

public enum ConfigFormat
{
    Json,
    Toml,
    Yaml
}

public enum AgentConnectionStatus
{
    NotFound,     // Agent not installed on machine
    Detected,     // Agent installed but not connected to this vault
    Connected,    // Active connection to this vault
    Error,        // Config exists but parse failed
    Unknown       // Probe did not complete (e.g. WSL timeout). NEVER cache this -
                  // caching a "did not answer in time" as NotFound would
                  // permanently convince LogMark the agent does not exist.
}

public record AgentDefinition(
    string Slug,
    string DisplayName,
    string Vendor,
    AgentCapabilities Capabilities,
    ConfigFormat Format,
    string ConfigKey,
    string[] ConfigPaths,
    string? CliCommand,
    string[]? CliArgs,
    int MaxContextTokens = 32_000,
    int MaxOutputTokens = 4096,
    QualityTier Quality = QualityTier.Mid,
    CostProfile Cost = CostProfile.UserPaid,
    AgentScopes SupportedScopes = AgentScopes.User,
    string[]? ProjectConfigPaths = null,
    NativeWireCli? NativeWireCli = null,
    DeeplinkScheme? DeeplinkScheme = null,
    ConfigWriterKind WriterKind = ConfigWriterKind.JsonFlat,
    string[]? LegacyConfigPaths = null,
    string MethodLabel = "JSON config file",
    string[]? SkillsDirPaths = null,
    string[]? ProjectSkillsDirPaths = null,
    AgentHost Host = AgentHost.Windows,
    bool SupportsLocalScope = false);

public record AgentStatus(
    AgentDefinition Definition,
    AgentConnectionStatus InboundStatus,
    AgentConnectionStatus OutboundStatus,
    string? ResolvedConfigPath,
    string? ErrorMessage);

[Flags]
public enum AgentScopes
{
    None = 0,
    User = 1,
    Project = 2,
    System = 4
}

public enum ConfigWriterKind
{
    JsonFlat,
    JsonServers,
    Toml,
    Yaml,
    Zed,
    AmazonQAgentDefinition
}

public record NativeWireCli(
    string Executable,
    string AddSubcommand,
    string? ScopeFlagUser,
    string? ScopeFlagProject,
    NativeCliShape Shape);

public enum NativeCliShape
{
    ClaudeMcpAdd,
    VsCodeAddMcp,
    CodexMcpAdd,
    AmazonQMcpAdd
}

public record DeeplinkScheme(
    DeeplinkShape Shape,
    string UrlTemplate);

public enum DeeplinkShape
{
    CursorBase64,
    VsCodeUrlEncoded
}

/// <summary>
/// Where an MCP entry is written. Local is Claude Code's per-project slot in
/// the user file (~/.claude.json projects["&lt;dir&gt;"].mcpServers): it beats
/// User and does not inherit into subfolders. Only agents with
/// AgentDefinition.SupportsLocalScope accept it for MCP; hooks and skills are
/// per-user installs and treat Local as User.
/// </summary>
public enum WireScope { User, Project, Local }
public enum VaultTarget { FollowActive, PinCurrent, Custom }

public enum AgentWireStatus
{
    NotInstalled,
    Detected,
    WiredGlobal,
    WiredVault,
    WiredCustom,
    WiredBroken,
    Error
}

public record AgentWireState(
    AgentWireStatus Status,
    string? ResolvedConfigPath,
    string? CurrentEntryVaultPath,
    bool VaultMismatch,
    string? BrokenReason = null);
