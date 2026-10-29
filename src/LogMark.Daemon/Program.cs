using System.Reflection;
using LogMark.Services;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Hosting;
using LogMark.Services.Providers;
using LogMark.Services.Vault;

namespace LogMark.Daemon;

/// <summary>
/// The unified `logmark` binary. argv[0] dispatches into one of three open
/// composition roots in-process (no child exes):
/// <list type="bullet">
///   <item><c>serve</c> - the headless vault host (this file's standby loop):
///   holds the vault host role behind an app-priority mutex (design 4.3,
///   amended 2026-07-13) and serves the cockpit HTTP/SSE API v0 on loopback.</item>
///   <item><c>mcp</c> - <see cref="LogMark.Mcp.Program.Run"/>, the MCP stdio
///   server, over this process's own stdio (open path, no closed producer).</item>
///   <item>a verb registered in the open CLI registry - <see
///   cref="LogMark.CLI.Program.Run"/>.</item>
/// </list>
/// Bare invocation, <c>--help</c>, <c>--version</c>, and unknown verbs are
/// handled by the dispatcher without booting any subsystem. The dispatcher is
/// unconditional (same for the private `logmark-daemon` name and the OSS
/// `logmark` name): the <c>serve</c> path is preserved verbatim, so no wired
/// invocation regresses - nothing spawns the daemon (the desktop app hosts
/// in-process); the only real invocation today is a dev running
/// <c>serve --vault ...</c>, which is unchanged. Only the usage/help text is
/// richer than the old serve-only banner.
///
/// Explicit-verbs-only (controller ruling, chunk A review): argv[0] must be
/// exactly one of <c>serve</c>, <c>mcp</c>, a help/version spelling, or a
/// registered CLI verb - never an implicit capture. LogMark.CLI's own
/// dispatcher (<see cref="LogMark.CLI.CommandRunner.LooksLikeCapture"/>)
/// keeps its capture-first UX for the private `lm` binary; this unified
/// binary is a different surface (agents and scripts invoking it by name)
/// where a typo'd flag or verb must fail loudly (exit 64) rather than get
/// silently appended to the vault as a log entry. <c>serve</c>, <c>mcp</c>,
/// <c>help</c>, and <c>version</c> match case-insensitively (mirrors the
/// CLI's own <see cref="LogMark.CLI.CommandRegistry"/>, which looks verbs up
/// in an OrdinalIgnoreCase dictionary); a real CLI verb is likewise matched
/// case-insensitively via <see cref="IsCliVerb"/>.
/// </summary>
public static class Program
{
    private const int DefaultPort = 4747;

    // Global flags the CLI layer accepts before the verb. Their VALUES are
    // skipped too, so `--vault <path> routes` dispatches `routes`.
    private static readonly HashSet<string> GlobalFlagsWithValue = new(StringComparer.Ordinal) { "--vault" };
    private static readonly HashSet<string> GlobalFlagsBare = new(StringComparer.Ordinal) { "--json", "--quiet", "-q" };

    internal static int FindVerbIndex(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (GlobalFlagsWithValue.Contains(args[i])) { i++; continue; }
            if (GlobalFlagsBare.Contains(args[i])) continue;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Slices the args from the verb (or after it) for a host verb (serve/mcp)
    /// while keeping a leading global <c>--vault &lt;v&gt;</c> (last one wins)
    /// that would otherwise be dropped with everything before the verb. A
    /// trailing <c>--vault</c> still wins because it comes last.
    /// </summary>
    internal static string[] ForwardLeadingVault(string[] args, int verbIndex, bool keepVerb)
    {
        var rest = args[(keepVerb ? verbIndex : verbIndex + 1)..];
        if (verbIndex <= 0) return rest;
        string? vault = null;
        for (var i = 0; i < verbIndex - 1; i++)
            if (args[i] == "--vault") vault = args[i + 1];
        if (vault is null) return rest;
        if (keepVerb)
        {
            if (rest.Contains("--vault")) return rest;
            return [rest[0], "--vault", vault, .. rest[1..]];
        }
        return ["--vault", vault, .. rest];
    }

    public static async Task<int> Main(string[] args)
    {
        // Cross-product coexistence: record this install in the provider registry,
        // prune dead siblings, refresh the hook shim (best-effort, never blocks boot).
        // Self-heal only on the long-running `serve` surface - not on `mcp`/CLI
        // verbs that agents spawn frequently, where scanning every framework's
        // config on each invocation would be needless churn.
        var verbIndex = FindVerbIndex(args);
        var selfHeal = verbIndex >= 0 && Is(args[verbIndex], "serve");
        ProviderRegistration.Refresh(SelfManifest(), selfHeal: selfHeal);

        if (args.Length == 0)
        {
            PrintUsage(Console.Error);
            return 64;
        }

        if (verbIndex < 0)
        {
            Console.Error.WriteLine("logmark: no command given");
            PrintUsage(Console.Error);
            return 64;
        }
        var verb = args[verbIndex];

        if (Is(verb, "--version") || Is(verb, "-v"))
        {
            PrintVersion();
            return 0;
        }
        if (Is(verb, "--help") || Is(verb, "-h"))
        {
            PrintUsage(Console.Out);
            return 0;
        }
        if (Is(verb, "help"))
        {
            if (args.Length > verbIndex + 1)
            {
                // `help serve` / `help mcp` are host-level help, not CLI
                // command help - route them to the host usage printers
                // rather than falling through to the CLI, which has no
                // "serve"/"mcp" command in its own registry.
                if (Is(args[verbIndex + 1], "serve"))
                {
                    PrintServeUsage(Console.Out);
                    return 0;
                }
                if (Is(args[verbIndex + 1], "mcp"))
                {
                    PrintMcpUsage(Console.Out);
                    return 0;
                }
                // Same product marker the CLI-verb path sets below, so CLI
                // help names `logmark` rather than `lm` in its usage lines.
                LogMark.CLI.Commands.ConnectCommand.OwnerId = "oss-cli";
                return LogMark.CLI.Program.Run(args);
            }
            PrintUsage(Console.Out);
            return 0;
        }
        if (Is(verb, "serve"))
        {
            return await RunServeAsync(ForwardLeadingVault(args, verbIndex, keepVerb: true));
        }
        if (Is(verb, "mcp"))
        {
            // Strip the "mcp" verb so the MCP arg parser sees its own
            // flags (--agent, --route, --vault-override, ...). Producer
            // stays null: open build ships kernel tools only.
            return await LogMark.Mcp.Program.Run(ForwardLeadingVault(args, verbIndex, keepVerb: false));
        }
        if (IsCliVerb(verb))
        {
            // Unified binary: the MCP server is this process's own `mcp` verb,
            // not a sidecar logmark-mcp.exe. `connect --mcp` wires a registered
            // provider's binary when one resolves (the desktop's full server
            // wins over this kernel one) and falls back to "<this binary> mcp"
            // only when none does. Only this dispatch path sets the seam;
            // lm.exe and desktop never do.
            if (Environment.ProcessPath is { } self)
                LogMark.CLI.Commands.ConnectCommand.SelfMcpLaunch =
                    new LogMark.Services.Agents.McpLaunchSpec(self, new[] { "mcp" });

            // Same dispatch-only seam as SelfMcpLaunch above: the unified OSS
            // binary is a distinct product from the desktop app, and both can
            // install the same skill pack to the same shared skills dir (see
            // SkillsInstaller's refcounted marker). Tagging this product's
            // writes "oss-cli" keeps `connect --skills`/`disconnect --skills`
            // from deleting the other product's install out from under it.
            LogMark.CLI.Commands.ConnectCommand.OwnerId = "oss-cli";
            return LogMark.CLI.Program.Run(args);
        }

        // Genuinely unknown top-level verb (not a host verb, not a
        // registered CLI verb). Explicit-verbs-only: never falls back to an
        // implicit vault capture, no matter how many tokens follow argv[0]
        // or what they look like - a multi-token invocation of a typo'd or
        // wrong verb (`srve --vault X`, `--port 4747`) must fail loudly
        // rather than silently writing a log entry to whatever vault
        // happens to resolve.
        Console.Error.WriteLine($"logmark: unknown command '{verb}'");
        PrintUsage(Console.Error);
        return 64;
    }

    /// <summary>
    /// This install's self-manifest for the provider registry (design 4.x
    /// cross-product coexistence). Shared by every <see
    /// cref="ProviderRegistration.Refresh"/> call site in this file so
    /// version/path derivation is not duplicated: the boot-time record-self
    /// call, the vault-resolution publish at host-role acquire, and the
    /// clear at host-role release.
    /// </summary>
    internal static ProviderManifest SelfManifest(string? activeVault = null) =>
        ProviderSelfDescription.OssCli(
            Environment.ProcessPath ?? "logmark",
            BuildInfo.DisplayVersion(typeof(Program).Assembly),
            activeVault);

    /// <summary>Case-insensitive comparison for the fixed host/help/version tokens.</summary>
    private static bool Is(string arg, string token) =>
        string.Equals(arg, token, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="verb"/> is a registered open CLI verb.</summary>
    private static bool IsCliVerb(string verb) =>
        LogMark.CLI.Program.VerbSummaries()
            .Any(v => string.Equals(v.Name, verb, StringComparison.OrdinalIgnoreCase));

    private static void PrintVersion() => Console.WriteLine($"{LogMark.Services.Providers.ProductNames.Engine} {BuildInfo.DisplayVersion(typeof(Program).Assembly)}");

    /// <summary>Printed once while another process holds the vault host role.
    /// The lock does not name its holder (the desktop app or another serve),
    /// so the line states what is known and no more.</summary>
    internal const string StandbyMessage =
        "standby: another LogMark process is hosting this vault; waiting to take over when it exits";

    private static void PrintUsage(TextWriter w)
    {
        w.WriteLine($"{LogMark.Services.Providers.ProductNames.Engine} {BuildInfo.DisplayVersion(typeof(Program).Assembly)} - the open LogMark memory engine (binary: logmark)");
        w.WriteLine();
        w.WriteLine("Usage: logmark <command> [args]");
        w.WriteLine();
        w.WriteLine("Host commands:");
        w.WriteLine("  serve    Run the headless vault host (cockpit HTTP/SSE API on loopback, default port 4747)");
        w.WriteLine("  mcp      Run the MCP stdio server for AI agents");
        w.WriteLine();
        w.WriteLine("CLI commands:");
        var verbs = LogMark.CLI.Program.VerbSummaries();
        var width = verbs.Count == 0 ? 0 : verbs.Max(v => v.Name.Length);
        foreach (var (name, desc) in verbs)
        {
            w.WriteLine($"  {name.PadRight(width + 2)}{desc}");
        }
        w.WriteLine();
        w.WriteLine("  logmark [--vault <path>] <command> [args]   global flags may precede or follow the command");
        w.WriteLine();
        w.WriteLine("Run 'logmark help <command>' for command-specific help.");
        w.WriteLine();
        w.WriteLine("To capture text, use: logmark capture \"your text\"");
    }

    private static void PrintServeUsage(TextWriter w)
    {
        w.WriteLine("Usage: logmark serve --vault <path> [--port <port>] [--no-activevault]");
        w.WriteLine();
        w.WriteLine("  Hold the vault host role behind an app-priority standby mutex and");
        w.WriteLine("  serve the cockpit HTTP/SSE API v0 on http://127.0.0.1:<port>");
        w.WriteLine("  (default 4747). The cockpit SPA is served at / when embedded.");
        w.WriteLine("  --no-activevault   host the vault but do not publish it as the machine-wide ActiveVault (use when the desktop app owns resolution for this vault)");
    }

    private static void PrintMcpUsage(TextWriter w)
    {
        w.WriteLine("Usage: logmark mcp [--agent <id>] [--route <+route>] [--vault-override <path> | --vault <path>]");
        w.WriteLine("                    [--bootstrap-path <path>] [--session-timeout <minutes>]");
        w.WriteLine();
        w.WriteLine("  Run the MCP stdio server for AI agents (open kernel tool set - Producer");
        w.WriteLine("  is null, so only kernel tools register, never the closed producer set).");
        w.WriteLine("  Vault is read from the desktop app's bootstrap.json unless --vault-override");
        w.WriteLine("  (or its alias --vault) is set. Uses NDJSON over stdio, not Content-Length framing.");
    }

    internal sealed record ServeOptions(string Vault, int Port, bool PublishActiveVault);

    internal static ServeOptions? ParseServeOptions(string[] args)
    {
        var vault = ArgValue(args, "--vault");
        if (vault is null) return null;
        var port = int.TryParse(
            ArgValue(args, "--port") ?? Environment.GetEnvironmentVariable("LOGMARK_DAEMON_PORT"),
            out var parsed) ? parsed : DefaultPort;
        var publish = !args.Contains("--no-activevault");
        return new ServeOptions(Path.GetFullPath(vault), port, publish);
    }

    private static async Task<int> RunServeAsync(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintServeUsage(Console.Out);
            return 0;
        }

        var options = ParseServeOptions(args);
        if (options is null)
        {
            Console.Error.WriteLine("serve: --vault <path> is required");
            PrintServeUsage(Console.Error);
            return 2;
        }
        var vault = options.Vault;
        var port = options.Port;
        if (!Directory.Exists(vault))
        {
            Console.Error.WriteLine($"serve: vault not found: {vault}");
            return 2;
        }

        var logmarkPathForStamp = Path.Combine(vault, "logmark");
        // Backfill on ordinary open: pre-stamp vaults would otherwise stay
        // unstamped forever, since EnsureWritten only runs on init/top-up paths.
        // Any `serve` against an unstamped vault backfills format 1 here,
        // best-effort - a write failure must not block serving.
        if (VaultStamp.Check(logmarkPathForStamp) == VaultStampState.Missing)
        {
            try
            {
                VaultStamp.EnsureWritten(logmarkPathForStamp,
                    $"oss-cli/{BuildInfo.ManifestVersion(typeof(Program).Assembly)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warning: VaultStamp backfill failed: {ex.Message}");
            }
        }

        if (VaultStamp.Check(logmarkPathForStamp) == VaultStampState.NewerThanMine)
        {
            Console.Error.WriteLine(
                "warning: vault was written by a newer LogMark; serving read paths, skipping re-seeding");
        }

        // lm-vault SPEC-VERSION: a reader must not silently read a vault that
        // declares a format MAJOR it does not implement.
        if (VaultManifestFrontmatter.UnsupportedMajorWarningForFile(Path.Combine(logmarkPathForStamp, "vault.md"))
            is string schemaWarning)
        {
            Console.Error.WriteLine($"warning: {schemaWarning}");
        }

        DaemonLog.Init(vault);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DaemonLog.Write("fatal", $"unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DaemonLog.WriteException("task", e.Exception);
            e.SetObserved();
        };

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        Console.WriteLine($"logmark serve: vault {vault}, port {port}");

        using var wantSignal = VaultHostSignal.Open(vault);

        var standbyAnnounced = false;
        var random = new Random();
        var consecutiveFaults = 0;
        while (!shutdown.IsCancellationRequested)
        {
            var hostLock = VaultHostLock.TryAcquireDetached(vault);
            if (hostLock is null)
            {
                if (!standbyAnnounced)
                {
                    Console.WriteLine(StandbyMessage);
                    standbyAnnounced = true;
                }
                try
                {
                    // Jittered backoff, 1-5s.
                    await Task.Delay(TimeSpan.FromMilliseconds(1000 + random.Next(4000)), shutdown.Token);
                }
                catch (OperationCanceledException) { }
                continue;
            }

            standbyAnnounced = false;
            Console.WriteLine("host role acquired");
            DaemonLog.Write("host", "role acquired");

            // Publish this vault as our ActiveVault so VaultResolutionPolicy
            // (CLI, MCP) can find this live host instead of diverging to
            // whatever bootstrap.json says. Best-effort, never blocks serving.
            if (options.PublishActiveVault)
                ProviderRegistration.Refresh(SelfManifest(activeVault: vault));
            else
                Console.WriteLine("host role acquired (not published as ActiveVault: --no-activevault)");

            // While hosting: watch the want-signal event-driven (no polling)
            // and cancel yieldCts the moment the desktop app announces it
            // wants the role back. shutdown.Token stays linked so a plain
            // Ctrl+C shutdown cancels HostAsync exactly as before.
            //
            // retireCts is this iteration's OWN retirement handle, separate
            // from yieldCts: it is what lets the loop deterministically wind
            // down yieldWatch on every exit path - yield, shutdown, AND a
            // genuine fault - before yieldCts is disposed below. Without
            // this, a fault (which cancels neither shutdown nor yieldCts)
            // would leave yieldWatch pending against the shared wantSignal
            // past yieldCts.Dispose(); the next iteration's watch would then
            // race it, and a later announce would drive the stale watch to
            // call Cancel() on an already-disposed CTS.
            using var retireCts = new CancellationTokenSource();
            using var yieldCts = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var yieldWatch = YieldWatch.WatchAsync(wantSignal, yieldCts, shutdown.Token, retireCts.Token);

            try
            {
                await HostAsync(vault, port, yieldCts.Token);
                consecutiveFaults = 0;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                // Clean Ctrl+C shutdown - not a fault, nothing to log.
                consecutiveFaults = 0;
            }
            catch (OperationCanceledException) when (yieldCts.IsCancellationRequested)
            {
                // Yielded to the desktop app - not a fault, nothing to log.
                consecutiveFaults = 0;
            }
            catch (Exception ex)
            {
                DaemonLog.WriteException("host", ex);
                Console.Error.WriteLine($"host loop faulted (logged to .logmark/daemon.log): {ex.Message}");
                consecutiveFaults++;
                // Backoff so a persistent fault (e.g. port 4747 already bound by an
                // uncoordinated process) cannot hot-loop the rebuild-and-fail cycle.
                var delay = TimeSpan.FromSeconds(Math.Min(30, 2 * consecutiveFaults));
                try { await Task.Delay(delay, shutdown.Token); } catch (OperationCanceledException) { }
            }

            // Retire this iteration's watch on EVERY exit path above (yield,
            // shutdown, fault) before yieldCts is disposed. On the yield
            // path the watch is already done (it is what drove the cancel);
            // on shutdown it unwinds via the same linked token; on a fault
            // this is what actually stops it. Bounded (2s cap) so a wedged
            // watch can never hang the loop.
            await YieldWatch.RetireAsync(retireCts, yieldWatch);

            // Release the lock now, explicitly - not via an implicit `using`
            // deferred to the end of this iteration's block, which would run
            // after the grace wait below and leave the OS mutex held for the
            // app's entire grace window, defeating the yield. Dispose is
            // idempotent, so this is safe even though hostLock is also
            // reachable if an earlier return path existed.
            hostLock.Dispose();
            Console.WriteLine("host role released");
            DaemonLog.Write("host", "role released");

            // Clear ActiveVault: we no longer hold the host role for this
            // vault, so VaultResolutionPolicy must not point anyone at us.
            if (options.PublishActiveVault)
                ProviderRegistration.Refresh(SelfManifest(activeVault: null));

            // A yield is a cancelled yieldCts that is NOT also a shutdown.
            var wasYield = yieldCts.IsCancellationRequested && !shutdown.IsCancellationRequested;
            if (wasYield)
            {
                Console.WriteLine("host role yielded");
                DaemonLog.Write("host", "role yielded");
                await WaitForHandoffGraceAsync(wantSignal, shutdown.Token);
                if (!shutdown.IsCancellationRequested)
                {
                    Console.WriteLine("handoff grace over, resuming standby");
                    DaemonLog.Write("host", "handoff grace over, resuming standby");
                }
            }
        }

        return 0;
    }

    private static readonly TimeSpan DefaultHandoffGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandoffGracePollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Grace window after yielding the host role: polls the want-signal every
    /// <paramref name="pollInterval"/> (default 250ms) so the loop resumes
    /// standby as soon as the app clears it, but bails out unconditionally
    /// after <paramref name="graceDuration"/> (default 15s) or on shutdown.
    /// Both durations are parameters (not hardcoded constants) precisely so
    /// tests can exercise this without ever sleeping the real 15s.
    /// </summary>
    internal static async Task WaitForHandoffGraceAsync(
        VaultHostSignal signal,
        CancellationToken shutdown,
        TimeSpan? graceDuration = null,
        TimeSpan? pollInterval = null)
    {
        var grace = graceDuration ?? DefaultHandoffGrace;
        var interval = pollInterval ?? HandoffGracePollInterval;
        var deadline = DateTime.UtcNow + grace;

        while (signal.IsAnnounced && DateTime.UtcNow < deadline && !shutdown.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, shutdown);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Defensive clear: if the grace fully expired with the signal still
        // set, the announcer never acquired (a live app acquires within one
        // poll of the release and clears) - most likely it was hard-killed
        // between Announce and Clear. The manual-reset event outlives the
        // dead process because this daemon holds a handle to it, so without
        // this clear the next acquire would observe the stale announce and
        // spuriously yield again, oscillating every grace period forever.
        // Clearing is race-free against a live app: its acquire poll does
        // not read the signal, only the mutex.
        if (!shutdown.IsCancellationRequested && signal.IsAnnounced)
        {
            signal.Clear();
            Console.WriteLine("cleared stale want-signal (announcer never acquired)");
            DaemonLog.Write("host", "cleared stale want-signal (announcer never acquired)");
        }
    }

    private static async Task HostAsync(string vault, int port, CancellationToken shutdown)
    {
        var services = VaultHostBootstrap.Build(vault);
        try
        {
            SweepAbandonedSessions(services.SessionService, services.Logger);

            if (services.EntryCache.WarmupTask is { } warmup)
            {
                await warmup.ConfigureAwait(false);
            }

            var producerHost = new ProducerHost();
            // The open daemon registers no producers (design 4.2): closed
            // assemblies are simply not present in this composition root.
            await producerHost.StartAsync(shutdown);

            using var sse = new SseChannel();
            using var watcher = new VaultChangeWatcher(vault, sse.Broadcast);
            var settingsStore = new SettingsStore(vault);
            var routes = new V0Routes(
                services,
                new ConceptPageStore(vault),
                settingsStore,
                sse,
                HostIdentity.Engine);
            var compat = new CockpitCompatRoutes(routes, settingsStore);
            using var server = new DaemonHttpServer(port, routes, msg => DaemonLog.Write("http", msg), compat);
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"cannot bind http://127.0.0.1:{port} - port in use by another process?", ex);
            }
            Console.WriteLine($"serving v0 API on http://127.0.0.1:{port}");

            try
            {
                await Task.Delay(Timeout.Infinite, shutdown);
            }
            catch (OperationCanceledException) { }

            // Shutdown order: HTTP, watcher (both via using), then producers.
            await producerHost.StopAsync();
        }
        finally
        {
            // services.EntryCache is IDisposable (owns a FileSystemWatcher);
            // dispose it on every exit path - including exceptions thrown
            // before the try body reaches its normal end - after the HTTP
            // server and producers are down. Every other VaultHostServices
            // member is not IDisposable (see the disposal census in the
            // task report).
            services.EntryCache.Dispose();
        }
    }

    /// <summary>
    /// One sweep per host acquisition. Orphans only appear when a process dies
    /// without notice, so there is nothing to catch on a timer. Reuses the live
    /// SessionService and SystemLogger off the kernel bootstrap rather than
    /// constructing throwaway ones. Must never prevent host acquisition from
    /// succeeding: a failed sweep is logged and swallowed.
    ///
    /// Stays synchronous, unlike the app's copy. The app's runs on the WPF UI
    /// thread, where the sweep's file IO delays the first frame and every vault
    /// switch, so it was moved to the thread pool. Here there is no UI thread and
    /// no one to perceive the latency - this is a headless host coming up - and
    /// running inline keeps the useful ordering for free: the vault is swept before
    /// the daemon starts serving, so the API's first answer about active sessions
    /// is already the reclaimed one.
    /// </summary>
    private static void SweepAbandonedSessions(SessionService sessionService, SystemLogger logger)
    {
        try
        {
            var sweeper = new SessionSweeper(
                sessionService, logger, new SystemProcessLiveness(), TimeSpan.FromHours(24));
            sweeper.Sweep(DateTime.Now);
        }
        catch (Exception ex)
        {
            logger.Warn($"Session sweep failed: {ex.Message}");
            DaemonLog.WriteException("host", ex);
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}

/// <summary>
/// Per-iteration lifecycle for the standby loop's want-signal watch. Split
/// out of <see cref="Program"/> so the fault-retirement race (a genuine
/// HostAsync fault leaves the watch pending against the shared
/// <c>wantSignal</c> after that iteration's yieldCts is disposed, letting a
/// later announce drive a stale watch to cancel a disposed CTS) has a
/// testable seam - <c>internal</c>, exposed to LogMark.Daemon.Tests via
/// InternalsVisibleTo.
/// </summary>
internal static class YieldWatch
{
    /// <summary>Upper bound on how long RetireAsync will wait for a watch to unwind.</summary>
    public static readonly TimeSpan DefaultRetireCap = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Awaits the per-vault want-signal on a token linked from
    /// <paramref name="shutdown"/> AND <paramref name="retire"/>, so this
    /// watch ends silently - exactly like a plain shutdown - the moment its
    /// owning loop iteration retires it, without ever needing to touch
    /// <paramref name="yieldCts"/> after that point. Only a genuine announce
    /// (neither shutdown nor retirement) reaches the cancel below.
    ///
    /// The <c>yieldCts.Cancel()</c> call is additionally guarded against
    /// <see cref="ObjectDisposedException"/>: deterministic retirement (see
    /// <see cref="RetireAsync"/>) is what is relied on to prevent the race
    /// in the first place, but this catch is the belt - no conceivable
    /// scheduling race can let it throw into an unobserved task.
    /// </summary>
    public static async Task WatchAsync(
        VaultHostSignal signal,
        CancellationTokenSource yieldCts,
        CancellationToken shutdown,
        CancellationToken retire)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown, retire);
        try
        {
            await signal.WaitForAnnounceAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown or retirement - both are silent, non-fault endings.
            return;
        }

        Console.WriteLine("yield requested by desktop app");
        DaemonLog.Write("host", "yield requested by desktop app");
        try
        {
            yieldCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Belt: the owning iteration already retired and disposed
            // yieldCts by the time this watch's continuation ran. Nothing
            // to cancel into - the iteration has already moved on.
        }
    }

    /// <summary>
    /// Deterministically retires a loop iteration's watch: cancels
    /// <paramref name="retireCts"/> so the linked token inside
    /// <see cref="WatchAsync"/> trips, then awaits <paramref name="watch"/>
    /// (bounded by <paramref name="cap"/>, default 2s) so it is guaranteed
    /// to have completed before the caller disposes that iteration's
    /// yieldCts. Must be called on every HostAsync exit path - yield,
    /// shutdown, and fault alike - not only the fault path, so the behavior
    /// is uniform and easy to reason about.
    /// </summary>
    public static async Task RetireAsync(CancellationTokenSource retireCts, Task watch, TimeSpan? cap = null)
    {
        retireCts.Cancel();
        await Task.WhenAny(watch, Task.Delay(cap ?? DefaultRetireCap));
    }
}
