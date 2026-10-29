namespace LogMark.Services.Vault;

/// <summary>
/// Resolves the <c>persona_context</c> string that gets interpolated into
/// inference prompts (monthly + weekly digest). The atom JSONs declare
/// <c>persona_context.source: "vault.persona"</c>; this resolver is the
/// other side of that contract.
///
/// Priority:
///   1. <c>vault.md</c> <c>Purpose</c> paragraph (user-authored, wins)
///   2. Bundle lookup in <c>persona-contexts.json</c>
///      (keyed on <c>BootstrapConfig.BundleId</c>)
///   3. <c>_default</c> from the same catalog
///
/// Reads <see cref="VaultConstitutionService.Current"/> on every resolve so
/// edits to vault.md (via NativeViewer's VaultMdEditor) take effect on the
/// next inference run without a service restart.
///
/// v0.18 QA C4 fix.
/// </summary>
public sealed class VaultPersonaResolver
{
    private readonly VaultConstitutionService? _constitution;
    private readonly PersonaContextCatalog _catalog;
    private readonly string? _bundleId;

    public VaultPersonaResolver(
        VaultConstitutionService? constitution,
        PersonaContextCatalog catalog,
        string? bundleId)
    {
        _constitution = constitution;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bundleId = bundleId;
    }

    public string Resolve()
    {
        var purpose = _constitution?.Current?.Purpose?.Trim();
        if (!string.IsNullOrWhiteSpace(purpose)) return purpose!;
        return _catalog.ForArchetype(_bundleId);
    }
}
