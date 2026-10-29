using LogMark.Services.EntryCache;
using LogMark.Services.Hosting;

namespace LogMark.Mcp;

/// <summary>
/// Open, kernel-only vault-service initializer. Builds the kernel service graph
/// (VaultHostBootstrap) and wires it onto <see cref="VaultServices"/>. The open
/// build has no sensitivity masking gate, no local-embedding semantic search,
/// and no feed/protocol/view stores: those are Core-backed and assembled by the closed
/// producer's own initializer, which replaces this one when a producer is present.
/// Called at startup and on every vault switch when running the open build.
/// </summary>
public static class McpServerBootstrap
{
    /// <summary>How long a tool call waits for EntryCache warmup before answering anyway.</summary>
    public static readonly TimeSpan CacheWarmupBound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Wait (bounded) for the current vault's EntryCache warmup. Reads
    /// services.EntryCache at call time, so a vault switch is followed. A
    /// warmup that outlives the bound is left running and the call proceeds
    /// on the partial cache rather than hanging (bead lmk-fjcj).
    /// </summary>
    public static async Task AwaitCacheWarmupAsync(VaultServices services, TimeSpan bound, CancellationToken ct)
    {
        if (services.EntryCache?.WarmupTask is not { IsCompleted: false } warmup)
            return;
        try
        {
            await warmup.WaitAsync(bound, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"logmark-mcp: entry cache still warming after {bound.TotalSeconds:0}s; answering from a partial cache");
        }
    }

    public static void InitializeOpenVaultServices(VaultServices services, McpVaultInit init)
    {
        // Kernel-pure service graph (substrate split Phase 3 shared bootstrap).
        // sensitivity: null -> no masking; the open build declares no private routes.
        var kernel = VaultHostBootstrap.Build(init.VaultPath, new HeadlessRoutePromptService(), sensitivity: null);

        var sessionContext = new McpSessionContext(kernel.SessionService, kernel.Logger, init.AgentId, init.DefaultRoute, init.HostPid);
        sessionContext.SessionTimeout = TimeSpan.FromMinutes(init.SessionTimeoutMinutes);

        services.VaultPath = init.VaultPath;
        services.VaultWriter = kernel.VaultWriter;
        // No closed masking gate in the open build. Organize/lifecycle edits are
        // in-place rewrites that pass no route boundary, so EntryOrganizer runs
        // with a null gate.
        services.VaultWriteGate = null;
        services.VaultService = kernel.VaultService;
        services.NotationParser = kernel.NotationParser;
        services.RouteService = kernel.RouteService;
        services.Logger = kernel.Logger;
        services.EntryCache = kernel.EntryCache;
        services.SearchService = kernel.SearchService;
        services.EntryResolver = kernel.EntryResolver;
        services.SessionService = kernel.SessionService;
        services.SessionContext = sessionContext;
        services.TaskTracker = kernel.TaskTracker;
        services.SubpageWriter = kernel.SubpageWriter;
        services.EntryPageLinker = kernel.EntryPageLinker;
        // No local embedding model in the open build; search degrades to lexical-only.
        services.SemanticSearchService = null;
    }
}
