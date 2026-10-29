using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Models.AgentMemory;
using LogMark.Services;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Executes an ImportPlan against the vault. All writes go through
/// BackfillEngine (VaultWriteGuard-allowlisted). Per-fact continue-on-error;
/// a partially applied batch heals on the next run because idempotency is
/// frontmatter-based.
/// </summary>
public partial class ImportApplier
{
    [GeneratedRegex(@"\[\[([^\[\]|]+)\]\]")]
    private static partial Regex WikilinkPattern();

    private readonly string _vaultPath;
    private readonly RouteService _routeService;
    private readonly BackfillEngine _backfill;

    public ImportApplier(string vaultPath, RouteService routeService, IVaultFileWriter? vaultWriter = null)
    {
        _vaultPath = vaultPath;
        _routeService = routeService;
        _backfill = new BackfillEngine(vaultPath, vaultWriter);
    }

    public ImportReport Apply(ImportPlan plan, DateTime importTime)
    {
        var route = MirrorScanner.GetMirrorRoute(plan.SourceId);
        EnsureRoute(route);

        // Wiki targets for in-batch link rewriting: FactId and Title -> page wiki path
        var wikiTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plan.Facts)
        {
            if (p.Fact == null) continue;
            var wiki = BackfillEngine.GetPageWikiPath(route, "diary", PageStem(p.Fact));
            wikiTargets.TryAdd(p.Fact.FactId, wiki);
            wikiTargets.TryAdd(p.Fact.Title, wiki);
        }

        int newN = 0, unchangedN = 0, changedN = 0, conflictN = 0, removedN = 0;
        var failures = new List<ImportFailure>();
        var conflicts = new List<string>();

        foreach (var planned in plan.Facts)
        {
            try
            {
                switch (planned.Class)
                {
                    case FactClass.New:
                        var failure = LandNew(planned, route, wikiTargets, importTime);
                        if (failure != null) failures.Add(failure);
                        else newN++;
                        break;
                    case FactClass.Unchanged:
                        if (planned.MirrorPagePath != null) ReviveIfRemoved(planned.MirrorPagePath);
                        unchangedN++;
                        break;
                    case FactClass.Changed:
                        RewriteChanged(planned, planned.MirrorPagePath!, route, wikiTargets, importTime);
                        changedN++;
                        break;
                    case FactClass.Conflict:
                        conflicts.Add(planned.MirrorPagePath!);
                        conflictN++;
                        break;
                    case FactClass.RemovedAtSource:
                        FlipStatus(planned.MirrorPagePath!, "removed-at-source", route);
                        removedN++;
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ImportFailure(planned.EffectiveSourcePath, ex.Message));
            }
        }

        return new ImportReport(plan.SourceId, newN, unchangedN, changedN, conflictN, removedN,
            plan.MalformedFiles, failures, conflicts,
            Groups: ImportPlanGrouping.Group(plan));
    }

    private void EnsureRoute(string route)
    {
        var exists = _routeService.GetRoutes().Any(r =>
            string.Equals(r.Path, route, StringComparison.OrdinalIgnoreCase));
        if (exists) return;
        _routeService.AddRoute(new Route
        {
            Name = route,
            Path = route,
            Description = "Staging inbox for imported AI agent memories",
            Status = "active"
        });
    }

    private static string PageStem(AgentMemoryFact fact)
        => $"{AgentMemoryFrontmatter.ToKebab(fact.Project)}--{fact.FactId}";

    /// <summary>
    /// Lands a New-classified fact: writes its mirror page then appends the
    /// diary entry line. Returns an <see cref="ImportFailure"/> (and writes
    /// nothing) when the target page stem already belongs to a different
    /// source path -- the ledger identity is SourcePath, but the page-file
    /// identity is project+factId, so two distinct source paths (e.g. an old
    /// and a moved copy of the same project dir) can collide on one file.
    /// Overwriting that file would clobber another fact's page, possibly a
    /// human-edited one. Returns null on success.
    /// </summary>
    private ImportFailure? LandNew(PlannedFact planned, string route,
        IReadOnlyDictionary<string, string> wikiTargets, DateTime importTime)
    {
        var fact = planned.Fact!;
        var stem = PageStem(fact);
        var pagesDir = MirrorScanner.GetMirrorPagesDir(_vaultPath, fact.SourceId);
        var pagePath = Path.Combine(pagesDir, stem + ".md");

        if (File.Exists(pagePath))
        {
            string? existingSourcePath = null;
            var readable = false;
            try
            {
                var existingContent = File.ReadAllText(pagePath);
                readable = AgentMemoryFrontmatter.TryParse(existingContent, out var existingFm, out _);
                if (readable) existingSourcePath = existingFm.SourcePath;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            var sameSource = readable &&
                string.Equals(existingSourcePath, fact.SourcePath, StringComparison.OrdinalIgnoreCase);

            if (!sameSource)
            {
                var owner = readable ? existingSourcePath : "an unreadable page";
                return new ImportFailure(fact.SourcePath,
                    $"page stem collision: {stem}.md already belongs to {owner}");
            }
            // Same source path landed this page before (e.g. a crash between
            // AddPage and AddEntry on a prior run) -- safe to overwrite.
        }

        var body = RewriteWikilinks(fact.Body, fact.FactId, fact.Title, wikiTargets);
        var fm = new MirrorFrontmatter(
            fact.SourceId, fact.Project, fact.SourcePath, fact.SourceType,
            fact.ContentHash, AgentMemoryFrontmatter.ComputeHash(body),
            importTime.ToString("yyyy-MM-dd"), "live",
            SuggestedRoute: planned.Resolution?.SuggestedRoute ?? "",
            RouteConfidence: planned.Resolution?.ConfidenceValue ?? "none",
            OverlapPrior: OverlapPrior.FromSourceType(fact.SourceType));

        _backfill.AddPage(route, "diary", stem + ".md",
            AgentMemoryFrontmatter.Serialize(fm) + body);

        var wiki = BackfillEngine.GetPageWikiPath(route, "diary", stem);
        var projectKebab = AgentMemoryFrontmatter.ToKebab(fact.Project);
        var content = $"{fact.Description} [[{wiki}|{fact.Title}]]" +
            $" #agent-memory #mem-{fact.SourceType} #{projectKebab}";

        try
        {
            _backfill.AddEntry(new SeedEntry(
                Date: importTime,
                Type: EntryType.Log,
                Content: content,
                Route: route,
                Tags: new[] { "agent-memory", $"mem-{fact.SourceType}", projectKebab },
                Source: "agent-memory-import"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Page landed but the diary line didn't. The page is entirely
            // machine-authored within this same call (never user content),
            // so best-effort delete it to heal the ledger: a re-run must see
            // this fact as New again, not Unchanged, or the entry line is
            // lost forever. Rethrow so the per-fact loop records the failure.
            try { File.Delete(pagePath); } catch { /* best-effort cleanup */ }
            throw;
        }

        return null;
    }

    private void RewriteChanged(PlannedFact planned, string pagePath, string route,
        IReadOnlyDictionary<string, string> wikiTargets, DateTime importTime)
    {
        var fact = planned.Fact!;
        // The page may have been promoted: write to its ACTUAL home, and a
        // dismissed page stays dismissed (dismissal is human-sovereign).
        var actualRoute = MirrorScanner.RouteOfPagePath(_vaultPath, pagePath) ?? route;
        var existingStatus = "live";
        MirrorFrontmatter? existingFm = null;
        try
        {
            if (AgentMemoryFrontmatter.TryParse(File.ReadAllText(pagePath), out var parsed, out _))
            {
                existingFm = parsed;
                existingStatus = parsed.Status;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var status = existingStatus == "dismissed" ? "dismissed" : "live";

        // A promoted page's SuggestedRoute/RouteConfidence were set by the
        // promotion (Task 4) to its destination, not by this import's
        // workdir resolution. Carry the existing values through instead of
        // overwriting them with planned.Resolution, which still reflects the
        // staging-side guess.
        var promoted = actualRoute != route;
        var suggestedRoute = promoted
            ? actualRoute
            : planned.Resolution?.SuggestedRoute ?? "";
        var routeConfidence = promoted
            ? existingFm?.RouteConfidence ?? "none"
            : planned.Resolution?.ConfidenceValue ?? "none";

        var body = RewriteWikilinks(fact.Body, fact.FactId, fact.Title, wikiTargets);
        var fm = new MirrorFrontmatter(
            fact.SourceId, fact.Project, fact.SourcePath, fact.SourceType,
            fact.ContentHash, AgentMemoryFrontmatter.ComputeHash(body),
            importTime.ToString("yyyy-MM-dd"), status,
            SuggestedRoute: suggestedRoute,
            RouteConfidence: routeConfidence,
            OverlapPrior: OverlapPrior.FromSourceType(fact.SourceType),
            PromotedAt: existingFm?.PromotedAt ?? "");
        _backfill.AddPage(actualRoute, "diary", Path.GetFileName(pagePath),
            AgentMemoryFrontmatter.Serialize(fm) + body);
    }

    private void ReviveIfRemoved(string pagePath)
    {
        var content = File.ReadAllText(pagePath);
        if (!AgentMemoryFrontmatter.TryParse(content, out var fm, out _)) return;
        if (fm.Status != "removed-at-source") return;
        FlipStatus(pagePath, "live", null);
    }

    private void FlipStatus(string pagePath, string newStatus, string? routeHint)
    {
        var content = File.ReadAllText(pagePath);
        if (!AgentMemoryFrontmatter.TryParse(content, out var fm, out var body)) return;
        var updated = fm with { Status = newStatus };
        var route = MirrorScanner.RouteOfPagePath(_vaultPath, pagePath)
            ?? routeHint ?? MirrorScanner.GetMirrorRoute(updated.Source);
        _backfill.AddPage(route, "diary", Path.GetFileName(pagePath),
            AgentMemoryFrontmatter.Serialize(updated) + body);
    }

    private static string RewriteWikilinks(string body, string selfFactId, string selfTitle,
        IReadOnlyDictionary<string, string> wikiTargets)
    {
        return WikilinkPattern().Replace(body, m =>
        {
            var target = m.Groups[1].Value.Trim();
            if (target.Equals(selfFactId, StringComparison.OrdinalIgnoreCase) ||
                target.Equals(selfTitle, StringComparison.OrdinalIgnoreCase)) return target;
            return wikiTargets.TryGetValue(target, out var wiki)
                ? $"[[{wiki}|{target}]]"
                : target;
        });
    }
}
