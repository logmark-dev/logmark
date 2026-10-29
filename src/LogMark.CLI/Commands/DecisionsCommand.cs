using LogMark.Models;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

public class DecisionsCommand : ICommand
{
    public string Name => "decisions";
    public string Description => "List decision entries";
    public string Usage => "decisions [--route <name>] [--days <n>]";
    public bool RequiresVault => true;

    public string? DetailedHelp => "Lists decision entries from the vault.\n\n" +
        "Options:\n" +
        "  --route <name>   Filter by route\n" +
        "  --days <n>       Only show decisions from the last N days";

    public string[]? Examples => new[]
    {
        "lm decisions",
        "lm decisions --days 30",
        "lm decisions --route backend"
    };

    public int Execute(CliContext context, string[] args)
    {
        string? route = null;
        int? days = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--route" && i + 1 < args.Length)
            {
                route = args[i + 1];
                i++;
            }
            else if (args[i] == "--days" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var d))
                    days = d;
                i++;
            }
        }

        context.WaitForCacheWarmup();

        var query = new SearchQuery
        {
            Types = new[] { EntryType.Decision },
            Route = route,
            After = days.HasValue ? DateTime.Today.AddDays(-days.Value) : null
        };

        var result = context.SearchService.Search(query);
        Formatting.WriteResults(context, result);
        return 0;
    }
}
