using LogMark.Services.Providers;

namespace LogMark.Services.Agents;

/// <summary>
/// The three per-agent wiring surfaces `logmark connect` can reconcile.
/// </summary>
public enum Capability { Hooks, Mcp, Skills }

/// <summary>
/// The reconciliation outcome for one capability against one framework slug.
/// </summary>
public enum PlanAction { Create, Update, Noop, Remove, Unsupported }

public sealed record ConnectRequest(
    string Framework, IReadOnlySet<Capability> Capabilities, WireScope Scope,
    string? ProjectDir, string? Route, string CliExePath,
    WiringVerb Verb = WiringVerb.Connect, string? RequestedVault = null,
    WiringOverrides? Overrides = null,
    IReadOnlyDictionary<Capability, WiringOverrides>? CapabilityOverrides = null);

/// <param name="Note">
/// A sub-capability caveat that does not change Supported/Action: the
/// capability as a whole is wired (or will be), but one part of it is not.
/// Today the only producer is PlanHooks, set when a framework's HookProfile
/// has CanInject=false - Hooks itself stays Supported=true and reports its
/// normal Create/Noop action (capture, and checkpoint where available, are
/// really wired), but inject specifically never fires for that agent. Null
/// for every other capability and for Hooks frameworks that can inject.
/// Distinct from BrokenReason (which flags an installed-but-dead MCP entry,
/// a install-time defect) and from a whole-capability Unsupported (which
/// means the capability has no registry entry at all, e.g. gemini-cli's
/// Mcp). Reusing either would misreport: BrokenReason would call working
/// wiring "broken", and Unsupported would say grok/copilot-cli have no
/// hooks at all when capture and (for grok) checkpoint work fine.
/// </param>
public sealed record CapabilityPlan(
    Capability Capability, bool Supported, string? TargetPath, PlanAction Action, string Detail,
    string? BrokenReason = null, string? Note = null,
    WiringDecisionResult? Decision = null, WiringFacts? Facts = null);

public sealed record ConnectPlan(string Framework, WireScope Scope, IReadOnlyList<CapabilityPlan> Items);

/// <summary>
/// The per-capability outcome of an Apply() or Remove() call: what was
/// attempted (Action, carried through from the Plan that drove it) and
/// whether it succeeded. Unsupported capabilities are carried through with
/// Success=true (they were never attempted, so they are not a failure) and
/// Error set to the reason.
/// </summary>
/// <param name="Error">
/// Null on a successful action, including a skipped Noop. Non-null on a
/// failure, and non-null on the Unsupported "success" path (Success=true,
/// Action=Unsupported) - there it carries the reason the capability was
/// never attempted.
/// </param>
public sealed record CapabilityOutcome(Capability Capability, bool Success, string? Error, PlanAction Action,
    bool ChoiceNeeded = false, string? Message = null, IReadOnlyList<WiringChoice>? Choices = null);

public sealed record ConnectResult(string Framework, WireScope Scope, IReadOnlyList<CapabilityOutcome> Items)
{
    public bool AnyFailure => Items.Any(i => !i.Success);

    /// <summary>2 on any failure, 3 when a capability changed nothing because a
    /// choice is needed (root design 2026-10-21 section 3.2), else 0.</summary>
    public int ExitCode => AnyFailure ? 2 : Items.Any(i => i.ChoiceNeeded) ? WiringDecision.ExitChoiceNeeded : 0;
}

/// <summary>
/// One capability's install state as reported by Status(): whether the
/// framework's registry knows this capability at all (Supported) and, if so,
/// whether the underlying service currently considers it installed.
/// Unsupported capabilities always report Installed=false and
/// TargetPath=null - there is nothing to check state for.
/// </summary>
/// <param name="Note">Carried through from CapabilityPlan.Note (see its doc) - the
/// sub-capability caveat surfaced by `lm connect --status`, e.g. Hooks reporting
/// inject unsupported for a framework whose hooks are otherwise installed.</param>
public sealed record CapabilityState(Capability Capability, bool Installed, bool Supported, string? TargetPath,
    string? BrokenReason = null, string? Note = null);

public sealed record ConnectStatus(string Framework, WireScope Scope, IReadOnlyList<CapabilityState> Items);

/// <summary>
/// Orchestrates HooksService, InboundWirer, and SkillsService into a single
/// per-framework reconciliation: for each requested capability, resolve
/// whether the framework slug is known to that capability's registry, and if
/// so, derive the on-disk target path and the action Apply would take. Plan()
/// never writes - it is pure read/derive over the existing wiring services'
/// own state-inspection surfaces (IsInstalled/GetState/GetStatus). The CLI
/// `logmark connect` command uses Plan() for the dry-run report; Apply()
/// (below) performs the writes this plan describes.
/// </summary>
public sealed class ConnectService
{
    // The only skill pack LogMark currently ships. Skills v1 has no per-pack
    // selection UI yet, so Plan() targets this pack for every framework that
    // supports file-based skills. Matches the pack slug used by App.Startup's
    // SkillsService composition and the packs shipped under
    // LogMark.Kernel/Resources/SkillPacks.
    private const string DefaultSkillPackSlug = "managing-work-in-logmark";

    // Status() reports on all three capabilities regardless of what a caller
    // would request for Plan/Apply - there is no partial-status query in v1.
    private static readonly IReadOnlySet<Capability> AllCapabilities =
        new HashSet<Capability> { Capability.Hooks, Capability.Mcp, Capability.Skills };

    // Fixed report order for Plan/Apply/Remove output items. request.Capabilities
    // is a HashSet, so its enumeration order is incidental (implementation
    // detail of hash bucketing, not the order callers passed --hooks/--mcp/
    // --skills). Iterating this array and filtering by membership instead
    // makes CapabilityPlan/CapabilityOutcome item order deterministic:
    // Hooks, Mcp, Skills, always - no behavior change beyond stable ordering.
    private static readonly Capability[] CapabilityOrder =
        { Capability.Hooks, Capability.Mcp, Capability.Skills };

    private readonly HooksService _hooks;
    private readonly InboundWirer _mcp;
    private readonly SkillsService _skills;
    private readonly WslBridge? _wsl;
    private readonly WiringOwnership? _ownership;
    private readonly Func<McpEntryInfo, string?>? _resolveEntryVault;

    /// <param name="wsl">
    /// Shared WSL bridge, reused for the ad-hoc SkillsPathResolver PlanSkills
    /// constructs below. Without it, a WSL-hosted agent's skills TargetPath
    /// resolves as a bare Windows path instead of \\wsl.localhost\... -
    /// silently wrong even though _mcp (InboundWirer) and _hooks were wired
    /// with the real bridge at the composition root. Optional and defaulted
    /// so existing callers (tests) that construct ConnectService with three
    /// args are unaffected.
    /// </param>
    /// <param name="resolveEntryVault">
    /// The vault an existing MCP entry reaches, resolved the way the MCP server
    /// resolves it (see WiringOwnership.EntryVault). The CLI wires the real
    /// live-host policy; null falls back to the vault the entry names itself.
    /// </param>
    public ConnectService(HooksService hooks, InboundWirer mcp, SkillsService skills, WslBridge? wsl = null,
        WiringOwnership? ownership = null, Func<McpEntryInfo, string?>? resolveEntryVault = null)
    {
        _hooks = hooks;
        _mcp = mcp;
        _skills = skills;
        _wsl = wsl;
        _ownership = ownership;
        _resolveEntryVault = resolveEntryVault;
    }

    public ConnectPlan Plan(ConnectRequest request)
    {
        var items = CapabilityOrder
            .Where(request.Capabilities.Contains)
            .Select(capability => capability switch
            {
                Capability.Hooks => PlanHooks(request),
                Capability.Mcp => PlanMcp(request),
                Capability.Skills => PlanSkills(request),
                _ => throw new ArgumentOutOfRangeException(nameof(request),
                    $"Unknown capability: {capability}")
            })
            .ToList();

        return new ConnectPlan(request.Framework, request.Scope, items);
    }

    /// <summary>
    /// Performs the writes Plan() describes. Reuses Plan() internally (never
    /// duplicates its supported/action derivation) so Apply and Plan can never
    /// diverge. Each capability is applied independently: an unsupported
    /// capability is carried through untouched, a Noop capability is reported
    /// without re-invoking its writer (idempotency - the underlying services
    /// already no-op on re-install, but Apply skips the call entirely rather
    /// than relying on that), and a thrown exception or a failed WiringResult
    /// from one capability's service is caught and recorded as that
    /// capability's failure without aborting the others.
    /// </summary>
    public ConnectResult Apply(ConnectRequest request)
    {
        // Apply is connect whatever Verb the caller set, mirroring Remove.
        var connect = request with { Verb = WiringVerb.Connect };
        var plan = Plan(connect);
        var items = plan.Items.Select(item => ApplyCapability(connect, item)).ToList();
        return new ConnectResult(request.Framework, request.Scope, items);
    }

    private CapabilityOutcome ApplyCapability(ConnectRequest request, CapabilityPlan item)
    {
        if (!item.Supported)
            return new CapabilityOutcome(item.Capability, true, item.Detail, PlanAction.Unsupported);

        if (item.Decision is { } decision) return ApplyDecided(request, item, decision);

        if (item.Action == PlanAction.Noop)
            return new CapabilityOutcome(item.Capability, true, null, PlanAction.Noop);

        try
        {
            var result = item.Capability switch
            {
                Capability.Hooks => _hooks.Install(
                    request.Framework, PerUserScope(request.Scope), request.ProjectDir, request.CliExePath, request.Route),
                Capability.Mcp => ApplyMcp(request),
                Capability.Skills => _skills.Install(request.Framework, DefaultSkillPackSlug),
                _ => throw new ArgumentOutOfRangeException(nameof(item),
                    $"Unknown capability: {item.Capability}")
            };
            return new CapabilityOutcome(item.Capability, result.Success, result.Error, item.Action);
        }
        catch (Exception ex)
        {
            return new CapabilityOutcome(item.Capability, false, ex.Message, item.Action);
        }
    }

    private WiringResult ApplyMcp(ConnectRequest request, string? repointVault = null)
    {
        var agent = AgentRegistry.GetBySlug(request.Framework);
        if (agent is null)
            return new WiringResult(false, $"Unknown agent: {request.Framework}");

        // --repoint pins the requested vault with --vault-override: a
        // --bootstrap-path is only the MCP server's last fallback, so a live
        // host's ActiveVault would keep agents on the old vault (final review
        // Important 1). Self-heal never rewrites a --vault-override entry.
        if (repointVault is not null)
            return _mcp.Connect(agent, request.Scope, request.ProjectDir, VaultTarget.FollowActive, null,
                vaultOverride: repointVault);

        // PinCurrent mirrors InboundWirer.RepairToActiveVault's default: `logmark
        // connect` wires the agent at whatever vault the app is currently
        // hosting, not a floating "whatever vault is active later" pointer.
        return _mcp.Connect(agent, request.Scope, request.ProjectDir, VaultTarget.PinCurrent, null);
    }

    /// <summary>
    /// The vault to pin when a Rewrite was caused by --repoint over a vault
    /// mismatch (the entry reaches another vault than the requested one);
    /// null for every other write.
    /// </summary>
    private static string? RepointVault(ConnectRequest request, CapabilityPlan item, WiringDecisionResult d)
    {
        if (d.Action != WiringAction.Rewrite || item.Capability != Capability.Mcp) return null;
        if (!OverridesFor(request, Capability.Mcp).Repoint) return null;
        if (item.Facts is not { Origin: EntryOrigin.LogMark, EntryVault: { } entryVault }) return null;
        return request.RequestedVault is { } requested && !WiringDecision.SameVault(requested, entryVault)
            ? requested
            : null;
    }

    /// <summary>
    /// Reports per-capability install state for all three capabilities,
    /// without writing anything. Reuses Plan() rather than re-deriving
    /// installed/supported/target-path logic: a capability's Plan() Action is
    /// Noop exactly when the underlying service already considers it
    /// installed (see PlanHooks/PlanMcp/PlanSkills), so Installed maps
    /// directly from Action == Noop. Unsupported capabilities - including
    /// Skills at Project scope, per PlanSkills' v1 gate - carry
    /// Action == Unsupported (never Noop) and TargetPath == null, so they
    /// fall out of this mapping as Installed=false with no special-casing.
    /// CliExePath and Route are irrelevant to Plan() (only Apply's writers
    /// use them), so Status() passes placeholder values.
    ///
    /// Coupling note: PlanAction.Update exists in the enum but no Plan*
    /// method emits it today (only Noop/Create/Unsupported). If a future
    /// Plan change starts emitting Update for an installed-but-drifted
    /// capability, this mapping must be revisited - Installed would need
    /// to also cover Update, or an installed-drifted entry would wrongly
    /// report Installed=false here.
    /// </summary>
    public ConnectStatus Status(string framework, WireScope scope, string? projectDir)
    {
        var request = new ConnectRequest(framework, AllCapabilities, scope, projectDir, null, string.Empty);
        var plan = Plan(request);
        var items = plan.Items
            .Select(item => new CapabilityState(
                item.Capability,
                Installed: item.Supported && item.Action == PlanAction.Noop,
                item.Supported,
                item.TargetPath,
                item.BrokenReason,
                item.Note))
            .ToList();
        return new ConnectStatus(framework, scope, items);
    }

    /// <summary>
    /// Reverses Apply(): for each requested capability, calls the underlying
    /// service's Uninstall/Disconnect. All three (HooksService.Uninstall,
    /// InboundWirer.Disconnect, SkillsService.Uninstall) are idempotent - a
    /// capability that was never installed reports Success=true without
    /// touching disk - so Remove does not need to check installed state
    /// first and can call unconditionally for every Supported capability.
    /// Uses Plan() to gate on Supported the same way Apply does: an
    /// Unsupported capability (e.g. Skills at Project scope) is skipped
    /// (never attempted) and carried through with Success=true, Error set to
    /// the reason, and Action=Unsupported - identical to
    /// ApplyCapability's treatment, so the Unsupported "success" contract
    /// documented on CapabilityOutcome.Error stays one rule for both
    /// directions. Each capability is isolated in its own try/catch so one
    /// capability's removal failure never aborts the others.
    /// </summary>
    public ConnectResult Remove(ConnectRequest request)
    {
        var disconnect = request with { Verb = WiringVerb.Disconnect };
        var plan = Plan(disconnect);
        var items = plan.Items.Select(item => item is { Supported: true, Decision: not null }
            ? ApplyDecided(disconnect, item, item.Decision)
            : RemoveCapability(disconnect, item)).ToList();
        return new ConnectResult(request.Framework, request.Scope, items);
    }

    private CapabilityOutcome RemoveCapability(ConnectRequest request, CapabilityPlan item)
    {
        if (!item.Supported)
            return new CapabilityOutcome(item.Capability, true, item.Detail, PlanAction.Unsupported);

        try
        {
            var result = item.Capability switch
            {
                Capability.Hooks => _hooks.Uninstall(request.Framework, PerUserScope(request.Scope), request.ProjectDir),
                Capability.Mcp => RemoveMcp(request),
                Capability.Skills => _skills.Uninstall(request.Framework, DefaultSkillPackSlug),
                _ => throw new ArgumentOutOfRangeException(nameof(item),
                    $"Unknown capability: {item.Capability}")
            };
            return new CapabilityOutcome(item.Capability, result.Success, result.Error, PlanAction.Remove);
        }
        catch (Exception ex)
        {
            return new CapabilityOutcome(item.Capability, false, ex.Message, PlanAction.Remove);
        }
    }

    private CapabilityOutcome ApplyDecided(ConnectRequest request, CapabilityPlan item, WiringDecisionResult d)
    {
        var choiceNeeded = d.ExitCode == WiringDecision.ExitChoiceNeeded;
        try
        {
            var skills = item.Capability == Capability.Skills;
            var result = d.Action switch
            {
                WiringAction.Nothing => new WiringResult(true),
                WiringAction.AddOwner when skills => _skills.Install(request.Framework, DefaultSkillPackSlug, item.Facts?.Owners),
                WiringAction.AddOwner => new WiringResult(true),
                WiringAction.Create or WiringAction.Rewrite => Write(request, item.Capability, item.Facts?.Owners,
                    RepointVault(request, item, d)),
                WiringAction.RemoveOwner when skills => _skills.Uninstall(request.Framework, DefaultSkillPackSlug),
                WiringAction.RemoveOwner => new WiringResult(true),
                WiringAction.RemoveEntry => Erase(request, item.Capability),
                _ => throw new ArgumentOutOfRangeException(nameof(d), $"Unknown action: {d.Action}")
            };
            if (result.Success && !skills && item.Facts is { } facts)
                _ownership!.Apply(Target(request, item.Capability), facts, d.Action);
            return new CapabilityOutcome(item.Capability, result.Success, result.Error, item.Action,
                choiceNeeded, d.Message, d.Choices);
        }
        catch (Exception ex)
        {
            return new CapabilityOutcome(item.Capability, false, ex.Message, item.Action);
        }
    }

    private WiringResult Write(ConnectRequest request, Capability capability,
        IEnumerable<string>? carryOwners = null, string? repointVault = null) => capability switch
    {
        Capability.Hooks => _hooks.Install(request.Framework, PerUserScope(request.Scope), request.ProjectDir,
            request.CliExePath, request.Route),
        Capability.Mcp => ApplyMcp(request, repointVault),
        Capability.Skills => _skills.Install(request.Framework, DefaultSkillPackSlug, carryOwners),
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private WiringResult Erase(ConnectRequest request, Capability capability) => capability switch
    {
        Capability.Hooks => _hooks.Uninstall(request.Framework, PerUserScope(request.Scope), request.ProjectDir),
        Capability.Mcp => RemoveMcp(request),
        Capability.Skills => OverridesFor(request, Capability.Skills).AllProducts
            ? _skills.UninstallAll(request.Framework, DefaultSkillPackSlug)
            : _skills.Uninstall(request.Framework, DefaultSkillPackSlug),
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    private static WiringOverrides OverridesFor(ConnectRequest request, Capability capability) =>
        request.CapabilityOverrides is { } per && per.TryGetValue(capability, out var o)
            ? o
            : request.Overrides ?? WiringOverrides.None;

    private static WiringTarget Target(ConnectRequest request, Capability capability)
    {
        var scope = capability == Capability.Hooks ? PerUserScope(request.Scope) : request.Scope;
        var scopeName = scope.ToString().ToLowerInvariant();
        return new WiringTarget(request.Framework, capability.ToString().ToLowerInvariant(), scopeName,
            scope == WireScope.User ? null : request.ProjectDir);
    }

    private static PlanAction ToPlanAction(WiringAction action) => action switch
    {
        WiringAction.Create => PlanAction.Create,
        WiringAction.Rewrite => PlanAction.Update,
        WiringAction.RemoveEntry => PlanAction.Remove,
        _ => PlanAction.Noop
    };

    private WiringResult RemoveMcp(ConnectRequest request)
    {
        var agent = AgentRegistry.GetBySlug(request.Framework);
        if (agent is null)
            return new WiringResult(false, $"Unknown agent: {request.Framework}");

        return _mcp.Disconnect(agent, request.Scope, request.ProjectDir);
    }

    private CapabilityPlan PlanHooks(ConnectRequest request)
    {
        var profile = HookProfileRegistry.For(request.Framework);
        if (profile is null)
            return Unsupported(Capability.Hooks,
                $"No lifecycle hook support for agent '{request.Framework}'.");

        var scope = PerUserScope(request.Scope);
        var path = _hooks.ResolveHookPath(request.Framework, scope, request.ProjectDir);
        if (path is null)
            return Unsupported(Capability.Hooks,
                $"{profile.DisplayName} has no {scope} hook path.");

        var installed = _hooks.IsInstalled(request.Framework, scope, request.ProjectDir);
        var action = installed ? PlanAction.Noop : PlanAction.Create;
        var detail = installed ? "Hooks already installed" : "Hooks will be installed";

        // Hooks is one registry entry covering three independently-flagged
        // sub-capabilities (inject/capture/checkpoint); a framework can be
        // wired (Supported=true, normal Create/Noop action) while still
        // missing one of them. CanInject=false (copilot-cli, grok) is the
        // only such gap today - surface it as a Note rather than folding it
        // into Detail, so both the plan/apply text (WritePlan reads Detail)
        // and the --status report (which drops Detail but keeps Note) show
        // it without the two mechanisms drifting apart.
        string? note = null;
        if (!profile.CanInject)
        {
            var stillWired = profile.CanCheckpoint ? "capture and checkpoint" : "capture";
            note = $"Inject unsupported for {profile.DisplayName} - {stillWired} still wired.";
        }

        if (_ownership is not null)
        {
            var probe = new EntryProbe(installed, installed ? ShimWriter.CliShimPath() : null, null);
            var facts = _ownership.Facts(Target(request, Capability.Hooks), probe, "cli");
            var decision = WiringDecision.Decide(request.Verb, _ownership.CallerId, facts, null,
                OverridesFor(request, Capability.Hooks), $"{request.Framework} hooks", offerLocalScope: false);
            return new CapabilityPlan(Capability.Hooks, true, path, ToPlanAction(decision.Action),
                decision.Message, Note: note, Decision: decision, Facts: facts);
        }

        return new CapabilityPlan(Capability.Hooks, true, path, action, detail, Note: note);
    }

    private CapabilityPlan PlanMcp(ConnectRequest request)
    {
        var agent = AgentRegistry.GetBySlug(request.Framework);
        if (agent is null || !agent.Capabilities.HasFlag(AgentCapabilities.Inbound))
            return Unsupported(Capability.Mcp, $"No MCP support for agent '{request.Framework}'.");

        if (request.Scope == WireScope.Local && (!agent.SupportsLocalScope || request.ProjectDir is null))
            return Unsupported(Capability.Mcp,
                $"{agent.DisplayName} has no local MCP scope; use --scope user or --scope project.");

        if (request.Scope == WireScope.Project
            && (request.ProjectDir is null || agent.ProjectConfigPaths is null))
            return Unsupported(Capability.Mcp,
                $"{agent.DisplayName} does not support project-scope MCP wiring here.");

        var path = _mcp.ResolveConfigPathForUi(agent, request.Scope, request.ProjectDir);
        var state = _mcp.GetState(agent, request.Scope, request.ProjectDir);
        var installed = state.Status is AgentWireStatus.WiredGlobal
            or AgentWireStatus.WiredVault or AgentWireStatus.WiredCustom;
        var broken = state.Status == AgentWireStatus.WiredBroken;
        var action = installed ? PlanAction.Noop : PlanAction.Create;
        var detail = broken
            ? $"MCP wired but broken - {state.BrokenReason}"
            : installed ? $"MCP already wired ({state.Status})" : "MCP entry will be created";
        if (_ownership is not null)
        {
            var info = _mcp.ReadEntryInfo(agent, request.Scope, request.ProjectDir);
            var entryVault = info is null ? null
                : _resolveEntryVault is { } resolve ? resolve(info) : WiringOwnership.NamedVault(info);
            var probe = new EntryProbe(info is not null, info?.Command, entryVault, broken);
            var facts = _ownership.Facts(Target(request, Capability.Mcp), probe, "mcp");
            var decision = WiringDecision.Decide(request.Verb, _ownership.CallerId, facts,
                request.Verb == WiringVerb.Connect ? request.RequestedVault : null,
                OverridesFor(request, Capability.Mcp), $"{request.Framework} MCP entry",
                offerLocalScope: agent.SupportsLocalScope && request.Scope != WireScope.Local,
                servedByWinner: true);
            return new CapabilityPlan(Capability.Mcp, true, path, ToPlanAction(decision.Action),
                decision.Message, broken ? state.BrokenReason : null, Decision: decision, Facts: facts);
        }

        return new CapabilityPlan(Capability.Mcp, true, path, action, detail,
            broken ? state.BrokenReason : null);
    }

    private CapabilityPlan PlanSkills(ConnectRequest request)
    {
        // SkillsService v1 is hardcoded to WireScope.User internally (GetStatus
        // always calls SkillsInstaller.GetState at User scope). Deriving
        // Action/Detail from GetStatus for a Project-scope request would
        // report the User-scope install state next to a Project-scope
        // TargetPath, which can disagree (e.g. Action=Noop/"Installed" beside
        // a project path that does not exist). Rather than thread scope
        // through SkillsService - a larger change to a pre-existing service -
        // report Skills wiring as unsupported at Project scope.
        if (request.Scope == WireScope.Project)
            return Unsupported(Capability.Skills, "Skills wiring is user-scope only in v1.");

        // SkillsService.GetStatus() enumerates only agents its own resolver
        // considers skills-capable, so absence from the result is the
        // "no registry entry" signal for this capability.
        var agentStatus = _skills.GetStatus()
            .FirstOrDefault(s => s.Agent.Slug == request.Framework);
        if (agentStatus is null)
            return Unsupported(Capability.Skills,
                $"No file-based skills support for agent '{request.Framework}'.");

        var packState = agentStatus.Packs.FirstOrDefault(p => p.PackSlug == DefaultSkillPackSlug);
        if (packState is null)
            return Unsupported(Capability.Skills,
                $"Skill pack '{DefaultSkillPackSlug}' is not available.");

        // SkillsPathResolver is stateless (no injected dependencies), so it is
        // safe to construct locally purely to derive the target path - the
        // same install/uninstall path SkillsInstaller resolves internally.
        // Pass the shared bridge so a WSL-hosted agent's TargetPath resolves
        // under \\wsl.localhost\... instead of a bare Windows path.
        var targetPath = new SkillsPathResolver(_wsl)
            .Resolve(agentStatus.Agent, PerUserScope(request.Scope), request.ProjectDir);

        var installed = packState.Status == SkillInstallStatus.Installed;
        var action = installed ? PlanAction.Noop : PlanAction.Create;
        var detail = packState.Detail ?? packState.Status.ToString();
        if (_ownership is not null)
        {
            var owners = _skills.GetOwners(request.Framework, DefaultSkillPackSlug);
            var facts = _ownership.FactsFromOwners(owners, "cli", broken: owners is not null && !installed);
            var decision = WiringDecision.Decide(request.Verb, _ownership.CallerId, facts, null,
                OverridesFor(request, Capability.Skills), $"{request.Framework} skills", offerLocalScope: false);
            return new CapabilityPlan(Capability.Skills, true, targetPath, ToPlanAction(decision.Action),
                decision.Message, Decision: decision, Facts: facts);
        }

        return new CapabilityPlan(Capability.Skills, true, targetPath, action, detail);
    }

    /// <summary>
    /// Local is an MCP-only scope (Claude Code's per-project slot in
    /// ~/.claude.json). Hooks and skills are per-user installs with no local
    /// slot, so a Local request wires them at User scope. Without this,
    /// HooksService would read any non-User scope as Project and write a
    /// project hook file the caller never asked for.
    /// </summary>
    private static WireScope PerUserScope(WireScope scope) =>
        scope == WireScope.Local ? WireScope.User : scope;

    private static CapabilityPlan Unsupported(Capability capability, string detail) =>
        new(capability, false, null, PlanAction.Unsupported, detail);

    /// <summary>
    /// True when the slug is a real framework - known to either registry
    /// Plan() consults (HookProfileRegistry for Hooks, AgentRegistry for
    /// Mcp/Skills) - as opposed to an entirely unrecognized slug (typically a
    /// typo). Distinguishes that case from a real framework that simply lacks
    /// one capability (e.g. a framework with no MCP entry still reports Hooks
    /// normally): those frameworks are Unsupported per-capability in Plan()
    /// but AnyFailure-false is still the correct outcome for them, whereas an
    /// unknown slug reporting every capability Unsupported would otherwise
    /// exit 0 and silently mask a typo. Callers (ConnectCommand,
    /// DisconnectCommand) use this to fail fast with the valid-slug list
    /// before invoking Plan/Apply/Remove at all.
    /// </summary>
    public static bool IsKnownFramework(string slug) =>
        HookProfileRegistry.For(slug) is not null || AgentRegistry.GetBySlug(slug) is not null;
}
