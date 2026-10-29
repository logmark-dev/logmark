using LogMark.Models;

namespace LogMark.Services.Lint;

/// <summary>
/// Coverage check between vault folders and configured routes. A folder is covered
/// iff some route's Path equals it, is its prefix (folder is descendant of a route),
/// or has it as a prefix (folder is a parent of a route).
/// Built once per scan from RouteService.GetRoutes(); consumed by OrphanedEntryLint.
/// </summary>
public class RouteCoverageIndex
{
    private readonly List<string> _normalizedRoutePaths;

    public RouteCoverageIndex(IEnumerable<Route> routes)
    {
        _normalizedRoutePaths = routes
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => Normalize(r.Path!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public bool IsCovered(string vaultRelativeFolder)
    {
        if (string.IsNullOrWhiteSpace(vaultRelativeFolder)) return false;
        var folder = Normalize(vaultRelativeFolder);

        foreach (var routePath in _normalizedRoutePaths)
        {
            if (routePath == folder) return true;
            if (folder.StartsWith(routePath + "/", StringComparison.Ordinal)) return true;
            if (routePath.StartsWith(folder + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').Trim('/').ToLowerInvariant();
}
