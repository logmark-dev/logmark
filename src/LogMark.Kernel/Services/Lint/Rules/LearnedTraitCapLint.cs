using LogMark.Services.Alignment;

namespace LogMark.Services.Lint.Rules;

/// <summary>
/// Only the TraitPromptSelection of confirmed session-distill traits reaches
/// the distill prompt: at most TraitFormat.PromptCap, most evidence first,
/// within the ~600-token block budget. When any confirmed trait is left out
/// - by the count cap OR the char budget - ask the user to consolidate or
/// retire (alignment-loop-v1 design sec 5.2; final review F4). Inert without
/// traits, so it needs no release gate of its own.
/// </summary>
public class LearnedTraitCapLint : ILintRule
{
    public string Id => "learned-trait-cap";
    public string Category => "alignment";
    public LintCostClass Cost => LintCostClass.Cheap;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var confirmed = TraitReader.ReadConfirmed(ctx.Entries.GetEntriesForRoute(TraitFormat.RouteName), TraitPromptSelection.Scope);
        var selected = TraitPromptSelection.Select(confirmed).Count;
        if (confirmed.Count <= selected) return Task.FromResult<IReadOnlyList<LintIssue>>(Array.Empty<LintIssue>());
        var issue = new LintIssue(
            Id: Guid.NewGuid().ToString(), RuleId: Id, Severity: LintSeverity.Suggest, SubjectRef: "+" + TraitFormat.RouteName,
            Description: $"{confirmed.Count} confirmed session-distill traits; only {selected} reach the distill prompt " +
                         $"(cap {TraitFormat.PromptCap} / ~{TraitPromptSelection.TraitBlockCharBudget / 4} tokens). Consolidate or retire some.",
            SuggestedFixVerbId: null, FixPayloadJson: null,
            DismissalStateHash: LintHashing.Compute($"traits:{confirmed.Count}:selected:{selected}"),
            FirstSeenAt: DateTime.UtcNow, LastSeenAt: DateTime.UtcNow);
        return Task.FromResult<IReadOnlyList<LintIssue>>(new[] { issue });
    }
}
