using LogMark.Models;

namespace LogMark.Services.Routes;

/// <summary>Where a path sits: its route, that route's folder, and the scope below it.</summary>
public sealed record RouteLocation(string? Route, string? RoutePath, string? Scope)
{
    public static readonly RouteLocation None = new(null, null, null);
}

/// <summary>
/// The one read-side answer to "which route and scope does this path belong
/// to", built from routes.md. Writes place an entry through its route's
/// <c>path:</c>; this is the inverse, and every reader that derives a route
/// from a path should go through it.
///
/// Route: the declared route whose path is the longest segment-aligned prefix
/// of the given path, so a route nested in another route's folder
/// (<c>fin-inv</c> at <c>domains/finance/investing</c>) is not swallowed by the
/// parent. Undeclared <c>projects|domains|clients/X</c> falls back to route X.
/// Scope: the first folder below the route folder that is not a <c>_type</c>
/// folder. Never found by searching the path for the route name, which fails
/// whenever a route's name differs from its folder.
///
/// Paths are logmark-relative (<c>projects/x/_tasks/2026.md</c>), either
/// separator, case-insensitive. Design:
/// docs/plans/2026-09-28-route-scope-resolution-design.md.
/// </summary>
public sealed class RouteLocator
{
    private static readonly string[] ConventionCategories = { "projects", "domains", "clients" };

    public static readonly RouteLocator Empty = new(Array.Empty<Route>());

    // Longest path first; ties keep declaration order (first declared wins).
    private readonly List<(string Path, string Name)> _byLength;
    private readonly Dictionary<string, string> _pathByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _ancestors = new(StringComparer.OrdinalIgnoreCase);

    public RouteLocator(IEnumerable<Route> routes)
    {
        var declared = new List<(string Path, string Name)>();
        foreach (var route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.Path)) continue;
            var path = Normalize(route.Path);
            if (path.Length == 0) continue;
            declared.Add((path, route.Name));
            _pathByName.TryAdd(route.Name, path);
        }

        _byLength = declared
            .Select((d, i) => (d, i))
            .OrderByDescending(x => x.d.Path.Length)
            .ThenBy(x => x.i)
            .Select(x => x.d)
            .ToList();

        foreach (var (name, path) in _pathByName)
        {
            _ancestors[name] = _pathByName
                .Where(o => !o.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                            && IsUnder(path, o.Value))
                .OrderByDescending(o => o.Value.Length)
                .Select(o => o.Key)
                .ToList();
        }
    }

    /// <summary>Locate a logmark-relative file or directory path.</summary>
    public RouteLocation Locate(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return RouteLocation.None;
        var path = Normalize(relativePath);
        if (path.Length == 0 || path.StartsWith("..", StringComparison.Ordinal)) return RouteLocation.None;

        foreach (var (routePath, name) in _byLength)
        {
            if (path.Equals(routePath, StringComparison.OrdinalIgnoreCase) || IsUnder(path, routePath))
                return new RouteLocation(name, routePath, ScopeBelow(path, routePath));
        }

        var parts = path.Split('/');
        if (parts.Length >= 2 && ConventionCategories.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
        {
            var routePath = $"{parts[0]}/{parts[1]}";
            return new RouteLocation(parts[1], routePath, ScopeBelow(path, routePath));
        }

        return RouteLocation.None;
    }

    /// <summary>Declared routes whose folder contains this route's folder, nearest first.</summary>
    public IReadOnlyList<string> AncestorsOf(string? route) =>
        route != null && _ancestors.TryGetValue(route, out var a) ? a : Array.Empty<string>();

    /// <summary>Declared routes whose nearest ancestor is <paramref name="route"/>.</summary>
    public IReadOnlyList<string> ChildrenOf(string route) =>
        _ancestors
            .Where(kv => kv.Value.Count > 0 && kv.Value[0].Equals(route, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// True when an entry on <paramref name="entryRoute"/> belongs under a
    /// filter for <paramref name="filterRoute"/>: the same route, or one
    /// nested inside it. The one predicate route filters use.
    /// </summary>
    public bool IsWithin(string? entryRoute, string filterRoute)
    {
        if (string.IsNullOrEmpty(entryRoute)) return false;
        if (entryRoute.Equals(filterRoute, StringComparison.OrdinalIgnoreCase)) return true;
        return AncestorsOf(entryRoute).Contains(filterRoute, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The declared logmark-relative folder of a route, or null.</summary>
    public string? PathOf(string route) => _pathByName.TryGetValue(route, out var p) ? p : null;

    private static string? ScopeBelow(string path, string routePath)
    {
        if (path.Length <= routePath.Length + 1) return null;
        var rest = path[(routePath.Length + 1)..].Split('/');
        // A lone trailing segment is the file (or the folder itself), not a scope.
        if (rest.Length < 2) return null;
        return rest[0].StartsWith('_') ? null : rest[0];
    }

    private static bool IsUnder(string path, string folder) =>
        path.Length > folder.Length
        && path[folder.Length] == '/'
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
