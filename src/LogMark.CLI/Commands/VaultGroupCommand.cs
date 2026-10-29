namespace LogMark.CLI.Commands;

public class VaultGroupCommand : IGroupCommand
{
    public string Name => "vault";
    public string Description => "Vault management commands";
    public string Usage => "vault <subcommand> [options]";
    public bool RequiresVault => false;

    private readonly Dictionary<string, ICommand> _subcommands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Subcommands => _subcommands;

    public VaultGroupCommand()
    {
        var init = new VaultInitCommand();
        var templates = new VaultTemplatesCommand();
        _subcommands[init.Name] = init;
        _subcommands[templates.Name] = templates;
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

        Console.Error.WriteLine($"Unknown vault subcommand: {subName}");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        Console.WriteLine($"Usage: {CommandRunner.CliName} vault <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        foreach (var sub in _subcommands.Values.OrderBy(s => s.Name))
        {
            Console.WriteLine($"  {sub.Name,-15} {sub.Description}");
        }
    }
}
