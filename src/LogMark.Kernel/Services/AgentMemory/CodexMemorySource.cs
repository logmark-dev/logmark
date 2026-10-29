using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Detection-only Codex adapter. The local store observed 2026-07 is empty
/// scaffolding (memories/ dir empty, memories_1.sqlite schema with zero rows),
/// so v1 reports presence and never enumerates. Fact enumeration lands when
/// real Codex data exists anywhere (see design doc, out-of-scope).
/// </summary>
public class CodexMemorySource : IAgentMemorySource
{
    private readonly string _home;

    public CodexMemorySource(string? homeOverride = null)
    {
        _home = homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string SourceId => "codex";
    public IReadOnlyList<string> MalformedFiles => Array.Empty<string>();

    public AgentSourceInfo Discover()
    {
        var codexDir = Path.Combine(_home, ".codex");
        var memoriesDir = Path.Combine(codexDir, "memories");
        var sqlite = Path.Combine(codexDir, "memories_1.sqlite");
        var found = Directory.Exists(memoriesDir) || File.Exists(sqlite);
        return new AgentSourceInfo(SourceId, found, found ? codexDir : null,
            Array.Empty<AgentProjectInfo>(), found ? "found, no memories yet" : null);
    }

    public IEnumerable<AgentMemoryFact> Enumerate() => Enumerable.Empty<AgentMemoryFact>();
}
