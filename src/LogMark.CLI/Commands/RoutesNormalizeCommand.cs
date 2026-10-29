using LogMark.Services.Routes;

namespace LogMark.CLI.Commands;

public class RoutesNormalizeCommand : ICommand
{
    public string Name => "routes-normalize";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>();

    public string Description => "Normalize and deduplicate routes.md (one-shot repair)";
    public string Usage => "routes-normalize";
    public bool RequiresVault => true;

    public string[]? Examples => new[]
    {
        "lm routes-normalize",
        "lm --vault /path/to/vault routes-normalize"
    };

    public int Execute(CliContext context, string[] args)
    {
        var routesPath = Path.Combine(context.VaultPath!, "logmark", "_system", "routes.md");
        if (!File.Exists(routesPath))
        {
            Console.Error.WriteLine($"routes.md not found at {routesPath}");
            return 1;
        }

        var content = File.ReadAllText(routesPath);
        var normalized = RouteSetSerializer.Normalize(content);

        if (normalized == content)
        {
            Console.WriteLine("routes.md already canonical - nothing to do");
            return 0;
        }

        File.WriteAllText(routesPath, normalized);
        Console.WriteLine($"normalized routes.md ({content.Length} -> {normalized.Length} bytes)");
        return 0;
    }
}
