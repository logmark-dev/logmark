using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Pure diff between a source scan and the mirror ledger. No I/O.
/// The user approves the resulting plan, not a surprise (observe-and-propose
/// at batch level).
/// </summary>
public static class ImportPlanner
{
    public static ImportPlan Plan(
        string sourceId,
        IReadOnlyList<AgentMemoryFact> facts,
        IReadOnlyList<MirrorRecord> mirror,
        IReadOnlyList<string> malformedFiles)
    {
        var byPath = mirror.ToDictionary(r => r.Frontmatter.SourcePath, StringComparer.OrdinalIgnoreCase);
        var planned = new List<PlannedFact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fact in facts)
        {
            seen.Add(fact.SourcePath);
            if (!byPath.TryGetValue(fact.SourcePath, out var record))
            {
                planned.Add(new PlannedFact(fact, FactClass.New, null, fact.SourcePath, fact.Project));
                continue;
            }

            var cls = record.Frontmatter.ContentHash == fact.ContentHash
                ? FactClass.Unchanged
                : record.CurrentBodyHash == record.Frontmatter.LandedHash
                    ? FactClass.Changed
                    : FactClass.Conflict;
            planned.Add(new PlannedFact(fact, cls, record.PagePath, fact.SourcePath, fact.Project));
        }

        foreach (var record in mirror)
        {
            if (seen.Contains(record.Frontmatter.SourcePath)) continue;
            if (record.Frontmatter.Status is "removed-at-source" or "dismissed") continue;
            planned.Add(new PlannedFact(null, FactClass.RemovedAtSource,
                record.PagePath, record.Frontmatter.SourcePath, record.Frontmatter.SourceProject));
        }

        return new ImportPlan(sourceId, planned, malformedFiles);
    }
}
