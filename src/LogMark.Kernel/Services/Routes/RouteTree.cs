using LogMark.Models;

namespace LogMark.Services.Routes;

/// <summary>
/// Read model over a RouteConfig. Unifies declared routes and larval scopes
/// into a single tree of RouteNode, answering parent/child/leaf questions by
/// path. This is the single source the desktop browse layer consults; it
/// replaces depth-based role inference (PathClassifier) and the two-level
/// "category/route" assumption (RouteEnumerator.GetByCategory).
/// </summary>
public sealed class RouteTree
{
    private readonly List<RouteNode> _nodes;

    public RouteTree(RouteConfig? config)
    {
        _nodes = BuildNodes(config);
    }

    private static List<RouteNode> BuildNodes(RouteConfig? config)
    {
        var nodes = new List<RouteNode>();
        if (config?.Routes == null)
            return nodes;

        // A path can be claimed more than once: by a declared route, by a
        // same-path scope on its parent, by two hand-authored declared routes,
        // or by a synthetic grouping container (see EnsureAncestors). Without
        // dedup these surface as sibling nodes -- duplicate children and an
        // inflated tally. Collapse by normalized path here with a precedence:
        // declared route (2) > larval scope (1) > synthetic container (0). A
        // higher-rank node supersedes a lower-rank one in place; among equals
        // the first declaration wins. index maps path -> position in `nodes`.
        var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static int Rank(RouteNode n) => n.IsContainer ? 0 : (n.IsLarval ? 1 : 2);

        void AddOrSupersede(RouteNode node)
        {
            if (indexByPath.TryGetValue(node.Path, out var idx))
            {
                if (Rank(node) > Rank(nodes[idx]))
                    nodes[idx] = node;
                return;
            }
            indexByPath[node.Path] = nodes.Count;
            nodes.Add(node);
        }

        // Synthesize grouping containers for every implied ancestor of `path`
        // that no declared route or scope claims. Without this, a vault whose
        // routes are all declared at depth >= 2 (e.g. "projects/factory",
        // "domains/photography" with no bare "projects"/"domains" route) has no
        // top-level nodes, so GetChildren("") returns empty and the whole tree
        // renders blank. Ancestors are added top-down so group rows precede
        // their children in `nodes` order. A real declaration parsed later
        // supersedes the container via the Rank precedence above.
        void EnsureAncestors(string path)
        {
            var segments = path.Split('/');
            var acc = "";
            for (var i = 0; i < segments.Length - 1; i++)
            {
                acc = i == 0 ? segments[i] : $"{acc}/{segments[i]}";
                AddOrSupersede(new RouteNode
                {
                    Path = acc,
                    Name = LastSegment(acc),
                    IsLarval = false,
                    IsContainer = true,
                    Section = null,
                    Description = null
                });
            }
        }

        foreach (var route in config.Routes)
        {
            if (string.IsNullOrEmpty(route.Path))
                continue; // reference-only route, not a tree node

            var path = Normalize(route.Path);
            EnsureAncestors(path);
            AddOrSupersede(new RouteNode
            {
                Path = path,
                Name = LastSegment(path),
                IsLarval = false,
                Section = route.Section,
                Description = route.Description
            });

            // Larval scopes: each scope key is a child node under route.Path.
            foreach (var scope in route.Scopes.Keys)
            {
                var scopePath = Normalize($"{path}/{scope}");
                AddOrSupersede(new RouteNode
                {
                    Path = scopePath,
                    Name = LastSegment(scopePath),
                    IsLarval = true,
                    Section = route.Section,
                    Description = null
                });
            }
        }

        return nodes;
    }

    /// <summary>The node at an exact path, or null.</summary>
    public RouteNode? GetNode(string path)
    {
        var p = Normalize(path);
        return _nodes.FirstOrDefault(n =>
            string.Equals(n.Path, p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Direct children of a path (one segment deeper). Declared routes and larval scopes alike.</summary>
    public IReadOnlyList<RouteNode> GetChildren(string path)
    {
        var normalized = Normalize(path);

        // Root case: empty path returns the top-level nodes (those with no "/").
        if (string.IsNullOrEmpty(normalized))
            return _nodes.Where(n => !n.Path.Contains('/')).ToList();

        var prefix = normalized + "/";
        return _nodes
            .Where(n => n.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Path.Substring(prefix.Length).Contains('/'))
            .ToList();
    }

    public bool HasChildren(string path) => GetChildren(path).Count > 0;

    private static string Normalize(string path) =>
        path.Replace('\\', '/').Trim('/').ToLowerInvariant();

    private static string LastSegment(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path.Substring(idx + 1) : path;
    }
}
