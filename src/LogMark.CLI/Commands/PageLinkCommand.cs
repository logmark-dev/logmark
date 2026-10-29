using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.CLI.Commands;

public class PageLinkCommand : ICommand
{
    public string Name => "link";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--text"] = true,
    };

    public string Description => "Link an entry to a page via wiki link";
    public string Usage => "page link <entry-id> <wiki-path> [--text <display>]";
    public bool RequiresVault => true;

    public string DetailedHelp =>
        "Inserts a wiki link into an entry identified by its ID.\n" +
        "Uses scoped idempotency - won't duplicate an existing link to the same page.\n\n" +
        "Parameters:\n" +
        "  <entry-id>       Entry ID (8-char hex ShortId, e.g., 757ef6ae)\n" +
        "  <wiki-path>      Wiki path of the page to link to\n" +
        "  --text <display>  Optional display text for the wiki link";

    public string[] Examples => new[]
    {
        "lm page link 757ef6ae logmark/_inbox/_ideas/pages/2026-03-16-my-idea",
        "lm page link abc12345 logmark/projects/backend/_tasks/pages/2026-03-16-api-design --text \"API Design\""
    };

    public int Execute(CliContext context, string[] args)
    {
        // Parse args
        string? displayText = null;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--text" && i + 1 < args.Length)
            {
                displayText = args[i + 1];
                i++;
            }
            else if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 1)
        {
            context.WriteError("Missing required parameter: entry-id");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        if (positional.Count < 2)
        {
            context.WriteError("Missing required parameter: wiki-path");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var entryId = positional[0];
        var wikiPath = positional[1];
        displayText ??= wikiPath;

        // Find the entry via cache
        context.WaitForCacheWarmup();
        var resolved = context.EntryCache.ResolveShortId(entryId);

        if (resolved == null)
        {
            context.WriteError($"Entry not found with ID: {entryId}");
            return 1;
        }

        var (filePath, entry) = resolved.Value;

        try
        {
            var markdown = File.ReadAllText(filePath);
            var result = EntrySerializer.InsertWikiLink(
                markdown, entry.Timestamp, wikiPath, displayText, entry.ShortId);
            var modified = result != markdown;

            if (modified)
            {
                context.VaultWriter.WriteFile(filePath, result);
            }

            if (context.JsonOutput)
            {
                var jsonObj = new Dictionary<string, object?>
                {
                    ["modified"] = modified,
                    ["entry_id"] = entryId,
                    ["wiki_path"] = wikiPath
                };
                var json = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                context.WriteResult(json);
            }
            else
            {
                if (modified)
                    context.WriteResult($"Linked entry {entryId} to [[{wikiPath}|{displayText}]]");
                else
                    context.WriteResult($"Entry {entryId} already links to {wikiPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            context.WriteError($"Failed to link entry to page: {ex.Message}");
            return 2;
        }
    }
}
