namespace LogMark.Services.Lint;

public enum LintSeverity { Info, Suggest, Warn }
public enum LintCostClass { Cheap, Medium, Expensive }

public record LintAction(
    string Label,
    string VerbId,
    string? PayloadJson,
    bool Batchable = false
);

public record BatchResult(int Fixed, int Failed, int Skipped);

public record LintIssue(
    string Id,
    string RuleId,
    LintSeverity Severity,
    string SubjectRef,
    string Description,
    string? SuggestedFixVerbId,
    string? FixPayloadJson,
    string DismissalStateHash,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string Category = "",
    IReadOnlyList<LintAction>? Actions = null
);

public record LintFixResult(
    bool Success,
    string? ErrorMessage,
    string? AppliedPayload,
    bool WasCancelled = false
);

public record ScanResult(
    int IssuesFound,
    int IssuesRemoved,
    DateTime CompletedAt,
    TimeSpan Duration
);
