namespace LogMark.Services.Lint.Rules;

public class TagPluralSLint : ILintRule
{
    public string Id => "tag-plural-s";
    public string Category => "tags";
    public LintCostClass Cost => LintCostClass.Cheap;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var counts = TagLintHelpers.CountAllTags(ctx, ct);
        var issues = new List<LintIssue>();
        foreach (var group in TagVariantClassifier.GroupPluralSVariants(counts.Keys))
        {
            ct.ThrowIfCancellationRequested();
            var canonical = TagLintHelpers.SelectCanonical(
                group.Select(s => new KeyValuePair<string, int>(s, counts[s])));
            var description = TagLintHelpers.FormatDescription(
                canonical, group,
                singularConnector: "is a plural form of",
                pluralConnector: "are plural forms of");
            issues.Add(TagLintHelpers.BuildIssue(Id, canonical, group, description));
        }
        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }
}
