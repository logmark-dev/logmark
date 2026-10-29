namespace LogMark.Services.Lint.Persistence;

public interface ILintStateStore
{
    Task<LintState> LoadAsync(CancellationToken ct);
    Task SaveAsync(LintState state, CancellationToken ct);
}
