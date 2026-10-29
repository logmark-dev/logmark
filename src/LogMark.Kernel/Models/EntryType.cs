namespace LogMark.Models;

/// <summary>
/// Entry type prefixes (task:, idea:, block:, decision:, timetrack:, question:, gap:, analogy:)
/// </summary>
public enum EntryType
{
    Task,
    Idea,
    Block,
    Decision,
    Log,      // For no-prefix entries
    Timetrack,
    Question, // Advanced: open inquiry (q:)
    Gap,      // Advanced: knowledge debt (g:)
    Analogy   // Advanced: structural mapping (a:)
}
