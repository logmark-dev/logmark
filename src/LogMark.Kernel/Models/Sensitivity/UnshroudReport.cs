namespace LogMark.Models.Sensitivity;

/// <summary>
/// Result of a completed BackfillService.UnshroudFolderAsync call.
/// </summary>
public record UnshroudReport(
    int FilesProcessed,
    int TokensRevealed,
    int OrphanTokens,
    IReadOnlyList<string> FailedFiles);
