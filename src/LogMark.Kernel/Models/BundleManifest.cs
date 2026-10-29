namespace LogMark.Models;

/// <summary>
/// Manifest for a vault bundle. Loaded from bundles/&lt;id&gt;/bundle.yaml.
/// Posture is hidden from the user; it drives future drift detection and
/// fallback persona context.
/// </summary>
public class BundleManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Posture { get; set; } = "";
    public bool SubjectRequired { get; set; }
    public string? SubjectPrompt { get; set; }
    public bool SeedContentDefault { get; set; } = true;

    private static readonly HashSet<string> ValidPostures = new(StringComparer.OrdinalIgnoreCase)
    {
        "tracking",
        "learning",
        "building",
        "reflecting",
        "reference"
    };

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Id)) return false;
        if (string.IsNullOrWhiteSpace(Name)) return false;
        if (string.IsNullOrWhiteSpace(Description)) return false;
        if (!ValidPostures.Contains(Posture)) return false;
        if (SubjectRequired && string.IsNullOrWhiteSpace(SubjectPrompt)) return false;
        return true;
    }
}
