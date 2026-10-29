using LogMark.Services.Lint;

namespace LogMark.Services.Lint.Persistence;

public record LintState(
    int Version,
    DateTime? LastScannedAt,
    IReadOnlyList<LintIssue> OpenIssues,
    IReadOnlyList<LintDismissal> Dismissals
)
{
    // v6: LintIssue.Actions added (labeled per-issue actions). Bumping drops prior
    // state on first load after upgrade so issues re-scan with the new shape.
    public const int CurrentVersion = 6;

    public static LintState Empty => new(
        Version: CurrentVersion,
        LastScannedAt: null,
        OpenIssues: Array.Empty<LintIssue>(),
        Dismissals: Array.Empty<LintDismissal>());
}

public record LintDismissal(
    string RuleId,
    string SubjectRef,
    string DismissalStateHash,
    DateTime DismissedAt
);
