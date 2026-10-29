namespace LogMark.Services.Lint.Rules;

/// <summary>
/// A scope a route declares must not also be another route's folder. Nesting
/// is implicit in routes.md (a route whose path sits inside another route's
/// folder), and the two readings collide: entries in that folder resolve to
/// the nested route, so the parent's declared scope never holds anything.
/// </summary>
public class ScopeRouteOverlapLint : ILintRule
{
    public string Id => "scope-route-overlap";
    public string Category => "routes";
    public LintCostClass Cost => LintCostClass.Cheap;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var routes = ctx.Routes.GetRoutes().Where(r => !string.IsNullOrWhiteSpace(r.Path)).ToList();
        var routeByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in routes)
            routeByPath.TryAdd(Normalize(r.Path!), r.Name);

        var issues = new List<LintIssue>();
        foreach (var route in routes)
        {
            foreach (var scope in route.Scopes.Keys)
            {
                ct.ThrowIfCancellationRequested();
                var scopeFolder = $"{Normalize(route.Path!)}/{Normalize(scope)}";
                if (!routeByPath.TryGetValue(scopeFolder, out var other)) continue;

                var subject = $"route:{route.Name}/{scope}";
                issues.Add(new LintIssue(
                    Id: Guid.NewGuid().ToString(),
                    RuleId: Id,
                    Severity: LintSeverity.Warn,
                    SubjectRef: subject,
                    Description:
                        $"Scope '{scope}' of route '{route.Name}' is also the folder of route '{other}' " +
                        $"({scopeFolder}); entries there read as '{other}'. Remove the scope or the route.",
                    SuggestedFixVerbId: null,
                    FixPayloadJson: null,
                    DismissalStateHash: LintHashing.Compute($"{subject}->{other}"),
                    FirstSeenAt: DateTime.UtcNow,
                    LastSeenAt: DateTime.UtcNow));
            }
        }
        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
