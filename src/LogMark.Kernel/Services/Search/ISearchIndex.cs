namespace LogMark.Services.Search;

public interface ISearchIndex
{
    Task<IReadOnlyList<SearchHit>> QueryAsync(SearchRequest request, CancellationToken ct = default);
}
