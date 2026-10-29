using LogMark.Services.Providers;

namespace LogMark.Services;

/// <summary>
/// Single vault-resolution policy shared by every OSS composition root (CLI,
/// MCP server; daemon `serve` defines the host role instead of resolving
/// against it). Order: explicit -> live-hosted ActiveVault from any provider
/// manifest -> bootstrapFallback -> null. A manifest's ActiveVault is only
/// trusted when <paramref name="isHosted"/> confirms a live holder still
/// backs it (a stale ActiveVault survives its process's exit) - this is what
/// keeps every surface pointed at whichever vault is actually being served,
/// instead of silently diverging from a live host.
/// </summary>
public static class VaultResolutionPolicy
{
    public static string? Resolve(string? explicitVault,
        ProviderManifestStore store, Func<string, bool> isHosted, Func<string?> bootstrapFallback)
    {
        if (!string.IsNullOrEmpty(explicitVault))
            return explicitVault;

        var hostedActiveVault = store.LoadAll()
            .Where(m => m.ActiveVault != null && isHosted(m.ActiveVault))
            .OrderByDescending(m => m.RefreshedAt)
            .Select(m => m.ActiveVault)
            .FirstOrDefault();

        if (hostedActiveVault != null)
            return hostedActiveVault;

        return bootstrapFallback();
    }
}
