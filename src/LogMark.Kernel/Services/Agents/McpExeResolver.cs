namespace LogMark.Services.Agents;

public class McpExeResolver
{
    private const string ExeName = "logmark-mcp.exe";

    /// <summary>
    /// Machine-local directory holding the dev-live MCP copy. Deliberately
    /// outside the repo (and outside any synced folder) so build outputs and
    /// the binary agent sessions actually run are different files.
    /// </summary>
    public static string DefaultDevLiveDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogMark-Dev", "mcp");

    private readonly string? _repoRoot;
    private readonly string _installedDir;
    private readonly string _devLiveDir;
    private readonly string? _preferredConfig;

    public McpExeResolver(
        string? repoRoot = null,
        string? installedDir = null,
        string? preferredConfig = null,
        string? devLiveDir = null)
    {
        _repoRoot = repoRoot;
        // Default to the directory beside the running assembly. Under a
        // Velopack install the app runs from %LocalAppData%\LogMark\current\
        // and the MCP exe ships at current\logmark-mcp.exe, beside LogMark.exe
        // -- so the base directory resolves correctly without knowing the
        // Velopack root. App composition passes an explicit installedDir
        // derived from InstallPathResolver (the canonical Velopack-aware
        // source of truth); this default is the agnostic fallback for any
        // other consumer.
        //
        // Through 0.19.3 this was BaseDirectory + `mcp`. The sidecars moved
        // into the install root so all three executables share one
        // self-contained runtime instead of carrying ~96 MB each. A stale
        // wiring entry still pointing at the old `mcp\` path resolves to a
        // dead command, which WiringSelfHeal rewrites on next startup.
        _installedDir = installedDir
            ?? AppContext.BaseDirectory;
        _devLiveDir = devLiveDir ?? DefaultDevLiveDir;
        _preferredConfig = preferredConfig;
    }

    public string? Resolve()
    {
        // 1. Installed location
        var installed = Path.Combine(_installedDir, ExeName);
        if (File.Exists(installed)) return installed;

        if (_repoRoot == null) return null;

        // 2. Dev-live copy. Agent sessions hold their MCP server open for
        // hours, and a running server locks every DLL in its own directory -
        // so wiring them straight at src\LogMark.Mcp.Producers\bin\Debug made
        // `dotnet build src` and `dotnet test src` fail on a shared Core.dll
        // that the servers had loaded. The choice was break MCP for every
        // session on each build, or stop rebuilding. Neither, now: sessions
        // run a stable copy here, refreshed on the MCP's own (slower) cadence
        // by scripts\refresh-mcp-live.ps1, while Core+App keep their fast
        // Debug loop untouched. The copy going stale between refreshes is the
        // accepted, explicit cost.
        //
        // Debug callers only, and never a fallback for anything else: the
        // copy is a Debug build, so BootstrapReader's compile-time #if DEBUG
        // resolves the LogMark-Debug AppData root. Handing it to a Release
        // composition root is lmk-yrf9 inverted - the wired server would
        // follow the Debug bootstrap while the app that wired it follows the
        // Release one. Placing this inside Resolve rather than at a call site
        // is load-bearing: WiringSelfHeal rewrites any MCP entry that is not
        // "the current resolver winner", so the resolver is the only place a
        // new candidate survives a restart.
        if (string.Equals(_preferredConfig, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            var devLive = Path.Combine(_devLiveDir, ExeName);
            if (File.Exists(devLive)) return devLive;
        }

        // 3. Config-matched repo build (bead lmk-yrf9). When the caller knows
        // its own build config, only that config's exe is eligible - the other
        // config is intentionally skipped, never a fallback, mirroring
        // CliExeResolver: Debug and Release binaries use different AppData
        // roots (LogMark-Debug vs LogMark), so a config-mismatched MCP exe in
        // FollowActive mode follows the WRONG bootstrap and silently ignores
        // the wiring app's vault switches. Before this rule, a Debug app with
        // a leftover Release publish registered the stale Release exe into
        // the provider manifest, and every product wired agents to it.
        if (_preferredConfig != null)
        {
            var matched = RepoBuildPath(_preferredConfig);
            return File.Exists(matched) ? matched : null;
        }

        // Legacy order for callers that cannot know a config (no composition
        // root of their own): prefer Release, then Debug. The dev-live copy is
        // deliberately not offered here - a caller that cannot name its config
        // cannot know the copy's bootstrap domain matches its own.
        var release = RepoBuildPath("Release");
        if (File.Exists(release)) return release;

        var debug = RepoBuildPath("Debug");
        if (File.Exists(debug)) return debug;

        return null;
    }

    private string RepoBuildPath(string config) =>
        Path.Combine(_repoRoot!, "src", "LogMark.Mcp.Producers", "bin", config, "net8.0", ExeName);
}
