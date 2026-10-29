namespace LogMark.CLI.Commands;

public class LintGroupCommand : IGroupCommand
{
    public string Name => "lint";
    public string Description => "Vault hygiene: scan and fix tag/route/entry issues";
    public string Usage => "lint <subcommand> [options]";
    public bool RequiresVault => true;

    private readonly Dictionary<string, ICommand> _subcommands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Subcommands => _subcommands;

    public LintGroupCommand()
    {
        var scan = new LintScanCommand();
        var fix = new LintFixCommand();
        _subcommands[scan.Name] = scan;
        _subcommands[fix.Name] = fix;
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

        Console.Error.WriteLine($"Unknown lint subcommand: {subName}");
        PrintUsage();
        return 1;
    }

    private void PrintUsage()
    {
        Console.WriteLine($"Usage: {CommandRunner.CliName} lint <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        foreach (var sub in _subcommands.Values.OrderBy(s => s.Name))
            Console.WriteLine($"  {sub.Name,-15} {sub.Description}");
    }
}
