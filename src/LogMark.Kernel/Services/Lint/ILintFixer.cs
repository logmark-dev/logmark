namespace LogMark.Services.Lint;

public interface ILintFixer
{
    string Id { get; }
    Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct);
}
