namespace LogMark.Models.Sensitivity;

public record BackfillReport(
    int FilesProcessed,
    int SpansMasked,
    IReadOnlyList<string> FailedFiles
);
