namespace LogMark.Services.Search;

public static class HitMerger
{
    public static IReadOnlyList<SearchHit> Merge(IEnumerable<SearchHit> candidates, int limit)
    {
        var merged = new List<SearchHit>();
        foreach (var group in candidates.GroupBy(h => h.Record.Id))
        {
            var best = group.OrderByDescending(h => h.Score).First();
            var boost = 0.1 * (group.Select(h => h.Reason).Distinct().Count() - 1);
            merged.Add(best with { Score = best.Score + boost });
        }

        return merged
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Record.Timestamp)
            .Take(limit)
            .ToList();
    }
}
