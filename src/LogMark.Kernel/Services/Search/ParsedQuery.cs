namespace LogMark.Services.Search;

public record ParsedQuery(
    IReadOnlyList<string> Terms,
    string? Phrase,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Routes);
