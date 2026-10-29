namespace LogMark.CLI;

/// <summary>
/// A CLI command that can be registered and dispatched.
/// </summary>
public interface ICommand
{
    /// <summary>Command name used on the command line (e.g. "capture", "recent").</summary>
    string Name { get; }

    /// <summary>One-line description for help listing.</summary>
    string Description { get; }

    /// <summary>Usage string (e.g. "capture &lt;text&gt; [--type &lt;type&gt;]").</summary>
    string Usage { get; }

    /// <summary>Multi-line detailed help with parameter descriptions. Null = no detailed help.</summary>
    string? DetailedHelp => null;

    /// <summary>Usage examples shown in help output. Null = no examples.</summary>
    string[]? Examples => null;

    /// <summary>
    /// Long flags this command accepts (value: true when the flag takes an
    /// argument). Non-null opts the command into rejection: CommandRunner
    /// exits 64 on any other --flag. Every command that writes declares it.
    /// </summary>
    IReadOnlyDictionary<string, bool>? KnownFlags => null;

    /// <summary>False for help, version, vault init -- commands that work without a vault.</summary>
    bool RequiresVault { get; }

    /// <summary>Execute the command. Returns exit code (0 = success, 1 = user error, 2 = system error).</summary>
    int Execute(CliContext context, string[] args);
}

/// <summary>
/// A command that has subcommands (e.g. "vault init", "vault templates").
/// </summary>
public interface IGroupCommand : ICommand
{
    IReadOnlyDictionary<string, ICommand> Subcommands { get; }
}
