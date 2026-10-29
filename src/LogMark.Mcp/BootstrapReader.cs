using System.Text.Json;

namespace LogMark.Mcp;

/// <summary>
/// Reads vault path from the desktop app's bootstrap.json.
/// </summary>
public static class BootstrapReader
{
    public static string GetDefaultBootstrapPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if DEBUG
            "LogMark-Debug"
#else
            "LogMark"
#endif
        );
        return Path.Combine(appDataPath, "bootstrap.json");
    }

    /// <summary>
    /// Resolves the bootstrap path to read, honoring the wiring contract that a
    /// null OR empty/whitespace --bootstrap-path means "follow the active vault"
    /// (use the default APPDATA bootstrap). The on-disk config writers emit
    /// ["--bootstrap-path", ""] in FollowActive mode, so an empty string must
    /// fall through to the default rather than being treated as a literal path.
    /// </summary>
    public static string ResolveBootstrapPath(string? overridePath)
        => string.IsNullOrWhiteSpace(overridePath) ? GetDefaultBootstrapPath() : overridePath;

    public static string? ReadVaultPath(string bootstrapPath)
    {
        if (!File.Exists(bootstrapPath))
            return null;

        try
        {
            var json = File.ReadAllText(bootstrapPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("VaultPath", out var vaultPathElement)
                && vaultPathElement.ValueKind == JsonValueKind.String)
            {
                var path = vaultPathElement.GetString();
                return !string.IsNullOrEmpty(path) ? path : null;
            }
        }
        catch (Exception)
        {
            // Corrupt or locked file
        }

        return null;
    }

    // NOTE: this class used to expose a static bootstrap-token reader here
    // (a bootstrap.json read of the paid-tier installation token). Its only
    // caller was the closed McpProducer (LogMark.Mcp.Producers) -- read but
    // unused in OSS builds. It now lives as a private helper on McpProducer
    // itself, behind the producer seam, so the OSS-published open tree never
    // touches this secret. See
    // OpenBuildContractTests.Open_tree_sources_do_not_read_licensing_tokens
    // (LogMark.CLI.Producers.Tests).
}
