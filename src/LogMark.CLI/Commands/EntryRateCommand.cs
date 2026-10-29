using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.CLI.Commands;

public class EntryRateCommand : ICommand
{
    public string Name => "rate";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>();

    public string Description => "Set a rating on an entry";
    public string Usage => "entry rate <entry-id> <rating>";
    public bool RequiresVault => true;

    public string DetailedHelp =>
        "Sets a rating (0-5) on an entry identified by its ID.\n" +
        "Writes a rating: metadata field to the entry.\n\n" +
        "Parameters:\n" +
        "  <entry-id>   Entry ID (8-char hex ShortId, e.g., 757ef6ae)\n" +
        "  <rating>     Rating value from 0 to 5 (0 = unrated)";

    public string[] Examples => new[]
    {
        "lm entry rate 757ef6ae 4",
        "lm entry rate abc12345 0"
    };

    public int Execute(CliContext context, string[] args)
    {
        if (args.Length < 1)
        {
            context.WriteError("Missing required parameter: entry-id");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        if (args.Length < 2)
        {
            context.WriteError("Missing required parameter: rating");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var entryId = args[0];

        if (!int.TryParse(args[1], out var rating) || rating < 0 || rating > 5)
        {
            context.WriteError($"Invalid rating: {args[1]}. Must be 0-5.");
            return 1;
        }

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
            var result = EntrySerializer.SetMetadataField(markdown, entry.Timestamp, "rating", rating.ToString(), entry.ShortId);
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
                    ["rating"] = rating
                };
                var json = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                context.WriteResult(json);
            }
            else
            {
                if (modified)
                    context.WriteResult($"Set rating {rating} on entry {entryId}");
                else
                    context.WriteResult($"Entry {entryId} already has rating {rating}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            context.WriteError($"Failed to set rating: {ex.Message}");
            return 2;
        }
    }
}
