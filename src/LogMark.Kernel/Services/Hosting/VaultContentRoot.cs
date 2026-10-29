namespace LogMark.Services.Hosting;

/// <summary>
/// Resolves the vault content root: `&lt;vault&gt;/logmark` if that directory
/// exists, else `&lt;vault&gt;` itself. Wrapped vaults (CommonVault-style,
/// companion vaults sharing a root with other tools) keep LogMark content
/// under a `logmark/` subdirectory; flat vaults use the vault root directly.
/// Shared by <see cref="Http.ConstitutionLocator"/> and
/// <see cref="Cockpit.ConceptPageStore"/> so the rule lives in exactly one
/// place.
/// </summary>
public static class VaultContentRoot
{
    public static string Resolve(string vaultPath)
    {
        var wrapped = Path.Combine(vaultPath, "logmark");
        return Directory.Exists(wrapped) ? wrapped : vaultPath;
    }
}
