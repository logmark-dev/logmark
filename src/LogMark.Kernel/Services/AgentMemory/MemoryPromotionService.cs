using LogMark.Models;
using LogMark.Models.AgentMemory;
using LogMark.Services;
using LogMark.Services.EntryParser;

namespace LogMark.Services.AgentMemory;

public record StagedMemoryRef(
    string EntryFilePath,      // absolute path of the staging diary file
    DateTime EntryTimestamp,
    string? EntryShortId,
    string PagePath,           // absolute path of the staging mirror page
    string Stem);              // page filename without extension

public record PromotionResult(bool Success, string? Error, string? NewPagePath = null, string? TargetDiaryPath = null)
{
    public static PromotionResult Ok(string? newPagePath = null, string? targetDiaryPath = null) =>
        new(true, null, newPagePath, targetDiaryPath);
    public static PromotionResult Fail(string error) => new(false, error);
}

/// <summary>
/// The staging inbox's exit (Slice 2 design). Promote moves a staged memory's
/// page AND entry to the target route - destination page first, entry reroute
/// second (wikilink rewritten in transit), staging copy deleted last. A crash
/// between any two steps is recoverable by simply calling Promote again with
/// the same item: the collision guard recognizes a same-source destination
/// page and allows the overwrite; if the entry is no longer in the staging
/// diary, Promote checks whether the target diary already carries the
/// rewritten wikilink - if so the prior run completed and this retry only
/// cleans up the stale staging copy, if not the item is a bad ref and Promote
/// fails without destroying any destination page that predates this run.
/// Dismiss trashes the entry and flips the page to status: dismissed in
/// place - the page stays as the ledger record so re-imports never resurrect
/// the fact. Dismiss is likewise retry-safe: if the entry is already gone
/// from staging (prior Dismiss completed, or a duplicate call), it reports
/// success rather than a spurious trash failure.
/// </summary>
public class MemoryPromotionService
{
    private readonly string _vaultPath;
    private readonly EntryOrganizer _organizer;
    private readonly BackfillEngine _backfill;

    public MemoryPromotionService(string vaultPath, EntryOrganizer organizer, IVaultFileWriter? vaultWriter = null)
    {
        _vaultPath = vaultPath;
        _organizer = organizer;
        _backfill = new BackfillEngine(vaultPath, vaultWriter);
    }

    public PromotionResult Promote(StagedMemoryRef item, string targetRoute, DateTime now)
    {
        if (!File.Exists(item.PagePath))
            return PromotionResult.Fail($"staging page not found: {item.PagePath}");
        string content;
        try { content = File.ReadAllText(item.PagePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return PromotionResult.Fail(ex.Message); }
        if (!AgentMemoryFrontmatter.TryParse(content, out var fm, out var body))
            return PromotionResult.Fail($"staging page has no parseable frontmatter: {item.PagePath}");

        var stagingRoute = MirrorScanner.RouteOfPagePath(_vaultPath, item.PagePath);
        if (stagingRoute == null)
            return PromotionResult.Fail($"page is not under a _diary/pages dir: {item.PagePath}");

        // Target stem collision guard (same rule as LandNew): a page with this
        // stem belonging to a DIFFERENT source fact fails loud, item stays staged.
        var canonicalType = EntryTypeMapper.ResolveTypeName("diary")!;
        var targetPath = Path.Combine(_vaultPath,
            PathConstants.GetRoutedPagesFolder(targetRoute, canonicalType), item.Stem + ".md");
        if (File.Exists(targetPath))
        {
            string? existingSourcePath = null;
            var readable = false;
            try
            {
                readable = AgentMemoryFrontmatter.TryParse(File.ReadAllText(targetPath), out var existing, out _);
                if (readable) existingSourcePath = existing.SourcePath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            var sameSource = readable && string.Equals(existingSourcePath, fm.SourcePath, StringComparison.OrdinalIgnoreCase);
            if (!sameSource)
            {
                var owner = readable ? existingSourcePath : "an unreadable page";
                return PromotionResult.Fail($"page stem collision in {targetRoute}: {item.Stem}.md belongs to {owner}");
            }
            // Same source: crash-retry, safe to overwrite.
        }

        // Retry-awareness: capture state before any writes, so the branches
        // below can tell a fresh run from a retry of a partially- or fully-
        // completed one. entryStillStaged false means either a prior run
        // already rerouted the entry (crash before the staging-copy delete)
        // or item is a bad ref; targetExistedBefore gates whether a failure
        // path is allowed to delete the destination page (never destroy a
        // page that predates this run - it may be a completed prior promotion).
        var entryStillStaged = IsEntryStillStaged(item);
        var targetExistedBefore = File.Exists(targetPath);

        // 1. Destination page first.
        var promotedFm = fm with { SuggestedRoute = targetRoute, PromotedAt = now.ToString("yyyy-MM-dd") };
        _backfill.AddPage(targetRoute, "diary", item.Stem + ".md",
            AgentMemoryFrontmatter.Serialize(promotedFm) + body);

        // 2. Entry reroute with the wikilink rewritten in transit - unless a
        // prior run already completed it.
        var oldWiki = BackfillEngine.GetPageWikiPath(stagingRoute, "diary", item.Stem);
        var newWiki = BackfillEngine.GetPageWikiPath(targetRoute, "diary", item.Stem);

        // Set from RerouteEntry's own TargetFilePath on the fresh-promotion path
        // below - the caller (NativeViewerWindow.PromoteMemoryItems) invalidates
        // this file in EntryCache instead of recomputing it via
        // PathConstants.GetRoutedFilePath. Left null on the already-promoted
        // retry branch (entry no longer staged): the caller falls back to its
        // own recomputation there.
        string? targetDiaryPath = null;

        if (entryStillStaged)
        {
            var reroute = _organizer.RerouteEntry(item.EntryFilePath, item.EntryTimestamp,
                targetRoute, EntryType.Log, item.EntryShortId,
                contentTransform: raw => raw.Replace(oldWiki, newWiki, StringComparison.OrdinalIgnoreCase));
            if (!reroute.Success)
            {
                // Compensate: remove the destination copy so a retry starts
                // clean, but only if this run created it just now.
                if (!targetExistedBefore)
                {
                    try { File.Delete(targetPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return PromotionResult.Fail(reroute.Error ?? "entry reroute failed");
            }
            if (reroute.TargetFilePath != null)
                targetDiaryPath = Path.Combine(_vaultPath, reroute.TargetFilePath);
        }
        else
        {
            // The staged entry is gone. Distinguish a completed-promotion
            // retry (reroute succeeded before, process died before the
            // staging-copy delete) from a bad ref by checking whether the
            // target diary already carries the rewritten wikilink. This is a
            // local check only - targetDiaryPath (the outer, result-carrying
            // variable) stays null on this branch per its contract above.
            var alreadyPromotedDiaryPath = Path.Combine(_vaultPath, PathConstants.GetRoutedFilePath(
                targetRoute, EntryTypeMapper.GetFolderName(EntryType.Log), item.EntryTimestamp.Year));
            var alreadyPromoted = false;
            try
            {
                alreadyPromoted = File.Exists(alreadyPromotedDiaryPath)
                    && File.ReadAllText(alreadyPromotedDiaryPath).Contains(newWiki, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            if (!alreadyPromoted)
            {
                if (!targetExistedBefore)
                {
                    try { File.Delete(targetPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return PromotionResult.Fail(
                    $"staged entry not found at {item.EntryTimestamp:yyyy-MM-dd HH:mm} and no promoted entry present in {targetRoute}");
            }
            // Prior run completed the reroute - fall through to staging cleanup.
        }

        // 3. Staging copy last - the only planned deletion in this feature.
        try { File.Delete(item.PagePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* stale copy remains; scanner dedup wins and ApplyImport heals it */ }

        return PromotionResult.Ok(targetPath, targetDiaryPath);
    }

    public PromotionResult Dismiss(StagedMemoryRef item)
    {
        // Flip the ledger first so a failure between the two steps leaves the
        // safer state (page dismissed, entry still visible; Dismiss is retryable).
        try
        {
            if (File.Exists(item.PagePath)
                && AgentMemoryFrontmatter.TryParse(File.ReadAllText(item.PagePath), out var fm, out var body))
            {
                var stagingRoute = MirrorScanner.RouteOfPagePath(_vaultPath, item.PagePath);
                if (stagingRoute != null && fm.Status != "dismissed")
                    _backfill.AddPage(stagingRoute, "diary", item.Stem + ".md",
                        AgentMemoryFrontmatter.Serialize(fm with { Status = "dismissed" }) + body);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return PromotionResult.Fail(ex.Message); }

        // Retry-awareness: if a prior Dismiss already trashed the entry (crash
        // between the two steps, or Dismiss called twice), the entry is gone
        // from staging and TrashEntry would report a spurious "source file not
        // found" failure. The desired end state - page dismissed, entry gone -
        // already holds, so report success instead of re-trashing.
        if (!IsEntryStillStaged(item))
            return PromotionResult.Ok(item.PagePath);

        var trash = _organizer.TrashEntry(item.EntryFilePath, item.EntryTimestamp,
            item.EntryShortId, movePages: false);
        return trash.Success ? PromotionResult.Ok(item.PagePath)
                             : PromotionResult.Fail(trash.Error ?? "trash failed");
    }

    /// <summary>
    /// True when the staged entry <paramref name="item"/> refers to is still
    /// present in its staging diary file. Requires both a FindEntry match
    /// (ShortId-first, same disambiguation rule as RerouteEntry/TrashEntry) AND
    /// that the matched entry's own timestamp agrees with
    /// <see cref="StagedMemoryRef.EntryTimestamp"/> - a ShortId hit alone is not
    /// enough, because FindEntry returns a ShortId match regardless of how far
    /// off the supplied timestamp is, which would otherwise let a stale or
    /// malformed ref be treated as "still staged" and rerouted into the wrong
    /// year's diary file. False (via File.Exists or the timestamp check) means
    /// either the entry was already moved out by a prior run, or item is a bad ref.
    /// </summary>
    private static bool IsEntryStillStaged(StagedMemoryRef item)
    {
        if (!File.Exists(item.EntryFilePath)) return false;
        var found = EntryParser.EntryParser.FindEntry(
            File.ReadAllText(item.EntryFilePath), item.EntryTimestamp, item.EntryShortId);
        return found != null && Math.Abs((found.Timestamp - item.EntryTimestamp).TotalSeconds) < 1;
    }
}
