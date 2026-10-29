using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

public class RecentCommand : ICommand
{
    public string Name => "recent";
    public string Description => "Show recent entries";
    public string Usage => "recent [count] [--days <n>]";
    public bool RequiresVault => true;

    public string? DetailedHelp => "Shows recent entries from the vault, sorted by timestamp descending.\n\n" +
        "Parameters:\n" +
        "  [count]         Maximum number of entries to show (default: 50)\n" +
        "  --days <n>      Look back N days (default: 7)";

    public string[]? Examples => new[]
    {
        "lm recent",
        "lm recent 10",
        "lm recent --days 30"
    };

    public int Execute(CliContext context, string[] args)
    {
        int days = 7;
        int? limit = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--days" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var d))
                    days = d;
                i++;
            }
            else if (int.TryParse(args[i], out var count))
            {
                limit = count;
            }
        }

        context.WaitForCacheWarmup();

        var query = new SearchQuery { Limit = limit ?? 50 };
        var result = context.SearchService.GetRecent(days, query);

        Formatting.WriteResults(context, result);
        return 0;
    }
}
