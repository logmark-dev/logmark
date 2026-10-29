using LogMark.Services.Agents;

namespace LogMark.CLI.Commands;

/// <summary>
/// `logmark disconnect &lt;framework&gt;` - thin CLI wrapper over
/// ConnectService.Remove(), the kernel orchestrator that reverses hooks, MCP,
/// and skills wiring for an agent framework in one call. Mirrors
/// ConnectCommand's capability/scope parsing (BuildRequestedCapabilities,
/// TryParseScope) rather than re-deriving it.
///
/// Unlike `connect --mcp`, disconnect does NOT apply connect's
/// no-resolvable-vault guard: InboundWirer.Disconnect only resolves the
/// agent's config path and removes the logmark entry from it - it never reads
/// _activeVaultPath (that field is only consulted by Connect's
/// EnsureBootstrap/PinCurrent write path). A vault-less InboundWirer is
/// therefore safe to remove through; guarding it here would incorrectly block
/// `lm disconnect ... --mcp` for a user who never configured a vault.
/// </summary>
public class DisconnectCommand : ICommand
{
    public string Name => "disconnect";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--hooks"] = false, ["--mcp"] = false, ["--skills"] = false, ["--all"] = false,
        ["--scope"] = true, ["--project"] = true, ["--dry-run"] = false, ["--all-products"] = false,
    };

    public string Description => "Remove hooks, MCP, and skills wiring for an agent framework";
    public string Usage =>
        "disconnect <framework> [--hooks] [--mcp] [--skills] [--all] [--scope user|project|local] [--project <dir>] [--dry-run] [--all-products]";

    public string? DetailedHelp =>
        "  disconnect removes only what this product connected. An entry another LogMark product also uses stays, and an entry this product did not create is left alone (exit 3).\n" +
        "  --all-products   remove it for every LogMark product\n" +
        "  --dry-run        print what would happen and change nothing";

    internal static readonly IReadOnlySet<string> DisconnectFlagNames = new HashSet<string>(StringComparer.Ordinal)
    { "--hooks", "--mcp", "--skills", "--all", "--scope", "--project", "--dry-run", "--all-products" };
    public bool RequiresVault => false;

    public string[]? Examples => new[]
    {
        "lm disconnect claude-code --all",
        "lm disconnect codex --mcp --scope project --project .",
        "lm disconnect claude-code --mcp --dry-run"
    };

    public int Execute(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var framework = args[0];

        var flags = ConnectCommand.ParseFlags(args, 1, DisconnectFlagNames);
        if (ConnectCommand.RejectUnknown(context, flags, Usage)) return 64;

        var capabilities = ConnectCommand.BuildRequestedCapabilities(flags.All, flags.Hooks, flags.Mcp, flags.Skills);

        if (capabilities.Count == 0)
        {
            context.WriteError(
                "No capability selected. Specify at least one of --hooks, --mcp, --skills, or --all.");
            return 2;
        }

        if (!ConnectCommand.TryParseScope(flags.Scope, context, out var scope))
            return 2;

        // Same unknown-slug guard as ConnectCommand: without it, an entirely
        // unrecognized framework reports every capability Unsupported and
        // Remove() carries each through as Success=true, so a typo falsely
        // exits 0 here too.
        if (!ConnectService.IsKnownFramework(framework))
        {
            context.WriteError(
                $"Unknown framework '{framework}'. Valid frameworks: {string.Join(", ", HookProfileRegistry.SupportedSlugs)}");
            return 2;
        }

        var projectDir = scope is WireScope.Project or WireScope.Local ? (flags.Project ?? Environment.CurrentDirectory) : flags.Project;

        // Route and CliExePath are Apply-only concerns (hook injection content,
        // MCP command-line wiring) - Remove()'s underlying Uninstall/Disconnect
        // calls never read them, so placeholder values match ConnectService's
        // own Status() call.
        var service = ConnectCommand.BuildConnectService(context.VaultPath);
        var request = new ConnectRequest(framework, capabilities, scope, projectDir, Route: null, CliExePath: string.Empty,
            WiringVerb.Disconnect, null, new WiringOverrides(AllProducts: flags.AllProducts));
        return ConnectCommand.RunDecided(context, service, request, dryRun: flags.DryRun, explicitChoice: flags.AllProducts);
    }
}
