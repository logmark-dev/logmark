namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Resolves the constitution file for a vault (release review P0-6).
/// CommonVault names `vault.md` as the anchor artifact; wrapped vaults
/// keep content under `<vault>/logmark/`. `constitution.md` stays as a
/// legacy fallback for pre-CommonVault companion vaults.
/// </summary>
public static class ConstitutionLocator
{
    public static string Resolve(string vaultPath)
    {
        var root = VaultContentRoot.Resolve(vaultPath);
        var vaultMd = Path.Combine(root, "vault.md");
        if (File.Exists(vaultMd)) return vaultMd;
        var legacy = Path.Combine(root, "constitution.md");
        if (File.Exists(legacy)) return legacy;
        return vaultMd;
    }
}
