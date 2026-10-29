namespace LogMark.CLI.Commands;

public class VersionCommand : ICommand
{
    public string Name => "version";
    public string Description => "Show version information";
    public string Usage => "version";
    public bool RequiresVault => false;

    public int Execute(CliContext context, string[] args)
    {
        var version = BuildInfo.DisplayVersion(typeof(Program).Assembly);
        Console.WriteLine(ConnectCommand.OwnerId == "oss-cli"
            ? $"{LogMark.Services.Providers.ProductNames.Engine} {version}"
            : $"LogMark CLI v{version}");
        return 0;
    }
}
