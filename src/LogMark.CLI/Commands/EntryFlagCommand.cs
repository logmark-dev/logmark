using System.Text.RegularExpressions;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.CLI.Commands;

public partial class EntryFlagCommand : ICommand
{
    public string Name => "flag";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>();

    public string Description => "Append a flag to an entry";
    public string Usage => "entry flag <entry-id> <flag-type> [note]";
    public bool RequiresVault => true;

    public string DetailedHelp =>
        "Appends a flag to an entry's comma-separated flags: metadata field.\n" +
        "If the flag already exists, no change is made.\n\n" +
        "Parameters:\n" +
        "  <entry-id>    Entry ID (8-char hex ShortId, e.g., 757ef6ae)\n" +
        "  <flag-type>   Flag value (lowercase alphanumeric with hyphens)\n" +
        "  [note]        Optional note (not stored in metadata)";

    public string[] Examples => new[]
    {
        "lm entry flag 757ef6ae review-needed",
        "lm entry flag abc12345 important \"High priority item\""
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
            context.WriteError("Missing required parameter: flag-type");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var entryId = args[0];
        var flagType = args[1];

        if (!FlagTypePattern().IsMatch(flagType))
        {
            context.WriteError($"Invalid flag-type: '{flagType}'. Must match ^[a-z0-9-]+$ (lowercase alphanumeric with hyphens).");
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
            // Read current flags from ExtendedMetadata
            var currentFlags = entry.ExtendedMetadata.TryGetValue("flags", out var existingFlags)
                ? existingFlags
                : "";

            // Parse existing flags into a list
            var flagList = string.IsNullOrWhiteSpace(currentFlags)
                ? new List<string>()
                : currentFlags.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();

            // Check if flag already present
            if (flagList.Contains(flagType, StringComparer.OrdinalIgnoreCase))
            {
                if (context.JsonOutput)
                {
                    var jsonObj = new Dictionary<string, object?>
                    {
                        ["modified"] = false,
                        ["entry_id"] = entryId,
                        ["flags"] = currentFlags
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    context.WriteResult(json);
                }
                else
                {
                    context.WriteResult($"Entry {entryId} already has flag '{flagType}'");
                }
                return 0;
            }

            // Append the new flag
            flagList.Add(flagType);
            var updatedFlags = string.Join(", ", flagList);

            var markdown = File.ReadAllText(filePath);
            var result = EntrySerializer.SetMetadataField(markdown, entry.Timestamp, "flags", updatedFlags, entry.ShortId);
            var modified = result != markdown;

            if (modified)
            {
                context.VaultWriter.WriteFile(filePath, result);
            }

            if (context.JsonOutput)
            {
                var flagJsonObj = new Dictionary<string, object?>
                {
                    ["modified"] = modified,
                    ["entry_id"] = entryId,
                    ["flags"] = updatedFlags
                };
                var flagJson = System.Text.Json.JsonSerializer.Serialize(flagJsonObj,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                context.WriteResult(flagJson);
            }
            else
            {
                context.WriteResult($"Added flag '{flagType}' to entry {entryId}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            context.WriteError($"Failed to flag entry: {ex.Message}");
            return 2;
        }
    }

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex FlagTypePattern();
}
