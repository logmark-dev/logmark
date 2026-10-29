using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Services.EntryCache;
using LogMark.Services.Pages;

namespace LogMark.Services.Hosting;

/// <summary>
/// The kernel-pure vault service graph shared by every host composition root
/// (MCP server, standalone daemon, desktop app). Extracted from
/// McpServerBootstrap.InitializeVaultServices (substrate split Phase 3).
/// Closed concerns stay parameters: sensitivity arrives as the kernel-seam
/// interface or not at all; license, semantic search (local embedding model),
/// feed storage, and protocol stores are assembled by the closed callers on
/// top of this.
/// </summary>
public static class VaultHostBootstrap
{
    public static VaultHostServices Build(
        string vaultPath,
        IRoutePromptService? routePrompt = null,
        ISensitivityService? sensitivity = null)
    {
        var notationParser = new NotationParser();
        var systemLogger = new SystemLogger(vaultPath);

        var entryCache = new EntryCache.EntryCache(vaultPath);
        entryCache.StartWarmup();

        var vaultWriter = new VaultWriter(entryCache);
        var vaultService = new VaultService(vaultPath, notationParser, vaultWriter: vaultWriter);

        var routeService = new RouteService(
            vaultPath,
            systemLogger,
            routePrompt ?? new HeadlessRoutePromptService(),
            vaultWriter: vaultWriter,
            sensitivity: sensitivity);
        routeService.LoadRoutes();
        routeService.RoutesUpdated += entryCache.InvalidateRoutes;

        var searchService = new SearchService(entryCache);
        var entryResolver = new EntryResolver(entryCache, vaultPath);
        var sessionService = new SessionService(vaultPath, systemLogger, vaultWriter: vaultWriter);
        var taskTracker = new TaskTracker(vaultPath, systemLogger, vaultWriter: vaultWriter);
        var subpageWriter = new SubpageWriter(
            vaultPath, vaultWriter: vaultWriter,
            routePathResolver: routeService.ResolveRouteFolderPath);
        var entryPageLinker = new EntryPageLinker(vaultPath, subpageWriter, vaultWriter);

        return new VaultHostServices(
            vaultPath,
            notationParser,
            systemLogger,
            entryCache,
            vaultWriter,
            vaultService,
            routeService,
            searchService,
            entryResolver,
            sessionService,
            taskTracker,
            subpageWriter,
            entryPageLinker);
    }
}

/// <summary>The kernel-pure service graph for one hosted vault.</summary>
public sealed record VaultHostServices(
    string VaultPath,
    NotationParser NotationParser,
    SystemLogger Logger,
    EntryCache.EntryCache EntryCache,
    VaultWriter VaultWriter,
    VaultService VaultService,
    RouteService RouteService,
    SearchService SearchService,
    EntryResolver EntryResolver,
    SessionService SessionService,
    TaskTracker TaskTracker,
    SubpageWriter SubpageWriter,
    EntryPageLinker EntryPageLinker);

/// <summary>
/// Headless route resolution: with exactly one section, file under that
/// section's convention; with no sections, file flat; with several, decline
/// rather than guess or file at the vault root.
/// </summary>
public class HeadlessRoutePromptService : IRoutePromptService
{
    public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
    {
        // More than one section and no human to pick: decline. Filing flat
        // put +USD and +Security beside projects/ and domains/ (2026-08/09).
        if (options.Choices.Count > 1)
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Cancel, null));

        var path = options.Choices.Count == 1 ? options.Choices[0].SuggestedPath : routeName;
        return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
    }
}
