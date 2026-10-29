namespace LogMark.Services.Agents;

/// <summary>One pack's install state for one agent.</summary>
public sealed record SkillsPackState(
    string PackSlug, string PackDisplayName, SkillInstallStatus Status, string? Detail);

/// <summary>One skills-capable agent with its per-pack install states.</summary>
public sealed record SkillsAgentStatus(
    AgentDefinition Agent, bool Detected, IReadOnlyList<SkillsPackState> Packs);

/// <summary>
/// Orchestrates skill-pack install/uninstall/status across skills-capable
/// agents, driving the catalog -> adapter -> installer. Pack-parameterized: no
/// pack slug is hardcoded. v1 is fixed to WireScope.User. The agent list is
/// injected (App passes AgentRegistry.GetAll(); tests inject a temp-dir agent).
/// </summary>
public sealed class SkillsService
{
    private readonly SkillPackCatalog _catalog;
    private readonly SkillsInstaller _installer;
    private readonly SkillsPathResolver _resolver;
    private readonly AgentDetector _detector;
    private readonly IReadOnlyList<AgentDefinition> _agents;

    // Identifies which product owns this SkillsService instance's writes
    // (e.g. "desktop" or "oss-cli"), threaded into every SkillsInstaller
    // call so the shared ".logmark-pack" marker can refcount ownership
    // across products that install the same pack to the same on-disk
    // skills dir - see SkillsInstaller's class doc.
    private readonly string _ownerId;

    public SkillsService(
        SkillPackCatalog catalog, SkillsInstaller installer,
        SkillsPathResolver resolver, AgentDetector detector,
        IReadOnlyList<AgentDefinition> agents, string ownerId)
    {
        _catalog = catalog;
        _installer = installer;
        _resolver = resolver;
        _detector = detector;
        _agents = agents;
        _ownerId = ownerId;
    }

    private IEnumerable<AgentDefinition> SkillsCapableAgents =>
        _agents.Where(a => _resolver.Supports(a));

    public IReadOnlyList<SkillsAgentStatus> GetStatus()
    {
        var packs = _catalog.Packs;
        var result = new List<SkillsAgentStatus>();

        foreach (var agent in SkillsCapableAgents)
        {
            var detected = _detector.DetectInbound(agent) != AgentConnectionStatus.NotFound;
            var packStates = packs
                .Select(pack =>
                {
                    // GetState hashes file content, so it must see the SAME
                    // content Install writes. Install adapts per framework
                    // (codex rewrites frontmatter, appends a boundary section,
                    // emits a sidecar), so passing the canonical pack here
                    // would report every adapted install as permanently
                    // Outdated and make self-heal rewrite it on every launch.
                    var adapted = Adapted(pack, agent);
                    var state = _installer.GetState(agent, adapted, WireScope.User, null, _ownerId);
                    return new SkillsPackState(pack.Slug, pack.DisplayName, state.Status, state.Detail);
                })
                .ToList();
            result.Add(new SkillsAgentStatus(agent, detected, packStates));
        }
        return result;
    }

    public WiringResult Install(string agentSlug, string packSlug, IEnumerable<string>? carryOwners = null)
    {
        var agent = _agents.FirstOrDefault(a => a.Slug == agentSlug);
        if (agent is null) return new WiringResult(false, $"Unknown agent: {agentSlug}");
        if (!_resolver.Supports(agent))
            return new WiringResult(false, $"{agent.DisplayName} does not support file-based skills");

        var pack = _catalog.Get(packSlug);
        if (pack is null) return new WiringResult(false, $"Unknown pack: {packSlug}");

        var adapted = SkillPackAdapter.Adapt(pack, FrameworkProfileRegistry.For(agentSlug));
        return _installer.Install(agent, adapted, WireScope.User, null, _ownerId, carryOwners);
    }

    public WiringResult Uninstall(string agentSlug, string packSlug)
    {
        var agent = _agents.FirstOrDefault(a => a.Slug == agentSlug);
        if (agent is null) return new WiringResult(false, $"Unknown agent: {agentSlug}");

        var pack = _catalog.Get(packSlug);
        if (pack is null) return new WiringResult(false, $"Unknown pack: {packSlug}");

        // GetState/Uninstall key on pack.Slug + skill names, which the adapter
        // preserves, so the canonical catalog pack is correct here.
        return _installer.Uninstall(agent, pack, WireScope.User, null, _ownerId);
    }

    public IReadOnlyList<string>? GetOwners(string agentSlug, string packSlug)
    {
        var agent = _agents.FirstOrDefault(a => a.Slug == agentSlug);
        var pack = _catalog.Get(packSlug);
        if (agent is null || pack is null) return null;
        return _installer.GetOwners(agent, pack, WireScope.User, null);
    }

    /// <summary>Removes the pack for every owner (disconnect --all-products).</summary>
    public WiringResult UninstallAll(string agentSlug, string packSlug)
    {
        var agent = _agents.FirstOrDefault(a => a.Slug == agentSlug);
        if (agent is null) return new WiringResult(false, $"Unknown agent: {agentSlug}");
        var pack = _catalog.Get(packSlug);
        if (pack is null) return new WiringResult(false, $"Unknown pack: {packSlug}");
        return _installer.Uninstall(agent, pack, WireScope.User, null, _ownerId, allOwners: true);
    }

    private static SkillPack Adapted(SkillPack pack, AgentDefinition agent) =>
        SkillPackAdapter.Adapt(pack, FrameworkProfileRegistry.For(agent.Slug));

    /// <summary>
    /// Reinstalls every pack whose installed copy is stale and unmodified, and
    /// records hashes for hash-less markers whose content already matches.
    /// Returns the number of packs reinstalled. Best-effort per agent and per
    /// pack: one failure never stops the rest. Locally modified installs are
    /// deliberately skipped - they surface as Outdated for the user to decide.
    /// </summary>
    public int SelfHeal()
    {
        var healed = 0;
        foreach (var agent in SkillsCapableAgents)
        {
            // WSL-hosted agents are deliberately excluded from startup
            // self-heal - mirrors the identical guard in both
            // WiringSelfHeal.Run loops. _installer.GetState below resolves
            // the skills path via SkillsPathResolver -> PathTemplateExpander,
            // which for a WSL-hosted agent reads WslBridge.LinuxHome/UncRoot
            // (booting the WSL VM if it isn't already running) and then walks
            // \\wsl.localhost\... with Directory.Exists/File.ReadAllText.
            // SelfHeal runs unconditionally on every app/daemon launch
            // (App.Startup.cs), whether or not the WSL agent is even
            // installed - exactly the multi-second-launch-delay hazard this
            // whole plan's lazy-detection decision exists to prevent.
            //
            // This is NOT a gap in user-facing functionality: GetStatus()
            // above (the on-demand path a user triggers by opening the
            // Settings agent surface) is NOT guarded and is allowed to probe
            // WSL - that is the lazy path working as designed. Only this
            // automatic, unconditional startup sweep is excluded for
            // WSL-hosted agents. Do NOT "fix" this asymmetry.
            if (agent.Host == AgentHost.Wsl) continue;

            foreach (var pack in _catalog.Packs)
            {
                try
                {
                    var adapted = Adapted(pack, agent);
                    var state = _installer.GetState(agent, adapted, WireScope.User, null, _ownerId);
                    if (!state.NeedsRepair) continue;
                    if (_installer.Install(agent, adapted, WireScope.User, null, _ownerId).Success)
                        healed++;
                }
                catch (SkillAdaptationException) { /* unadaptable pack, leave it */ }
                catch (Exception) { /* best-effort per pack */ }
            }
        }
        return healed;
    }
}
