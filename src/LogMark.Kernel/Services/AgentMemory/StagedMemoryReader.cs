using LogMark.Models.AgentMemory;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Grouped review model for one staged memory item: the staging diary entry
/// joined to its mirror page.
/// </summary>
public record StagedMemory(
    StagedMemoryRef Ref,
    string Title,              // wikilink display text, fallback stem
    string EntryText,          // the entry's raw text (description + link line)
    string BodyPreview,        // first 300 chars of the page body
    string SuggestedRoute,     // "" when none
    string RouteConfidence,    // exact | ancestry | sibling | none
    string OverlapPrior,       // low | candidate
    string SourceProject);

/// <summary>One suggested destination route, split into confident-new vs overlap-candidate.</summary>
public record StagedRouteGroup(
    string SuggestedRoute, string Confidence,
    IReadOnlyList<StagedMemory> ConfidentNew,
    IReadOnlyList<StagedMemory> OverlapCandidates);

/// <summary>The full staging inbox, grouped for the review surface.</summary>
public record StagedReviewModel(
    IReadOnlyList<StagedRouteGroup> Groups,          // routed, largest first
    IReadOnlyList<StagedMemory> NoDestination,       // confidence none
    int TotalStaged);

/// <summary>
/// Read-side join for the memory review surface: staging-route entries
/// (EntryCache) joined to their mirror pages (MirrorScanner) into a grouped
/// review model. Read-only; all mutation goes through MemoryPromotionService.
/// </summary>
public class StagedMemoryReader
{
    private readonly string _vaultPath;
    private readonly EntryCache.EntryCache _cache;

    public StagedMemoryReader(string vaultPath, EntryCache.EntryCache cache)
    {
        _vaultPath = vaultPath;
        _cache = cache;
    }

    /// <summary>
    /// Staged count without the full model join: scans the ledger and counts
    /// live pages still under the staging route. No page-body reads, no
    /// entry join. For lightweight surfaces (dashboard landmark).
    /// </summary>
    public int CountStaged(string sourceId = "claude-code")
    {
        var stagingRoute = MirrorScanner.GetMirrorRoute(sourceId);
        return new MirrorScanner(_vaultPath).Scan(sourceId)
            .Count(r => r.Frontmatter.Status == "live"
                && string.Equals(MirrorScanner.RouteOfPagePath(_vaultPath, r.PagePath), stagingRoute,
                    StringComparison.OrdinalIgnoreCase));
    }

    public StagedReviewModel Read(string sourceId = "claude-code")
    {
        var stagingRoute = MirrorScanner.GetMirrorRoute(sourceId);
        var scanner = new MirrorScanner(_vaultPath);
        var staged = scanner.Scan(sourceId)
            .Where(r => r.Frontmatter.Status == "live"
                && string.Equals(MirrorScanner.RouteOfPagePath(_vaultPath, r.PagePath), stagingRoute,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => Path.GetFileNameWithoutExtension(r.PagePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var items = new List<StagedMemory>();
        foreach (var indexed in _cache.GetEntriesUnderPath(StagingFolder(stagingRoute)))
        {
            var raw = indexed.Entry.Content;
            var link = ContentParser.GetPageLinks(raw).FirstOrDefault();
            if (link == null) continue;
            var stem = Path.GetFileNameWithoutExtension(link.Path);
            if (!staged.TryGetValue(stem, out var record)) continue;

            string body = "";
            try
            {
                if (AgentMemoryFrontmatter.TryParse(File.ReadAllText(record.PagePath), out _, out var b)) body = b;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            var fm = record.Frontmatter;
            items.Add(new StagedMemory(
                new StagedMemoryRef(indexed.FilePath, indexed.Entry.Timestamp, indexed.Entry.ShortId,
                    record.PagePath, stem),
                Title: string.IsNullOrWhiteSpace(link.DisplayText) ? stem : link.DisplayText,
                EntryText: raw.Trim(),
                // Flattened before capping - BodyPreview is display-only, and a
                // memory body is dense with [[name]] refs the review card shows
                // as plain text.
                BodyPreview: FlattenedPreview(body, 300),
                SuggestedRoute: fm.SuggestedRoute,
                RouteConfidence: fm.RouteConfidence,
                OverlapPrior: fm.OverlapPrior,
                SourceProject: fm.SourceProject));
        }

        var routed = items.Where(i => i.SuggestedRoute.Length > 0)
            .GroupBy(i => (i.SuggestedRoute, i.RouteConfidence))
            .Select(g => new StagedRouteGroup(
                g.Key.SuggestedRoute, g.Key.RouteConfidence,
                g.Where(i => i.OverlapPrior == Models.AgentMemory.OverlapPrior.Low).ToList(),
                g.Where(i => i.OverlapPrior == Models.AgentMemory.OverlapPrior.Candidate).ToList()))
            .OrderByDescending(g => g.ConfidentNew.Count + g.OverlapCandidates.Count)
            .ThenBy(g => g.SuggestedRoute, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var none = items.Where(i => i.SuggestedRoute.Length == 0)
            .OrderBy(i => i.SourceProject, StringComparer.OrdinalIgnoreCase).ToList();

        return new StagedReviewModel(routed, none, items.Count);
    }

    /// <summary>
    /// The most recent agent-authored entries under a route, spanning all type
    /// folders (agent-authored entries land in every type folder: _tasks, _ideas,
    /// _decisions, etc.).
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetRouteAgentContext(string routePath, int count = 5)
        => _cache.GetEntriesUnderPath(Path.Combine(_vaultPath, "logmark",
                routePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(e => e.Entry.Source != null
                && e.Entry.Source.StartsWith("agent", StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToList();

    // GetEntriesUnderPath normalizes via Path.GetFullPath, so it expects an
    // absolute path (see EntryCache.NormalizePath / existing call sites like
    // SummaryDataCollector and VaultWriterCacheIntegrationTests, which always
    // pass an absolute folder). logmark/{route}/_diary is where diary-typed
    // entries land (EntryType.Log -> EntryTypeMapper "diary"), matching the
    // sibling logmark/{route}/_diary/pages mirror-page location.
    private string StagingFolder(string routePath)
        => Path.Combine(_vaultPath, PathConstants.GetRoutedTypeFolder(routePath, "diary"));

    /// <summary>
    /// Display-only body preview: wiki links flattened to their reader-facing
    /// text first, then capped. Capping first can cut a [[link]] in half and
    /// the fragment survives into the review card as raw brackets.
    /// </summary>
    private static string FlattenedPreview(string body, int cap)
    {
        var flat = ContentParser.FlattenWikiLinks(body);
        return flat.Length > cap ? flat[..cap] : flat;
    }
}
