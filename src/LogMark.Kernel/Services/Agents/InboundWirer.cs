using System.Text.Json;
using LogMark.Services.Agents.ConfigWriters;
using LogMark.Services.Providers;

namespace LogMark.Services.Agents;

public record WiringResult(bool Success, string? Error = null);

public class InboundWirer
{
    private readonly McpExeResolver _resolver;
    private readonly ConfigPathResolver _pathResolver;
    private readonly INativeCliDelegate _cliDelegate;
    private readonly string _activeVaultPath;
    private readonly McpLaunchSpec? _launchOverride;
    private readonly ProviderResolver? _providerResolver;
    private readonly WslBridge? _wsl;

    public InboundWirer(McpExeResolver resolver, ConfigPathResolver pathResolver,
        INativeCliDelegate cliDelegate, string activeVaultPath, McpLaunchSpec? launchOverride = null,
        ProviderResolver? providerResolver = null, WslBridge? wsl = null)
    {
        _resolver = resolver;
        _pathResolver = pathResolver;
        _cliDelegate = cliDelegate;
        _activeVaultPath = activeVaultPath;
        _launchOverride = launchOverride;
        _providerResolver = providerResolver;
        _wsl = wsl;
    }

    /// <param name="vaultOverride">
    /// When set, the entry pins this vault with `--vault-override &lt;path&gt;`
    /// (the MCP server's top resolution tier, which self-heal never rewrites)
    /// and <paramref name="vaultTarget"/> is ignored: the bootstrap path is
    /// written empty, so the server still reads the app bootstrap for its
    /// token. Used by `connect --repoint`.
    /// </param>
    public WiringResult Connect(AgentDefinition agent, WireScope scope,
        string? projectFolderPath, VaultTarget vaultTarget, string? customBootstrapPath,
        string? vaultOverride = null)
    {
        if (vaultOverride is not null) vaultTarget = VaultTarget.FollowActive;

        var scopeSupported = scope switch
        {
            WireScope.User => agent.SupportedScopes.HasFlag(AgentScopes.User),
            WireScope.Project => agent.SupportedScopes.HasFlag(AgentScopes.Project),
            WireScope.Local => agent.SupportsLocalScope,
            _ => false
        };
        if (!scopeSupported)
            return new WiringResult(false, $"{agent.DisplayName} does not support {scope} scope");

        // Resolve the current winning provider from the machine registry first.
        // Shared resolution is what makes both products write byte-identical
        // entries - never resolve from this process's own layout. The launch
        // override (the unified binary's own `mcp` verb) is only a fallback for
        // a machine where no provider resolves: on a machine with the desktop
        // app, an Engine write must keep the desktop's full tool set (design
        // 2026-10-21 D2, final review Important 4).
        string mcpExe;
        IReadOnlyList<string>? prefixArgs;
        var resolved = _providerResolver?.Resolve("mcp");
        if (resolved is not null)
        {
            mcpExe = resolved.Path;
            prefixArgs = resolved.PrefixArgs;
        }
        else if (_launchOverride is not null)
        {
            mcpExe = _launchOverride.Command;
            prefixArgs = _launchOverride.PrefixArgs;
        }
        else
        {
            return new WiringResult(false, NoMcpProviderMessage());
        }

        // The writers place the prefix ahead of their own --bootstrap-path and
        // --agent args, so the pin rides after the launch prefix.
        if (vaultOverride is not null)
            prefixArgs = (prefixArgs ?? Array.Empty<string>()).Concat(new[] { "--vault-override", vaultOverride }).ToArray();

        // WSL-hosted agents: the agent resolves the executable inside Linux,
        // so the command must be /mnt/c/... form. Args are NOT translated -
        // WSL interop passes them to the Windows process unmodified, which
        // interprets them as Windows paths (see WslArgPolicy).
        if (agent.Host == AgentHost.Wsl && _wsl is not null)
        {
            var linuxExe = _wsl.ToLinuxPath(mcpExe);
            if (linuxExe is null)
                return new WiringResult(false,
                    $"Could not translate '{mcpExe}' to a WSL path. Is WSL available?");
            mcpExe = linuxExe;
        }

        var args = WslArgPolicy.Translate((prefixArgs ?? Array.Empty<string>())
            .Concat(BuildArgs(agent, vaultTarget, customBootstrapPath)).ToArray());

        var configPath = _pathResolver.Resolve(agent, scope, projectFolderPath);

        // Generate the pinned vault-bootstrap.json BEFORE any wiring path runs.
        // BuildArgs above embeds `--bootstrap-path <vault>/vault-bootstrap.json`
        // into the entry that gets written; that file must exist whether the native
        // CLI delegate or the file writer ends up writing the entry, or the wired
        // MCP server points at a bootstrap that was never created. Previously this
        // ran only after the native-CLI early-return, so a first-run native success
        // reported ok while leaving no bootstrap (bead lmk-xw1e).
        var bootstrap = vaultTarget switch
        {
            VaultTarget.FollowActive => "",
            VaultTarget.PinCurrent => EnsureBootstrap(_activeVaultPath),
            VaultTarget.Custom => customBootstrapPath
                ?? throw new ArgumentException("Custom vault target requires customBootstrapPath"),
            _ => throw new ArgumentException($"Unknown VaultTarget: {vaultTarget}")
        };

        // Native CLI delegation is user-scope only. `<cli> mcp add --scope project`
        // writes a .mcp.json in the delegate's current working directory, but the
        // delegate runs the CLI in THIS process's cwd, not projectFolderPath - so a
        // project-scope delegate silently writes the config to the wrong directory
        // while reporting success, leaving the requested --project dir empty
        // (bead lmk-xw1e). The file writer below resolves the exact
        // <projectDir>/.mcp.json path, so project scope always takes it. Local
        // scope has the same cwd problem (`claude mcp add --scope local` keys
        // the entry on the CLI's cwd), so it takes the file writer too.
        if (scope == WireScope.User
            && agent.NativeWireCli != null && _cliDelegate.IsAvailable(agent.NativeWireCli))
        {
            var cliResult = _cliDelegate.Add(agent.NativeWireCli, agent, scope,
                mcpExe, args, projectFolderPath);
            if (cliResult.Success) return cliResult;
            // Fall through to file writer on CLI failure.
        }

        var writer = GetWriter(agent, scope, projectFolderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        try
        {
            writer.WriteLogmarkEntry(configPath, agent.ConfigKey, mcpExe, bootstrap, agent.Slug,
                prefixArgs);
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to write config: {ex.Message}");
        }
    }

    public WiringResult Disconnect(AgentDefinition agent, WireScope scope,
        string? projectFolderPath)
    {
        var configPath = _pathResolver.Resolve(agent, scope, projectFolderPath);
        if (!File.Exists(configPath))
            return new WiringResult(true);  // already disconnected

        // Native CLI delegation is user-scope only, mirroring Connect. The
        // delegate runs `<cli> mcp remove` in THIS process's cwd, not
        // projectFolderPath, so a Project or Local removal resolves against
        // the wrong directory - and an unscoped claude remove then takes the
        // user-scope entry instead (bead lmk-1yd8). The file writer below
        // targets the exact <projectDir> config.
        if (scope == WireScope.User
            && agent.NativeWireCli != null && _cliDelegate.IsAvailable(agent.NativeWireCli))
        {
            var cliResult = _cliDelegate.Remove(agent.NativeWireCli, agent, scope, projectFolderPath);
            if (cliResult.Success) return cliResult;
        }

        var writer = GetWriter(agent, scope, projectFolderPath);
        try
        {
            writer.RemoveLogmarkEntry(configPath, agent.ConfigKey);
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to remove config: {ex.Message}");
        }
    }

    public AgentWireState GetState(AgentDefinition agent, WireScope scope,
        string? projectFolderPath)
    {
        var configPath = _pathResolver.Resolve(agent, scope, projectFolderPath);
        var parentDir = Path.GetDirectoryName(configPath);
        if (parentDir == null || !Directory.Exists(parentDir))
            return new AgentWireState(AgentWireStatus.NotInstalled, configPath, null, false);

        var writer = GetWriter(agent, scope, projectFolderPath);
        try
        {
            if (!writer.HasLogmarkEntry(configPath, agent.ConfigKey))
                return new AgentWireState(AgentWireStatus.Detected, configPath, null, false);

            // Classify wired entries into WiredGlobal / WiredVault / WiredCustom based on the
            // --bootstrap-path arg read from the config. No --bootstrap-path means FollowActive
            // mode (WiredGlobal). A bootstrap path pointing at <activeVault>/vault-bootstrap.json
            // means PinCurrent mode against the active vault (WiredVault). Anything else is Custom.
            var (bootstrapPath, target) = ReadEntryArgsInfo(agent, writer, configPath);

            var status = target switch
            {
                VaultTarget.FollowActive => AgentWireStatus.WiredGlobal,
                VaultTarget.PinCurrent => AgentWireStatus.WiredVault,
                VaultTarget.Custom => AgentWireStatus.WiredCustom,
                _ => AgentWireStatus.WiredGlobal
            };

            // VaultMismatch: WiredVault entries match the active vault by definition of how we
            // classify them (the path equality check is what put them in WiredVault), so they
            // are never mismatched. WiredCustom always counts as mismatched semantically.
            // WiredGlobal follows the active vault so never mismatched.
            var mismatch = status == AgentWireStatus.WiredCustom;

            var brokenReason = DiagnoseWiredEntry(agent, writer, configPath);
            if (brokenReason != null)
                return new AgentWireState(AgentWireStatus.WiredBroken, configPath,
                    bootstrapPath, false, brokenReason);

            return new AgentWireState(status, configPath, bootstrapPath, mismatch);
        }
        catch
        {
            return new AgentWireState(AgentWireStatus.Error, configPath, null, false);
        }
    }

    /// <summary>
    /// Reads the managed "logmark" entry for the ownership contract. Null when
    /// no such entry exists; an entry under another key is the user's own even
    /// when it runs a LogMark binary (rc7.2 linux QA, O2). When the entry exists but cannot be read (an
    /// unparseable config), returns an info with null fields: the decision then
    /// treats it as custom and never removes it.
    /// </summary>
    public McpEntryInfo? ReadEntryInfo(AgentDefinition agent, WireScope scope, string? projectFolderPath)
    {
        string configPath;
        try { configPath = _pathResolver.Resolve(agent, scope, projectFolderPath); }
        catch { return null; }
        if (!File.Exists(configPath)) return null;

        try
        {
            var writer = GetWriter(agent, scope, projectFolderPath);
            // A JSON config that does not parse is unreadable, not "no entry":
            // report it as an entry of unknown origin so a disconnect never
            // removes it and a connect never overwrites it.
            if (agent.Format == ConfigFormat.Json && !ParsesAsJsonc(configPath))
                return new McpEntryInfo(null, null, null);
            if (!writer.HasLogmarkEntry(configPath, agent.ConfigKey)) return null;
            if (!HasCanonicalEntry(agent, writer, configPath)) return null;
            var text = ReadScopedEntryText(agent, writer, configPath);
            if (text is null) return new McpEntryInfo(null, null, null);
            return new McpEntryInfo(
                ExtractCommand(text),
                NullIfEmpty(ExtractFlagValue(text, "--bootstrap-path")),
                NullIfEmpty(ExtractFlagValue(text, "--vault-override")));
        }
        catch
        {
            return new McpEntryInfo(null, null, null);
        }

        static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
    }

    private static bool ParsesAsJsonc(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return true;
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return true;
        }
        catch { return false; }
    }

    public WiringResult MigrateLegacy(AgentDefinition agent)
    {
        if (agent.LegacyConfigPaths == null || agent.LegacyConfigPaths.Length == 0)
            return new WiringResult(true);

        foreach (var legacyPathRaw in agent.LegacyConfigPaths)
        {
            var legacyPath = PathTemplateExpander.Expand(legacyPathRaw);
            if (!File.Exists(legacyPath)) continue;

            // Read the existing entry; determine VaultTarget from args; rewrite to new path.
            var (existingExe, existingArgs) = ReadExistingEntry(legacyPath, agent);
            if (existingExe == null) continue;

            var vaultTarget = InferVaultTarget(existingArgs, out var customBootstrap);
            var connectResult = Connect(agent, WireScope.User, null, vaultTarget, customBootstrap);
            if (!connectResult.Success) return connectResult;

            // Remove the legacy entry after successful migration.
            var legacyWriter = GetWriter(agent.WriterKind, agent.Format);
            try { legacyWriter.RemoveLogmarkEntry(legacyPath, agent.ConfigKey); } catch { }
        }
        return new WiringResult(true);
    }

    private string[] BuildArgs(AgentDefinition agent, VaultTarget target, string? customBootstrap)
    {
        return target switch
        {
            VaultTarget.FollowActive => new[] { "--agent", agent.Slug },
            VaultTarget.PinCurrent => new[]
            {
                "--bootstrap-path", Path.Combine(_activeVaultPath, "vault-bootstrap.json"),
                "--agent", agent.Slug
            },
            VaultTarget.Custom => new[]
            {
                "--bootstrap-path", customBootstrap ?? "",
                "--agent", agent.Slug
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// UI escape hatch: resolves the path to logmark-mcp.exe so the Settings
    /// MCP view can build deeplinks and the live preview without re-implementing
    /// resolver logic. Returns null when the MCP exe cannot be located on disk.
    /// Same order as Connect: the registered provider winner first, then the
    /// launchOverride (unified binary), then the sidecar exe resolver.
    /// </summary>
    public string? ResolveMcpExe() =>
        _providerResolver?.Resolve("mcp")?.Path ?? _launchOverride?.Command ?? _resolver.Resolve();

    /// <summary>
    /// Startup self-heal for one agent's user-scope MCP entry. If a "logmark"
    /// entry exists whose command path is dead (rooted and missing) or is not the
    /// current resolver winner, rewrite it via Connect - preserving a pinned
    /// --bootstrap-path when present (VaultTarget.Custom) and otherwise following
    /// the active vault (VaultTarget.FollowActive). No entry, or an already-current
    /// entry, is a no-op. An entry carrying --vault-override is a deliberate
    /// operator pin and is never rewritten. Never throws (user scope only; project scopes are not
    /// enumerable). The rewrite resolves through this wirer's own ProviderResolver,
    /// so it produces the byte-identical entry any product would write.
    /// </summary>
    public void HealUserScopeEntry(AgentDefinition agent, ProviderResolver resolver)
    {
        try
        {
            var configPath = _pathResolver.Resolve(agent, WireScope.User, null);
            if (!File.Exists(configPath)) return;

            var writer = GetWriter(agent, WireScope.User, null);
            if (!writer.HasLogmarkEntry(configPath, agent.ConfigKey)) return;
            if (!HasCanonicalEntry(agent, writer, configPath)) return;

            var text = ReadScopedEntryText(agent, writer, configPath);
            if (text is null) return;

            // A --vault-override pin is a deliberate operator act (the only
            // resolution tier that beats a live daemon's ActiveVault). Heal
            // never rewrites it - not on winner change, not even when its
            // binary is dead: `connect --status` reports that as WiredBroken
            // and `connect --mcp` is the explicit repair path.
            if (!string.IsNullOrEmpty(ExtractFlagValue(text, "--vault-override")))
                return;

            var command = ExtractCommand(text);
            var winner = resolver.Resolve("mcp")?.Path;

            var commandLive = !string.IsNullOrEmpty(command)
                && Path.IsPathRooted(command) && File.Exists(command);
            var isWinner = winner is not null && command is not null && PathsEqual(command, winner);
            if (commandLive && isWinner) return; // already the current winner - nothing to heal

            var bootstrapPath = ExtractFlagValue(text, "--bootstrap-path");
            if (!string.IsNullOrEmpty(bootstrapPath))
                Connect(agent, WireScope.User, null, VaultTarget.Custom, bootstrapPath);
            else
                Connect(agent, WireScope.User, null, VaultTarget.FollowActive, null);
        }
        catch { /* per-agent best effort - one agent's malformed config never aborts the rest */ }
    }

    /// <summary>
    /// UI escape hatch: builds the args array that would be written into a
    /// client config for the given agent/vault target. Used by the Settings
    /// MCP view for live preview rendering and deeplink construction so the
    /// preview always matches what Connect would actually write.
    /// </summary>
    public string[] BuildArgsForUi(AgentDefinition agent, VaultTarget target, string? customBootstrap)
        => BuildArgs(agent, target, customBootstrap);

    /// <summary>
    /// UI escape hatch: resolves the on-disk config path for an agent at the
    /// given scope. Used by the Settings MCP view to show the user where the
    /// wiring will be written and to open the containing folder in Explorer.
    /// </summary>
    public string ResolveConfigPathForUi(AgentDefinition agent, WireScope scope, string? projectFolderPath)
        => _pathResolver.Resolve(agent, scope, projectFolderPath);

    /// <summary>
    /// Repairs a WiredBroken entry by force-regenerating the active vault's
    /// bootstrap pin (overwriting stale content) and repointing the config entry
    /// at the active vault (PinCurrent). Fixes both a stale/missing pin and an
    /// entry pointing at a vault that has moved, by assuming the user wants the
    /// wiring on the vault the app is currently using.
    /// </summary>
    public WiringResult RepairToActiveVault(AgentDefinition agent, WireScope scope,
        string? projectFolderPath)
    {
        WriteBootstrapFile(_activeVaultPath);
        return Connect(agent, scope, projectFolderPath, VaultTarget.PinCurrent, null);
    }

    private static string EnsureBootstrap(string vaultPath)
    {
        var bootstrapPath = Path.Combine(vaultPath, "vault-bootstrap.json");
        if (!File.Exists(bootstrapPath)) WriteBootstrapFile(vaultPath);
        return bootstrapPath;
    }

    private static string WriteBootstrapFile(string vaultPath)
    {
        var bootstrapPath = Path.Combine(vaultPath, "vault-bootstrap.json");
        var json = JsonSerializer.Serialize(new { VaultPath = vaultPath },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(bootstrapPath, json);
        return bootstrapPath;
    }

    /// <summary>
    /// Scope-aware writer selection. An agent with local scope (Claude Code)
    /// keeps local entries under projects["&lt;dir&gt;"] in its user file:
    /// Local reads and writes go through ClaudeLocalScopeWriter, and User
    /// reads go through ClaudeUserScopeWriter so a local entry never answers
    /// for the user scope. Every other scope/agent pair keeps its kind-based
    /// writer.
    /// </summary>
    private static IAgentConfigWriter GetWriter(AgentDefinition agent, WireScope scope, string? projectDir)
    {
        if (agent.SupportsLocalScope && scope == WireScope.Local)
            return new ClaudeLocalScopeWriter(projectDir
                ?? throw new ArgumentException("Local scope requires projectFolderPath"));
        if (agent.SupportsLocalScope && scope == WireScope.User)
            return new ClaudeUserScopeWriter();
        return GetWriter(agent.WriterKind, agent.Format);
    }

    private static IAgentConfigWriter GetWriter(ConfigWriterKind kind, ConfigFormat format) => kind switch
    {
        ConfigWriterKind.JsonFlat => new JsonMcpServersWriter(),
        ConfigWriterKind.JsonServers => new JsonServersWriter(),
        ConfigWriterKind.Toml => new TomlMcpServersWriter(),
        ConfigWriterKind.Yaml => new YamlMcpServersWriter(),
        ConfigWriterKind.Zed => new ZedConfigWriter(),
        ConfigWriterKind.AmazonQAgentDefinition => new AmazonQAgentDefinitionWriter(),
        _ => throw new ArgumentException($"Unknown WriterKind: {kind}")
    };

    /// <summary>
    /// Reads the --bootstrap-path argument out of the agent's wired config entry and infers
    /// the VaultTarget that would round-trip back to the same args via BuildArgs:
    /// - No --bootstrap-path arg (or empty value) => FollowActive. The on-disk writers all emit
    ///   `["--bootstrap-path", ""]` in FollowActive mode, so the empty-value branch covers both
    ///   "arg absent" and "arg present with empty path".
    /// - --bootstrap-path == &lt;activeVault&gt;/vault-bootstrap.json => PinCurrent.
    /// - --bootstrap-path elsewhere => Custom.
    /// Parses the raw config text: scans for the literal `--bootstrap-path`, then reads the
    /// next non-empty token. Handles JSON (quoted), YAML (unquoted or quoted), and TOML
    /// (quoted in array literals) uniformly because the value token always sits on/after the
    /// next non-delimiter character.
    /// </summary>
    private (string? bootstrapPath, VaultTarget target) ReadEntryArgsInfo(AgentDefinition agent,
        IAgentConfigWriter writer, string configPath)
    {
        // Scope the flag scan to OUR "logmark" entry. A single JSON config can
        // hold several LogMark MCP servers under different keys -- e.g. a
        // research vault pinned under "logmark-research" with its own custom
        // --bootstrap-path. An unscoped whole-file scan reads whichever
        // --bootstrap-path appears first, so a sibling listed before "logmark"
        // would make a vault-pinned entry read back as Custom (the radio
        // flips to Custom right after Save). Narrow to the entry block first.
        var text = ReadScopedEntryText(agent, writer, configPath);
        if (text is null)
            return (null, VaultTarget.FollowActive);

        // Preferred path: --bootstrap-path. The wirer writes this for entries it
        // creates itself; classification compares against the active vault's
        // bootstrap.json file.
        var bootstrapPath = ExtractFlagValue(text, "--bootstrap-path");
        if (!string.IsNullOrEmpty(bootstrapPath))
        {
            var activeVaultBootstrap = Path.Combine(_activeVaultPath, "vault-bootstrap.json");
            return PathsEqual(bootstrapPath, activeVaultBootstrap)
                ? (bootstrapPath, VaultTarget.PinCurrent)
                : (bootstrapPath, VaultTarget.Custom);
        }

        // Fallback path: --vault-override. External tooling (claude mcp add,
        // codex CLI, hand-edits) often pins via this flag instead. The value
        // is a vault directory, not a bootstrap.json - compare it directly
        // against the active vault path.
        var vaultOverride = ExtractFlagValue(text, "--vault-override");
        if (!string.IsNullOrEmpty(vaultOverride))
        {
            return PathsEqual(vaultOverride, _activeVaultPath)
                ? (vaultOverride, VaultTarget.PinCurrent)
                : (vaultOverride, VaultTarget.Custom);
        }

        // Neither flag present: the entry follows whichever vault the desktop
        // app is currently bound to (read at MCP startup from bootstrap.json).
        return (null, VaultTarget.FollowActive);
    }

    /// <summary>
    /// The managed entry's text: the writer's own structural read when it has
    /// one (ClaudeLocalScopeWriter returns only its project's entry, so a
    /// user-scope "logmark" earlier in the same file is never read in its
    /// place), else the whole file narrowed by text scoping. Null when the
    /// config cannot be read.
    /// </summary>
    private static string? ReadScopedEntryText(AgentDefinition agent, IAgentConfigWriter writer,
        string configPath)
    {
        var entryText = writer.ReadLogmarkEntryText(configPath, agent.ConfigKey);
        if (entryText is not null) return entryText;

        string fullText;
        try { fullText = File.ReadAllText(configPath); }
        catch { return null; }
        return ScopeToLogmarkEntry(fullText, agent);
    }

    /// <summary>
    /// Narrows the config text to the single managed "logmark" server entry so the
    /// flag scan cannot read a sibling server's args. Each host format has its own
    /// scoper; all fall back to the full text when the entry can't be isolated, so
    /// an unrecognized layout degrades to the previous whole-file behavior.
    /// </summary>
    private static string ScopeToLogmarkEntry(string text, AgentDefinition agent)
        => TryScopeToLogmarkEntry(text, agent) ?? text;

    /// <summary>
    /// The managed "logmark" entry's text, or null when the config holds no
    /// entry under that exact key (an entry under another key, such as the
    /// user's own "logmark-research", is not ours).
    /// </summary>
    private static string? TryScopeToLogmarkEntry(string text, AgentDefinition agent) => agent.WriterKind switch
    {
        ConfigWriterKind.JsonFlat or ConfigWriterKind.JsonServers
            or ConfigWriterKind.Zed or ConfigWriterKind.AmazonQAgentDefinition => TryScopeJson(text),
        ConfigWriterKind.Toml => TryScopeToml(text, agent.ConfigKey),
        ConfigWriterKind.Yaml => TryScopeYaml(text),
        _ => text,
    };

    /// <summary>
    /// Whether the config holds an entry under the canonical key.
    /// <see cref="Connect"/> always writes that key and Disconnect removes only
    /// that key, so self-heal and the ownership contract read only that entry:
    /// "healing" an entry found under another key added a second entry beside
    /// the user's own (rc7.1 linux QA, O2), and reading it as ours made connect
    /// refuse a stranger's first install (rc7.2 linux QA, O2).
    /// ClaudeLocalScopeWriter already detects by key, inside its project.
    /// </summary>
    private static bool HasCanonicalEntry(AgentDefinition agent, IAgentConfigWriter writer, string configPath)
    {
        if (writer is ClaudeUserScopeWriter claude)
            return claude.HasCanonicalEntry(configPath, agent.ConfigKey);
        if (writer is ClaudeLocalScopeWriter)
            return true;
        string text;
        try { text = File.ReadAllText(configPath); }
        catch { return false; }
        return TryScopeToLogmarkEntry(text, agent) is not null;
    }

    /// <summary>
    /// JSON-family: matches the exact quoted key `"logmark"` (so it does not match
    /// `"logmark-research"` nor the logmark-mcp command path), then brace-matches
    /// the entry object. Null when no entry has that exact key.
    /// </summary>
    private static string? TryScopeJson(string text)
    {
        const string key = "\"logmark\"";
        var keyIdx = text.IndexOf(key, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        var open = text.IndexOf('{', keyIdx + key.Length);
        if (open < 0) return text.Substring(keyIdx);

        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(open, i - open + 1);
            }
        }
        return text.Substring(open);
    }

    /// <summary>
    /// TOML: the managed entry is the table `[{configKey}.logmark]`. The exact
    /// header match excludes `[{configKey}.logmark-mcp]` (ends `-mcp]`) and our own
    /// sub-tables like `[{configKey}.logmark.tools.read_entry]` (has `.tools`). The
    /// block runs to the next table header `[` or EOF; the `args` line lives in it.
    /// </summary>
    private static string? TryScopeToml(string text, string configKey)
    {
        var header = "[" + configKey + ".logmark]";
        var lines = text.Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == header) { start = i; break; }
        }
        if (start < 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.Append(lines[start]).Append('\n');
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("[", StringComparison.Ordinal)) break;
            sb.Append(lines[i]).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// YAML: the managed entry is a list item `- name: logmark` under the servers
    /// key. The block is that line plus every following line that is blank or
    /// indented deeper than the item; it stops at the first non-blank line at or
    /// below the item's indent (the next `- name:` sibling, or a dedent).
    /// </summary>
    private static string? TryScopeYaml(string text)
    {
        var lines = text.Split('\n');
        int start = -1;
        int itemIndent = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal)) continue;
            var afterDash = trimmed.Substring(1).TrimStart();
            if (!afterDash.StartsWith("name:", StringComparison.Ordinal)) continue;
            var name = afterDash.Substring("name:".Length).Trim().Trim('"');
            if (name == "logmark")
            {
                start = i;
                itemIndent = lines[i].Length - trimmed.Length;
                break;
            }
        }
        if (start < 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.Append(lines[start]).Append('\n');
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) { sb.Append(lines[i]).Append('\n'); continue; }
            var indent = lines[i].Length - lines[i].TrimStart().Length;
            if (indent <= itemIndent) break;
            sb.Append(lines[i]).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Format-agnostic scan: find the given flag literal in the config text, then read the next
    /// value token. Skips JSON array delimiters (`,`), YAML list dashes/markers, and whitespace.
    /// Honours quoted tokens (JSON, TOML, optional in YAML) and falls back to reading until a
    /// structural delimiter for unquoted YAML scalars.
    /// </summary>
    private static string? ExtractFlagValue(string text, string marker)
    {
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;

        // Cursor walks past the marker and any closing-quote / structural chars that follow it
        // (e.g. `"--bootstrap-path",` in JSON, or `- --bootstrap-path\n- value` in YAML).
        var i = idx + marker.Length;

        // Skip trailing quote on the marker itself if present.
        if (i < text.Length && text[i] == '"') i++;

        // Walk to the next value token, skipping delimiters and whitespace. A YAML list marker
        // ("- ") is treated as a delimiter only when followed by whitespace -- otherwise a bare
        // dash is part of the literal value (e.g. "--agent"), and we must not strip it.
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                // Quoted value: read until closing quote.
                var endQuote = text.IndexOf('"', i + 1);
                if (endQuote < 0) return null;
                return text.Substring(i + 1, endQuote - i - 1).Replace("\\\\", "\\");
            }
            if (c == '\'')
            {
                // Single-quoted YAML scalar.
                var endQuote = text.IndexOf('\'', i + 1);
                if (endQuote < 0) return null;
                return text.Substring(i + 1, endQuote - i - 1);
            }
            if (c == ',' || c == ' ' || c == '\t' || c == '\r' || c == '\n'
                || c == '[' || c == ']' || c == '{' || c == '}')
            {
                i++;
                continue;
            }
            // YAML list marker: "- " or "-\n" or "-\r" or "-\t". Single trailing whitespace
            // confirms this is a marker, not the start of "--agent" or similar.
            if (c == '-' && i + 1 < text.Length
                && (text[i + 1] == ' ' || text[i + 1] == '\t'
                    || text[i + 1] == '\r' || text[i + 1] == '\n'))
            {
                i += 2;
                continue;
            }
            // Unquoted scalar (YAML/TOML): read until end-of-line, comma, or structural close.
            var start = i;
            while (i < text.Length)
            {
                var d = text[i];
                if (d == '\r' || d == '\n' || d == ',' || d == ']' || d == '}') break;
                i++;
            }
            return text.Substring(start, i - start).Trim();
        }
        return null;
    }

    /// <summary>
    /// For a wired entry, resolves the vault directory the MCP server would open
    /// and returns a non-null reason when that vault is missing or unreadable, OR
    /// when the entry's own command binary is dead. Applies uniformly to ALL
    /// entries - FollowActive, bootstrap-path-pinned, and vault-override-pinned
    /// alike: a pinned entry names an explicit command the user (or external
    /// tooling) chose, and that choice can go stale exactly like a FollowActive
    /// one (uninstall, moved build dir). A vault defect is checked first per
    /// branch and returned immediately (more specific, and matches prior
    /// behavior); only when the vault target resolves fine does command liveness
    /// get checked, so a dead command is never masked into a green status by a
    /// healthy vault target. FollowActive entries resolve to the active vault,
    /// which the desktop app is already running on, so only their command can be
    /// diagnosed as broken. Pure file-read - never launches the server.
    /// </summary>
    private string? DiagnoseWiredEntry(AgentDefinition agent, IAgentConfigWriter writer, string configPath)
    {
        var text = ReadScopedEntryText(agent, writer, configPath);
        if (text is null)
            return null; // config unreadable on second read - not a liveness defect; preserve the earlier classification

        var bootstrapPath = ExtractFlagValue(text, "--bootstrap-path");
        if (!string.IsNullOrEmpty(bootstrapPath))
        {
            if (!File.Exists(bootstrapPath))
                return $"bootstrap file missing: {bootstrapPath}";

            string? vaultPath;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(bootstrapPath));
                vaultPath = doc.RootElement.TryGetProperty("VaultPath", out var vp)
                    && vp.ValueKind == JsonValueKind.String
                        ? vp.GetString()
                        : null;
            }
            catch { return $"bootstrap unreadable: {bootstrapPath}"; }

            if (string.IsNullOrEmpty(vaultPath))
                return $"bootstrap unreadable: {bootstrapPath}";
            if (!VaultDirExists(vaultPath))
                return $"target vault not found: {vaultPath}";
            return DiagnoseCommandLiveness(text, agent);
        }

        var vaultOverride = ExtractFlagValue(text, "--vault-override");
        if (!string.IsNullOrEmpty(vaultOverride))
        {
            return VaultDirExists(vaultOverride)
                ? DiagnoseCommandLiveness(text, agent)
                : $"target vault not found: {vaultOverride}";
        }

        // FollowActive: the active vault, already loaded by the app. Its vault
        // target is healthy by definition, but the binary the entry launches can
        // still be gone (uninstall, moved build dir) - that is a real defect.
        return DiagnoseCommandLiveness(text, agent);
    }

    /// <summary>
    /// Command liveness: a wired entry whose command is a rooted path that no
    /// longer exists must not report healthy, even when its vault target resolves
    /// fine. Checked for every branch of <see cref="DiagnoseWiredEntry"/> (after
    /// that branch's own vault check passes), so a dead rooted command is never
    /// masked into a green status regardless of vault target. Relative-path or
    /// bare-name commands (e.g. "logmark", "x") are not diagnosable and pass
    /// through.
    /// </summary>
    /// <remarks>
    /// Bug found during task 10b's real end-to-end verification (2026-07-31):
    /// for a WSL-hosted agent, Connect (above) writes the command as a Linux
    /// path (ToLinuxPath, e.g. "/mnt/c/.../logmark-mcp.exe") because grok
    /// resolves it inside the distro. This method used to run
    /// Path.IsPathRooted + File.Exists on that string unconditionally - on
    /// Windows, a leading "/" IS rooted (relative to the current drive), so
    /// File.Exists("/mnt/c/...") checks "C:\mnt\c\..." and always returns
    /// false, so `connect --status grok` reported "broken - binary missing"
    /// for an entry whose target genuinely existed. Every existing test drove
    /// this through a FakeProcessRunner/no bridge at all, so nothing exercised
    /// a real Linux-path command string through this check before the live
    /// `lm connect --status grok` run in task-10b-report.md caught it.
    ///
    /// Regression found during fix-round 1 review (2026-07-31): the first fix
    /// above moved the WSL branch out from under the shared
    /// `Path.IsPathRooted(command)` guard, so a hand-wired entry with a bare
    /// command (e.g. `command = "logmark"`, no path at all) fell into the WSL
    /// branch, got translated to a bogus `\\wsl.localhost\...` path, and
    /// reported "binary missing" - a NEW false positive in the exact branch
    /// this method exists to keep honest. Restored the rootedness check
    /// ahead of BOTH branches, matching this method's own doc comment above
    /// ("bare-name commands are not diagnosable and pass through").
    /// </remarks>
    private string? DiagnoseCommandLiveness(string entryText, AgentDefinition agent)
    {
        var command = ExtractCommand(entryText);
        if (string.IsNullOrEmpty(command) || !Path.IsPathRooted(command)) return null;

        if (agent.Host == AgentHost.Wsl)
        {
            // Translate the Linux-side command back to a Windows-checkable
            // path before testing existence. A null bridge or an
            // untranslatable path degrades to "not diagnosable" rather than a
            // false-positive "missing" - mirrors WslBridge's own
            // never-throws-on-translation contract.
            var windowsPath = _wsl?.ToWindowsPath(command);
            return windowsPath is not null && !File.Exists(windowsPath)
                ? $"binary missing: {command}"
                : null;
        }

        return File.Exists(command) ? null : $"binary missing: {command}";
    }

    /// <summary>
    /// Reads the "command" value out of the scoped entry text across host
    /// formats: JSON/Zed/AmazonQ (<c>"command": "..."</c>), TOML
    /// (<c>command = "..."</c>), and YAML (<c>command: ...</c>, quoted or bare).
    /// Returns null when absent. Used by the liveness diagnosis and by the
    /// self-heal winner comparison.
    /// </summary>
    private static string? ExtractCommand(string entryText)
    {
        const string marker = "command";
        var idx = entryText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;

        var i = idx + marker.Length;
        // Consume the key's closing quote (JSON/Zed), then whitespace, then the
        // key/value separator (':' JSON/YAML, '=' TOML), then whitespace - stopping
        // at the value token. The value's own opening quote is left for the quoted
        // branch below (never skipped here, or a quoted value would be misread as
        // a bare scalar and keep its trailing quote).
        if (i < entryText.Length && entryText[i] == '"') i++;
        while (i < entryText.Length && (entryText[i] == ' ' || entryText[i] == '\t')) i++;
        if (i < entryText.Length && (entryText[i] == ':' || entryText[i] == '=')) i++;
        while (i < entryText.Length && (entryText[i] == ' ' || entryText[i] == '\t'
            || entryText[i] == '\r' || entryText[i] == '\n')) i++;
        if (i >= entryText.Length) return null;

        if (entryText[i] == '"')
        {
            var end = entryText.IndexOf('"', i + 1);
            if (end < 0) return null;
            return entryText.Substring(i + 1, end - i - 1).Replace("\\\\", "\\");
        }
        if (entryText[i] == '\'')
        {
            var end = entryText.IndexOf('\'', i + 1);
            if (end < 0) return null;
            return entryText.Substring(i + 1, end - i - 1);
        }

        // Unquoted YAML scalar: read to end of line / comma.
        var start = i;
        while (i < entryText.Length && entryText[i] != '\r' && entryText[i] != '\n' && entryText[i] != ',')
            i++;
        return entryText.Substring(start, i - start).Trim();
    }

    private static bool VaultDirExists(string path)
    {
        try { return Directory.Exists(Path.GetFullPath(path)); }
        catch { return false; }
    }

    private (string? exe, string[] args) ReadExistingEntry(string configPath, AgentDefinition agent)
    {
        // For migration, we just need the args array. Simplified parse.
        // Caller falls back to FollowActive if parse fails.
        return (null, Array.Empty<string>());
    }

    private static VaultTarget InferVaultTarget(string[] args, out string? customBootstrap)
    {
        customBootstrap = null;
        if (args.Length == 0) return VaultTarget.FollowActive;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--bootstrap-path")
            {
                customBootstrap = args[i + 1];
                return VaultTarget.Custom;
            }
        }
        return VaultTarget.FollowActive;
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Why no MCP provider could be resolved, in terms the reader can act on.
    ///
    /// "Nothing is registered" and "what is registered is not on disk" have
    /// different remedies, and conflating them cost a QA round: the machine
    /// registry named a logmark-mcp.exe belonging to a deleted install, the
    /// resolver dropped it silently, and the message told the operator to run
    /// any logmark command - which is exactly what had written the stale entry.
    /// Found by the 0.22 release QA run (G2).
    /// </summary>
    private string NoMcpProviderMessage()
    {
        var missing = _providerResolver?.DeclaredButMissing("mcp") ?? Array.Empty<string>();
        if (missing.Count > 0)
            return "The registered LogMark MCP server is not on disk: "
                + string.Join(", ", missing)
                + ". That entry belongs to an install that has been removed. Launch the LogMark "
                + "app you want to wire (or run its own lm.exe once) so it registers its MCP "
                + "server, then retry.";

        return "No LogMark MCP provider is registered on this machine. " +
               "Start the LogMark app or run any logmark command once, then retry.";
    }

    // Backward-compat shim
    [Obsolete("Use Connect(agent, WireScope, projectFolderPath, VaultTarget, customBootstrapPath) instead")]
    public WiringResult Connect(AgentDefinition agent, string configPath, string vaultPath)
        => Connect(agent, WireScope.User, null, VaultTarget.PinCurrent, null);

    [Obsolete("Use Disconnect(agent, WireScope, projectFolderPath) instead")]
    public WiringResult Disconnect(AgentDefinition agent, string configPath)
        => Disconnect(agent, WireScope.User, null);
}
