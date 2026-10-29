namespace LogMark.Models.Sensitivity;

public record RegistryStats(
    int TokenCount,
    int FilesContainingTokens,
    IReadOnlyDictionary<string, int> CountsByType
);
