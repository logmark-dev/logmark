namespace LogMark.CLI.Commands;

public class EntryGroupCommand : IGroupCommand
{
    public string Name => "entry";
    public string Description => "Entry mutation commands";
    public string Usage => "entry <subcommand> [options]";
    public bool RequiresVault => false;

    private readonly Dictionary<string, ICommand> _subcommands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Subcommands => _subcommands;

    public EntryGroupCommand()
    {
        var rate = new EntryRateCommand();
        var flag = new EntryFlagCommand();
        var mintAnchors = new EntriesMintAnchorsCommand();
        _subcommands[rate.Name] = rate;
        _subcommands[flag.Name] = flag;
        _subcommands[mintAnchors.Name] = mintAnchors;
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

        Console.Error.WriteLine($"Unknown entry subcommand: {subName}");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        Console.WriteLine($"Usage: {CommandRunner.CliName} entry <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        foreach (var sub in _subcommands.Values.OrderBy(s => s.Name))
        {
            Console.WriteLine($"  {sub.Name,-15} {sub.Description}");
        }
    }
}
