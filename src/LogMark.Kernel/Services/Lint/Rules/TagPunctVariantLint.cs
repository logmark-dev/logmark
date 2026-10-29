namespace LogMark.Services.Lint.Rules;

public class TagPunctVariantLint : ILintRule
{
    public string Id => "tag-punct-variant";
    public string Category => "tags";
    public LintCostClass Cost => LintCostClass.Cheap;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var counts = TagLintHelpers.CountAllTags(ctx, ct);
        var issues = new List<LintIssue>();
        foreach (var group in TagVariantClassifier.GroupPunctVariants(counts.Keys))
        {
            ct.ThrowIfCancellationRequested();
            var canonical = TagLintHelpers.SelectCanonical(
                group.Select(s => new KeyValuePair<string, int>(s, counts[s])));
            var description = TagLintHelpers.FormatDescription(
                canonical, group,
                singularConnector: "is a punctuation variant of",
                pluralConnector: "are punctuation variants of");
            issues.Add(TagLintHelpers.BuildIssue(Id, canonical, group, description));
        }
        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }
}
