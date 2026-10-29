namespace LogMark.Services.Search;

public record SearchHit(SearchIndexRecord Record, MatchReason Reason, double Score);
