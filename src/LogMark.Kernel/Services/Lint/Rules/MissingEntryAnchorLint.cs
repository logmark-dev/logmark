using System.Text.Json;

namespace LogMark.Services.Lint.Rules;

/// <summary>
/// Surfaces entries the parser recognizes but that carry no block anchor.
///
/// Such an entry is captured and readable but unreachable by every lifecycle
/// mutation: the lookup layer indexes only entries with a ShortId, and the
/// write layer locates an entry by matching its "_id:"/"_task:" line in file
/// text. Complete, retype, reroute, and retag all fail on it. The 2026-07-18
/// and 2026-07-27 grooming runs each lost about 19 of 22 approved actions this
/// way, item for item, with nothing in between to explain the repeat.
///
/// AnchorMintSweep has been able to repair this since it shipped; until
/// 2026-07-27 nothing ever invoked it. This rule is one of the two invocations
/// added then (the other is the lm-groom-backlog precondition), so the vault
/// stays converged instead of being converged once by hand.
///
/// Deliberately NOT a startup self-heal: minting rewrites the user's own vault
/// content, which earns a higher bar than the startup rewrites LogMark performs
/// on its own artifacts.
/// </summary>
public class MissingEntryAnchorLint : ILintRule
{
    public string Id => "missing-entry-anchor";
    public string Category => "entries";

    /// <summary>
    /// Expensive: reads and parses every entry file in the vault. The rule
    /// cannot use ctx.Entries, because a cached ParsedEntry cannot distinguish
    /// "id line with an anchor" from "id line without one" - both surface the
    /// same ShortId. Only the raw file text carries the distinction.
    /// </summary>
    public LintCostClass Cost => LintCostClass.Expensive;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var issues = new List<LintIssue>();

        foreach (var gap in new AnchorMintSweep(ctx.VaultPath).PlanByFile())
        {
            ct.ThrowIfCancellationRequested();

            var noun = gap.Count == 1 ? "entry" : "entries";
            var payload = JsonSerializer.Serialize(new { path = gap.VaultRelativePath });

            issues.Add(new LintIssue(
                Id: Guid.NewGuid().ToString(),
                RuleId: Id,
                // Warn, not Suggest: this is not hygiene. Until it is fixed, the
                // entries in this file cannot be closed, retyped, or rerouted.
                Severity: LintSeverity.Warn,
                SubjectRef: $"file:{gap.VaultRelativePath}",
                Description: $"{gap.Count} {noun} in '{gap.VaultRelativePath}' lack a block anchor "
                    + "- lifecycle mutations (complete, retype, reroute) cannot address them",
                // Single-action rule: the fixer reads Actions[0].PayloadJson.
                SuggestedFixVerbId: null,
                FixPayloadJson: null,
                // The count is part of the dismissal identity on purpose. The
                // subject is a file, not an entry, so a plain path hash would
                // let one dismissal silence every future anchor-less entry that
                // lands in the same file.
                DismissalStateHash: LintHashing.Compute($"{gap.VaultRelativePath}|{gap.Count}"),
                FirstSeenAt: DateTime.UtcNow,
                LastSeenAt: DateTime.UtcNow,
                Category: Category,
                Actions: new List<LintAction>
                {
                    new("Mint anchors", "mint-anchors", payload, Batchable: true),
                }));
        }

        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }
}
