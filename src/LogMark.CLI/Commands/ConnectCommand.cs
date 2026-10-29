using System.Text;
using System.Text.Json;
using LogMark.Services.Agents;

namespace LogMark.CLI.Commands;

/// <summary>
/// `logmark connect &lt;framework&gt;` - thin CLI wrapper over ConnectService,
/// the kernel orchestrator that reconciles hooks, MCP, and skills wiring for
/// an agent framework in one call. Mirrors HooksGroupCommand's construction
/// style (own the wiring services locally, no DI container). Also hosts
/// `connect --status [&lt;framework&gt;]` (read-only, over ConnectService.Status) -
/// kept on this command rather than split out so `lm connect` stays the one
/// entry point for the whole capability lifecycle except disconnect, which
/// has its own verb (DisconnectCommand) since removal is a distinct action
/// worth its own top-level name.
/// </summary>
public class ConnectCommand : ICommand
{
    public string Name => "connect";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--hooks"] = false, ["--mcp"] = false, ["--skills"] = false, ["--all"] = false,
        ["--scope"] = true, ["--project"] = true, ["--route"] = true,
        ["--dry-run"] = false, ["--repoint"] = false, ["--replace"] = false, ["--status"] = false,
    };

    public string Description => "Wire hooks, MCP, and skills for an agent framework";
    public string Usage =>
        "connect <framework> [--hooks] [--mcp] [--skills] [--all] [--scope user|project|local] [--project <dir>] [--route <route>] [--dry-run] [--repoint] [--replace] | connect --status [<framework>] [--scope user|project|local] [--project <dir>]";
    public bool RequiresVault => false;

    public string? DetailedHelp =>
        "  --scope local is Claude Code only: it writes the MCP entry for one project directory and overrides a user-scope entry there (hooks and skills stay user-scope).\n" +
        "  Agents share one LogMark memory. When another LogMark product already serves an entry, connect records this build as a fallback and changes nothing. Where a more capable LogMark product is installed (the desktop app), this build never creates an MCP entry: agents reach LogMark through that product, so connect them from there (exit 3).\n" +
        "  The requested vault is --vault, or LOGMARK_VAULT when --vault is absent; connect compares it with the vault the existing entry reaches.\n" +
        "  --repoint   point an existing shared entry at --vault for every LogMark product (the entry then pins that vault)\n" +
        "  --replace   replace an entry named logmark that LogMark did not write\n" +
        "  Exit codes: 0 done or nothing to do, 2 a capability failed, 3 nothing changed because a choice is needed (the alternatives are printed with their flags), 64 unknown flag.";

    public string[]? Examples => new[]
    {
        "lm connect claude-code --all",
        "lm connect claude-code --hooks --mcp --dry-run",
        "lm --json connect codex --all --scope project --project .",
        "lm connect claude-code --mcp --scope local --project .",
        "lm connect --status claude-code",
        "lm --json connect --status"
    };

    /// <summary>True when a person can answer a prompt: stdin and stdout are both terminals.
    /// Agents and scripts redirect at least one, so they never block.</summary>
    public static Func<bool> IsInteractive { get; set; } = () => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public static Func<string?> ReadChoice { get; set; } = Console.ReadLine;

    internal static readonly IReadOnlySet<string> ConnectFlagNames = new HashSet<string>(StringComparer.Ordinal)
    { "--hooks", "--mcp", "--skills", "--all", "--scope", "--project", "--route", "--dry-run", "--repoint", "--replace" };

    internal static string CliName => OwnerId == "oss-cli" ? "logmark" : "lm";

    /// <summary>
    /// Set only by the unified `logmark` binary (LogMark.Daemon), before any
    /// CLI verb dispatches: the MCP launch command InboundWirer falls back to
    /// ("&lt;this running binary&gt; mcp") when no registered provider resolves
    /// the mcp role. A registered provider always wins, so on a machine with the
    /// desktop app an Engine write keeps the desktop's binary. Null on lm.exe
    /// and desktop.
    /// </summary>
    public static McpLaunchSpec? SelfMcpLaunch { get; set; }

    /// <summary>
    /// Identifies which product this running binary is, threaded into
    /// SkillsService's refcounted ownership marker (see
    /// SkillsInstaller.PackMarker) so `connect --skills`/`disconnect --skills`
    /// write/remove this product's entry in the shared ".logmark-pack" marker
    /// rather than another product's. Defaults to "desktop" (lm.exe and the
    /// desktop app's own CLI paths); set to "oss-cli" only by the unified
    /// binary's CLI dispatch, alongside SelfMcpLaunch, so the two products
    /// sharing a user's skills dir never delete each other's install.
    /// </summary>
    public static string OwnerId { get; set; } = "desktop";

    public int Execute(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        if (args[0] == "--status")
            return ExecuteStatus(context, args.Skip(1).ToArray());

        var framework = args[0];

        var flags = ParseFlags(args, 1, ConnectFlagNames);
        if (RejectUnknown(context, flags, Usage)) return 64;
        var capabilities = BuildRequestedCapabilities(flags.All, flags.Hooks, flags.Mcp, flags.Skills);

        if (capabilities.Count == 0)
        {
            context.WriteError(
                "No capability selected. Specify at least one of --hooks, --mcp, --skills, or --all.");
            return 2;
        }

        if (!TryParseScope(flags.Scope, context, out var scope))
            return 2;

        // An entirely unrecognized slug (typically a typo, e.g. "cluade-code")
        // would otherwise plan every capability Unsupported and Apply would
        // carry each through as Success=true - AnyFailure stays false and the
        // command falsely exits 0. Checked uniformly for dry-run and apply
        // (and before the --mcp vault guard below, so a typo reports the
        // slug list rather than a misleading "requires a vault" error) -
        // there is no plan worth printing for a framework neither registry
        // has ever heard of.
        if (!ConnectService.IsKnownFramework(framework))
        {
            context.WriteError(
                $"Unknown framework '{framework}'. Valid frameworks: {string.Join(", ", HookProfileRegistry.SupportedSlugs)}");
            return 2;
        }

        var projectDir = scope is WireScope.Project or WireScope.Local ? (flags.Project ?? Environment.CurrentDirectory) : flags.Project;

        // Hooks reference the stable shim, never this process's own path - both
        // products then write byte-identical hook entries (coexistence design 2.1).
        var cliExe = LogMark.Services.Providers.ShimWriter.CliShimPath();

        // Apply writes to disk. A bare Mcp Apply with no resolvable vault would
        // otherwise construct InboundWirer against an empty vault path, which
        // writes a stray relative vault-bootstrap.json into the process CWD and
        // wires a broken MCP config - fail closed instead of applying anything.
        // Hoisted above Plan/WritePlan (rather than just before Apply) so a
        // vault-less --mcp apply fails atomically with nothing on stdout - a
        // JSON consumer never sees a plan with no matching result. Gated on
        // !dryRun: dry-run never writes to disk regardless of vault
        // availability, so it still prints its plan without one.
        if (!flags.DryRun && capabilities.Contains(Capability.Mcp) && string.IsNullOrEmpty(context.VaultPath))
        {
            context.WriteError(
                "connect --mcp requires a vault - pass --vault <path> or set LOGMARK_VAULT");
            return 2;
        }

        var service = BuildConnectService(context.VaultPath);
        var request = new ConnectRequest(framework, capabilities, scope, projectDir, flags.Route, cliExe,
            WiringVerb.Connect, context.VaultPath,
            new WiringOverrides(Repoint: flags.Repoint, Replace: flags.Replace));

        return RunDecided(context, service, request, dryRun: flags.DryRun, explicitChoice: flags.Repoint || flags.Replace);
    }

    internal sealed record ConnectFlags(bool Hooks, bool Mcp, bool Skills, bool All, string Scope, string? Project,
        bool DryRun, string? Route, bool AllProducts, bool Repoint, bool Replace, IReadOnlyList<string> Unknown);

    /// <summary>
    /// One scan of args from startIndex (index 0 is the framework). A value token
    /// consumed by --scope/--project/--route is never re-examined as a flag. Any
    /// token not in <paramref name="allowed"/> - unknown flags and stray
    /// positionals alike - lands in Unknown; callers reject it with exit 64
    /// (rc5 O2: an ignored --dry-run once removed a real entry).
    /// </summary>
    internal static ConnectFlags ParseFlags(string[] args, int startIndex, IReadOnlySet<string> allowed)
    {
        bool hooks = false, mcp = false, skills = false, all = false, dryRun = false;
        bool allProducts = false, repoint = false, replace = false;
        string scope = "user";
        string? project = null, route = null;
        var unknown = new List<string>();

        for (var i = startIndex; i < args.Length; i++)
        {
            var a = args[i];
            if (!allowed.Contains(a)) { unknown.Add(a); continue; }
            switch (a)
            {
                case "--hooks": hooks = true; break;
                case "--mcp": mcp = true; break;
                case "--skills": skills = true; break;
                case "--all": all = true; break;
                case "--dry-run": dryRun = true; break;
                case "--all-products": allProducts = true; break;
                case "--repoint": repoint = true; break;
                case "--replace": replace = true; break;
                case "--scope" when i + 1 < args.Length: scope = args[++i]; break;
                case "--project" when i + 1 < args.Length: project = args[++i]; break;
                case "--route" when i + 1 < args.Length: route = args[++i]; break;
                default: unknown.Add(a); break;
            }
        }
        return new ConnectFlags(hooks, mcp, skills, all, scope, project, dryRun, route, allProducts, repoint, replace, unknown);
    }

    internal static bool RejectUnknown(CliContext context, ConnectFlags flags, string usage)
    {
        if (flags.Unknown.Count == 0) return false;
        context.WriteError($"{CliName}: unknown argument '{flags.Unknown[0]}'");
        context.WriteError($"Usage: {CliName} {usage}");
        return true;
    }

    internal static int RunDecided(CliContext context, ConnectService service, ConnectRequest request,
        bool dryRun, bool explicitChoice)
    {
        var plan = service.Plan(request);
        WritePlan(context, plan);
        if (dryRun)
        {
            // A dry run exits with the code the real run would and prints the
            // same alternatives, so harnesses can rehearse a choice safely.
            if (!context.JsonOutput)
                foreach (var item in plan.Items)
                    WriteAlternatives(context, request, item.Capability, item.Decision?.Choices);
            return plan.Items.Any(i => i.Decision?.ExitCode == WiringDecision.ExitChoiceNeeded)
                ? WiringDecision.ExitChoiceNeeded : 0;
        }

        if (!explicitChoice && !context.JsonOutput && IsInteractive())
            request = PromptForChoices(context, plan, request);

        var result = request.Verb == WiringVerb.Disconnect ? service.Remove(request) : service.Apply(request);
        WriteResult(context, result, request);
        return result.ExitCode;
    }

    private static ConnectRequest PromptForChoices(CliContext context, ConnectPlan plan, ConnectRequest request)
    {
        var chosen = new Dictionary<Capability, WiringOverrides>();
        foreach (var item in plan.Items)
        {
            if (item.Decision is not { OffersChoice: true } d) continue;
            context.WriteResult($"{d.Message}");
            for (var i = 0; i < d.Choices.Count; i++)
                context.WriteResult($"  {i + 1}) {d.Choices[i].Description}{(i == 0 ? " (default)" : "")}");
            context.WriteResult($"Choose 1-{d.Choices.Count} [1]:");

            var answer = ReadChoice()?.Trim();
            var index = int.TryParse(answer, out var n) && n >= 1 && n <= d.Choices.Count ? n - 1 : 0;
            switch (d.Choices[index].Id)
            {
                case "all-products": chosen[item.Capability] = new WiringOverrides(AllProducts: true); break;
                case "repoint": chosen[item.Capability] = new WiringOverrides(Repoint: true); break;
                case "replace": chosen[item.Capability] = new WiringOverrides(Replace: true); break;
                case "scope-local":
                    context.WriteResult($"Run: {CliName} connect {request.Framework} --mcp --scope local --project \"{Environment.CurrentDirectory}\" --vault \"{request.RequestedVault}\"");
                    break;
            }
        }
        return chosen.Count == 0 ? request : request with { CapabilityOverrides = chosen };
    }

    /// <summary>
    /// One line per non-default choice, naming the exact command that selects
    /// it: the request's scope, project and requested vault are repeated so the
    /// line is safe to paste (rc6 QA O2).
    /// </summary>
    internal static void WriteAlternatives(CliContext context, ConnectRequest request,
        Capability capability, IReadOnlyList<WiringChoice>? choices)
    {
        foreach (var choice in (choices ?? Array.Empty<WiringChoice>()).Where(c => c.Flag is not null))
        {
            var tail = new StringBuilder();
            if (choice.Flag!.StartsWith("--scope", StringComparison.Ordinal))
                tail.Append($" --project \"{request.ProjectDir ?? Environment.CurrentDirectory}\"");
            else if (request.Scope != WireScope.User)
            {
                tail.Append($" --scope {request.Scope.ToString().ToLowerInvariant()}");
                if (request.ProjectDir is not null) tail.Append($" --project \"{request.ProjectDir}\"");
            }
            if (request.Verb == WiringVerb.Connect && request.RequestedVault is not null)
                tail.Append($" --vault \"{request.RequestedVault}\"");
            context.WriteResult($"    to {char.ToLowerInvariant(choice.Description[0])}{choice.Description[1..]}: " +
                $"{CliName} {(request.Verb == WiringVerb.Disconnect ? "disconnect" : "connect")} {request.Framework} " +
                $"--{capability.ToString().ToLowerInvariant()} {choice.Flag}{tail}");
        }
    }

    /// <summary>
    /// Shared with DisconnectCommand: --all always means all three
    /// capabilities regardless of any --hooks/--mcp/--skills flags also
    /// present, matching connect's existing wantAll-wins behavior.
    /// </summary>
    internal static IReadOnlySet<Capability> BuildRequestedCapabilities(
        bool all, bool hooks, bool mcp, bool skills)
    {
        if (all)
            return new HashSet<Capability> { Capability.Hooks, Capability.Mcp, Capability.Skills };

        var set = new HashSet<Capability>();
        if (hooks) set.Add(Capability.Hooks);
        if (mcp) set.Add(Capability.Mcp);
        if (skills) set.Add(Capability.Skills);
        return set;
    }

    /// <summary>
    /// Shared with DisconnectCommand and the --status branch: parses the
    /// --scope value, writing the same invalid-value error connect has always
    /// used. Returns false (and writes the error) on an unrecognized value.
    /// </summary>
    internal static bool TryParseScope(string scopeArg, CliContext context, out WireScope scope)
    {
        if (scopeArg.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            scope = WireScope.User;
            return true;
        }

        if (scopeArg.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            scope = WireScope.Project;
            return true;
        }

        if (scopeArg.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            scope = WireScope.Local;
            return true;
        }

        context.WriteError($"Invalid --scope value '{scopeArg}'. Valid values: user, project, local.");
        scope = default;
        return false;
    }

    /// <summary>
    /// `connect --status [&lt;framework&gt;]` - read-only report over
    /// ConnectService.Status(), never Plan()/Apply(). With a framework, reports
    /// that framework alone; without one, iterates every slug
    /// HookProfileRegistry knows (the same five frameworks connect's hooks
    /// capability supports) and reports each. Always exits 0 - there is
    /// nothing to fail at, only frameworks/capabilities to report as
    /// unsupported.
    /// </summary>
    private static int ExecuteStatus(CliContext context, string[] args)
    {
        string? framework = null;
        var scopeArg = "user";
        string? project = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scope" when i + 1 < args.Length: scopeArg = args[++i]; break;
                case "--project" when i + 1 < args.Length: project = args[++i]; break;
                default:
                    if (framework is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                        framework = args[i];
                    break;
            }
        }

        if (!TryParseScope(scopeArg, context, out var scope))
            return 2;

        var projectDir = scope is WireScope.Project or WireScope.Local ? (project ?? Environment.CurrentDirectory) : project;
        var service = BuildConnectService(context.VaultPath);
        var frameworks = framework is not null
            ? new[] { framework }
            : HookProfileRegistry.SupportedSlugs.ToArray();

        var statuses = frameworks.Select(f => service.Status(f, scope, projectDir)).ToList();

        if (context.JsonOutput)
        {
            // A single requested framework emits its per-capability state
            // array directly (items only) - the natural shape for "status of
            // this one framework". Multiple frameworks (no framework given)
            // emit one framework-wrapped object per slug, since a bare
            // capability array can't distinguish which framework each state
            // belongs to.
            object payload = framework is not null
                ? StatusItemsJson(statuses[0])
                : statuses.Select(StatusJson);
            context.WriteResult(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        foreach (var status in statuses)
            WriteStatus(context, status, projectDir);

        return 0;
    }

    private static object StatusJson(ConnectStatus status) => new
    {
        framework = status.Framework,
        scope = status.Scope.ToString(),
        items = StatusItemsJson(status)
    };

    private static IEnumerable<object> StatusItemsJson(ConnectStatus status) =>
        status.Items.Select(i => new
        {
            capability = i.Capability.ToString(),
            installed = i.Installed,
            supported = i.Supported,
            targetPath = i.TargetPath,
            brokenReason = i.BrokenReason,
            note = i.Note
        });

    private static void WriteStatus(CliContext context, ConnectStatus status, string? projectDir)
    {
        context.WriteResult($"Status for {status.Framework} ({status.Scope}):");
        // Claude Code reads a local entry only for a session started at exactly
        // that directory; a session started in a subfolder falls back to user
        // scope. Say so, or "installed" reads as "installed everywhere below".
        if (status.Scope == WireScope.Local)
        {
            var projectDirShown = projectDir is not null ? ClaudeProjectKey.Normalize(projectDir) : "(no project dir)";
            context.WriteResult($"  note: local scope is keyed to {projectDirShown} and does not inherit into subfolders; start sessions at that directory");
        }
        foreach (var item in status.Items)
        {
            // A wired-but-broken entry (e.g. its binary was uninstalled) reports
            // honestly rather than as "not installed" - the whole point of the
            // dead-command diagnosis.
            var state = !item.Supported ? "unsupported"
                : item.BrokenReason is not null ? $"broken - {item.BrokenReason}"
                : item.Installed ? "installed" : "not installed";
            var path = item.TargetPath is not null ? $" -> {item.TargetPath}" : "";
            var note = item.Note is not null ? $" ({item.Note})" : "";
            context.WriteResult($"  {item.Capability,-8} {state}{path}{note}");
        }
    }

    /// <summary>
    /// Constructs ConnectService over the three real wiring services, mirroring
    /// the production composition in App.Startup/App.TrayAndShell (BuildInboundWirer,
    /// the SkillsService block). The CLI has no Velopack-aware InstallPathResolver
    /// (that type lives in LogMark.App, a net8.0-windows project the CLI does not
    /// reference), so McpExeResolver falls back to its own base-directory default
    /// (AppContext.BaseDirectory/mcp) - the same "agnostic fallback for any other
    /// consumer" its constructor comment documents.
    /// </summary>
    internal static ConnectService BuildConnectService(string? vaultPath)
    {
        // One bridge per composition root, reused across every service below
        // (including HooksService - a WSL-hosted agent's hooks file lives
        // under the distro's $HOME same as its MCP config and skills dir)
        // so its lazy distro/home caches are shared instead of re-probed per
        // service. Constructing it is free - discovery only runs on first
        // property access - so this never touches wsl.exe on its own.
        var wsl = new WslBridge(new SystemProcessRunner());

        var hooks = new HooksService(wsl);

        // WindowsRegistryProbe is [SupportedOSPlatform("windows")]; the CLI
        // project targets plain net8.0 (no platform annotation) since it also
        // needs to build on non-Windows CI. LogMark itself is Windows-only in
        // practice (see repo CLAUDE.md), so suppress CA1416 narrowly here
        // rather than annotating the whole project.
        // Shared machine registry: resolve the winning MCP provider from
        // providers/ so both products (app settings + `lm connect`) write
        // byte-identical entries. The SelfMcpLaunch override (set by the unified
        // binary) is only the fallback when no provider resolves (Important 4).
        var providerResolver = new LogMark.Services.Providers.ProviderResolver(
            new LogMark.Services.Providers.ProviderManifestStore(
                LogMark.Services.Providers.ProviderManifestStore.DefaultRoot()));

#pragma warning disable CA1416
        var mcp = new InboundWirer(
            new McpExeResolver(),
            new ConfigPathResolver(new WindowsRegistryProbe(), wsl),
            new ProcessRunnerNativeCliDelegate(new SystemProcessRunner()),
            activeVaultPath: vaultPath ?? string.Empty,
            launchOverride: SelfMcpLaunch,
            providerResolver: providerResolver,
            wsl: wsl);
#pragma warning restore CA1416

        var skills = new SkillsService(
            new SkillPackCatalog(Path.Combine(AppContext.BaseDirectory, "Resources", "SkillPacks")),
            new SkillsInstaller(new SkillsPathResolver(wsl)),
            new SkillsPathResolver(wsl),
            new AgentDetector(wsl),
            AgentRegistry.GetAll(),
            ownerId: OwnerId);

        return new ConnectService(hooks, mcp, skills, wsl,
            LogMark.Services.Agents.WiringOwnership.Default(OwnerId), info => VaultResolver.ResolveEntryVault(info));
    }

    private static void WritePlan(CliContext context, ConnectPlan plan)
    {
        if (context.JsonOutput)
        {
            var json = JsonSerializer.Serialize(new
            {
                framework = plan.Framework,
                scope = plan.Scope.ToString(),
                items = plan.Items.Select(i => new
                {
                    capability = i.Capability.ToString(),
                    supported = i.Supported,
                    targetPath = i.TargetPath,
                    action = i.Action.ToString(),
                    detail = i.Detail,
                    note = i.Note,
                    message = i.Decision?.Message,
                    choices = (i.Decision?.Choices ?? Array.Empty<WiringChoice>()).Select(c => new { id = c.Id, flag = c.Flag, description = c.Description })
                })
            }, new JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
            return;
        }

        context.WriteResult($"Plan for {plan.Framework} ({plan.Scope}):");
        foreach (var item in plan.Items)
        {
            var path = item.TargetPath is not null ? $" -> {item.TargetPath}" : "";
            var note = item.Note is not null ? $" ({item.Note})" : "";
            context.WriteResult($"  {item.Capability,-8} {item.Action,-11} {item.Detail}{path}{note}");
        }
    }

    /// <summary>
    /// Shared with DisconnectCommand: both connect and disconnect end in a
    /// ConnectResult (Apply() and Remove() respectively) reported in the same
    /// shape.
    /// </summary>
    internal static void WriteResult(CliContext context, ConnectResult result, ConnectRequest request)
    {
        if (context.JsonOutput)
        {
            var json = JsonSerializer.Serialize(new
            {
                framework = result.Framework,
                scope = result.Scope.ToString(),
                anyFailure = result.AnyFailure,
                exitCode = result.ExitCode,
                items = result.Items.Select(i => new
                {
                    capability = i.Capability.ToString(),
                    success = i.Success,
                    error = i.Error,
                    action = i.Action.ToString(),
                    message = i.Message,
                    choiceNeeded = i.ChoiceNeeded,
                    choices = (i.Choices ?? Array.Empty<WiringChoice>()).Select(c => new { id = c.Id, flag = c.Flag, description = c.Description })
                })
            }, new JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
            return;
        }

        context.WriteResult($"Result for {result.Framework} ({result.Scope}):");
        foreach (var item in result.Items)
        {
            // A capability that waits on a choice changed nothing: never "ok" beside exit 3.
            var status = !item.Success ? "FAILED" : item.ChoiceNeeded ? "unchanged" : "ok";
            var detail = item.Error is not null ? $" - {item.Error}" : "";
            context.WriteResult($"  {item.Capability,-8} {item.Action,-11} {status}{detail}");
            if (item.Message is not null)
                context.WriteResult($"    {item.Message}");
            WriteAlternatives(context, request, item.Capability, item.Choices);
        }
    }
}
