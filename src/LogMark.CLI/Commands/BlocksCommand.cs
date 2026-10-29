using LogMark.Models;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

public class BlocksCommand : ICommand
{
    public string Name => "blocks";
    public string Description => "List block entries";
    public string Usage => "blocks [--route <name>] [--limit <n>]";
    public bool RequiresVault => true;

    public string? DetailedHelp => "Lists block entries (things you are stuck on) from the vault.\n\n" +
        "Options:\n" +
        "  --route <name>   Filter by route\n" +
        "  --limit <n>      Show up to n blocks (default 50)";

    public string[]? Examples => new[]
    {
        "lm blocks",
        "lm blocks --route backend"
    };

    public int Execute(CliContext context, string[] args)
    {
        string? route = null;
        int? limit = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--route" && i + 1 < args.Length)
            {
                route = args[i + 1];
                i++;
            }
            else if (args[i] == "--limit" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var l) && l > 0)
                    limit = l;
                i++;
            }
        }

        context.WaitForCacheWarmup();

        var query = new SearchQuery
        {
            Types = new[] { EntryType.Block },
            // Open excludes completed, #resolved and #dismissed blocks.
            Lifecycle = LifecycleFilter.Open,
            Route = route
        };

        if (limit is int n)
            query = query with { Limit = n };

        var result = context.SearchService.Search(query);
        Formatting.WriteResults(context, result);
        return 0;
    }
}
