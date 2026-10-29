using System.Text.Json;

namespace LogMark.Services.Agents;

public class AgentDetector
{
    private readonly WslBridge? _wsl;

    /// <summary>
    /// wsl defaults to null so every existing call site (startup, tests,
    /// Settings inbound detection) keeps compiling unchanged and keeps
    /// getting NotFound for WSL-hosted agents rather than a real probe.
    /// Passing a live WslBridge is an opt-in only the Settings agent surface
    /// and `lm connect --status` make - see DetectOutbound's Wsl branch and
    /// WiringSelfHeal's startup exclusion for why nobody else should.
    /// </summary>
    public AgentDetector(WslBridge? wsl = null) => _wsl = wsl;

    public AgentConnectionStatus DetectInbound(AgentDefinition agent)
    {
        if (!agent.Capabilities.HasFlag(AgentCapabilities.Inbound))
            return AgentConnectionStatus.NotFound;

        var configPath = FindConfigFile(agent);
        if (configPath == null)
            return AgentConnectionStatus.NotFound;

        return agent.Format switch
        {
            ConfigFormat.Json => DetectInboundJson(configPath, agent.ConfigKey),
            ConfigFormat.Toml => DetectInboundToml(configPath, agent.ConfigKey),
            ConfigFormat.Yaml => DetectInboundYaml(configPath, agent.ConfigKey),
            _ => AgentConnectionStatus.Error
        };
    }

    public AgentConnectionStatus DetectOutbound(AgentDefinition agent)
    {
        if (!agent.Capabilities.HasFlag(AgentCapabilities.Outbound))
            return AgentConnectionStatus.NotFound;
        if (agent.CliCommand == null)
            return AgentConnectionStatus.NotFound;

        if (agent.Host == AgentHost.Wsl)
        {
            // Lazy by contract: this runs only when a surface asks (Settings
            // agent view, `lm connect --status`) - NEVER during startup
            // self-heal (see WiringSelfHeal's explicit skip of AgentHost.Wsl).
            // Probing wsl.exe, and even merely reading a \\wsl.localhost\...
            // path, boots the WSL VM - a multi-second cost nobody should pay
            // on every LogMark launch for a subsystem they may not touch that
            // day. Do NOT "fix" this asymmetry with the other five frameworks.
            if (_wsl is null || !_wsl.IsAvailable) return AgentConnectionStatus.NotFound;

            // One probe, two facts: ProbeCommandStatus reports Found and
            // TimedOut from a SINGLE wsl.exe invocation. Do not replace this
            // with ProbeTimedOut(...) followed by ProbeCommand(...) - that
            // would fire wsl.exe twice for the same answer. A timeout is
            // Unknown, never NotFound: caching a timeout as NotFound would
            // permanently convince LogMark that grok does not exist (the
            // exact bug already fixed once at the WslBridge layer for
            // DefaultDistro/LinuxHome - see WslBridge's "never latched"
            // comments).
            var (found, timedOut) = _wsl.ProbeCommandStatus(agent.CliCommand);
            if (timedOut) return AgentConnectionStatus.Unknown;
            return found ? AgentConnectionStatus.Detected : AgentConnectionStatus.NotFound;
        }

        try
        {
            return RunWhere(agent.CliCommand) ? AgentConnectionStatus.Detected : AgentConnectionStatus.NotFound;
        }
        catch { return AgentConnectionStatus.NotFound; }
    }

    public AgentConnectionStatus DetectOutboundFromVault(AgentDefinition agent, string vaultPath)
    {
        if (!agent.Capabilities.HasFlag(AgentCapabilities.Outbound))
            return AgentConnectionStatus.NotFound;

        var agentsJsonPath = Path.Combine(vaultPath, "logmark", "_system", "agents.json");
        if (!File.Exists(agentsJsonPath))
            return DetectOutbound(agent);

        try
        {
            var json = File.ReadAllText(agentsJsonPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("outbound", out var outbound)
                && outbound.TryGetProperty(agent.Slug, out _))
                return AgentConnectionStatus.Connected;
        }
        catch { }

        return DetectOutbound(agent);
    }

    public string? FindConfigFile(AgentDefinition agent)
    {
        foreach (var pathTemplate in agent.ConfigPaths)
        {
            if (pathTemplate.StartsWith("http://") || pathTemplate.StartsWith("https://"))
                continue;

            var expanded = ResolveConfigPath(pathTemplate, agent.Host);

            // Handle glob pattern (JetBrains)
            if (expanded.Contains('*'))
            {
                var dir = Path.GetDirectoryName(expanded);
                var pattern = Path.GetFileName(expanded);
                if (dir != null && Directory.Exists(Path.GetDirectoryName(dir)))
                {
                    var parentDir = Path.GetDirectoryName(dir)!;
                    var dirPattern = Path.GetFileName(dir);
                    foreach (var matchDir in Directory.EnumerateDirectories(parentDir, dirPattern))
                    {
                        var candidate = Path.Combine(matchDir, pattern);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                continue;
            }

            if (File.Exists(expanded)) return expanded;
        }
        return null;
    }

    /// <summary>
    /// Host-agnostic expansion. Kept for callers that have a template but no
    /// agent (and for the existing test that pins %TEMP% expansion). Prefer
    /// the host-aware overload whenever an AgentDefinition is in hand.
    /// </summary>
    public string ResolveConfigPath(string pathTemplate)
    {
        return PathTemplateExpander.Expand(pathTemplate);
    }

    /// <summary>
    /// Host-aware expansion, MINOR 1 from the final whole-feature review
    /// (2026-07-31): FindConfigFile called the single-argument expander for
    /// every agent, so a WSL-hosted agent's config template resolved to a
    /// Windows path that never exists, DetectInbound always returned NotFound,
    /// and grok's row in Settings -&gt; Session hooks read "not detected"
    /// permanently even when correctly wired
    /// (SettingsHooksView uses DetectInbound as its detected predicate).
    ///
    /// Laziness is preserved and load-bearing: this can reach
    /// WslBridge.LinuxHome/UncRoot, which boots the WSL VM. Verified before
    /// changing it that DetectInbound is NOT reachable from any startup path -
    /// its callers are SettingsHooksView.Refresh (user opens the Settings
    /// hooks surface), SkillsService.GetStatus (user opens the Settings skills
    /// surface, and `lm connect --status`), and AgentService (no production
    /// caller). The two unconditional startup sweeps both skip WSL-hosted
    /// agents explicitly: WiringSelfHeal.Run in BOTH loops, and
    /// SkillsService.SelfHeal (which is what App.Startup actually calls, not
    /// GetStatus). Do not add a startup caller of DetectInbound.
    /// </summary>
    public string ResolveConfigPath(string pathTemplate, AgentHost host)
    {
        return PathTemplateExpander.Expand(pathTemplate, host, _wsl);
    }

    private AgentConnectionStatus DetectInboundJson(string configPath, string key)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var servers)
                && servers.ValueKind == JsonValueKind.Object
                && servers.TryGetProperty("logmark", out _))
                return AgentConnectionStatus.Connected;
            return AgentConnectionStatus.Detected;
        }
        catch { return AgentConnectionStatus.Error; }
    }

    private AgentConnectionStatus DetectInboundToml(string configPath, string key)
    {
        try
        {
            var content = File.ReadAllText(configPath);
            if (content.Contains($"[{key}.logmark]"))
                return AgentConnectionStatus.Connected;
            return AgentConnectionStatus.Detected;
        }
        catch { return AgentConnectionStatus.Error; }
    }

    private AgentConnectionStatus DetectInboundYaml(string configPath, string key)
    {
        try
        {
            var content = File.ReadAllText(configPath);
            if (content.Contains("name: logmark") || content.Contains("name: \"logmark\""))
                return AgentConnectionStatus.Connected;
            return AgentConnectionStatus.Detected;
        }
        catch { return AgentConnectionStatus.Error; }
    }

    private static bool RunWhere(string command)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }
}
