using System.Text;
using LogMark.Mcp.Prompts;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryParser;
using LogMark.Services.Hosting;
using LogMark.Services.Providers;
using LogMark.Services.Verbs;

namespace LogMark.Mcp;

/// <summary>
/// Open MCP server composition root. Registers the kernel tool set and runs the
/// STDIO JSON-RPC server. The open build ships this alone (kernel-only): no
/// license gating, no cloud inference, no closed services. The closed
/// LogMark.Mcp.Producers assembly sets <see cref="Producer"/> before calling
/// <see cref="Run"/> to add the Pro/closed tools and services - explicit
/// registration, no assembly scanning, and the open tree compiles with the
/// producer project simply absent.
/// </summary>
public class Program
{
    /// <summary>
    /// Optional closed composition seam. The open build leaves this null and
    /// ships only the kernel tools. The closed build (LogMark.Mcp.Producers)
    /// assigns it. In-process tests that need the closed tools set it in their
    /// module initializer.
    /// </summary>
    public static IMcpProducer? Producer;

    /// <summary>The open build (no producer) is LogMark Engine; the closed build is LogMark.</summary>
    public static string ServerTitleFor(IMcpProducer? producer) =>
        producer is null ? LogMark.Services.Providers.ProductNames.Engine : LogMark.Services.Providers.ProductNames.Desktop;

    /// <summary>The command a hint tells the user to run: the open build ships
    /// as `logmark`, the desktop app's sidecar CLI is `lm`.</summary>
    public static string CliName => Producer is null ? "logmark" : "lm";

    public static Task<int> Main(string[] args) => Run(args);

    /// Last `--vault-override` or `--vault` wins. Both spellings are accepted
    /// because the unified binary documents `--vault` everywhere else.
    internal static string? ParseVaultOverride(string[] args)
    {
        string? value = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--vault-override" or "--vault")
            {
                value = args[i + 1];
                i++;
            }
        }
        return value;
    }

    public static async Task<int> Run(string[] args)
    {
        // Force UTF-8 on the Console code page. This affects diagnostic stderr writes
        // (Console.Error.WriteLine) on Windows hosts whose default console code page is not
        // UTF-8 (e.g. cp1252, cp866). The MCP protocol path itself does NOT rely on these
        // settings -- it goes through Console.OpenStandardInput/Output which return raw byte
        // streams. UTF-8 decoding for the JSON-RPC wire format is enforced explicitly in
        // McpServer.ReadMessageAsync / WriteResponseAsync.
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.Error.WriteLine("logmark-mcp: Starting...");
        Console.Error.Flush();

        // Parse command line arguments
        string agentId = "mcp";
        string? defaultRoute = null;
        int sessionTimeoutMinutes = 30;
        string? bootstrapPathOverride = null;
        string? vaultOverride = ParseVaultOverride(args);

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--agent" || args[i] == "-a") && i + 1 < args.Length)
            {
                agentId = args[i + 1];
                i++;
            }
            else if ((args[i] == "--route" || args[i] == "-r") && i + 1 < args.Length)
            {
                defaultRoute = args[i + 1];
                i++;
            }
            else if (args[i] == "--session-timeout" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var timeout))
                    sessionTimeoutMinutes = timeout;
                i++;
            }
            else if (args[i] == "--bootstrap-path" && i + 1 < args.Length)
            {
                bootstrapPathOverride = args[i + 1];
                i++;
            }
        }

        // Resolve vault path. --vault-override is the explicit tier; otherwise
        // VaultResolutionPolicy tries a live-hosted provider's ActiveVault
        // before falling back to bootstrap.json. bootstrapPath is resolved
        // either way and handed to producer?.BuildServerHooks below -- the
        // closed producer reads the installation token (cloud inference
        // auth) from it there; the open build never reads that token itself.
        var bootstrapPath = BootstrapReader.ResolveBootstrapPath(bootstrapPathOverride);
        var vaultPath = VaultResolutionPolicy.Resolve(
            explicitVault: vaultOverride,
            new ProviderManifestStore(ProviderManifestStore.DefaultRoot()),
            VaultHostLock.IsCurrentlyHosted,
            () => BootstrapReader.ReadVaultPath(bootstrapPath));

        if (string.IsNullOrEmpty(vaultPath))
        {
            Console.Error.WriteLine($"Error: No vault configured. Run the LogMark desktop app first, or pass --vault-override <path>.");
            Console.Error.WriteLine($"  Bootstrap: {bootstrapPath}");
            return 1;
        }

        if (!Directory.Exists(vaultPath))
        {
            Console.Error.WriteLine($"Error: Vault path does not exist: {vaultPath}");
            return 1;
        }

        if (vaultOverride != null)
        {
            Console.Error.WriteLine($"logmark-mcp: --vault-override active: {vaultPath} (bootstrap.json vault path ignored)");
            Console.Error.Flush();
        }

        var producer = Producer;

        // Per-call gating + write telemetry. Null in the open build (all
        // registered tools available, no usage recorded).
        var serverHooks = producer?.BuildServerHooks(bootstrapPath);

        // Vault-dependent services. The closed build returns a subclass carrying
        // the Core-typed accessors; the open build gets the kernel-only holder.
        var services = producer?.CreateServices() ?? new VaultServices();

        // Resolved before service init because the session stamp needs the host pid,
        // and re-init on vault switch must stamp it too. Also feeds the watchdog below.
        var parentProcess = ParentProcess.TryGetParent();

        void InitializeVaultServices(string vp)
        {
            var init = new McpVaultInit(vp, agentId, defaultRoute, sessionTimeoutMinutes, parentProcess?.Id);
            if (producer != null)
                producer.InitializeVaultServices(services, init);
            else
                McpServerBootstrap.InitializeOpenVaultServices(services, init);
        }

        InitializeVaultServices(vaultPath);

        // Register tools: open kernel set, then any closed tools the producer adds.
        var toolRegistry = new ToolRegistry();
        RegisterOpenTools(toolRegistry, services);
        producer?.RegisterClosedTools(toolRegistry, services, bootstrapPath);

        // Register prompts
        var promptRegistry = new PromptRegistry();
        promptRegistry.Register(new LeonardoDepthPrompt());
        promptRegistry.Register(new VaultToolsGuidePrompt(toolRegistry));
        producer?.RegisterClosedPrompts(promptRegistry, toolRegistry);

        Console.Error.WriteLine($"logmark-mcp: Initialized with vault: {vaultPath}");
        Console.Error.Flush();

        // Run server
        var server = new McpServer(toolRegistry, promptRegistry, serverHooks);
        server.ServerTitle = ServerTitleFor(producer);
        server.BeforeToolCall = ct => McpServerBootstrap.AwaitCacheWarmupAsync(
            services, McpServerBootstrap.CacheWarmupBound, ct);

        // Start vault watcher for auto-switching -- only when no --vault-override is active.
        // With override, the bootstrap.json vault path is intentionally ignored, so watching
        // it would just churn services every time the desktop app saves a vault switch.
        VaultWatcher? vaultWatcher = null;
        if (vaultOverride == null)
        {
            vaultWatcher = new VaultWatcher(bootstrapPath, vaultPath);
            vaultWatcher.OnVaultPathChanged += (newPath) =>
            {
                server.SwitchSemaphore.Wait(); // Block until current tool call finishes
                try
                {
                    Console.Error.WriteLine($"logmark-mcp: Vault switch detected: {newPath}");

                    // Auto-end active session in old vault
                    try
                    {
                        services.SessionContext?.EndSession("vault-switch");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"logmark-mcp: Session auto-end failed: {ex.Message}");
                    }

                    // Reinitialize all vault-dependent services (kernel + any closed producer services)
                    InitializeVaultServices(newPath);
                    Console.Error.WriteLine($"logmark-mcp: Vault switched to: {newPath}");
                    Console.Error.Flush();
                }
                finally
                {
                    server.SwitchSemaphore.Release();
                }
            };
            vaultWatcher.Start();
        }
        else
        {
            Console.Error.WriteLine("logmark-mcp: VaultWatcher disabled (--vault-override is pinned)");
            Console.Error.Flush();
        }

        var cts = new CancellationTokenSource();

        // Parent-process watchdog. MCP hosts on Windows (Claude Desktop) can abandon
        // this server's stdin without closing the pipe on renderer reload / sleep-wake,
        // so ReadMessageAsync never sees EOF and the process orphans (observed: several
        // stale logmark-mcp.exe accumulating across a session while the host keeps
        // spawning fresh instances). Watching the host guarantees we exit when it does.
        // Resolution failures degrade to the pre-existing EOF-only shutdown.
        // Close this process's session record on the way out. Without this the
        // record stays status: active forever, because the 30-minute idle timeout
        // in McpSessionContext lives in memory and dies with the process.
        // Never throws: a failed close must not turn a clean exit into a crash or
        // a nonzero exit code. The sweeper is the backstop.
        void CloseSessionForShutdown()
        {
            try
            {
                services.SessionContext?.Shutdown();
            }
            catch (Exception ex)
            {
                // stderr may be a broken pipe if the host died abruptly.
                try
                {
                    Console.Error.WriteLine($"logmark-mcp: session close failed: {ex.Message}");
                    Console.Error.Flush();
                }
                catch { /* ignore */ }
            }
        }

        if (parentProcess != null)
        {
            Console.Error.WriteLine($"logmark-mcp: Watching parent process {parentProcess.ProcessName} (PID {parentProcess.Id})");
            Console.Error.Flush();

            var watchdog = new ParentProcessWatchdog(
                isParentAlive: () =>
                {
                    try { return !parentProcess.HasExited; }
                    catch { return false; }
                },
                onParentExit: () =>
                {
                    // stderr may be a broken pipe if the host died abruptly; never let a
                    // failed diagnostic write prevent the guaranteed exit below.
                    try
                    {
                        Console.Error.WriteLine("logmark-mcp: Parent process exited, shutting down.");
                        Console.Error.Flush();
                    }
                    catch { /* ignore */ }
                    CloseSessionForShutdown();
                    cts.Cancel();
                    Environment.Exit(0);
                },
                pollInterval: TimeSpan.FromSeconds(2));

            _ = watchdog.Start(cts.Token);
        }
        else
        {
            Console.Error.WriteLine("logmark-mcp: Parent process not resolvable; relying on stdin EOF for shutdown.");
            Console.Error.Flush();
        }

        Console.Error.WriteLine("logmark-mcp: Ready for connections");
        Console.Error.Flush();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await server.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            CloseSessionForShutdown();
        }
    }

    /// <summary>
    /// Registers the open (kernel-only) MCP tool set: CRUD/read tools, session
    /// tracking, routes/constitution/tags, plus write_page, create_entry_with_subpage,
    /// and organize_entry (kernel ORGANIZE verbs). These are the tools the public
    /// substrate MCP server ships. Extracted so the open-build contract test can
    /// inspect the registered set with <see cref="Producer"/> null.
    /// </summary>
    internal static void RegisterOpenTools(ToolRegistry toolRegistry, VaultServices services)
    {
        // Phase 1: Core CRUD (with session context for auto-tracking)
        toolRegistry.Register(new CreateEntryTool(services));
        toolRegistry.Register(new ListRecentTool(services));
        toolRegistry.Register(new GetRoutesTool(services));
        // Only a closed producer brings an embedding model (lmk-9hkn).
        toolRegistry.Register(new SearchTool(services, semanticSearch: Producer is not null));

        // Phase 1.5: Backfill (with session context)
        toolRegistry.Register(new BackfillEntryTool(services));
        toolRegistry.Register(new BackfillBatchTool(services));

        // Vault overview (agent orientation)
        toolRegistry.Register(new VaultSummaryTool(services));

        // Phase 2: Context Query
        toolRegistry.Register(new ReadEntryTool(services));
        toolRegistry.Register(new GetDecisionsTool(services));
        toolRegistry.Register(new GetTasksTool(services));
        toolRegistry.Register(new GetBlocksTool(services));

        // Phase 3: Project Context
        toolRegistry.Register(new GetProjectContextTool(services));

        // Phase 4: Task Management
        toolRegistry.Register(new UpdateEntryTool(services));

        // Phase 5: Route Management
        toolRegistry.Register(new UpdateRoutesTool(services));
        toolRegistry.Register(new UpdateConstitutionTool(services));
        toolRegistry.Register(new GetConstitutionTool(services));
        toolRegistry.Register(new UpdateTagsTool(services));

        // Phase E: Session Tracking (using context)
        toolRegistry.Register(new StartSessionTool(services));
        toolRegistry.Register(new EndSessionTool(services));
        toolRegistry.Register(new GetSessionsTool(services));

        // Phase 6: Page/subpage tools (kernel Pages)
        toolRegistry.Register(new CreateEntryWithSubpageTool(services));
        toolRegistry.Register(new WritePageTool(services));

        // ORGANIZE single-entry verbs via the shared kernel registry, packed into
        // one dispatcher tool. Design: docs/plans/2026-07-02-mcp-organize-consolidation-design.md
        toolRegistry.Register(new OrganizeEntryTool(services, BuildOrganizeRegistry(services)));
    }

    /// <summary>
    /// Builds the kernel ORGANIZE verb registry (reroute/convert/amend/link/retag)
    /// backing the organize_entry tool. All kernel-homed (EntryOrganizer, EntryCache,
    /// RouteService, BuiltinVerbs).
    /// </summary>
    private static VerbRegistry BuildOrganizeRegistry(VaultServices services)
    {
        var organizeRegistry = new VerbRegistry(
            audit: msg => services.Logger?.Info(msg));
        var organizeOrganizer = new EntryOrganizer(
            services.VaultPath!, services.VaultWriter!, services.VaultWriteGate);
        BuiltinVerbs.RegisterReroute(organizeRegistry, services.EntryCache!, organizeOrganizer, services.RouteService!);
        BuiltinVerbs.RegisterConvertType(organizeRegistry, services.EntryCache!, organizeOrganizer);
        BuiltinVerbs.RegisterAmend(organizeRegistry, services.EntryCache!, organizeOrganizer);
        BuiltinVerbs.RegisterLinkEntryToPage(organizeRegistry, services.EntryCache!, organizeOrganizer);
        BuiltinVerbs.RegisterRetag(organizeRegistry, services.EntryCache!, organizeOrganizer);
        return organizeRegistry;
    }

    /// <summary>
    /// Builds a tool registry for the current <see cref="Producer"/> state without
    /// running the server. Used by the open-build contract test to pin the kernel
    /// tool set (Producer null) and by the shipped build to enumerate the full set.
    /// The organize registry is constructed against the provided services, so pass
    /// a holder (initialized or empty) that will not be dereferenced until a tool
    /// executes.
    /// </summary>
    internal static ToolRegistry BuildToolRegistry(VaultServices services, string bootstrapPath = "")
    {
        var registry = new ToolRegistry();
        RegisterOpenTools(registry, services);
        Producer?.RegisterClosedTools(registry, services, bootstrapPath);
        return registry;
    }
}
