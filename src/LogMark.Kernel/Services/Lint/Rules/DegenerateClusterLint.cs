using System.Text.Json;
using LogMark.Services;

namespace LogMark.Services.Lint.Rules;

/// <summary>
/// Flags manual clusters in tags.md whose members collapse (via ClusterMemberNormalizer)
/// to fewer than 2 distinct canonical members: case-only duplicates, plural/punct
/// variants, and single-tag clusters. These add no grouping value -- leaf-indexing
/// and tag-rewrite already reconcile them. Semantic near-duplicates (e.g. block /
/// blocker) are NOT mechanical variants and are intentionally not flagged.
/// </summary>
public class DegenerateClusterLint : ILintRule
{
    public string Id => "degenerate-cluster";
    public string Category => "tags";
    public LintCostClass Cost => LintCostClass.Cheap;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var issues = new List<LintIssue>();
        foreach (var cluster in ctx.TagsFile.Parse())
        {
            ct.ThrowIfCancellationRequested();
            var collapsed = ClusterMemberNormalizer.CollapseVariants(cluster.Tags, cluster.CanonicalTag);
            if (collapsed.Count >= 2) continue;

            var description = collapsed.Count == 1
                ? $"Cluster '{cluster.Name}' groups only variant spellings of #{collapsed[0]}; leaf-indexing and tag-rewrite already reconcile these."
                : $"Cluster '{cluster.Name}' has no distinct tags; it can be removed.";
            var payload = JsonSerializer.Serialize(new { name = cluster.Name });
            var hash = LintHashing.Compute($"degenerate-cluster|{cluster.Name}");

            issues.Add(new LintIssue(
                Id: Guid.NewGuid().ToString(),
                RuleId: Id,
                Severity: LintSeverity.Suggest,
                SubjectRef: $"cluster:{cluster.Name}",
                Description: description,
                SuggestedFixVerbId: "remove-cluster",
                FixPayloadJson: payload,
                DismissalStateHash: hash,
                FirstSeenAt: DateTime.UtcNow,
                LastSeenAt: DateTime.UtcNow,
                Category: Category,
                Actions: new[]
                {
                    new LintAction($"Remove cluster '{cluster.Name}'", "remove-cluster", payload, Batchable: true),
                }));
        }
        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }
}
