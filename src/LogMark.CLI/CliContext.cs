using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.CLI;

/// <summary>
/// Shared context for CLI commands with lazy service initialization.
/// Services are created on first access, following the same wiring pattern as MCP Program.cs.
///
/// This class exposes ONLY kernel services (types under LogMark.Kernel). Closed
/// services (Behaviors, Inference, Thinktank, Feed, Sensitivity, Ink,
/// ImageAsset, embeddings, transcript readers, License/Usage) live on the
/// closed <c>ProducerCliContext</c> subclass in LogMark.CLI.Producers, wired in
/// only for the closed build. The seams the open commands still need
/// (sensitivity for private-route writes, semantic search, usage recording)
/// are declared here as virtual no-ops / null returns and overridden by the
/// producer. This keeps the open build compiling against Kernel alone.
/// </summary>
public class CliContext
{
    public string? VaultPath { get; }
    public bool JsonOutput { get; }
    public bool Quiet { get; }

    private NotationParser? _notationParser;
    private IVaultFileWriter? _vaultWriter;
    private VaultService? _vaultService;
    private SystemLogger? _systemLogger;
    private RouteService? _routeService;
    private EntryCache? _entryCache;
    private EntryCache? _entryCacheOverride;
    private SearchService? _searchService;
    private EntryResolver? _entryResolver;
    private SessionService? _sessions;
    private bool _sessionsInitialized;

    /// <summary>
    /// Test/producer-injected sensitivity service. Held here (the type is a
    /// Kernel interface) so open-side tests can inject a stub via
    /// <see cref="SetSensitivityServiceForTesting"/> and the producer can seed
    /// the real service. Null in the open build unless a test injects one.
    /// </summary>
    protected ISensitivityService? SensitivityOverride;
    protected bool SensitivityOverrideSet;

    public CliContext(string? vaultPath, bool jsonOutput, bool quiet)
    {
        VaultPath = vaultPath;
        JsonOutput = jsonOutput;
        Quiet = quiet;
    }

    public NotationParser NotationParser => _notationParser ??= new NotationParser();

    public IVaultFileWriter VaultWriter => _vaultWriter ??= new VaultWriter();

    public VaultService VaultService
    {
        get
        {
            if (_vaultService == null)
            {
                EnsureVault();
                _vaultService = new VaultService(VaultPath!, NotationParser, RouteService, SystemLogger, vaultWriter: VaultWriter);
            }
            return _vaultService;
        }
    }

    public SystemLogger SystemLogger
    {
        get
        {
            if (_systemLogger == null)
            {
                EnsureVault();
                _systemLogger = new SystemLogger(VaultPath!);
            }
            return _systemLogger;
        }
    }

    public RouteService RouteService
    {
        get
        {
            if (_routeService == null)
            {
                EnsureVault();
                _routeService = new RouteService(VaultPath!, SystemLogger, new HeadlessRoutePromptService(), vaultWriter: VaultWriter, sensitivity: SensitivityService);
                _routeService.LoadRoutes();
            }
            return _routeService;
        }
    }

    /// <summary>
    /// Sensitivity service for private-route enforcement on CLI writes. Null in
    /// the open build (private-route writes then throw
    /// RoutePrivateDesktopOnlyException / degrade), non-null on the closed
    /// ProducerCliContext or when a test injects one. ISensitivityService is a
    /// Kernel interface so the seam lives on the open base; only the concrete
    /// SensitivityService construction is closed.
    /// </summary>
    public virtual ISensitivityService? SensitivityService => SensitivityOverride;

    /// <summary>
    /// Replace the sensitivity service for tests without touching the filesystem.
    /// </summary>
    public void SetSensitivityServiceForTesting(ISensitivityService? sensitivity)
    {
        SensitivityOverrideSet = true;
        SensitivityOverride = sensitivity;
    }

    public EntryCache EntryCache
    {
        get
        {
            if (_entryCacheOverride != null)
                return _entryCacheOverride;
            if (_entryCache == null)
            {
                EnsureVault();
                _entryCache = new EntryCache(VaultPath!);
                _entryCache.StartWarmup();
            }
            return _entryCache;
        }
    }

    /// <summary>
    /// Test-only: substitute the lazily-constructed EntryCache with a caller-built
    /// instance (e.g. one backed by an IFileAccessor whose warmup never finishes)
    /// so timeout logic in downstream commands - AgentCaptureCommand's bounded
    /// warmup wait chief among them - can be exercised deterministically instead
    /// of racing real file-system timing. Mirrors SetSensitivityServiceForTesting.
    /// Null (the default) preserves production behaviour: the lazy getter above
    /// constructs its own EntryCache against VaultPath.
    /// </summary>
    public void SetEntryCacheForTesting(EntryCache? entryCache)
    {
        _entryCacheOverride = entryCache;
    }

    public SearchService SearchService => _searchService ??= new SearchService(EntryCache);

    public EntryResolver EntryResolver => _entryResolver ??= new EntryResolver(EntryCache, VaultPath!);

    /// <summary>
    /// Session service for the running vault. Null when VaultPath is not set.
    /// SessionService is a Kernel service (the same _sessions/*.md records MCP's
    /// start_session/end_session write), so it lives on the open base alongside
    /// VaultService and RouteService rather than the closed ProducerCliContext.
    /// </summary>
    public SessionService? Sessions
    {
        get
        {
            if (!_sessionsInitialized)
            {
                _sessionsInitialized = true;
                if (!string.IsNullOrEmpty(VaultPath))
                    _sessions = new SessionService(VaultPath!, SystemLogger, VaultWriter);
            }
            return _sessions;
        }
    }

    /// <summary>
    /// Semantic search service backed by T1 embeddings. Null in the open build
    /// (search degrades to lexical-only). Overridden by the closed
    /// ProducerCliContext to return the real local-embedding-backed service when
    /// the model is present. The return type is a Kernel interface so open
    /// search code (SearchCommand) binds against it directly.
    /// </summary>
    public virtual ISemanticSearchService? SemanticSearchService => null;

    /// <summary>
    /// Distilled sessions staged for review in this route, or null when the
    /// queue is unreachable. SessionQueueStore lives in LogMark.Core, which
    /// this assembly deliberately does not reference (OpenBuildContractTests),
    /// so the producers build supplies the number and the open build reports
    /// nothing rather than a wrong zero.
    /// </summary>
    public virtual int? PendingDistillReviewCount(string? route) => null;

    /// <summary>
    /// Record a write for usage/quota tracking. No-op in the open build (usage
    /// tracking is a closed concern - IUsageStore lives in Core). Overridden by
    /// the closed ProducerCliContext to persist via the usage store.
    /// </summary>
    public virtual void RecordWrite(string surface, string type, string? route) { }

    /// <summary>
    /// Wait for entry cache warmup to complete. Call before query operations.
    /// </summary>
    public void WaitForCacheWarmup()
    {
        EntryCache.WarmupTask?.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Bounded variant of WaitForCacheWarmup: blocks the calling thread for at
    /// most <paramref name="timeout"/> waiting for warmup to finish, returning
    /// whether it completed in time. For a caller whose entire process budget
    /// is a hook deadline - AgentCaptureCommand's PreCompact path fires once per
    /// compaction, many times within a single session, each a fresh process
    /// against a cache with no persisted index to load from, on a vault that
    /// only grows - the unbounded overload above can push the whole hook past
    /// its own timeout. A caller that gets `false` back must treat the query as
    /// "unknown" and degrade exactly as it would for "nothing found"; it must
    /// never guess and never wait again. On `false`, the warmup task keeps
    /// running in the background (this does not cancel it) - a later call in
    /// the same process would find it further along, but nothing in this
    /// process should call again with a longer budget just to keep waiting.
    /// </summary>
    public bool WaitForCacheWarmup(TimeSpan timeout)
    {
        var warmupTask = EntryCache.WarmupTask;
        return warmupTask == null || warmupTask.Wait(timeout);
    }

    /// <summary>Write result to stdout (plain or JSON depending on --json flag).</summary>
    public void WriteResult(string text) => Console.WriteLine(text);

    /// <summary>Write info to stderr (suppressed by --quiet).</summary>
    public void WriteInfo(string text)
    {
        if (!Quiet)
            Console.Error.WriteLine(text);
    }

    /// <summary>Write error to stderr (never suppressed).</summary>
    public virtual void WriteError(string text) => Console.Error.WriteLine(text);

    protected void EnsureVault()
    {
        if (string.IsNullOrEmpty(VaultPath))
            throw new InvalidOperationException("Vault path not set. Use --vault, LOGMARK_VAULT env var, or run LogMark app first.");
    }

    // NOTE: this class used to expose two bootstrap-token reader methods here
    // (bootstrap.json reads of the installation token / capture-bridge token)
    // on the open base. Both were read-but-unused in OSS builds -- their
    // only callers are the closed ProducerCliContext (LogMark.CLI.Producers)
    // and the closed ThinktankPublishCommand. They now live on
    // ProducerCliContext itself, behind the producer seam, so the
    // OSS-published open tree never touches these paid-tier / pairing
    // secrets at all. See
    // OpenBuildContractTests.Open_tree_sources_do_not_read_licensing_tokens.
}

/// <summary>
/// Headless route prompt service (same policy as MCP): creates under the sole
/// section or flat in a sectionless vault, declines when there are several.
/// </summary>
public class HeadlessRoutePromptService : IRoutePromptService
{
    public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
    {
        // More than one section and no human to pick: decline. Filing flat
        // put +USD and +Security beside projects/ and domains/ (2026-08/09).
        if (options.Choices.Count > 1)
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Cancel, null));

        // Section-agnostic path: when the vault has exactly one section, file
        // under that section's convention; with none, file flat. No hardcoded
        // section prefix.
        var path = options.Choices.Count == 1 ? options.Choices[0].SuggestedPath : routeName;
        return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
    }
}
