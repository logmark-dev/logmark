using LogMark.Exceptions;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.CLI.Commands;

public class CaptureCommand : ICommand
{
    public string Name => "capture";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--type"] = true, ["--file"] = true, ["--source"] = true,
    };

    public string Description => "Capture a new entry to the vault";
    public string Usage => "capture <text> [--type <type>] [--file <path>] [--source <name>] [-]";
    public bool RequiresVault => true;

    public string? DetailedHelp => "Content supports notation prefixes (t:, i:, b:, d:, q:, g:, a:),\n" +
        "route references (+project), and tags (#tag). Multi-line content\n" +
        "from --file or stdin auto-creates a linked subpage.\n\n" +
        "Options:\n" +
        "  --type <type>       Override entry type\n" +
        "  --file <path>       Read content from file\n" +
        "  -                   Read the body from stdin (piped content with inline text)\n" +
        "  --source <name>     Who wrote it, for an agent shelling out (e.g. agent:codex).\n" +
        "                      Omit when a person captures: no source means human.";

    public string[]? Examples => new[]
    {
        "lm capture \"t: fix auth bug +backend #urgent\"",
        "lm capture --file notes.md --type idea +research",
        "echo \"d: use PostgreSQL\" | lm capture"
    };

    // Testability hook: override to simulate stdin redirection in tests
    public static Func<bool> IsInputRedirected { get; set; } = () => Console.IsInputRedirected;

    public int Execute(CliContext context, string[] args)
    {
        // Parse args: collect non-flag args as content, parse --type and --file flags
        string? explicitType = null;
        string? filePath = null;
        string? explicitSource = null;
        var contentParts = new List<string>();
        var readStdin = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--type" && i + 1 < args.Length)
            {
                explicitType = args[i + 1];
                i++;
            }
            else if (args[i] == "--file" && i + 1 < args.Length)
            {
                filePath = args[i + 1];
                i++;
            }
            else if (args[i] == "--source" && i + 1 < args.Length)
            {
                explicitSource = args[i + 1];
                i++;
            }
            else if (args[i] == "-")
            {
                readStdin = true;
            }
            else if (!args[i].StartsWith("--"))
            {
                contentParts.Add(args[i]);
            }
        }

        // Read extended content from stdin pipe or --file
        string? extendedContent = null;

        if (filePath != null)
        {
            if (!File.Exists(filePath))
            {
                context.WriteError($"File not found: {filePath}");
                return 1;
            }
            extendedContent = File.ReadAllText(filePath);
        }
        // Stdin is read only when asked for (`-`) or when there is no inline
        // text at all. An agent or harness that leaves stdin open and never
        // closes it would otherwise block capture forever (rc5 C2).
        else if (IsInputRedirected() && (readStdin || contentParts.Count == 0))
        {
            extendedContent = Console.In.ReadToEnd();
        }

        // Must have either inline text or extended content
        if (contentParts.Count == 0 && string.IsNullOrWhiteSpace(extendedContent))
        {
            context.WriteError("No content provided.");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var inlineContent = string.Join(" ", contentParts);

        // Prepend explicit type prefix if given
        if (explicitType != null)
        {
            var normalized = explicitType.ToLowerInvariant().TrimEnd(':');
            if (!inlineContent.StartsWith($"{normalized}:", StringComparison.OrdinalIgnoreCase) &&
                (inlineContent.Length == 0 || !inlineContent.StartsWith($"{normalized[0]}:", StringComparison.OrdinalIgnoreCase)))
            {
                inlineContent = $"{normalized}: {inlineContent}";
            }
        }

        // If no inline text but have extended content, use first line as inline
        if (string.IsNullOrWhiteSpace(inlineContent) && !string.IsNullOrWhiteSpace(extendedContent))
        {
            var firstLine = extendedContent.Split('\n', 2)[0].Trim();
            inlineContent = firstLine;
            // Rest becomes the extended content (if multi-line)
            if (extendedContent.Contains('\n'))
            {
                var rest = extendedContent[(extendedContent.IndexOf('\n') + 1)..];
                extendedContent = rest;
            }
            else
            {
                extendedContent = null;
            }
        }

        var entry = context.NotationParser.Parse(inlineContent, "cli");
        // lm-vault spec 3.2: no source line means a human wrote it. A person
        // at the terminal is that human; an agent shelling out names itself
        // with --source (bead lmk-baex). The "| cli" header channel stays.
        entry.Source = string.IsNullOrWhiteSpace(explicitSource) ? null : explicitSource.Trim();
        entry.Timestamp = DateTime.Now;

        // Build PasteContext for large content (>2 lines triggers subpage)
        if (!string.IsNullOrWhiteSpace(extendedContent))
        {
            var lineCount = extendedContent.Split('\n').Length;
            if (lineCount > 2)
            {
                entry.PasteContext = new PasteContext
                {
                    PlainTextContent = extendedContent,
                    SubpageContent = extendedContent,
                    UserPrefix = inlineContent,
                    LineCount = lineCount,
                    CharacterCount = extendedContent.Length,
                    SourceFilePath = filePath
                };
            }
            else
            {
                // Short piped content: append inline
                entry.RawContent = $"{entry.RawContent}\n{extendedContent}".Trim();
                entry.ExpandedContent = entry.RawContent;
            }
        }

        // Only declared routes route. Any other +word - in the text or in short
        // piped content - is prose: kept verbatim, never creates a route.
        var (routedTokens, proseTokens) = context.RouteService.PartitionHeadlessRouteTokens(entry.Projects, null);
        foreach (var token in NotationParser.ExtractProjects(entry.RawContent))
        {
            if (!routedTokens.Contains(token, StringComparer.OrdinalIgnoreCase)
                && !proseTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                proseTokens.Add(token);
        }
        entry.Projects = routedTokens;
        entry.ProseRouteTokens = proseTokens;
        foreach (var token in proseTokens)
            Console.Error.WriteLine(
                $"Note: +{token} is not a declared route; kept as text. Declare routes in logmark/_system/routes.md.");

        string savedPath;
        LogMark.Models.Sensitivity.VaultWriteResult writeResult;
        try
        {
            (savedPath, writeResult) = context.VaultService.SaveEntryWithResultAsync(entry).GetAwaiter().GetResult();
        }
        catch (RoutePrivateDesktopOnlyException ex)
        {
            context.WriteError(
                $"Error: Route '{ex.RouteName}' is private and can only be captured from the LogMark desktop app. " +
                "Non-UI surfaces (CLI, MCP, Telegram) cannot write to private routes.");
            return 2;
        }
        catch (LicenseRequiredException)
        {
            context.WriteError("Error: Captures to private routes are not available through the CLI. Use the LogMark desktop app.");
            return 2;
        }

        // Warn when the write gate degrades gracefully: entry saved as plain text
        // to a private route because Pro is not active.
        if (writeResult.Unmasked && writeResult.UnmaskedSpans.Count > 0)
        {
            var routeName = entry.Projects.FirstOrDefault();
            var noun = writeResult.UnmaskedSpans.Count == 1 ? "sensitive value" : "sensitive values";
            Console.Error.WriteLine(
                $"Saved unmasked to +{routeName} ({writeResult.UnmaskedSpans.Count} {noun}; Pro required for shroud).");
        }

        // Record usage after successful save. RecordWrite is a no-op on the open
        // build (usage/quota tracking is a closed concern) and persists via the
        // usage store on the closed ProducerCliContext.
        try
        {
            var typeLabel = (entry.Type ?? Models.EntryType.Log).ToString().ToLowerInvariant();
            context.RecordWrite("cli", typeLabel, entry.Projects.FirstOrDefault());
        }
        catch { /* usage tracking is best-effort */ }

        if (context.JsonOutput)
        {
            var jsonObj = new Dictionary<string, object?>
            {
                ["path"] = savedPath,
                ["type"] = (entry.Type ?? EntryType.Log).ToString().ToLowerInvariant(),
                ["route"] = entry.Projects.FirstOrDefault(),
                ["hasSubpage"] = entry.PasteContext?.RequiresSubpage == true
            };
            var json = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
        }
        else
        {
            var typeLabel = (entry.Type ?? EntryType.Log).ToString().ToLowerInvariant();
            var subpageNote = entry.PasteContext?.RequiresSubpage == true ? " (with linked page)" : "";
            context.WriteResult($"Saved {typeLabel} to {savedPath}{subpageNote}");
        }

        return 0;
    }
}
