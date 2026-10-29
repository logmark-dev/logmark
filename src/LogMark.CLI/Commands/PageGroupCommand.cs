namespace LogMark.CLI.Commands;

public class PageGroupCommand : IGroupCommand
{
    public string Name => "page";
    public string Description => "Page management commands";
    public string Usage => "page <subcommand> [options]";
    public bool RequiresVault => false;

    private readonly Dictionary<string, ICommand> _subcommands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Subcommands => _subcommands;

    public PageGroupCommand()
    {
        var write = new PageWriteCommand();
        var link = new PageLinkCommand();
        _subcommands[write.Name] = write;
        _subcommands[link.Name] = link;
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

        Console.Error.WriteLine($"Unknown page subcommand: {subName}");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        Console.WriteLine($"Usage: {CommandRunner.CliName} page <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        foreach (var sub in _subcommands.Values.OrderBy(s => s.Name))
        {
            Console.WriteLine($"  {sub.Name,-15} {sub.Description}");
        }
    }
}
