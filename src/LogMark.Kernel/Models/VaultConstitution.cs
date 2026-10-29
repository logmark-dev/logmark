namespace LogMark.Models;

public sealed record VaultConstitution
{
    public string? VaultName { get; init; }
    public string? Purpose { get; init; }
    public ConstitutionScope? Scope { get; init; }
    public string? AgentsHelpBy { get; init; }
    public string? FrozenDecisions { get; init; }
    public string? DontSurface { get; init; }
    public string? Conventions { get; init; }
    public string? RawMarkdown { get; init; }

    /// <summary>The format version the manifest declares in its frontmatter (`schema: lm-vault/1.0.0`), or null. Advisory: surfaced, never enforced.</summary>
    public string? Schema { get; init; }

    public static VaultConstitution Empty { get; } = new();

    public bool IsEmpty =>
        VaultName is null && Purpose is null && Scope is null
        && AgentsHelpBy is null && FrozenDecisions is null && DontSurface is null
        && Conventions is null;
}

public sealed record ConstitutionScope
{
    public string? IncludeProse { get; init; }
    public string? ExcludeProse { get; init; }
    public IReadOnlyList<string> IncludedRoutes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedRoutes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> IncludedTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedTags { get; init; } = Array.Empty<string>();

    public bool HasAnyExtractedRefs =>
        IncludedRoutes.Count > 0 || ExcludedRoutes.Count > 0
        || IncludedTags.Count > 0 || ExcludedTags.Count > 0;
}
