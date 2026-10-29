using LogMark.Interfaces;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

public class SearchCommand : ICommand
{
    /// <summary>
    /// Semantic search needs the closed build's embedding model. The open
    /// build never has one, so it offers no --semantic flag at all rather
    /// than one that always fails (bead lmk-9hkn).
    /// </summary>
    private readonly bool _semantic;

    public SearchCommand(bool semanticSearch = false)
    {
        _semantic = semanticSearch;
        var flags = new Dictionary<string, bool> { ["--route"] = true, ["--type"] = true, ["--limit"] = true };
        if (semanticSearch) flags["--semantic"] = false;
        KnownFlags = flags;
    }

    public string Name => "search";
    public string Description => "Search entries in the vault";
    public string Usage => "search <query> [--route <name>] [--type <type>] [--limit <n>]" + (_semantic ? " [--semantic]" : "");
    public bool RequiresVault => true;
    public IReadOnlyDictionary<string, bool>? KnownFlags { get; }

    public string? DetailedHelp => "Searches entries by text, route, and type. Supports tags (#tag),\n" +
        "route references (+project), and free-text queries.\n\n" +
        (_semantic
            ? "When text search returns 1 or 2 results and T1 embeddings are available,\n" +
              "semantic search runs automatically and results are shown in a separate section.\n" +
              "A no-match query returns a clean zero; use --semantic for meaning-based search.\n\n"
            : "A no-match query returns a clean zero.\n\n") +
        "Options:\n" +
        "  --route <name>   Filter by route\n" +
        "  --type <type>    Filter by entry type (task, idea, block, decision, log)\n" +
        "  --limit <n>      Maximum results (default: 50)" +
        (_semantic ? "\n  --semantic       Force semantic-only search (requires T1 embeddings)" : "");

    public string[]? Examples => _semantic
        ? new[]
        {
            "lm search \"auth bug\"",
            "lm search --route backend --type task",
            "lm search #urgent --limit 10",
            "lm search --semantic \"similar to caching approach\""
        }
        : new[]
        {
            "lm search \"auth bug\"",
            "lm search --route backend --type task",
            "lm search #urgent --limit 10"
        };

    /// <summary>
    /// Threshold below which semantic search is automatically attempted as a supplement.
    /// </summary>
    internal const int SemanticFallbackThreshold = 3;

    public int Execute(CliContext context, string[] args)
    {
        string? route = null;
        string? type = null;
        int limit = 50;
        bool semanticOnly = false;
        var queryParts = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--route" && i + 1 < args.Length)
            {
                route = args[i + 1];
                i++;
            }
            else if (args[i] == "--type" && i + 1 < args.Length)
            {
                type = args[i + 1];
                i++;
            }
            else if (args[i] == "--limit" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var l))
                    limit = l;
                i++;
            }
            else if (args[i] == "--semantic")
            {
                semanticOnly = true;
            }
            else if (!args[i].StartsWith("--"))
            {
                queryParts.Add(args[i]);
            }
        }

        if (queryParts.Count == 0)
        {
            context.WriteError("No search query provided.");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        // Route any +route token embedded in the free-text query into the
        // structured Route filter, mirroring MCP's `route` param. Route tokens are
        // stripped from stored content by FileRouter.StripNotation, so a substring
        // text match for "+photonotes" can never hit - it must become a Route
        // filter. An explicit --route flag wins over an embedded token. (#tag
        // tokens are left in the text on purpose: unlike +route, tags survive in
        // stored content and match as substrings.) (bead lmk-k20h)
        if (route == null)
        {
            for (int i = queryParts.Count - 1; i >= 0; i--)
            {
                var part = queryParts[i];
                if (part.StartsWith('+') && NotationParser.IsValidProjectToken(part[1..]))
                {
                    route = part[1..];
                    queryParts.RemoveAt(i);
                }
            }
        }

        var queryText = string.Join(" ", queryParts);

        context.WaitForCacheWarmup();

        // Semantic-only mode
        if (semanticOnly)
        {
            return ExecuteSemanticOnly(context, queryText, limit);
        }

        // Normal text search
        var query = new SearchQuery
        {
            Text = queryText,
            Route = route,
            Types = ParseTypes(type),
            Limit = limit
        };

        var result = context.SearchService.Search(query);
        Formatting.WriteResults(context, result);

        // Semantic supplement: only when the lexical query produced at least one
        // hit but fewer than the threshold. A no-match lexical query (exact-token
        // miss, e.g. "zzqxv") returns a clean zero - matching the MCP search path
        // (TotalCount:0, no semantic noise). A route-only query has no free text to
        // embed, so it is skipped too. Use --semantic for explicit meaning-based
        // search on a zero-hit query. (bead lmk-k20h)
        if (result.TotalCount > 0 &&
            result.TotalCount < SemanticFallbackThreshold &&
            !string.IsNullOrWhiteSpace(queryText))
        {
            AppendSemanticResults(context, queryText, limit, result);
        }

        // Supplement with long-form subpage/page content: subpages parse to zero
        // entries and are absent from the entry index. Skip when a --type filter is
        // set (subpages are untyped). A separate section, so a no-match query still
        // shows a clean zero (bead lmk-6asy).
        if (type == null)
        {
            AppendPageMatches(context, queryText, route, limit);
        }

        return 0;
    }

    /// <summary>
    /// Supplement search with long-form subpage/page content that parses to zero
    /// entries and never enters the entry index (bead lmk-6asy).
    /// </summary>
    private static void AppendPageMatches(CliContext context, string queryText, string? route, int limit)
    {
        var pages = LogMark.Services.Search.SubpageSearch.Find(context.VaultPath, queryText, route, limit);
        if (pages.Count == 0)
            return;

        if (context.JsonOutput)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                pageMatches = pages.Select(p => new
                {
                    title = p.Title,
                    path = p.FilePath,
                    route = p.Route,
                    preview = TruncateContent(p.SearchText, 70)
                }),
                total = pages.Count
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
            return;
        }

        context.WriteResult("");
        context.WriteResult("Page matches:");
        foreach (var p in pages)
        {
            var routeCol = p.Route != null ? $"+{p.Route}" : "";
            var title = TruncateContent(p.Title, 70);
            context.WriteResult($"  [p] {routeCol.PadRight(12)} {title}");
        }
    }

    /// <summary>
    /// Run semantic-only search via --semantic flag.
    /// </summary>
    private static int ExecuteSemanticOnly(CliContext context, string queryText, int limit)
    {
        var semanticService = context.SemanticSearchService;
        if (semanticService == null)
        {
            context.WriteError("Semantic search unavailable (T1 embeddings model not found).");
            return 1;
        }

        var results = semanticService.SearchAsync(queryText, limit).GetAwaiter().GetResult();
        if (results.Count == 0)
        {
            context.WriteInfo("No semantic matches found.");
            return 0;
        }

        WriteSemanticResults(context, results);
        return 0;
    }

    /// <summary>
    /// When text search returned few results, try semantic search and append results.
    /// </summary>
    private static void AppendSemanticResults(
        CliContext context, string queryText, int limit, SearchResult textResult)
    {
        var semanticService = context.SemanticSearchService;
        if (semanticService == null)
            return;

        var semanticResults = semanticService.SearchAsync(queryText, limit).GetAwaiter().GetResult();
        if (semanticResults.Count == 0)
            return;

        // Deduplicate: exclude entries already shown in text results
        var textEntryIds = new HashSet<string>(
            textResult.Entries
                .Where(e => e.Entry.ShortId != null)
                .Select(e => e.Entry.ShortId!),
            StringComparer.OrdinalIgnoreCase);

        var novel = semanticResults
            .Where(r => !textEntryIds.Contains(r.EntryId))
            .ToList();

        if (novel.Count == 0)
            return;

        context.WriteResult("");
        context.WriteResult("Semantic matches:");
        WriteSemanticResults(context, novel);
    }

    /// <summary>
    /// Write semantic search results to output.
    /// </summary>
    private static void WriteSemanticResults(CliContext context, IReadOnlyList<SemanticSearchResult> results)
    {
        if (context.JsonOutput)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                semanticResults = results.Select(r => new
                {
                    entryId = r.EntryId,
                    route = r.Route,
                    score = r.Score,
                    content = r.Content
                }),
                total = results.Count
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
        }
        else
        {
            foreach (var r in results)
            {
                var routeCol = r.Route != null ? $"+{r.Route}" : "";
                var content = TruncateContent(r.Content, 70);
                var score = r.Score.ToString("F2");
                context.WriteResult($"  [~] {score}  {routeCol.PadRight(12)} {content}");
            }
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var firstLine = content.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= maxLength)
            return firstLine;
        return firstLine[..(maxLength - 3)] + "...";
    }

    private static IReadOnlyList<LogMark.Models.EntryType>? ParseTypes(string? type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        if (Enum.TryParse<LogMark.Models.EntryType>(type, ignoreCase: true, out var parsed))
            return new[] { parsed };
        return null;
    }
}
