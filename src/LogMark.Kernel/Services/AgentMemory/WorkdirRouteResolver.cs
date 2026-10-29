using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Tiered destination router for agent memories (design 4b): exact workdir-key
/// match, then filesystem path-ancestry, then residual classification
/// (new-route-candidate vs unresolved). Deterministic, read-only, and total -
/// garbage config or evidence yields Unresolved, never an exception.
///
/// The contract is nucleus-shaped on purpose (design 4g): evidence bundle in,
/// (suggested route, channel, confidence) out - workdir matching is the first
/// channel implementation, not the interface.
///
/// The folder encoding is lossy ('-' vs path separator vs '.'), so we never
/// decode: real directories are encoded forward and matched, and ancestry is
/// confirmed by walking the real directory tree.
/// </summary>
public partial class WorkdirRouteResolver
{
    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();

    // Tier 1: encoded workdir key -> route path. First binding wins on the
    // (rare, misconfigured) case of two routes claiming one workdir.
    private readonly Dictionary<string, string> _exact;

    // Tier 2: (real workdir path, encoded key, route path), longest key first
    // so nested workdirs resolve to the most specific route.
    private readonly List<(string RealPath, string Key, string RoutePath)> _bindings;

    // Tier 3: existing parent dirs of all bound workdirs - where sibling
    // projects with no route would live.
    private readonly List<string> _roots;

    public WorkdirRouteResolver(IReadOnlyList<Route> routes)
    {
        _exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _bindings = new List<(string, string, string)>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.Path)) continue;
            foreach (var workdir in route.Workdirs)
            {
                string real;
                try { real = Normalize(workdir); }
                catch (Exception ex) when (IsPathError(ex)) { continue; }

                var key = Encode(real);
                _exact.TryAdd(key, route.Path);
                _bindings.Add((real, key, route.Path));

                try
                {
                    var parent = Path.GetDirectoryName(real);
                    if (parent != null && Directory.Exists(parent)) roots.Add(parent);
                }
                catch (Exception ex) when (IsPathError(ex)) { /* no root */ }
            }
        }

        _bindings.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        _roots = roots.ToList();
    }

    public RouteResolution Resolve(ResolutionEvidence evidence)
    {
        var folder = evidence.EncodedWorkdirFolder;
        if (string.IsNullOrWhiteSpace(folder)) return RouteResolution.Unresolved;

        if (_exact.TryGetValue(folder, out var routePath))
            return new RouteResolution(routePath, RouteConfidence.Exact);

        foreach (var (realPath, key, boundRoute) in _bindings)
        {
            if (!folder.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase)) continue;
            if (DescendantExists(realPath, folder))
                return new RouteResolution(boundRoute, RouteConfidence.Ancestry);
        }

        foreach (var root in _roots)
        {
            var candidate = FindChildByKey(root, folder);
            if (candidate != null)
                return new RouteResolution(null, RouteConfidence.None,
                    ResidualKind.NewRouteCandidate, candidate);
        }

        return RouteResolution.Unresolved;
    }

    /// <summary>Claude Code's cwd encoding: every non-alphanumeric char becomes '-'.</summary>
    public static string Encode(string realPath) => NonAlphanumeric().Replace(realPath, "-");

    private static string Normalize(string path)
        => Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsPathError(Exception ex)
        => ex is ArgumentException or NotSupportedException or PathTooLongException
            or IOException or UnauthorizedAccessException or System.Security.SecurityException;

    // Confirms a real descendant of start encodes exactly to target. At each
    // level, steps into the child whose encoding extends toward the target;
    // the LONGEST matching child encoding wins, so a child "Alpha.dev" beats
    // its sibling "Alpha" for target "...-Alpha-dev-...".
    private static bool DescendantExists(string start, string target)
    {
        var current = start;
        try
        {
            while (Directory.Exists(current))
            {
                string? next = null;
                var nextLen = -1;
                foreach (var child in Directory.EnumerateDirectories(current))
                {
                    var enc = Encode(TrimSeparators(child));
                    if (enc.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
                    if (target.StartsWith(enc + "-", StringComparison.OrdinalIgnoreCase)
                        && enc.Length > nextLen)
                    {
                        next = child;
                        nextLen = enc.Length;
                    }
                }
                if (next == null) return false;
                current = next;
            }
        }
        catch (Exception ex) when (IsPathError(ex)) { /* treat as no match */ }
        return false;
    }

    private static string? FindChildByKey(string root, string target)
    {
        try
        {
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                if (Encode(TrimSeparators(child)).Equals(target, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }
        catch (Exception ex) when (IsPathError(ex)) { /* no candidate */ }
        return null;
    }

    private static string TrimSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
