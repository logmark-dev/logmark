namespace LogMark.Models.Sensitivity;

/// <summary>
/// Preview result returned by BackfillService.PreviewUnshroud before the user
/// confirms a token -> plaintext restore. Contains counts for UI display;
/// does not require an unlocked session (regex scan only).
/// </summary>
public record UnshroudPreview(
    int FilesAffected,
    int TokensFound,
    IReadOnlyDictionary<string, int> CountsByType);
