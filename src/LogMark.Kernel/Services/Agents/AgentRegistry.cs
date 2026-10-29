namespace LogMark.Services.Agents;

using LogMark.Interfaces;

public static class AgentRegistry
{
    private static readonly AgentDefinition[] Agents =
    [
        // Tier 1: Standard mcpServers JSON
        new("claude-desktop", "Claude Desktop", "Anthropic",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%APPDATA%\\Claude\\claude_desktop_config.json"],
            null, null,
            SupportedScopes: AgentScopes.User,
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "Signed .mcpb bundle",
            SkillsDirPaths: [@"%USERPROFILE%\.claude\skills"]),

        new("claude-code", "Claude Code", "Anthropic",
            AgentCapabilities.Inbound | AgentCapabilities.Outbound,
            ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.claude.json"],
            "claude", ["-p", "--output-format", "json"],
            MaxContextTokens: 200_000,
            MaxOutputTokens: 8192,
            Quality: QualityTier.Frontier,
            Cost: CostProfile.UserPaid,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".mcp.json"],
            NativeWireCli: new NativeWireCli("claude", "mcp add",
                "--scope user", "--scope project", NativeCliShape.ClaudeMcpAdd),
            WriterKind: ConfigWriterKind.JsonFlat,
            LegacyConfigPaths: ["%USERPROFILE%\\.claude\\.mcp.json"],
            MethodLabel: "claude mcp add CLI",
            SkillsDirPaths: [@"%USERPROFILE%\.claude\skills"],
            ProjectSkillsDirPaths: [".claude/skills"],
            SupportsLocalScope: true),

        new("cursor", "Cursor", "Anysphere",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.cursor\\mcp.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".cursor/mcp.json"],
            DeeplinkScheme: new DeeplinkScheme(DeeplinkShape.CursorBase64,
                "cursor://anysphere.cursor-deeplink/mcp/install?name={name}&config={base64}"),
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "Cursor deeplink"),

        new("windsurf", "Windsurf", "Codeium",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.codeium\\windsurf\\mcp_config.json"],
            null, null,
            SupportedScopes: AgentScopes.User,
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "JSON config file"),

        new("copilot-cli", "GitHub Copilot CLI", "GitHub",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.copilot\\mcp-config.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".mcp.json"],
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "JSON config file"),

        // Gemini CLI reads MCP servers from the same settings.json its hooks
        // live in; JsonFlat rewrites only mcpServers.logmark (bead lmk-n117).
        new("gemini-cli", "Gemini CLI", "Google",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.gemini\\settings.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".gemini/settings.json"],
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "JSON config file"),

        new("cline", "Cline", "Cline",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%APPDATA%\\Code\\User\\globalStorage\\saoudrizwan.claude-dev\\settings\\cline_mcp_settings.json"],
            null, null,
            SupportedScopes: AgentScopes.User,
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "JSON config (VS Code extension)"),

        new("roo-code", "Roo Code", "Roo Veterinary",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%APPDATA%\\Code\\User\\globalStorage\\rooveterinaryinc.roo-cline\\settings\\mcp_settings.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".roo/mcp.json"],
            WriterKind: ConfigWriterKind.JsonFlat,
            MethodLabel: "JSON config (VS Code extension)"),

        new("amazon-q-cli", "Amazon Q CLI", "AWS",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.aws\\amazonq\\cli-agents\\default.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".amazonq/cli-agents/default.json"],
            NativeWireCli: new NativeWireCli("q", "mcp add",
                "--scope global", "--scope workspace", NativeCliShape.AmazonQMcpAdd),
            WriterKind: ConfigWriterKind.AmazonQAgentDefinition,
            LegacyConfigPaths: ["%USERPROFILE%\\.aws\\amazonq\\mcp.json"],
            MethodLabel: "q mcp add CLI"),

        new("amazon-q-ide", "Amazon Q IDE", "AWS",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.aws\\amazonq\\cli-agents\\default.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".amazonq/cli-agents/default.json"],
            WriterKind: ConfigWriterKind.AmazonQAgentDefinition,
            LegacyConfigPaths: ["%USERPROFILE%\\.aws\\amazonq\\agents\\default.json"],
            MethodLabel: "Q agent definition"),

        new("jetbrains", "JetBrains IDE", "JetBrains",
            AgentCapabilities.Inbound, ConfigFormat.Json, "mcpServers",
            ["%USERPROFILE%\\.junie\\mcp\\mcp.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".junie/mcp/mcp.json"],
            WriterKind: ConfigWriterKind.JsonFlat,
            LegacyConfigPaths: ["%APPDATA%\\JetBrains\\*\\mcp.json"],
            MethodLabel: "JSON config (Junie)"),

        // Tier 2: Different key or format
        new("vscode", "VS Code + Copilot", "Microsoft",
            AgentCapabilities.Inbound, ConfigFormat.Json, "servers",
            ["%APPDATA%\\Code\\User\\mcp.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".vscode/mcp.json"],
            NativeWireCli: new NativeWireCli("code", "--add-mcp",
                null, null, NativeCliShape.VsCodeAddMcp),
            DeeplinkScheme: new DeeplinkScheme(DeeplinkShape.VsCodeUrlEncoded,
                "vscode:mcp/install?{json}"),
            WriterKind: ConfigWriterKind.JsonServers,
            LegacyConfigPaths: ["%APPDATA%\\Code\\User\\settings.json"],
            MethodLabel: "vscode:mcp deeplink"),

        new("visual-studio", "Visual Studio", "Microsoft",
            AgentCapabilities.Inbound, ConfigFormat.Json, "servers",
            ["%USERPROFILE%\\.mcp.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".vs/mcp.json", ".mcp.json", ".vscode/mcp.json", ".cursor/mcp.json"],
            WriterKind: ConfigWriterKind.JsonServers,
            MethodLabel: "JSON config (servers)"),

        new("zed", "Zed", "Zed Industries",
            AgentCapabilities.Inbound, ConfigFormat.Json, "context_servers",
            ["%APPDATA%\\Zed\\settings.json"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".zed/settings.json"],
            WriterKind: ConfigWriterKind.Zed,
            MethodLabel: "JSON config (context_servers)"),

        new("codex", "Codex CLI", "OpenAI",
            AgentCapabilities.Inbound | AgentCapabilities.Outbound,
            ConfigFormat.Toml, "mcp_servers",
            ["%USERPROFILE%\\.codex\\config.toml"],
            "codex", ["-q"],
            MaxContextTokens: 128_000,
            MaxOutputTokens: 4096,
            Quality: QualityTier.Large,
            Cost: CostProfile.UserPaid,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".codex/config.toml"],
            NativeWireCli: new NativeWireCli("codex", "mcp add",
                null, null, NativeCliShape.CodexMcpAdd),
            WriterKind: ConfigWriterKind.Toml,
            MethodLabel: "codex mcp add CLI",
            SkillsDirPaths: [@"%USERPROFILE%\.codex\skills"],
            ProjectSkillsDirPaths: [".codex/skills"]),

        new("continue", "Continue.dev", "Continue",
            AgentCapabilities.Inbound, ConfigFormat.Yaml, "mcpServers",
            ["%USERPROFILE%\\.continue\\config.yaml"],
            null, null,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".continue/mcpServers/logmark.yaml"],
            WriterKind: ConfigWriterKind.Yaml,
            MethodLabel: "YAML config"),

        // Outbound-only: inference providers
        new("ollama", "Ollama", "Ollama",
            AgentCapabilities.Outbound, ConfigFormat.Json, "",
            ["http://localhost:11434"],
            "ollama", ["run"],
            Cost: CostProfile.FreeLocal,
            SupportedScopes: AgentScopes.None,
            MethodLabel: "(not applicable)"),

        new("lm-studio", "LM Studio", "LM Studio",
            AgentCapabilities.Outbound, ConfigFormat.Json, "",
            ["http://localhost:1234"],
            null, null,
            Cost: CostProfile.FreeLocal,
            SupportedScopes: AgentScopes.None,
            MethodLabel: "(not applicable)"),

        // WSL-hosted: no Windows binary exists. Config paths resolve to
        // \\wsl.localhost\... via PathTemplateExpander's host-aware overload.
        // Capability figures from grok's own models_cache.json (grok-4.5,
        // context_window 500000, max_completion_tokens null) on 2026-07-31.
        new("grok", "Grok CLI", "xAI",
            AgentCapabilities.Inbound | AgentCapabilities.Outbound,
            ConfigFormat.Toml, "mcp_servers",
            [@"%USERPROFILE%\.grok\config.toml"],
            "grok", ["--output-format", "json"],
            MaxContextTokens: 500_000,
            Quality: QualityTier.Frontier,
            Cost: CostProfile.UserPaid,
            SupportedScopes: AgentScopes.User | AgentScopes.Project,
            ProjectConfigPaths: [".grok/config.toml"],
            WriterKind: ConfigWriterKind.Toml,
            MethodLabel: "TOML config (WSL)",
            SkillsDirPaths: [@"%USERPROFILE%\.grok\skills"],
            ProjectSkillsDirPaths: [".grok/skills"],
            Host: AgentHost.Wsl),
    ];

    public static IReadOnlyList<AgentDefinition> GetAll() => Agents;

    public static AgentDefinition? GetBySlug(string slug) =>
        Agents.FirstOrDefault(a => a.Slug == slug);

    public static IReadOnlyList<AgentDefinition> GetInbound() =>
        Agents.Where(a => a.Capabilities.HasFlag(AgentCapabilities.Inbound)).ToList();

    public static IReadOnlyList<AgentDefinition> GetOutbound() =>
        Agents.Where(a => a.Capabilities.HasFlag(AgentCapabilities.Outbound)).ToList();
}
