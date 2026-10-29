using System.Text.Json;
using LogMark.CLI.Commands;

namespace LogMark.CLI;

/// <summary>
/// Orchestrates CLI execution: parse globals, resolve vault, check requirements, dispatch command.
/// </summary>
public class CommandRunner
{
    private readonly CommandRegistry _registry;
    private readonly Func<string?, bool, bool, CliContext> _contextFactory;

    /// <summary>
    /// Notation prefixes that indicate capture content (not a mistyped command).
    /// </summary>
    private static readonly string[] NotationPrefixes =
    {
        "t:", "i:", "b:", "d:",
        "task:", "idea:", "block:", "decision:"
    };

    /// <param name="contextFactory">
    /// Factory for the per-invocation <see cref="CliContext"/>. Null defaults to
    /// the kernel-only <see cref="CliContext"/>; the closed build passes a
    /// factory that constructs the producer context so closed verbs resolve
    /// their services.
    /// </param>
    public CommandRunner(CommandRegistry registry, Func<string?, bool, bool, CliContext>? contextFactory = null)
    {
        _registry = registry;
        _contextFactory = contextFactory ?? ((vault, json, quiet) => new CliContext(vault, json, quiet));
    }

    public int Run(string[] args)
    {
        var globals = GlobalArgParser.Parse(args);

        // --version flag
        if (globals.ShowVersion)
        {
            PrintVersion();
            return 0;
        }

        // --help-json flag
        if (globals.HelpJson)
        {
            PrintHelpJson();
            return 0;
        }

        // No command and --help, or no args at all
        if (globals.CommandName == null)
        {
            PrintHelp();
            return 0;
        }

        // --help with a command name: show command-specific help
        if (globals.ShowHelp)
        {
            return PrintCommandHelp(globals.CommandName, globals.RemainingArgs);
        }

        // "help" command, including `help <verb> <subverb>`
        if (globals.CommandName == "help")
        {
            if (globals.RemainingArgs.Length > 0)
                return PrintCommandHelp(globals.RemainingArgs[0], globals.RemainingArgs.Skip(1));
            PrintHelp();
            return 0;
        }

        // Find command
        var command = _registry.Find(globals.CommandName);

        // A help flag anywhere after the verb is a request for help, never an
        // argument: before this, `entry mint-anchors --help` ran the scan and
        // `vault init <dir> --help` created a vault (bead lmk-r2hs).
        if (command != null && globals.RemainingArgs.Any(IsHelpFlag))
        {
            return PrintCommandHelp(command, globals.RemainingArgs);
        }

        if (command == null)
        {
            // `lm "t: text" --help` must not capture the text either.
            if (globals.RemainingArgs.Any(IsHelpFlag))
            {
                PrintHelp();
                return 0;
            }

            // Check for typos first (distance 1 = likely typo)
            var similar = _registry.FindSimilar(globals.CommandName, maxDistance: 1);
            if (similar.Count > 0)
            {
                Console.Error.Write($"Unknown command: {globals.CommandName}");
                Console.Error.Write($". Did you mean '{similar[0]}'?");
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Run '{CliName} help' for usage information.");
                return 1;
            }

            // Check if input looks like capture content
            if (LooksLikeCapture(globals.CommandName, globals.RemainingArgs))
            {
                var captureCmd = _registry.Find("capture")!;
                var captureArgs = new[] { globals.CommandName }
                    .Concat(globals.RemainingArgs).ToArray();
                return ResolveAndExecute(captureCmd, globals, captureArgs);
            }

            // Genuinely unknown command
            Console.Error.Write($"Unknown command: {globals.CommandName}");

            // Try broader distance for suggestions
            var broader = _registry.FindSimilar(globals.CommandName);
            if (broader.Count > 0)
                Console.Error.Write($". Did you mean '{broader[0]}'?");

            Console.Error.WriteLine();
            Console.Error.WriteLine($"Run '{CliName} help' for usage information.");
            return 1;
        }

        return ResolveAndExecute(command, globals, globals.RemainingArgs);
    }

    private static readonly System.Text.RegularExpressions.Regex FlagToken =
        new(@"^--[A-Za-z][A-Za-z0-9-]*(=.*)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Descends group commands to the leaf the args name, then returns the first
    /// --flag that leaf does not declare, or null. A value-taking flag's next
    /// token is its value and is never checked. Leaves without KnownFlags are
    /// not checked. A bare "-" (stdin marker) is not a --flag and is ignored.
    /// </summary>
    internal static string? FindUnknownFlag(ICommand command, string[] args)
    {
        var leaf = command;
        var i = 0;
        while (leaf is IGroupCommand group && i < args.Length
               && group.Subcommands.TryGetValue(args[i], out var sub))
        {
            leaf = sub;
            i++;
        }
        if (leaf.KnownFlags is not { } known) return null;

        for (; i < args.Length; i++)
        {
            var arg = args[i];
            if (!FlagToken.IsMatch(arg)) continue;
            var name = arg.Split('=', 2)[0];
            if (!known.TryGetValue(name, out var takesValue)) return arg;
            if (takesValue && !arg.Contains('=')) i++;
        }
        return null;
    }

    /// <summary>
    /// Resolve vault and execute a command. Extracted to share between
    /// normal dispatch and implicit capture fallback.
    /// </summary>
    private int ResolveAndExecute(ICommand command, GlobalArgs globals, string[] commandArgs)
    {
        var unknown = FindUnknownFlag(command, commandArgs);
        if (unknown is not null)
        {
            Console.Error.WriteLine($"{CliName} {command.Name}: unknown flag '{unknown}'");
            Console.Error.WriteLine($"Run '{CliName} help {command.Name}' for usage information.");
            return 64;
        }

        string? vaultPath = null;
        if (command.RequiresVault)
        {
            vaultPath = VaultResolver.Resolve(globals.VaultPath);
            if (string.IsNullOrEmpty(vaultPath))
            {
                Console.Error.WriteLine("Vault path required. Use --vault <path>, set LOGMARK_VAULT env var, or run LogMark app first.");
                return 1;
            }
            if (!Directory.Exists(vaultPath))
            {
                Console.Error.WriteLine($"Vault path does not exist: {vaultPath}");
                return 1;
            }
        }
        else
        {
            // Non-required vault commands might still use it if available
            vaultPath = VaultResolver.Resolve(globals.VaultPath);
        }

        var context = _contextFactory(vaultPath, globals.JsonOutput, globals.Quiet);

        try
        {
            return command.Execute(context, commandArgs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Determine if the command name + remaining args look like capture content
    /// rather than a mistyped command.
    /// </summary>
    public static bool LooksLikeCapture(string commandName, string[] remainingArgs)
    {
        // Starts with notation prefix (t:, i:, task:, etc.)
        var lower = commandName.ToLowerInvariant();
        foreach (var prefix in NotationPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        // Starts with route reference (+project)
        if (commandName.StartsWith('+'))
            return true;

        // Multi-word: has remaining args (lm deployed to prod)
        if (remainingArgs.Length > 0)
            return true;

        // Contains whitespace (quoted: lm "deployed to prod")
        if (commandName.Contains(' '))
            return true;

        // Single unknown word -- not capture (lm zzzzzzz)
        return false;
    }

    public void PrintHelp()
    {
        Console.WriteLine(ProductBanner());
        Console.WriteLine();
        Console.WriteLine($"Usage: {CliName} [options] <command> [args]");
        Console.WriteLine($"       {CliName} [options] <text>{new string(' ', 16 - CliName.Length)}(implicit capture)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --vault <path>     Vault path (or set LOGMARK_VAULT)");
        Console.WriteLine("  --json             Output as JSON");
        Console.WriteLine("  --quiet, -q        Suppress info messages");
        Console.WriteLine("  --help, -h         Show help");
        Console.WriteLine("  --help-json        Output full command tree as JSON");
        Console.WriteLine("  --version, -v      Show version");
        Console.WriteLine();
        Console.WriteLine("Commands:");

        // Calculate max name width for alignment
        var maxWidth = _registry.Commands.Values.Max(c => c.Name.Length);

        foreach (var cmd in _registry.Commands.Values.OrderBy(c => c.Name))
        {
            Console.WriteLine($"  {cmd.Name.PadRight(maxWidth + 2)}{cmd.Description}");
        }

        Console.WriteLine();
        Console.WriteLine($"Run '{CliName} help <command>' for command-specific help.");
    }

    private void PrintHelpJson()
    {
        var version = BuildInfo.DisplayVersion(typeof(Program).Assembly);
        var commands = new List<Dictionary<string, object?>>();

        foreach (var cmd in _registry.Commands.Values.OrderBy(c => c.Name))
        {
            var cmdObj = new Dictionary<string, object?>
            {
                ["name"] = cmd.Name,
                ["description"] = cmd.Description,
                ["usage"] = cmd.Usage,
                ["requires_vault"] = cmd.RequiresVault
            };

            if (cmd.DetailedHelp != null)
                cmdObj["detailed_help"] = cmd.DetailedHelp;

            if (cmd.Examples != null && cmd.Examples.Length > 0)
                cmdObj["examples"] = cmd.Examples;

            if (cmd is IGroupCommand group)
            {
                var subs = new List<Dictionary<string, object?>>();
                foreach (var sub in group.Subcommands.Values.OrderBy(s => s.Name))
                {
                    var subObj = new Dictionary<string, object?>
                    {
                        ["name"] = sub.Name,
                        ["description"] = sub.Description,
                        ["usage"] = sub.Usage,
                        ["requires_vault"] = sub.RequiresVault
                    };

                    if (sub.DetailedHelp != null)
                        subObj["detailed_help"] = sub.DetailedHelp;

                    if (sub.Examples != null && sub.Examples.Length > 0)
                        subObj["examples"] = sub.Examples;

                    subs.Add(subObj);
                }
                cmdObj["subcommands"] = subs;
            }

            commands.Add(cmdObj);
        }

        var result = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["commands"] = commands
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }

    /// <summary>
    /// The name the user typed: the unified open binary is `logmark`, the
    /// desktop sidecar CLI `lm` (same OwnerId marker vault init uses).
    /// </summary>
    public static string CliName => ConnectCommand.OwnerId == "oss-cli" ? "logmark" : "lm";

    private static bool IsHelpFlag(string arg) => arg is "--help" or "-h";

    private int PrintCommandHelp(string commandName, IEnumerable<string> path)
    {
        var command = _registry.Find(commandName);
        if (command == null)
        {
            Console.Error.WriteLine($"Unknown command: {commandName}");
            return 1;
        }

        return PrintCommandHelp(command, path);
    }

    /// <summary>
    /// Help for <paramref name="command"/>, descending into a group's
    /// subcommand for each leading token of <paramref name="path"/> that names
    /// one (`page write --help` shows page write, not the page group).
    /// </summary>
    private static int PrintCommandHelp(ICommand command, IEnumerable<string> path)
    {
        foreach (var token in path)
        {
            if (token.StartsWith('-') || command is not IGroupCommand parent
                || !parent.Subcommands.TryGetValue(token, out var sub))
                break;
            command = sub;
        }

        Console.WriteLine($"Usage: {CliName} {command.Usage}");
        Console.WriteLine();
        Console.WriteLine($"  {command.Description}");

        if (command.DetailedHelp != null)
        {
            Console.WriteLine();
            Console.WriteLine(command.DetailedHelp);
        }

        if (command is IGroupCommand group)
        {
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            var maxWidth = group.Subcommands.Values.Max(c => c.Name.Length);
            foreach (var sub in group.Subcommands.Values.OrderBy(c => c.Name))
            {
                Console.WriteLine($"  {sub.Name.PadRight(maxWidth + 2)}{sub.Description}");
            }
        }

        if (command.Examples != null && command.Examples.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in command.Examples)
            {
                // Examples are written for lm; show the binary the user ran.
                var shown = example.StartsWith("lm ") ? CliName + example[2..] : example;
                Console.WriteLine($"  {shown}");
            }
        }

        return 0;
    }

    private static void PrintVersion() => Console.WriteLine(ProductBanner());

    /// <summary>"LogMark Engine &lt;version&gt;" under the open build, else
    /// "LogMark CLI v&lt;version&gt;". Shared by --version and the help header.</summary>
    private static string ProductBanner()
    {
        var version = BuildInfo.DisplayVersion(typeof(Program).Assembly);
        return Commands.ConnectCommand.OwnerId == "oss-cli"
            ? $"{LogMark.Services.Providers.ProductNames.Engine} {version}"
            : $"LogMark CLI v{version}";
    }
}
