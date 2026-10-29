namespace LogMark.Services.Vault;

using System.Reflection;
using System.Text.Json;

/// <summary>
/// Loads persona-context phrases keyed by vault bundle id.
/// Source: <c>LogMark.Core/Resources/Behaviors/persona-contexts.json</c>
/// (bundle ids + <c>_default</c>). Used by <see cref="VaultPersonaResolver"/>
/// as the fallback when the user has not authored a vault.md <c>Purpose</c>.
/// </summary>
public sealed class PersonaContextCatalog
{
    internal const string ResourceName = "LogMark.Resources.Behaviors.persona-contexts.json";
    private const string DefaultKey = "_default";
    private const string FallbackDefault = "knowledge worker tracking projects and ideas";

    private readonly Dictionary<string, string> _byBundle;
    private readonly string _default;

    /// <summary>
    /// Load the embedded persona catalog. Returns an empty-shaped catalog
    /// (just <c>_default</c>) if the resource is missing or unparseable.
    /// </summary>
    public static PersonaContextCatalog Load()
    {
        var assembly = typeof(PersonaContextCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null) return new PersonaContextCatalog(new Dictionary<string, string>());
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
            return new PersonaContextCatalog(dict);
        }
        catch (JsonException)
        {
            return new PersonaContextCatalog(new Dictionary<string, string>());
        }
    }

    public PersonaContextCatalog(Dictionary<string, string> byBundle)
    {
        _byBundle = new Dictionary<string, string>(byBundle, StringComparer.OrdinalIgnoreCase);
        _default = _byBundle.TryGetValue(DefaultKey, out var d) && !string.IsNullOrWhiteSpace(d)
            ? d
            : FallbackDefault;
    }

    public string Default => _default;

    /// <summary>
    /// Resolve the persona context for a bundle id (e.g.
    /// <c>"build"</c>, <c>"research"</c>). Unknown or null ids fall
    /// back to <see cref="Default"/>.
    /// </summary>
    public string ForArchetype(string? archetypeId)
    {
        if (string.IsNullOrWhiteSpace(archetypeId)) return _default;
        return _byBundle.TryGetValue(archetypeId, out var ctx) && !string.IsNullOrWhiteSpace(ctx)
            ? ctx
            : _default;
    }
}
