using LogMark.Services.Agents;

namespace LogMark.Services.Providers;

/// <summary>Factory helpers producing each distribution's self-manifest.</summary>
public static class ProviderSelfDescription
{
    public static ProviderManifest Desktop(string cliPath, string mcpPath, string version,
        string? activeVault = null)
    {
        // A role you cannot locate is a role you do not declare - a caller that
        // does not know a binary's path (e.g. the CLI composition root, which
        // has no view of the desktop app's MCP exe) must not assert an empty
        // path for that role. Refresh below relies on the role being absent
        // (not present-but-empty) to know it should carry over a sibling
        // caller's existing entry instead of clobbering it.
        var binaries = new Dictionary<string, ProviderBinary>();
        if (!string.IsNullOrEmpty(cliPath)) binaries["cli"] = new(cliPath);
        if (!string.IsNullOrEmpty(mcpPath)) binaries["mcp"] = new(mcpPath);

        return new(
        ProviderManifest.CurrentManifestVersion, "desktop", version,
        binaries,
        new ProviderCapabilities("full", "full", Producers: true),
        new Dictionary<string, int> { ["vaultFormat"] = VaultStamp.CurrentFormat },
        DateTimeOffset.UtcNow, activeVault);
    }

    /// <summary>
    /// Self-manifest for a CLI composition root, which states the MCP role when
    /// the binary is sitting beside it.
    ///
    /// The shipped desktop install is a single root: LogMark.exe, lm.exe and
    /// logmark-mcp.exe are siblings. The CLI used to pass an empty mcpPath on
    /// the reasoning that it "has no view of the desktop app's MCP exe" and the
    /// app would reassert the role on its next launch. That left two gaps found
    /// by the 0.22 release QA run (G2): `lm connect --mcp` fails outright on an
    /// install whose app has never been launched, and until the app runs, a
    /// role carried over from a since-deleted sibling install stands in for the
    /// live one. A sibling this process can see is not a guess.
    ///
    /// No sibling (a dev tree, where lm.exe sits alone in its project bin)
    /// still states nothing: absent, never present-but-wrong.
    /// </summary>
    public static ProviderManifest DesktopFromCli(string cliPath, string version,
        string? activeVault = null)
    {
        string mcpPath = "";
        try
        {
            var dir = Path.GetDirectoryName(cliPath);
            if (!string.IsNullOrEmpty(dir))
            {
                var sibling = Path.Combine(dir, "logmark-mcp.exe");
                if (File.Exists(sibling)) mcpPath = sibling;
            }
        }
        catch { /* an unusable process path simply states no mcp role */ }

        return Desktop(cliPath, mcpPath, version, activeVault);
    }

    public static ProviderManifest OssCli(string unifiedBinaryPath, string version,
        string? activeVault = null) => new(
        ProviderManifest.CurrentManifestVersion, "oss-cli", version,
        new Dictionary<string, ProviderBinary>
        {
            ["cli"] = new(unifiedBinaryPath),
            ["mcp"] = new(unifiedBinaryPath, new[] { "mcp" }),
            ["serve"] = new(unifiedBinaryPath, new[] { "serve" }),
        },
        new ProviderCapabilities("kernel", "kernel", Producers: false),
        new Dictionary<string, int> { ["vaultFormat"] = VaultStamp.CurrentFormat },
        DateTimeOffset.UtcNow, activeVault);
}

/// <summary>
/// Startup-time registry refresh, called by every composition root: record
/// self, prune dead providers, regenerate the hook shim from the current
/// ranked candidates. Idempotent and best-effort - never throws into startup.
/// </summary>
public static class ProviderRegistration
{
    /// <param name="selfHeal">
    /// When true, run <see cref="WiringSelfHeal"/> after the shim refresh to
    /// repair stale hook/MCP wiring for every framework whose user-scope entry
    /// exists. Off by default so callers that only want to record themselves
    /// (and unit tests) never touch agent configs; genuine product startups
    /// (desktop app, `logmark serve`) opt in.
    /// </param>
    public static void Refresh(ProviderManifest self,
        ProviderManifestStore? store = null, ShimWriter? shim = null, bool selfHeal = false)
    {
        try
        {
            // Test-host guard (lmk-uawe): a test suite that invokes a composition
            // root in-process (Daemon.Tests calling Program.Main) arrives here with
            // ProcessPath = testhost*, and defaulting the store or shim would write
            // the DEVELOPER'S LIVE registry - advertising the test runner as a
            // provider whose liveness check passes, because testhost.exe exists.
            // Tests that want registration exercised inject BOTH store and shim
            // (the established pattern in ProviderRegistrationTests); anything
            // less under a test host is a no-op.
            if (store is null || shim is null)
            {
                var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
                if (processName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            store ??= new ProviderManifestStore(ProviderManifestStore.DefaultRoot());
            shim ??= new ShimWriter(ShimWriter.DefaultBinDir());
            store.Save(MergeWithExisting(store, self));
            store.Prune(File.Exists);
            var resolver = new ProviderResolver(store);
            shim.WriteCliShim(resolver.Ranked("cli"));

            // Self-heal writes to the REAL agent config paths (~/.claude.json,
            // ~/.codex/..., Claude Desktop), which no env var redirects. A run
            // whose app-data is sandboxed must therefore never heal: with a
            // redirected registry the resolver would name a scratch binary the
            // winner and rewrite the developer's live wiring (seen 2026-09-04).
            var sandboxed = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOGMARK_APPDATA_PATH"));
            if (selfHeal && !sandboxed)
                RunSelfHeal(resolver);
        }
        catch { /* coordination is best-effort; products must still boot */ }
    }

    /// <summary>
    /// A caller that cannot resolve a role must not erase what a fuller caller
    /// recorded: for each role key present in the existing manifest for this
    /// ProviderId but absent from <paramref name="self"/>.Binaries (the
    /// factory omits roles it could not locate - see ProviderSelfDescription),
    /// carry the existing ProviderBinary over into the manifest being saved,
    /// PROVIDED its binary is still there.
    ///
    /// The liveness condition is not decoration. Every desktop install shares
    /// the ProviderId "desktop", so a role recorded by a sibling install that
    /// has since been deleted was carried forward by every later role-poor
    /// refresh, forever: Prune keeps the manifest because another role is
    /// alive, and the resolver silently drops the dead path, so `connect --mcp`
    /// reported "No LogMark MCP provider is registered" on a machine whose
    /// manifest named one. Found by the 0.22 release QA run (G2), where the
    /// carried-over path belonged to a deleted logmark-studio bundle.
    ///
    /// ActiveVault is intentionally NOT merged here: the daemon is the sole
    /// writer of ActiveVault and always states its intent explicitly,
    /// including clearing it to null - so self.ActiveVault (as given) wins
    /// unconditionally, exactly as before this fix.
    /// </summary>
    private static ProviderManifest MergeWithExisting(ProviderManifestStore store, ProviderManifest self)
    {
        var existing = store.LoadAll().FirstOrDefault(m => m.ProviderId == self.ProviderId);
        if (existing is null) return self;

        var merged = new Dictionary<string, ProviderBinary>(self.Binaries);
        foreach (var (role, binary) in existing.Binaries)
        {
            if (merged.ContainsKey(role)) continue;
            if (!FileExists(binary.Path)) continue;
            merged[role] = binary;
        }

        return self with { Binaries = merged };
    }

    /// <summary>
    /// Liveness for a carried-over role. A path that cannot even be probed
    /// (malformed, an unreachable share) counts as gone: the role is dropped
    /// rather than preserved on the strength of an exception.
    /// </summary>
    private static bool FileExists(string path)
    {
        try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
        catch { return false; }
    }

    /// <summary>
    /// Composes a HooksService and a per-agent InboundWirer (mirroring
    /// ConnectCommand.BuildConnectService) and runs the startup wiring repair.
    /// The wirer is built with the same ProviderResolver, so any rewrite it does
    /// produces the byte-identical entry any product would write.
    /// </summary>
    private static void RunSelfHeal(ProviderResolver resolver)
    {
        // One bridge for this composition root, reused for HooksService and
        // for every agent the wirerFactory below is invoked for. Constructing
        // it is free (lazy discovery), and WiringSelfHeal.Run's two loops
        // (hooks and MCP) both skip AgentHost.Wsl agents before touching a
        // service constructed with it, so passing a real bridge here never
        // causes this startup path to probe wsl.exe.
        var wsl = new WslBridge(new SystemProcessRunner());
        var hooks = new HooksService(wsl);

        // WindowsRegistryProbe is [SupportedOSPlatform("windows")]; the Kernel
        // targets plain net8.0. LogMark is Windows-only in practice, so suppress
        // CA1416 narrowly here (same treatment ConnectCommand.BuildConnectService
        // uses), rather than annotating the whole assembly.
#pragma warning disable CA1416
        WiringSelfHeal.Run(resolver, hooks, agent => new InboundWirer(
            new McpExeResolver(),
            new ConfigPathResolver(new WindowsRegistryProbe(), wsl),
            new ProcessRunnerNativeCliDelegate(new SystemProcessRunner()),
            activeVaultPath: string.Empty,
            launchOverride: null,
            providerResolver: resolver,
            wsl: wsl));
#pragma warning restore CA1416
    }
}
