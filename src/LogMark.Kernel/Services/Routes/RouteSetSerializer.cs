using System.Text;

namespace LogMark.Services.Routes;

/// <summary>
/// Parses routes.md into a route set, deduplicates by route name (keeping first
/// occurrence with its scope list also deduplicated), and re-serializes
/// deterministically. Idempotent.
/// </summary>
public static class RouteSetSerializer
{
    public static string Normalize(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder();
        var seenRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // We build by walking sections (## Projects / ## Domains) and routes (### name).
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.StartsWith("### "))
            {
                var routeName = line[4..].Trim();
                // Find end of this route block (next ### or ## or EOF).
                var j = i + 1;
                while (j < lines.Length && !lines[j].StartsWith("### ") && !lines[j].StartsWith("## "))
                    j++;
                if (seenRoutes.Add(routeName))
                {
                    var block = lines[i..j];
                    var deduped = DedupScopeEntries(block);
                    foreach (var b in deduped) output.Append(b).Append('\n');
                }
                i = j;
            }
            else
            {
                output.Append(line).Append('\n');
                i++;
            }
        }
        // Trim trailing extra newlines and re-normalize to a single trailing '\n'.
        return output.ToString().TrimEnd('\n', '\r') + "\n";
    }

    private static IEnumerable<string> DedupScopeEntries(IEnumerable<string> blockLines)
    {
        var seenScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inScopes = false;
        foreach (var line in blockLines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- scopes:"))
            {
                inScopes = true;
                yield return line;
                continue;
            }
            if (inScopes && trimmed.StartsWith("- "))
            {
                if (seenScopes.Add(trimmed)) yield return line;
                continue;
            }
            // Any non-list line ends the scopes section.
            if (inScopes && !trimmed.StartsWith("- ") && !string.IsNullOrWhiteSpace(line))
                inScopes = false;
            yield return line;
        }
    }
}
