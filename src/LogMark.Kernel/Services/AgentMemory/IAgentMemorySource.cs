using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// One local agent memory store. Everything provider-specific stays behind
/// Enumerate(); the pipeline downstream only sees AgentMemoryFact.
/// </summary>
public interface IAgentMemorySource
{
    string SourceId { get; }
    AgentSourceInfo Discover();
    IEnumerable<AgentMemoryFact> Enumerate();
    /// <summary>
    /// Files skipped during the last Enumerate() (empty or unreadable). Never fatal.
    /// Enumerate() is lazy: this list is only complete after the sequence has been
    /// fully drained (e.g. via ToList()). Reading it after a partial iteration
    /// (First, Take, early break) yields only the files seen so far.
    /// </summary>
    IReadOnlyList<string> MalformedFiles { get; }

    /// <summary>
    /// The encoded workdir folder a fact came from (source-specific encoding),
    /// or null when the source has no workdir notion. Feeds the destination
    /// resolver's evidence bundle (design 4b).
    /// </summary>
    string? EncodedWorkdirFolder(AgentMemoryFact fact) => null;
}
