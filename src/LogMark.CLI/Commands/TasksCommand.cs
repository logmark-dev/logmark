using LogMark.Models;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

public class TasksCommand : ICommand
{
    public string Name => "tasks";
    public string Description => "List pending tasks";
    public string Usage => "tasks [--overdue] [--route <name>] [--limit <n>]";
    public bool RequiresVault => true;

    public string? DetailedHelp => "Lists pending (incomplete) tasks from the vault.\n\n" +
        "Options:\n" +
        "  --overdue        Show only tasks past their deadline\n" +
        "  --route <name>   Filter by route\n" +
        "  --limit <n>      Show up to n tasks (default 50)";

    public string[]? Examples => new[]
    {
        "lm tasks",
        "lm tasks --overdue",
        "lm tasks --route backend"
    };

    public int Execute(CliContext context, string[] args)
    {
        bool overdue = false;
        string? route = null;
        int? limit = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--overdue")
            {
                overdue = true;
            }
            else if (args[i] == "--route" && i + 1 < args.Length)
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
            Types = new[] { EntryType.Task },
            IsCompleted = false,
            // Dismissed tasks are closed but carry no strikethrough, so
            // IsCompleted alone lists them as pending (lmk-cli-tasks-lists-dismissed-gdcc).
            Lifecycle = LifecycleFilter.Open,
            Route = route,
            Sort = overdue ? SearchSort.DeadlineAsc : SearchSort.TimestampDesc
        };

        if (limit is int n)
            query = query with { Limit = n };

        if (overdue)
        {
            query = query with { Before = DateTime.Now };
            // Overdue tasks: have deadlines before now
            // We filter further after search since SearchQuery doesn't have deadline-specific filter
        }

        var result = context.SearchService.Search(query);

        if (overdue)
        {
            // Post-filter: only entries with deadlines that have passed
            var overdueEntries = result.Entries
                .Where(ie => ie.Entry.TaskMeta?.Deadline != null && ie.Entry.TaskMeta.Deadline < DateTime.Now)
                .ToList();
            result = new SearchResult(overdueEntries, overdueEntries.Count);
        }

        Formatting.WriteResults(context, result);
        return 0;
    }
}
