namespace LogMark.Services.Lint;

public interface ILintRule
{
    string Id { get; }
    string Category { get; }
    LintCostClass Cost { get; }
    Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct);
}
