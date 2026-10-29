namespace LogMark.Services.Search;

public record SearchRequest(string Query, string? ScopeRoute = null, int Limit = 100);
