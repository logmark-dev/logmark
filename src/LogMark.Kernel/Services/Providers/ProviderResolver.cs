namespace LogMark.Services.Providers;

public sealed record ResolvedProvider(
    string Path, IReadOnlyList<string> PrefixArgs, ProviderManifest Manifest);

/// <summary>
/// Picks the binary serving a role among installed providers: live file,
/// largest capability set for the role ("full" > "kernel"), then highest
/// BinVersion, then most recently refreshed. Capability-based, never
/// edition-based: the desktop wins when present because it provides a
/// superset, not because it carries a rank.
/// </summary>
public sealed class ProviderResolver
{
    private readonly ProviderManifestStore _store;
    private readonly Func<string, bool> _fileExists;

    public ProviderResolver(ProviderManifestStore store, Func<string, bool>? fileExists = null)
    {
        _store = store;
        _fileExists = fileExists ?? File.Exists;
    }

    public ResolvedProvider? Resolve(string role) => Ranked(role).FirstOrDefault();

    /// <summary>
    /// Paths some manifest declares for <paramref name="role"/> that are not on
    /// disk. Ranked drops these silently, which is right for choosing a
    /// provider and wrong for explaining why there is none: a caller that says
    /// "nothing is registered" when a manifest names a since-deleted install
    /// sends the reader to a remedy that cannot work. Diagnosis only - never a
    /// selection input.
    /// </summary>
    public IReadOnlyList<string> DeclaredButMissing(string role) =>
        _store.LoadAll()
            .Where(m => m.Binaries.ContainsKey(role) && !_fileExists(m.Binaries[role].Path))
            .Select(m => m.Binaries[role].Path)
            .ToList();

    public IReadOnlyList<ResolvedProvider> Ranked(string role)
    {
        return _store.LoadAll()
            .Where(m => m.Binaries.ContainsKey(role) && _fileExists(m.Binaries[role].Path))
            .OrderByDescending(m => CapabilityRank(m, role))
            .ThenByDescending(m => m.BinVersion, SemVer.Comparer)
            .ThenByDescending(m => m.RefreshedAt)
            .Select(m => new ResolvedProvider(
                m.Binaries[role].Path,
                m.Binaries[role].PrefixArgs ?? Array.Empty<string>(),
                m))
            .ToList();
    }

    private static int CapabilityRank(ProviderManifest m, string role) => role switch
    {
        "mcp" => m.Capabilities.McpToolSet == "full" ? 1 : 0,
        "cli" => m.Capabilities.CliVerbSet == "full" ? 1 : 0,
        "serve" => m.Capabilities.Producers ? 1 : 0,
        _ => 0
    };
}
