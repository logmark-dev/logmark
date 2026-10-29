using LogMark.Models;

namespace LogMark.Interfaces;

/// <summary>
/// User-facing semantic search capabilities powered by T1 embeddings.
/// Orchestrates IEmbeddingService + IVectorIndex.
/// </summary>
public interface ISemanticSearchService
{
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(string query, int topK = 10, CancellationToken ct = default);
    Task<IReadOnlyList<string>> SuggestRoutesAsync(string content, int topK = 3, CancellationToken ct = default);
    Task<string?> CorrectTypoAsync(string input, IReadOnlyList<string> knownEntities, CancellationToken ct = default);
    Task<EntryType?> ClassifyEntryTypeAsync(string content, CancellationToken ct = default);
}

public record SemanticSearchResult(string EntryId, string Content, string? Route, float Score);
