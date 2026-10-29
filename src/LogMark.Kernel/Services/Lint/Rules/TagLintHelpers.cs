using System.Text.Json;

namespace LogMark.Services.Lint.Rules;

internal static class TagLintHelpers
{
    public static Dictionary<string, int> CountAllTags(IVaultContext ctx, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ie in ctx.Entries.GetAllEntries())
        {
            ct.ThrowIfCancellationRequested();
            var tags = Services.NotationParser.ExtractTagsPreservingCase(ie.Entry.Content);
            foreach (var tag in tags)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }
        return counts;
    }

    public static string SelectCanonical(IEnumerable<KeyValuePair<string, int>> variants)
    {
        return variants
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First().Key;
    }

    public static string FormatDescription(
        string canonical,
        IEnumerable<string> allVariants,
        string singularConnector,
        string pluralConnector)
    {
        var nonCanonical = allVariants
            .Where(v => v != canonical)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        if (nonCanonical.Count == 0)
            throw new ArgumentException(
                "allVariants must contain at least one non-canonical variant; passing only the canonical produces malformed output.",
                nameof(allVariants));
        var connector = nonCanonical.Count == 1 ? singularConnector : pluralConnector;
        return $"{string.Join(", ", nonCanonical.Select(v => "#" + v))} {connector} #{canonical}";
    }

    public static LintIssue BuildIssue(
        string ruleId,
        string canonical,
        IReadOnlyCollection<string> allVariants,
        string description)
    {
        var sortedVariants = allVariants
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var hashInput = string.Join("|", sortedVariants);
        var hash = LintHashing.Compute(hashInput);

        var payload = JsonSerializer.Serialize(new
        {
            canonical,
            variants = sortedVariants,
        });

        // Dual representation is intentional and carries the same payload:
        //   SuggestedFixVerbId/FixPayloadJson -- legacy single-fix path (ApplyFixAsync, GUI single-issue button)
        //   Actions -- batch/Fix-all path (ApplyBatchAsync); one Batchable action makes tag issues batch-eligible
        return new LintIssue(
            Id: Guid.NewGuid().ToString(),
            RuleId: ruleId,
            Severity: LintSeverity.Suggest,
            SubjectRef: $"tag-group:{canonical}",
            Description: description,
            SuggestedFixVerbId: "tag-rewrite",
            FixPayloadJson: payload,
            DismissalStateHash: hash,
            FirstSeenAt: DateTime.UtcNow,
            LastSeenAt: DateTime.UtcNow,
            Actions: new[]
            {
                new LintAction($"Rewrite to #{canonical}", "tag-rewrite", payload, Batchable: true),
            });
    }
}
