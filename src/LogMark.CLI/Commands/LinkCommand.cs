using LogMark.Services;

namespace LogMark.CLI.Commands;

/// <summary>
/// `lm link [&lt;route&gt;]` - bind the current working directory to a vault route by
/// appending a `- workdir: &lt;cwd&gt;` line to that route's block in routes.md (or,
/// with no route argument, to the file header as a vault-level binding). The
/// hooks then scope vault context to the bound route based on the agent's cwd.
///
/// Uses a surgical text insert (not RouteSerializer, which is lossy) so all
/// existing routes.md content - scopes, prose, other fields - is preserved.
/// </summary>
public class LinkCommand : ICommand
{
    public string Name => "link";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>();

    public string Description => "Bind the current directory to a route (or the vault) for hook context scoping";
    public string Usage => "link [<route>]";
    public bool RequiresVault => true;

    public int Execute(CliContext context, string[] args)
    {
        var routeName = args.FirstOrDefault(a => !a.StartsWith("-"));
        var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());

        var routesPath = new RouteParser(context.VaultPath!).GetRoutesFilePath();
        var dir = Path.GetDirectoryName(routesPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var content = File.Exists(routesPath) ? File.ReadAllText(routesPath) : "# Routes\n";
        var lines = content.Split('\n').ToList();

        var (start, endExclusive) = routeName is null
            ? VaultHeaderBlock(lines)
            : RouteBlock(lines, routeName);

        if (routeName != null && start < 0)
        {
            context.WriteError($"Error: route '{routeName}' not found in {routesPath}");
            return 2;
        }

        var workdirLine = $"- workdir: {cwd}";
        // Idempotent: skip if this exact binding is already in the target block.
        for (var i = start; i < endExclusive; i++)
        {
            if (lines[i].Trim().Equals(workdirLine, StringComparison.OrdinalIgnoreCase))
            {
                context.WriteResult($"Already linked: {cwd}");
                return 0;
            }
        }

        // Insert at the end of the block (after its last non-empty line).
        var insertAt = endExclusive;
        while (insertAt - 1 >= start && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
            insertAt--;
        lines.Insert(insertAt, workdirLine);

        File.WriteAllText(routesPath, string.Join("\n", lines));
        context.WriteResult(routeName is null
            ? $"Linked vault to {cwd}"
            : $"Linked +{routeName} to {cwd}");
        return 0;
    }

    // The file header block: from after "# Routes" to the first "## " section
    // (or first "### " route, or EOF). Returns [start, endExclusive).
    private static (int start, int endExclusive) VaultHeaderBlock(List<string> lines)
    {
        var start = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("# ") && !lines[i].TrimStart().StartsWith("## "))
            {
                start = i + 1;
                break;
            }
        }
        for (var i = start; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("## ") || t.StartsWith("### "))
                return (start, i);
        }
        return (start, lines.Count);
    }

    // A route block: from the "### <name>" line to the next "### "/"## " or EOF.
    private static (int start, int endExclusive) RouteBlock(List<string> lines, string routeName)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("### ")
                && t.Substring(4).Trim().Equals(routeName, StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }
        if (start < 0) return (-1, -1);

        for (var i = start; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("### ") || t.StartsWith("## "))
                return (start, i);
        }
        return (start, lines.Count);
    }
}
