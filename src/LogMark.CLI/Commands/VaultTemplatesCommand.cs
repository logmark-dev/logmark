using LogMark.Services;

namespace LogMark.CLI.Commands;

public class VaultTemplatesCommand : ICommand
{
    public string Name => "templates";
    public string Description => "List available vault bundles";
    public string Usage => "vault templates";
    public bool RequiresVault => false;

    public int Execute(CliContext context, string[] args)
    {
        var service = new BundleService();
        var bundles = service.GetBundles();
        // The open binary's `vault init` defaults to the "open" bundle; the
        // desktop CLI keeps BundleService's default ("build").
        var defaultId = ConnectCommand.OwnerId == "oss-cli"
            ? "open"
            : service.GetDefaultBundle().Id;

        Console.WriteLine("Available vault bundles:");
        Console.WriteLine();

        foreach (var bundle in bundles)
        {
            var defaultMarker = bundle.Id.Equals(defaultId, StringComparison.OrdinalIgnoreCase)
                ? " (default)"
                : "";
            Console.WriteLine($"  {bundle.Id,-15} {bundle.Name}{defaultMarker}");
            Console.WriteLine($"                  {bundle.Description}");
            Console.WriteLine();
        }

        return 0;
    }
}
