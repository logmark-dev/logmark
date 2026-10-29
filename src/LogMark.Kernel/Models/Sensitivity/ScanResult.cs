namespace LogMark.Models.Sensitivity;

public record SensitiveSpan(
    int Start,
    int Length,
    string EntityType,
    string Domain,
    double Confidence,
    string MatchReason,
    string? MatchedText = null
);

public record ScanResult(IReadOnlyList<SensitiveSpan> Spans);
