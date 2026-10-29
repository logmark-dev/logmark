using LogMark.Services.Agents;

namespace LogMark.CLI.Commands;

/// <summary>
/// `logmark hooks install|uninstall` - manage LogMark lifecycle hooks
/// (SessionStart context injection + session-end capture-back) for any
/// framework in HookProfileRegistry. Drives the same HooksService the desktop
/// Session-hooks section uses.
/// </summary>
public class HooksGroupCommand : IGroupCommand
{
    public string Name => "hooks";
    public string Description => "Install or remove agent lifecycle hooks for a project";
    public string Usage => "hooks <install|uninstall> [--agent <slug>] [--scope user|project] [--project <dir>] [--route <route>]";
    public bool RequiresVault => false;

    private readonly Dictionary<string, ICommand> _subcommands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Subcommands => _subcommands;

    public HooksGroupCommand()
    {
        var install = new HooksInstallCommand();
        var uninstall = new HooksUninstallCommand();
        _subcommands[install.Name] = install;
        _subcommands[uninstall.Name] = uninstall;
    }

    public int Execute(CliContext context, string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var subName = args[0].ToLowerInvariant();

        if (_subcommands.TryGetValue(subName, out var sub))
            return sub.Execute(context, args.Skip(1).ToArray());

        Console.Error.WriteLine($"Unknown hooks subcommand: {subName}");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        Console.WriteLine($"Usage: lm {Usage}");
        Console.WriteLine();
        Console.WriteLine("Supported agents: " + string.Join(", ", HookProfileRegistry.SupportedSlugs));
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        foreach (var sub in _subcommands.Values.OrderBy(s => s.Name))
        {
            Console.WriteLine($"  {sub.Name,-15} {sub.Description}");
        }
    }

    internal static (string Agent, WireScope Scope, string ProjectDir, string? Route) ParseTarget(string[] args)
    {
        string agent = "claude-code", scope = "project";
        string? project = null, route = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--agent" && i + 1 < args.Length) agent = args[++i];
            else if (args[i] == "--scope" && i + 1 < args.Length) scope = args[++i];
            else if (args[i] == "--project" && i + 1 < args.Length) project = args[++i];
            else if (args[i] == "--route" && i + 1 < args.Length) route = args[++i];
        }
        var wireScope = scope.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? WireScope.User : WireScope.Project;
        return (agent, wireScope, project ?? Environment.CurrentDirectory, route);
    }

    private sealed class HooksInstallCommand : ICommand
    {
        public string Name => "install";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--agent"] = true, ["--scope"] = true, ["--project"] = true, ["--route"] = true,
    };

        public string Description => "Write LogMark lifecycle hooks for an agent";
        public string Usage => "hooks install [--agent <slug>] [--scope user|project] [--project <dir>] [--route <route>]";
        public bool RequiresVault => false;

        public int Execute(CliContext context, string[] args)
        {
            var (agent, scope, projectDir, route) = ParseTarget(args);

            // Hooks reference the stable shim, never this process's own path - both
            // products then write byte-identical hook entries (coexistence design 2.1).
            var cliExe = LogMark.Services.Providers.ShimWriter.CliShimPath();
            var service = new HooksService(new WslBridge(new SystemProcessRunner()));
            var result = service.Install(agent, scope, projectDir, cliExe, route);
            if (!result.Success)
            {
                context.WriteError($"Error: {result.Error}");
                return 2;
            }
            var hookPath = service.ResolveHookPath(agent, scope, projectDir);
            context.WriteResult($"Hooks installed for {agent} ({scope})" + (hookPath is not null ? $" at {hookPath}" : ""));
            return 0;
        }
    }

    private sealed class HooksUninstallCommand : ICommand
    {
        public string Name => "uninstall";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--agent"] = true, ["--scope"] = true, ["--project"] = true,
    };

        public string Description => "Remove LogMark hooks for an agent";
        public string Usage => "hooks uninstall [--agent <slug>] [--scope user|project] [--project <dir>]";
        public bool RequiresVault => false;

        public int Execute(CliContext context, string[] args)
        {
            var (agent, scope, projectDir, _) = ParseTarget(args);
            var result = new HooksService(new WslBridge(new SystemProcessRunner())).Uninstall(agent, scope, projectDir);
            if (!result.Success)
            {
                context.WriteError($"Error: {result.Error}");
                return 2;
            }
            context.WriteResult($"Hooks removed for {agent} ({scope})");
            return 0;
        }
    }
}
