using LogMark.Services.Pages;

namespace LogMark.CLI.Commands;

public class PageWriteCommand : ICommand
{
    public string Name => "write";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--route"] = true, ["--file"] = true,
    };

    public string Description => "Write a markdown page to the vault";
    public string Usage => "page write [--route <route>] [--file <path>] <type> <slug>";
    public bool RequiresVault => true;

    public string DetailedHelp =>
        "Writes a markdown page to the vault pages directory.\n" +
        "Content is read from --file or stdin. The page is saved as {today}-{slug}.md\n" +
        "under the appropriate pages folder based on route and type.\n\n" +
        "Parameters:\n" +
        "  <type>          Entry type folder name (e.g., ideas, tasks, diary)\n" +
        "  <slug>          URL-safe slug for filename\n" +
        "  --route <route> Optional route path (e.g., projects/backend)\n" +
        "  --file <path>   Read content from file instead of stdin\n" +
        "  --json          Output as JSON with wiki_path";

    public string[] Examples => new[]
    {
        "echo '# My Idea' | lm page write ideas my-idea",
        "lm page write --file draft.md --route projects/backend tasks api-design",
        "lm page write --json ideas weekly-summary < content.md"
    };

    // Testability hook: override to simulate stdin redirection in tests
    public static Func<bool> IsInputRedirected { get; set; } = () => Console.IsInputRedirected;

    public int Execute(CliContext context, string[] args)
    {
        // Parse args
        string? route = null;
        string? filePath = null;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--route" && i + 1 < args.Length)
            {
                route = args[i + 1];
                i++;
            }
            else if (args[i] == "--file" && i + 1 < args.Length)
            {
                filePath = args[i + 1];
                i++;
            }
            else if (!args[i].StartsWith("--"))
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count < 1)
        {
            context.WriteError("Missing required parameter: type");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        if (positional.Count < 2)
        {
            context.WriteError("Missing required parameter: slug");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        var typeName = positional[0];
        var slug = positional[1];

        // Read content from --file or stdin
        string? content = null;

        if (filePath != null)
        {
            if (!File.Exists(filePath))
            {
                context.WriteError($"File not found: {filePath}");
                return 1;
            }
            content = File.ReadAllText(filePath);
        }
        else if (IsInputRedirected())
        {
            content = Console.In.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            context.WriteError("No content provided. Pipe content via stdin or use --file.");
            context.WriteError($"Usage: lm {Usage}");
            return 1;
        }

        // Generate filename: {today}-{slug}.md
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var filename = $"{today}-{slug}.md";

        // Write via SubpageWriter. The resolver turns a route alias into the
        // route's configured folder path; without it `--route quant` would
        // create logmark/quant/ next to the real project folder.
        var writer = new SubpageWriter(
            context.VaultPath!, context.VaultWriter, context.RouteService.ResolveRouteFolderPath);
        try
        {
            writer.WritePage(route, typeName, filename, content);
        }
        catch (Exception ex)
        {
            context.WriteError($"Failed to write page: {ex.Message}");
            return 2;
        }

        // Build result paths
        var fileNameWithoutExtension = $"{today}-{slug}";
        var wikiPath = writer.GetPageWikiPathFor(route, typeName, fileNameWithoutExtension);

        if (context.JsonOutput)
        {
            var jsonObj = new Dictionary<string, object?>
            {
                ["wiki_path"] = wikiPath,
                ["type"] = typeName,
                ["slug"] = slug,
                ["route"] = route
            };
            var json = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
        }
        else
        {
            var routeNote = route != null ? $" (route: {route})" : "";
            context.WriteResult($"Wrote page to {wikiPath}{routeNote}");
        }

        return 0;
    }
}
