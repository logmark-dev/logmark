using System.Text.Json;
using LogMark.Models;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Promotes an orphaned route-shaped folder into a declared route in routes.md.
///
/// Fix verb for <c>OrphanedEntryLint</c>'s orphan-folder issues. Payload is
/// <c>{"folderPath":"projects/cco","defaultName":"cco"}</c>. The route is created
/// directly: alias = <c>defaultName</c> (the leaf folder name), path =
/// <c>folderPath</c>. Both are fully determined by where the stranded entries
/// physically live -- a covering route MUST point at that exact path -- so no
/// prompt is needed. The alias can be renamed afterward in Routes settings.
///
/// A direct creation (rather than the capture-time route prompt) is required here:
/// the prompt renders inside the tray CapturePanel, which is not reachable from the
/// NativeViewer-hosted Lint inbox, so an awaited prompt would hang indefinitely.
///
/// A name collision (an existing route already uses <c>defaultName</c>) is a
/// failure: adding a duplicate would leave both aliases ambiguous.
/// </summary>
public class AddRouteFixer : ILintFixer
{
    public string Id => "add-route";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RouteService _routeService;

    public AddRouteFixer(RouteService routeService)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
    }

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "FixPayloadJson is empty", null));

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(issue.FixPayloadJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new LintFixResult(false, $"Malformed payload: {ex.Message}", null));
        }

        if (payload == null
            || string.IsNullOrWhiteSpace(payload.FolderPath)
            || string.IsNullOrWhiteSpace(payload.DefaultName))
            return Task.FromResult(new LintFixResult(false, "Payload missing folderPath or defaultName", null));

        // Collision check: the orphan folder's leaf name may already be a route alias
        // pointing elsewhere. Adding a duplicate would leave both ambiguous.
        var existing = ctx.Routes.GetRoutes()
            .FirstOrDefault(r => string.Equals(r.Name, payload.DefaultName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return Task.FromResult(new LintFixResult(false,
                $"Route name '{payload.DefaultName}' already exists (path: {existing.Path ?? "none"})", null));

        ct.ThrowIfCancellationRequested();
        var route = new Route { Name = payload.DefaultName, Path = payload.FolderPath };
        _routeService.AddRoute(route);
        return Task.FromResult(new LintFixResult(true, null, $"Added route +{route.Name} -> {route.Path}"));
    }

    private record Payload(string FolderPath, string DefaultName);
}
