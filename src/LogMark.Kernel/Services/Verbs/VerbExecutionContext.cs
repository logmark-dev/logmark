namespace LogMark.Services.Verbs;

/// <summary>
/// Per-call context handed to a verb handler. Carries the parsed args, the
/// calling surface (chat / mcp / cli / wpf) for logging, and the caller's tier.
/// Surfaces translate their own request shape into this before dispatch.
/// </summary>
public class VerbExecutionContext
{
    public required IReadOnlyDictionary<string, string?> Args { get; init; }
    public required string Surface { get; init; }
    public required VerbTier CallerTier { get; init; }

    public string? GetArg(string key) =>
        Args.TryGetValue(key, out var v) ? v : null;
}
