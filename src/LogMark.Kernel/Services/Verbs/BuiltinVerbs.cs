using System.Linq;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Verbs;

/// <summary>
/// Registers the wire-existing verbs whose backing already ships in Core.
/// Each registration binds one Verb record to a handler that calls the existing
/// service and invalidates the cache. New verbs follow this exact shape.
/// </summary>
public static class BuiltinVerbs
{
    public static void RegisterReroute(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        EntryOrganizer organizer,
        RouteService routeService)
    {
        var verb = Verb.Operation(
            "reroute_entry",
            VerbBand.Organize,
            "Move an entry to a different route");

        registry.Register(verb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            var targetRoute = ctx.GetArg("target_route");

            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));
            if (string.IsNullOrWhiteSpace(targetRoute))
                return Task.FromResult(VerbResult.Fail("Missing required arg: target_route"));

            var resolved = cache.ResolveShortId(entryId);
            if (resolved == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            // Resolve the route NAME to its registered vault-relative path. This is
            // the ghost-route guard: RerouteEntry treats its route argument as a
            // path, so passing the raw name would both misplace the file (name vs
            // path) and skip routes.md registration. Fail fast when the route is
            // not declared so the agent creates it (create_route) first.
            var routeName = targetRoute.TrimStart('+').Trim();
            var routePath = routeService.ResolveRoutePath(routeName);
            if (string.IsNullOrWhiteSpace(routePath))
                return Task.FromResult(VerbResult.Fail(
                    $"Route +{routeName} does not exist. Create the route first, then reroute."));

            var filePath = resolved.Value.filePath;
            var entry = resolved.Value.entry;

            var result = organizer.RerouteEntry(
                filePath,
                entry.Timestamp,
                routePath,
                entry.Type,
                entry.ShortId);

            if (!result.Success)
                return Task.FromResult(VerbResult.Fail(result.Error ?? "Reroute failed"));

            cache.InvalidateFile(filePath);
            if (result.TargetFilePath != null)
                cache.InvalidateFile(Path.Combine(cache.VaultPath, result.TargetFilePath));
            foreach (var referrer in result.RewrittenLinkFiles)
                cache.InvalidateFile(Path.Combine(cache.VaultPath, referrer));

            var linkNote = result.RewrittenLinkFiles.Count > 0
                ? $"; rewrote inbound links in {result.RewrittenLinkFiles.Count} file{(result.RewrittenLinkFiles.Count == 1 ? "" : "s")}"
                : "";
            return Task.FromResult(VerbResult.Ok($"Moved {entryId} to +{routeName}{linkNote}"));
        });
    }

    public static void RegisterConvertType(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        EntryOrganizer organizer)
    {
        var verb = Verb.Operation(
            "convert_type",
            VerbBand.Organize,
            "Convert an entry to a different type (task, idea, block, decision, question, gap, analogy, log)");

        registry.Register(verb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            var newTypeName = ctx.GetArg("new_type");
            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));
            if (string.IsNullOrWhiteSpace(newTypeName))
                return Task.FromResult(VerbResult.Fail("Missing required arg: new_type"));

            var newType = ParseEntryType(newTypeName);
            if (newType == null)
                return Task.FromResult(VerbResult.Fail($"Unknown entry type: {newTypeName}"));

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            var (filePath, entry) = located.Value;
            var result = organizer.ConvertEntryType(filePath, entry.Timestamp, newType.Value, entry.ShortId);
            if (!result.Success)
                return Task.FromResult(VerbResult.Fail(result.Error ?? "Convert failed"));

            cache.InvalidateFile(filePath);
            if (result.TargetFilePath != null)
                cache.InvalidateFile(Path.Combine(cache.VaultPath, result.TargetFilePath));
            foreach (var referrer in result.RewrittenLinkFiles)
                cache.InvalidateFile(Path.Combine(cache.VaultPath, referrer));

            var linkNote = result.RewrittenLinkFiles.Count > 0
                ? $"; rewrote inbound links in {result.RewrittenLinkFiles.Count} file{(result.RewrittenLinkFiles.Count == 1 ? "" : "s")}"
                : "";
            return Task.FromResult(VerbResult.Ok($"Converted {entryId} to {newTypeName}{linkNote}"));
        });
    }

    public static void RegisterAmend(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        EntryOrganizer organizer)
    {
        var verb = Verb.Operation(
            "amend_entry",
            VerbBand.Organize,
            "Append an amend note to an existing entry");

        registry.Register(verb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            var amendText = ctx.GetArg("amend_text");
            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));
            if (string.IsNullOrWhiteSpace(amendText))
                return Task.FromResult(VerbResult.Fail("Missing required arg: amend_text"));

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            var (filePath, entry) = located.Value;
            var result = organizer.AmendEntry(filePath, entry.Timestamp, amendText, "user", entry.ShortId);
            if (!result.Success)
                return Task.FromResult(VerbResult.Fail(result.Error ?? "Amend failed"));

            cache.InvalidateFile(filePath);
            return Task.FromResult(VerbResult.Ok($"Amended {entryId}"));
        });
    }

    public static void RegisterCreateRoute(VerbRegistry registry, RouteService routeService)
    {
        var verb = Verb.Operation(
            "create_route", VerbBand.Organize, "Create a new project route", idempotent: true);

        registry.Register(verb, ctx =>
        {
            var name = ctx.GetArg("name")?.Trim().TrimStart('+').Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(VerbResult.Fail("Missing required arg: name"));

            var existing = routeService.GetRoutes()
                .FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return Task.FromResult(VerbResult.Ok($"Route +{name} already exists"));

            var path = ctx.GetArg("path")?.Trim();
            if (string.IsNullOrWhiteSpace(path))
                path = InferRoutePath(routeService, name, ctx.GetArg("section")?.Trim());

            var description = ctx.GetArg("description")?.Trim();
            var route = new Route
            {
                Name = name,
                Path = path,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
            };

            routeService.AddRoute(route);
            return Task.FromResult(VerbResult.Ok($"Created route +{name} at {path}"));
        });
    }

    /// <summary>
    /// Resolve the vault-relative path for a new route without imposing a section.
    /// The section set is vault-specific (archetype/posture dependent), so there is
    /// no hardcoded "projects/" prefix. Precedence: an explicit, known section arg;
    /// else the sole section when the vault has exactly one; else flat top-level
    /// (the route name), which is correct for section-less/minimalist vaults and
    /// the honest fallback for multi-section vaults where the agent named none.
    /// </summary>
    private static string InferRoutePath(RouteService routeService, string name, string? section)
    {
        var routes = routeService.GetRoutes();
        var sections = routeService.GetSections();

        if (!string.IsNullOrWhiteSpace(section))
        {
            var match = sections.FirstOrDefault(s => s.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return RouteService.InferSuggestedPath(match, name, routes);
        }

        if (sections.Count == 1)
            return RouteService.InferSuggestedPath(sections[0], name, routes);

        return name;
    }

    public static void RegisterLinkEntryToPage(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        EntryOrganizer organizer)
    {
        var verb = Verb.Operation(
            "link_entry_to_page", VerbBand.Organize,
            "Insert a wiki link from an entry to a page", idempotent: true);

        registry.Register(verb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            var wikiPath = ctx.GetArg("wiki_path");
            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));
            if (string.IsNullOrWhiteSpace(wikiPath))
                return Task.FromResult(VerbResult.Fail("Missing required arg: wiki_path"));

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            var (filePath, entry) = located.Value;
            var displayText = ctx.GetArg("display_text");
            if (string.IsNullOrWhiteSpace(displayText))
                displayText = wikiPath;

            var result = organizer.LinkEntryToPage(filePath, entry.Timestamp, wikiPath, displayText, entry.ShortId);
            if (!result.Success)
                return Task.FromResult(VerbResult.Fail(result.Error ?? "Link failed"));

            cache.InvalidateFile(filePath);
            return Task.FromResult(VerbResult.Ok($"Linked {entryId} to [[{wikiPath}]]"));
        });
    }

    public static void RegisterRetag(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        EntryOrganizer organizer)
    {
        var verb = Verb.Operation(
            "retag_entry", VerbBand.Organize,
            "Add or remove #tags on an entry", idempotent: true);

        registry.Register(verb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));

            var add = ParseTagList(ctx.GetArg("add"));
            var remove = ParseTagList(ctx.GetArg("remove"));
            if (add.Count == 0 && remove.Count == 0)
                return Task.FromResult(VerbResult.Fail("Provide at least one tag in 'add' or 'remove'"));

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            var (filePath, entry) = located.Value;
            var result = organizer.RetagEntry(filePath, entry.Timestamp, add, remove, entry.ShortId);
            if (!result.Success)
                return Task.FromResult(VerbResult.Fail(result.Error ?? "Retag failed"));

            cache.InvalidateFile(filePath);

            var parts = new List<string>();
            if (add.Count > 0) parts.Add("added " + string.Join(", ", add.Select(t => "#" + t.TrimStart('#'))));
            if (remove.Count > 0) parts.Add("removed " + string.Join(", ", remove.Select(t => "#" + t.TrimStart('#'))));
            return Task.FromResult(VerbResult.Ok($"Retagged {entryId}: {string.Join("; ", parts)}"));
        });
    }

    /// <summary>Split a comma/space-separated tag argument into individual tag tokens.</summary>
    private static List<string> ParseTagList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(new[] { ',', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Registers the six REFLECT-band lifecycle verbs. Five (resolve/dismiss
    /// block, promote/reject decision, dismiss task) swap a lifecycle tag
    /// via DecisionApprovalService, which invalidates the cache on write. The
    /// sixth (complete_task) strikes the task via TaskTracker, which does not
    /// invalidate, so its handler does. These are the same backings the MCP
    /// update_entry tool wraps; both write through the raw IVaultFileWriter (no
    /// write gate) because a lifecycle change is an in-place edit, not a
    /// cross-route move, so no masking transition occurs.
    /// </summary>
    public static void RegisterReflect(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        DecisionApprovalService approvals,
        TaskTracker tasks)
    {
        RegisterLifecycleTag(registry, cache, approvals,
            "resolve_block", "Mark a block as resolved", EntryType.Block,
            (svc, path, id, resolution) => svc.ResolveBlockAsync(path, id, resolution));
        RegisterLifecycleTag(registry, cache, approvals,
            "dismiss_block", "Dismiss a block that no longer applies", EntryType.Block,
            (svc, path, id, resolution) => svc.DismissBlockAsync(path, id, resolution));
        RegisterLifecycleTag(registry, cache, approvals,
            "promote_decision", "Promote a decision to frozen (confirmed)", EntryType.Decision,
            (svc, path, id, resolution) => svc.PromoteDecisionAsync(path, id));
        RegisterLifecycleTag(registry, cache, approvals,
            "reject_decision", "Reject a decision", EntryType.Decision,
            (svc, path, id, resolution) => svc.RejectDecisionAsync(path, id));

        var completeVerb = Verb.Operation(
            "complete_task", VerbBand.Reflect, "Mark a task as complete", idempotent: true);

        registry.Register(completeVerb, ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            if (string.IsNullOrWhiteSpace(entryId))
                return Task.FromResult(VerbResult.Fail("Missing required arg: entry_id"));

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return Task.FromResult(VerbResult.Fail($"Entry not found: {entryId}"));

            var (filePath, entry) = located.Value;
            if (entry.Type != EntryType.Task)
                return Task.FromResult(VerbResult.Fail(
                    $"complete_task is only valid on task entries; {entryId} is a {entry.Type.ToString().ToLowerInvariant()}."));

            if (entry.IsCompleted)
                return Task.FromResult(VerbResult.Ok($"Task {entryId} was already complete"));

            var resolution = ctx.GetArg("resolution");
            // Report what was verified, not what was attempted: CompleteTask
            // returns false when it rewrote nothing, and reporting that as
            // success is what let a grooming run log 25 completions while the
            // vault's open task count went up.
            if (!tasks.CompleteTask(filePath, entry.ShortId ?? entryId, DateTime.Now, resolution))
                return Task.FromResult(VerbResult.Fail(
                    $"Could not complete {entryId}: no matching entry was rewritten in {filePath}"));

            cache.InvalidateFile(filePath);
            return Task.FromResult(VerbResult.Ok($"Completed {entryId}"));
        });

        var dismissVerb = Verb.Operation(
            "dismiss_task", VerbBand.Reflect,
            "Dismiss a task that no longer applies (overtaken, dead, or duplicate). Requires a reason.",
            idempotent: true);

        registry.Register(dismissVerb, async ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            if (string.IsNullOrWhiteSpace(entryId))
                return VerbResult.Fail("Missing required arg: entry_id");

            // Mandatory, unlike the block exits where it is optional. A task
            // dismissed without a stated reason is indistinguishable from a task
            // quietly dropped, and the whole point of this exit is that closing
            // an overtaken task should record the truth rather than a falsehood.
            var resolution = ctx.GetArg("resolution");
            if (string.IsNullOrWhiteSpace(resolution))
                return VerbResult.Fail(
                    "dismiss_task requires a resolution naming why the task no longer applies.");

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return VerbResult.Fail($"Entry not found: {entryId}");

            var (filePath, entry) = located.Value;
            if (entry.Type != EntryType.Task)
                return VerbResult.Fail(
                    $"dismiss_task is only valid on task entries; {entryId} is a {entry.Type.ToString().ToLowerInvariant()}.");

            if (entry.IsCompleted)
                return VerbResult.Fail(
                    $"Task {entryId} is already complete; dismissing a finished task would overwrite the truer state.");

            var ok = await approvals.DismissTaskAsync(filePath, entry.ShortId ?? entryId, resolution);
            return ok
                ? VerbResult.Ok($"Dismissed {entryId}: {resolution}")
                : VerbResult.Fail($"dismiss_task failed for {entryId}");
        });
    }

    /// <summary>
    /// Registers one exclusive-lifecycle-tag verb (block resolve/dismiss,
    /// decision promote/reject). Resolves the entry, guards its type, then calls
    /// the DecisionApprovalService method, which persists and invalidates the
    /// cache. Idempotent: re-applying an already-set tag returns success.
    /// Reads an optional "resolution" arg from the verb context and threads it
    /// to <paramref name="apply"/> unconditionally; the block lambdas forward it
    /// to DecisionApprovalService (see its class doc comment for the note's
    /// on-disk serialization), while the decision lambdas simply ignore it since
    /// PromoteDecisionAsync/RejectDecisionAsync take no resolution.
    /// </summary>
    private static void RegisterLifecycleTag(
        VerbRegistry registry,
        EntryCache.EntryCache cache,
        DecisionApprovalService approvals,
        string name,
        string summary,
        EntryType requiredType,
        Func<DecisionApprovalService, string, string, string?, Task<bool>> apply)
    {
        var verb = Verb.Operation(name, VerbBand.Reflect, summary, idempotent: true);

        registry.Register(verb, async ctx =>
        {
            var entryId = ctx.GetArg("entry_id");
            if (string.IsNullOrWhiteSpace(entryId))
                return VerbResult.Fail("Missing required arg: entry_id");

            var located = cache.ResolveShortId(entryId);
            if (located == null)
                return VerbResult.Fail($"Entry not found: {entryId}");

            var (filePath, entry) = located.Value;
            if (entry.Type != requiredType)
                return VerbResult.Fail(
                    $"{name} is only valid on {requiredType.ToString().ToLowerInvariant()} entries; {entryId} is a {entry.Type.ToString().ToLowerInvariant()}.");

            var resolution = ctx.GetArg("resolution");
            var ok = await apply(approvals, filePath, entry.ShortId ?? entryId, resolution);
            return ok
                ? VerbResult.Ok($"{name} applied to {entryId}")
                : VerbResult.Fail($"{name} failed for {entryId}");
        });
    }

    /// <summary>
    /// Maps a plain type-name argument ("task", "idea", ...) to EntryType.
    /// Explicit switch over only the 8 user-facing names (matches this verb's
    /// own summary string above), not Enum.TryParse(ignoreCase) -- TryParse
    /// would also accept "timetrack" (an internal-only type with no user-facing
    /// convert target), numeric strings ("5"), and out-of-range numbers ("99"),
    /// since enum parsing falls back to the underlying int for any digit string.
    /// No dedicated name-to-EntryType helper exists on EntryTypeMapper (it maps
    /// folder names, not plain type names), so this stays a private
    /// handler-local mapping rather than a new public mapper API.
    /// </summary>
    private static EntryType? ParseEntryType(string name) => name.Trim().ToLowerInvariant() switch
    {
        "task" => EntryType.Task,
        "idea" => EntryType.Idea,
        "block" => EntryType.Block,
        "decision" => EntryType.Decision,
        "question" => EntryType.Question,
        "gap" => EntryType.Gap,
        "analogy" => EntryType.Analogy,
        "log" => EntryType.Log,
        _ => null
    };
}
