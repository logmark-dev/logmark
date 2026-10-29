namespace LogMark.Services.Providers;

/// <summary>
/// One installed LogMark distribution's self-description, written to
/// providers/&lt;ProviderId&gt;.json at install and refreshed at every startup.
/// Machine-local coordination state (not a public contract); schema evolves
/// additively only, guarded by ManifestVersion.
/// </summary>
public sealed record ProviderManifest(
    int ManifestVersion,
    string ProviderId,
    string BinVersion,
    IReadOnlyDictionary<string, ProviderBinary> Binaries,
    ProviderCapabilities Capabilities,
    IReadOnlyDictionary<string, int> Contracts,
    DateTimeOffset RefreshedAt,
    string? ActiveVault = null)
{
    public const int CurrentManifestVersion = 1;
}

/// <summary>A role binary: absolute path plus any prefix args the binary needs
/// to serve the role (the unified OSS binary serves "mcp" as `logmark mcp`).</summary>
public sealed record ProviderBinary(string Path, IReadOnlyList<string>? PrefixArgs = null);

/// <summary>Capability sets, never editions: "kernel" or "full".</summary>
public sealed record ProviderCapabilities(string McpToolSet, string CliVerbSet, bool Producers);
