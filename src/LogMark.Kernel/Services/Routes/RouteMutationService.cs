using System.IO;

namespace LogMark.Services.Routes;

/// <summary>
/// Outcome of a route mutation. Reason is non-null only when Success is false.
/// </summary>
public sealed record RouteMutationResult(bool Success, string? Reason = null)
{
    public static RouteMutationResult Ok() => new(true);
    public static RouteMutationResult Fail(string reason) => new(false, reason);
}

/// <summary>
/// The single write primitive for reshaping route depth: promote a larval
/// scope to a named route, collapse a route back to a scope, or rename/reparent
/// a route. Operates on routes.md as text (scope-preserving) plus a conditional
/// Directory.Move when the on-disk path changes. Atomic: a failed move rolls
/// back the routes.md edit.
/// </summary>
public sealed class RouteMutationService
{
    private readonly string _logmarkPath;
    private readonly string _routesFile;

    public RouteMutationService(string logmarkPath)
    {
        _logmarkPath = logmarkPath;
        _routesFile = Path.Combine(logmarkPath, "_system", "routes.md");
    }

    /// <summary>
    /// Promote a larval scope to a first-class route. Metadata-only: the scope's
    /// path (parentPath/scopeName) is identical whether expressed as a scope or a
    /// route, so no files move.
    /// </summary>
    public RouteMutationResult PromoteScope(string parentPath, string scopeName)
    {
        if (!File.Exists(_routesFile))
            return RouteMutationResult.Fail("routes.md not found");

        var content = File.ReadAllText(_routesFile);
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();

        var newRoutePath = $"{parentPath.Trim('/')}/{scopeName}";

        // Reject non-compliant names before they become folders on disk.
        var nameError = RouteName.Validate(newRoutePath);
        if (nameError != null)
            return RouteMutationResult.Fail(nameError);

        // Reject if a route with this path already exists.
        if (lines.Any(l => l.Trim().Equals($"- path: {newRoutePath}", StringComparison.OrdinalIgnoreCase)))
            return RouteMutationResult.Fail($"A route with path '{newRoutePath}' already exists");

        // Find the parent route block and its section.
        var parentSection = FindSectionForRoutePath(lines, parentPath.Trim('/'));

        // Remove the "  - scopeName:" line under the parent's scopes block (best effort).
        var scopeLineIdx = lines.FindIndex(l =>
            l.TrimStart().StartsWith($"- {scopeName}:", StringComparison.OrdinalIgnoreCase) &&
            l.StartsWith("  ")); // indented under - scopes:
        if (scopeLineIdx >= 0)
            lines.RemoveAt(scopeLineIdx);

        // Append the new route block under the parent's section (or at end if none).
        var block = new List<string> { "", $"### {scopeName}", $"- path: {newRoutePath}" };
        var insertIdx = SectionEndIndex(lines, parentSection);
        lines.InsertRange(insertIdx, block);

        var normalized = RouteSetSerializer.Normalize(string.Join("\n", lines));
        File.WriteAllText(_routesFile, normalized);
        return RouteMutationResult.Ok();
    }

    /// <summary>
    /// Collapse a leaf route into a larval scope under its parent. Metadata-only
    /// when the route's path already nests under the parent path. A route whose
    /// path has no parent segment is rejected (would require a file move; deferred).
    /// </summary>
    public RouteMutationResult CollapseRoute(string routePath)
    {
        var path = routePath.Trim('/');
        var slash = path.LastIndexOf('/');
        if (slash < 0)
            return RouteMutationResult.Fail("Route has no parent path; cannot collapse without a file move (not supported in v1)");

        var parentPath = path.Substring(0, slash);
        var scopeName = path.Substring(slash + 1);

        if (!File.Exists(_routesFile))
            return RouteMutationResult.Fail("routes.md not found");

        var lines = File.ReadAllText(_routesFile).Replace("\r\n", "\n").Split('\n').ToList();

        // Remove the "### {scopeName}" block whose "- path:" equals routePath.
        var blockStart = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimEnd().Equals($"### {scopeName}", StringComparison.OrdinalIgnoreCase))
            {
                // Confirm the following lines contain the matching path.
                for (int j = i + 1; j < lines.Count && !lines[j].TrimEnd().StartsWith("### ") && !lines[j].TrimEnd().StartsWith("## "); j++)
                {
                    if (lines[j].Trim().Equals($"- path: {path}", StringComparison.OrdinalIgnoreCase))
                    {
                        blockStart = i;
                        break;
                    }
                }
            }
            if (blockStart >= 0) break;
        }
        if (blockStart < 0)
            return RouteMutationResult.Fail($"Route '{routePath}' not found");

        var blockEnd = blockStart + 1;
        while (blockEnd < lines.Count &&
               !lines[blockEnd].TrimEnd().StartsWith("### ") &&
               !lines[blockEnd].TrimEnd().StartsWith("## "))
            blockEnd++;
        lines.RemoveRange(blockStart, blockEnd - blockStart);

        // Add scopeName as a scope under the parent route block.
        AddScopeLine(lines, parentPath, scopeName);

        var normalized = RouteSetSerializer.Normalize(string.Join("\n", lines));
        File.WriteAllText(_routesFile, normalized);
        return RouteMutationResult.Ok();
    }

    /// <summary>
    /// Rename or reparent a route: change its path and move its on-disk folder.
    /// Atomic -- if the Directory.Move fails, the routes.md edit is rolled back.
    /// Rejects when the destination folder already exists.
    /// </summary>
    public RouteMutationResult Reparent(string oldPath, string newPath)
    {
        var from = oldPath.Trim('/');
        var to = newPath.Trim('/');
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return RouteMutationResult.Ok(); // no-op

        // Reject non-compliant destination paths before any disk move.
        var nameError = RouteName.Validate(to);
        if (nameError != null)
            return RouteMutationResult.Fail(nameError);

        if (!File.Exists(_routesFile))
            return RouteMutationResult.Fail("routes.md not found");

        var fromDir = Path.Combine(_logmarkPath, Path.Combine(from.Split('/')));
        var toDir = Path.Combine(_logmarkPath, Path.Combine(to.Split('/')));

        if (Directory.Exists(toDir))
            return RouteMutationResult.Fail($"Destination folder '{to}' already exists");

        // Privacy-posture continuity guard: a reparent is allowed only when the
        // destination's inherited privacy preset equals the node's current
        // effective preset. Posture-changing moves are blocked in v1.
        var fromPreset = EffectivePrivatePreset(from);
        var toPreset = EffectivePrivatePreset(ParentOf(to));
        if (!string.Equals(fromPreset, toPreset, StringComparison.OrdinalIgnoreCase))
            return RouteMutationResult.Fail(
                $"Move changes privacy posture (from '{fromPreset ?? "none"}' to '{toPreset ?? "none"}'); blocked in v1");

        var original = File.ReadAllText(_routesFile);

        // Rewrite the path line in routes.md.
        var lines = original.Replace("\r\n", "\n").Split('\n').ToList();
        int idx = lines.FindIndex(l => l.Trim().Equals($"- path: {from}", StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return RouteMutationResult.Fail($"Route '{oldPath}' not found");
        lines[idx] = $"- path: {to}";
        var normalized = RouteSetSerializer.Normalize(string.Join("\n", lines));
        File.WriteAllText(_routesFile, normalized);

        // Move files (only if the source exists on disk).
        try
        {
            if (Directory.Exists(fromDir))
            {
                var parent = Directory.GetParent(toDir)?.FullName;
                if (parent != null) Directory.CreateDirectory(parent);
                Directory.Move(fromDir, toDir);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(_routesFile, original); // rollback metadata
            return RouteMutationResult.Fail($"File move failed, rolled back: {ex.Message}");
        }

        return RouteMutationResult.Ok();
    }

    /// <summary>The "## Section" header that owns the route whose "- path:" matches routePath, or null.</summary>
    private static string? FindSectionForRoutePath(List<string> lines, string routePath)
    {
        string? section = null;
        string? lastSection = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].TrimEnd();
            if (t.StartsWith("## "))
                lastSection = t.Substring(3).Trim();
            if (t.Trim().Equals($"- path: {routePath}", StringComparison.OrdinalIgnoreCase))
            {
                section = lastSection;
                break;
            }
        }
        return section;
    }

    /// <summary>Index just before the next "## " header after the named section, or end of list.</summary>
    private static int SectionEndIndex(List<string> lines, string? section)
    {
        if (section == null)
            return lines.Count;

        int start = lines.FindIndex(l => l.TrimEnd().Equals($"## {section}", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return lines.Count;

        for (int i = start + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimEnd().StartsWith("## "))
                return i;
        }
        return lines.Count;
    }

    /// <summary>Insert "  - scope:" under the parent route's "- scopes:" block, creating the block if absent.</summary>
    private static void AddScopeLine(List<string> lines, string parentPath, string scope)
    {
        // Find parent route's "- path:" line.
        int pathIdx = lines.FindIndex(l => l.Trim().Equals($"- path: {parentPath}", StringComparison.OrdinalIgnoreCase));
        if (pathIdx < 0)
            return; // parent not found; scope is dropped (entries still resolve via folder)

        // Find end of the parent block.
        int blockEnd = pathIdx + 1;
        while (blockEnd < lines.Count &&
               !lines[blockEnd].TrimEnd().StartsWith("### ") &&
               !lines[blockEnd].TrimEnd().StartsWith("## "))
            blockEnd++;

        // Is there an existing "- scopes:" line in the block?
        int scopesIdx = -1;
        for (int i = pathIdx + 1; i < blockEnd; i++)
        {
            if (lines[i].Trim().Equals("- scopes:", StringComparison.OrdinalIgnoreCase))
            {
                scopesIdx = i;
                break;
            }
        }

        if (scopesIdx >= 0)
            lines.Insert(scopesIdx + 1, $"  - {scope}:");
        else
            lines.InsertRange(blockEnd, new[] { "- scopes:", $"  - {scope}:" });
    }

    private static string? ParentOf(string path)
    {
        var p = path.Trim('/');
        var slash = p.LastIndexOf('/');
        return slash < 0 ? null : p.Substring(0, slash);
    }

    /// <summary>
    /// The effective private preset for a path: the preset declared on the
    /// nearest ancestor-or-self route in routes.md, or null when none is private.
    /// </summary>
    private string? EffectivePrivatePreset(string? path)
    {
        if (path == null) return null;
        if (!File.Exists(_routesFile)) return null;

        var lines = File.ReadAllText(_routesFile).Replace("\r\n", "\n").Split('\n');

        // Walk from the path up to its ancestors; first private wins.
        var current = path.Trim('/');
        while (true)
        {
            var preset = PrivatePresetForExactPath(lines, current);
            if (preset != null) return preset;
            var slash = current.LastIndexOf('/');
            if (slash < 0) break;
            current = current.Substring(0, slash);
        }
        return null;
    }

    /// <summary>The "- private: X" value in the route block whose "- path:" equals path, or null.</summary>
    private static string? PrivatePresetForExactPath(string[] lines, string path)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Equals($"- path: {path}", StringComparison.OrdinalIgnoreCase))
            {
                for (int j = i + 1; j < lines.Length && !lines[j].TrimEnd().StartsWith("### ") && !lines[j].TrimEnd().StartsWith("## "); j++)
                {
                    var t = lines[j].Trim();
                    if (t.StartsWith("- private:", StringComparison.OrdinalIgnoreCase))
                        return t.Substring("- private:".Length).Trim();
                }
                return null;
            }
        }
        return null;
    }
}
