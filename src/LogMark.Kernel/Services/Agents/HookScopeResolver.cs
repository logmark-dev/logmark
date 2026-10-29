using LogMark.Models;

namespace LogMark.Services.Agents;

/// <summary>What scope a hook should use for the agent's working directory.</summary>
public enum HookScopeKind { Route, WholeVault, Unbound }

/// <summary>Result of resolving a working directory against route bindings.</summary>
public sealed record HookScopeResult(HookScopeKind Kind, string? Route);

/// <summary>
/// Resolves the agent's working directory (cwd) to a vault scope using the
/// filesystem bindings declared on routes (Route.Workdirs) and at the vault
/// level (RouteConfig.VaultWorkdirs). Pure, no I/O.
///
/// - cwd under some route's workdir  -> Route (the most specific / longest match)
/// - else cwd under a vault workdir  -> WholeVault (project-dedicated-vault root)
/// - else                            -> Unbound
/// </summary>
public static class HookScopeResolver
{
    public static HookScopeResult Resolve(
        string? cwd, IReadOnlyList<Route> routes, IReadOnlyList<string> vaultWorkdirs)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return new HookScopeResult(HookScopeKind.Unbound, null);

        var normalizedCwd = Normalize(cwd);

        string? bestRoute = null;
        var bestLen = -1;
        foreach (var route in routes)
        {
            foreach (var workdir in route.Workdirs)
            {
                var nw = Normalize(workdir);
                if (IsAncestorOrEqual(nw, normalizedCwd) && nw.Length > bestLen)
                {
                    bestLen = nw.Length;
                    bestRoute = route.Name;
                }
            }
        }

        if (bestRoute != null)
            return new HookScopeResult(HookScopeKind.Route, bestRoute);

        foreach (var vaultWorkdir in vaultWorkdirs)
        {
            if (IsAncestorOrEqual(Normalize(vaultWorkdir), normalizedCwd))
                return new HookScopeResult(HookScopeKind.WholeVault, null);
        }

        return new HookScopeResult(HookScopeKind.Unbound, null);
    }

    // Full path, normalized separators, no trailing separator.
    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    // True when candidate equals dir, or dir is inside candidate at a segment
    // boundary (so C:\myproj does not match C:\myproject).
    private static bool IsAncestorOrEqual(string candidate, string dir)
    {
        if (string.Equals(candidate, dir, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = candidate + Path.DirectorySeparatorChar;
        return dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
