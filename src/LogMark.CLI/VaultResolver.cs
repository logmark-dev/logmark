using System.Text.Json;
using LogMark.Services;
using LogMark.Services.Agents;
using LogMark.Services.Hosting;
using LogMark.Services.Providers;

namespace LogMark.CLI;

/// <summary>
/// Resolves vault path from 3 tiers:
/// 1. --vault flag
/// 2. LOGMARK_VAULT env var
/// 3. VaultResolutionPolicy: a live-hosted provider's ActiveVault, else
///    bootstrap.json in the build-isolated AppData folder
///    (%APPDATA%\LogMark-Debug in Debug, %APPDATA%\LogMark in Release)
/// </summary>
public static class VaultResolver
{
    public static string? Resolve(string? flagValue)
    {
        // Tier 1: explicit flag
        if (!string.IsNullOrEmpty(flagValue))
            return Path.GetFullPath(flagValue);

        // Tier 2: environment variable
        var envValue = Environment.GetEnvironmentVariable("LOGMARK_VAULT");
        if (!string.IsNullOrEmpty(envValue))
            return envValue;

        // Tier 3: live host's ActiveVault (any provider manifest currently
        // hosting one), falling back to bootstrap.json in AppData. Never
        // silently diverges from a live host - see VaultResolutionPolicy.
        return VaultResolutionPolicy.Resolve(
            explicitVault: null,
            new ProviderManifestStore(ProviderManifestStore.DefaultRoot()),
            VaultHostLock.IsCurrentlyHosted,
            () => ReadBootstrap());
    }

    /// <summary>
    /// The vault an agent's MCP server reaches through an existing entry,
    /// resolved in the server's own order (WiringOwnership.EntryVault):
    /// --vault-override, the live host's ActiveVault, the entry's bootstrap
    /// file, then bootstrap.json. Excludes --vault and LOGMARK_VAULT, which
    /// belong to this process, not to the agent's MCP server.
    /// </summary>
    /// <param name="appBootstrap">Test seam for the app bootstrap read.</param>
    public static string? ResolveEntryVault(McpEntryInfo info, Func<string?>? appBootstrap = null) =>
        WiringOwnership.EntryVault(info,
            liveHostVault: () => VaultResolutionPolicy.Resolve(
                explicitVault: null,
                new ProviderManifestStore(ProviderManifestStore.DefaultRoot()),
                VaultHostLock.IsCurrentlyHosted,
                () => null),
            appBootstrapVault: appBootstrap ?? ReadBootstrap);

    private static string? ReadBootstrap()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // Match the build-isolated folder the desktop app writes and the
        // installation-token reader uses; VaultResolver historically hardcoded
        // "LogMark", which diverged from the Debug build's "LogMark-Debug".
#if DEBUG
        var folder = "LogMark-Debug";
#else
        var folder = "LogMark";
#endif
        var bootstrapPath = Path.Combine(appData, folder, "bootstrap.json");

        if (!File.Exists(bootstrapPath))
            return null;

        try
        {
            return ParseVaultPath(File.ReadAllText(bootstrapPath));
        }
        catch
        {
            // Corrupt bootstrap -- treat as missing
            return null;
        }
    }

    /// <summary>
    /// Reads the vault path from bootstrap.json content. Matches the property
    /// name case-insensitively: the desktop app serializes PascalCase
    /// ("VaultPath"), so a case-sensitive lookup for "vaultPath" silently
    /// failed and the CLI never resolved the vault from bootstrap.
    /// </summary>
    internal static string? ParseVaultPath(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(prop.Name, "vaultPath", StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
            {
                var path = prop.Value.GetString();
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
        }
        return null;
    }
}
