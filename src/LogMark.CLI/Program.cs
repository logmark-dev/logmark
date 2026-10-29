using LogMark.CLI.Commands;

namespace LogMark.CLI;

public class Program
{
    /// <summary>
    /// Optional closed-command registrar. The open build leaves this null and
    /// ships only the kernel verbs. The closed build (LogMark.CLI.Producers)
    /// sets it to add the producer verbs (thinktank, concepts, dream, feed,
    /// asset, agent-capture) - explicit registration, no assembly scanning, and
    /// the open tree compiles with the closed project simply absent. In-process
    /// tests that need the closed verbs set this in their module initializer.
    /// </summary>
    public static Action<CommandRegistry>? ClosedCommandRegistrar;

    /// <summary>
    /// Optional context factory. The open build constructs a kernel-only
    /// <see cref="CliContext"/>; the closed build swaps in a factory that
    /// constructs <c>ProducerCliContext</c> so the producer verbs resolve their
    /// closed services.
    /// </summary>
    public static Func<string?, bool, bool, CliContext>? ContextFactory;

    public static int Main(string[] args) => Run(args);

    /// <summary>
    /// Builds the command registry (open verbs, then any closed verbs the
    /// closed build registered) and dispatches. Shared by the open Main and the
    /// closed LogMark.CLI.Producers entry point.
    /// </summary>
    public static int Run(string[] args)
    {
        var registry = BuildRegistry();
        var runner = new CommandRunner(registry, ContextFactory);
        return runner.Run(args);
    }

    /// <summary>
    /// Builds the full command registry (open verbs, any closed verbs the
    /// closed build registered via <see cref="ClosedCommandRegistrar"/>, plus
    /// help) without dispatching a command. Extracted from <see cref="Run"/>
    /// so tests can inspect the resulting verb set for a given state of the
    /// two hooks - in particular hooks left null, the open-build shape - and
    /// callers of Run always exercise the same construction path.
    /// </summary>
    internal static CommandRegistry BuildRegistry()
    {
        EnsureHooksPaired();

        var registry = new CommandRegistry();
        RegisterOpenCommands(registry);
        ClosedCommandRegistrar?.Invoke(registry);
        registry.Register(new HelpCommand(new CommandRunner(registry, ContextFactory)));
        return registry;
    }

    /// <summary>
    /// The registered verb set (name + one-line description), ordered by name.
    /// Public so the unified `logmark` binary's dispatcher (LogMark.Daemon)
    /// can tell a real CLI verb from an unknown top-level command and render
    /// the CLI section of its usage screen without re-declaring the verb list.
    /// Reflects the current hook state: the open build ships kernel verbs only;
    /// the closed build additionally surfaces the producer verbs.
    /// </summary>
    public static IReadOnlyList<(string Name, string Description)> VerbSummaries() =>
        BuildRegistry().Commands.Values
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => (c.Name, c.Description))
            .ToArray();

    /// <summary>
    /// Fails fast if only one of <see cref="ClosedCommandRegistrar"/> /
    /// <see cref="ContextFactory"/> is set. The two are always paired by
    /// design: the closed verbs the registrar adds assume the producer
    /// context's services are available, and a producer context without the
    /// closed verbs registered would silently expose closed services nothing
    /// dispatches to. Checked here (at registry-build time) rather than on
    /// each property setter, because the three real call sites (ClosedProgram,
    /// and the two test ModuleInits) assign the registrar before the context
    /// factory - a setter-level check would reject that order.
    /// </summary>
    private static void EnsureHooksPaired()
    {
        if ((ClosedCommandRegistrar == null) != (ContextFactory == null))
        {
            throw new InvalidOperationException(
                "ClosedCommandRegistrar and ContextFactory must both be set or both be null - " +
                "they are always paired (closed verbs assume the producer context's services). " +
                $"Current state: ClosedCommandRegistrar={(ClosedCommandRegistrar == null ? "null" : "set")}, " +
                $"ContextFactory={(ContextFactory == null ? "null" : "set")}.");
        }
    }

    /// <summary>
    /// Registers the open (kernel-only) verb set. These are the verbs the public
    /// substrate CLI ships: capture, query/read verbs, routes, lint, page,
    /// entry, vault, hooks/connect, link, plus help/version.
    /// </summary>
    private static void RegisterOpenCommands(CommandRegistry registry)
    {
        registry.Register(new VersionCommand());
        registry.Register(new VaultGroupCommand());
        registry.Register(new CaptureCommand());
        registry.Register(new RecentCommand());
        // Only the closed context can supply an embedding model (lmk-9hkn).
        registry.Register(new SearchCommand(semanticSearch: ContextFactory is not null));
        registry.Register(new TasksCommand());
        registry.Register(new BlocksCommand());
        registry.Register(new DecisionsCommand());
        registry.Register(new RoutesCommand());
        registry.Register(new RoutesNormalizeCommand());
        registry.Register(new PageGroupCommand());
        registry.Register(new EntryGroupCommand());
        registry.Register(new LintGroupCommand());
        registry.Register(new AgentContextCommand());
        registry.Register(new OpenAgentCaptureCommand());
        registry.Register(new HooksGroupCommand());
        registry.Register(new ConnectCommand());
        registry.Register(new DisconnectCommand());
        registry.Register(new LinkCommand());
    }
}
