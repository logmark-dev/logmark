using System.Text;

namespace LogMark.Services.Routes;

/// <summary>
/// Projects a vault's routes.md to the routes that have published content (plus
/// their ancestors), strips `- private:` presets, and drops emptied sections.
/// Text-in/text-out so `- scopes:` blocks, section notes, and vault notes pass
/// through verbatim (the scope-preserving contract, matching RouteSetSerializer).
///
/// Handles both shapes the serializer produces: `### route` blocks grouped under
/// `## section` headers, and section-less "loose" `### route` blocks with no
/// header (Section is retired from the model; depth is the taxonomy). A `## `
/// header is emitted lazily, only when its first surviving route is kept, so
/// emptied sections vanish; loose routes are emitted directly.
///
/// Returns null when the source is blank or no route block survives projection,
/// so the caller falls back to entry-derived routes.
/// </summary>
public static class RoutesMarkdownProjector
{
    public static string? Project(string? routesMd, IReadOnlySet<string> publishedPaths)
    {
        if (string.IsNullOrWhiteSpace(routesMd))
            return null;

        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in publishedPaths)
        {
            var n = Normalize(p);
            if (n.Length > 0) published.Add(n);
        }

        var lines = routesMd.Replace("\r\n", "\n").Split('\n');

        var output = new StringBuilder();
        var keptAny = false;
        var started = false;                     // seen the first "## " or "### " yet

        // A "## " header seen but not yet emitted; emitted lazily before its first
        // kept route so emptied sections produce no header.
        string? pendingSectionHeader = null;
        var pendingSectionPre = new List<string>();
        var pendingEmitted = false;

        // Current route block
        var inRoute = false;
        var routeName = string.Empty;
        var routeBlock = new List<string>();

        void FlushRoute()
        {
            if (!inRoute) return;
            var path = RoutePathOf(routeBlock, routeName);
            if (ShouldKeep(path, published))
            {
                if (pendingSectionHeader != null && !pendingEmitted)
                {
                    output.Append(pendingSectionHeader).Append('\n');
                    foreach (var l in pendingSectionPre) output.Append(l).Append('\n');
                    pendingEmitted = true;
                }
                foreach (var l in routeBlock)
                {
                    if (IsPrivateLine(l)) continue;   // strip private presets
                    output.Append(l).Append('\n');
                }
                keptAny = true;
            }
            inRoute = false;
            routeBlock.Clear();
            routeName = string.Empty;
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushRoute();
                started = true;
                inRoute = true;
                routeName = trimmed.Substring(4).Trim();
                routeBlock.Add(line);
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushRoute();
                started = true;
                pendingSectionHeader = line;       // not emitted until a route under it is kept
                pendingSectionPre.Clear();
                pendingEmitted = false;
                continue;
            }

            if (inRoute) { routeBlock.Add(line); continue; }

            if (pendingSectionHeader != null && !pendingEmitted)
            {
                pendingSectionPre.Add(line);       // section prose before its first route
                continue;
            }

            // Vault-notes/header region (before any section or route), or a line
            // trailing a kept section's routes -- preserved verbatim in order.
            if (!started || pendingEmitted || pendingSectionHeader == null)
                output.Append(line).Append('\n');
        }

        FlushRoute();

        if (!keptAny)
            return null;

        return output.ToString().TrimEnd('\n') + "\n";
    }

    private static bool ShouldKeep(string path, HashSet<string> published)
    {
        if (path.Length == 0) return false;
        if (published.Contains(path)) return true;
        var prefix = path + "/";
        foreach (var p in published)
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string RoutePathOf(List<string> block, string routeName)
    {
        foreach (var l in block)
        {
            var t = l.TrimStart();
            if (t.StartsWith("- path:", StringComparison.OrdinalIgnoreCase))
                return Normalize(t.Substring("- path:".Length));
        }
        return Normalize(routeName);
    }

    private static bool IsPrivateLine(string line) =>
        line.TrimStart().StartsWith("- private:", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? p) =>
        (p ?? string.Empty).Replace('\\', '/').Trim().TrimStart('+').Trim('/').Trim();
}
