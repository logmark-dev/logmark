using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Groups a resolved ImportPlan BY SUGGESTED ROUTE (design Slice 1). Kept
/// separable from any renderer on purpose: this is the first nucleus-scoped
/// view (design 4g) and is shared by the plan surface and the post-import
/// report/event.
/// </summary>
public static class ImportPlanGrouping
{
    public static PlanGroups Group(ImportPlan plan)
    {
        var withFact = plan.Facts.Where(f => f.Fact != null).ToList();

        var routes = withFact
            .GroupBy(f => (Route: f.Resolution?.SuggestedRoute,
                           Confidence: f.Resolution?.ConfidenceValue ?? "none"))
            .Select(g => new RouteGroupSummary(
                g.Key.Route,
                g.Key.Confidence,
                g.Count(f => f.Class == FactClass.New
                    && OverlapPrior.FromSourceType(f.Fact!.SourceType) == OverlapPrior.Low),
                g.Count(f => f.Class == FactClass.New
                    && OverlapPrior.FromSourceType(f.Fact!.SourceType) == OverlapPrior.Candidate),
                g.Count()))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.SuggestedRoute, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = withFact
            .Where(f => f.Resolution is { Residual: ResidualKind.NewRouteCandidate })
            .GroupBy(f => Path.GetFileName(f.Resolution!.CandidateDirectory ?? "") ?? "")
            .Where(g => g.Key.Length > 0)
            .Select(g => new NewRouteCandidateSummary(g.Key, g.Count()))
            .OrderByDescending(c => c.FactCount)
            .ThenBy(c => c.Directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PlanGroups(routes, candidates);
    }
}
