namespace LogMark.Models.AgentMemory;

/// <summary>
/// Which identity channel produced a destination suggestion (design 4c).
/// Sibling is reserved vocabulary: defined by the schema, emitted only when a
/// semantic-sibling tier exists (post-Slice-1).
/// </summary>
public enum RouteConfidence { Exact, Ancestry, Sibling, None }

/// <summary>Classification of an unroutable memory (design 4b tier 3).</summary>
public enum ResidualKind { None, NewRouteCandidate, Unresolved }

/// <summary>
/// Evidence bundle for destination resolution. The contract is nucleus-shaped
/// on purpose (design 4g): evidence in, (suggested nucleus, channel,
/// confidence) out. Slice 1 resolves on the encoded workdir folder only;
/// SourceType rides along for future channels.
/// </summary>
public record ResolutionEvidence(string? EncodedWorkdirFolder, string SourceType);

/// <summary>A resolved destination suggestion. Advisory only - never moves a file.</summary>
public record RouteResolution(
    string? SuggestedRoute,
    RouteConfidence Confidence,
    ResidualKind Residual = ResidualKind.None,
    string? CandidateDirectory = null)
{
    /// <summary>Frontmatter value: exact | ancestry | sibling | none.</summary>
    public string ConfidenceValue => Confidence.ToString().ToLowerInvariant();

    public static readonly RouteResolution Unresolved =
        new(null, RouteConfidence.None, ResidualKind.Unresolved);
}

/// <summary>
/// source_type -> overlap prior (design 4d stage 1). Deterministic and free:
/// only project-typed memories can echo session-distill output; feedback,
/// reference, user, and unknown are confident-new.
/// </summary>
public static class OverlapPrior
{
    public const string Low = "low";
    public const string Candidate = "candidate";

    public static string FromSourceType(string sourceType)
        => string.Equals(sourceType, "project", StringComparison.OrdinalIgnoreCase)
            ? Candidate
            : Low;
}

/// <summary>One destination group in a resolved plan. Null route = staging inbox only.</summary>
public record RouteGroupSummary(
    string? SuggestedRoute,
    string Confidence,              // exact | ancestry | sibling | none
    int NewConfident,               // New-class facts with overlap_prior low
    int NewOverlapCandidates,       // New-class facts with overlap_prior candidate
    int Total);

/// <summary>A real directory with agent memory but no vault route (design 4b tier 3).</summary>
public record NewRouteCandidateSummary(string Directory, int FactCount);

public record PlanGroups(
    IReadOnlyList<RouteGroupSummary> Routes,
    IReadOnlyList<NewRouteCandidateSummary> NewRouteCandidates);
