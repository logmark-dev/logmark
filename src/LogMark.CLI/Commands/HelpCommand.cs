namespace LogMark.CLI.Commands;

public class HelpCommand : ICommand
{
    private readonly CommandRunner _runner;

    public string Name => "help";
    public string Description => "Show help information";
    public string Usage => "help [command]";
    public bool RequiresVault => false;

    public HelpCommand(CommandRunner runner)
    {
        _runner = runner;
    }

    public int Execute(CliContext context, string[] args)
    {
        // Delegated to CommandRunner in the dispatch flow
        _runner.PrintHelp();
        return 0;
    }
}
