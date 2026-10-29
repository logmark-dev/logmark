using System.Text.RegularExpressions;
using LogMark.Services.Agents.ConfigWriters;
using LogMark.Services.Providers;

namespace LogMark.Services.Agents;

/// <summary>
/// Orchestrates lifecycle-hook wiring for any framework in HookProfileRegistry:
/// resolve the profile, validate the route, build inject/capture command
/// entries honoring the capability matrix, then delegate to the per-host
/// writer. Parallels SkillsService; the CLI `hooks` command and the desktop
/// Session-hooks section both call this.
/// </summary>
public sealed partial class HooksService
{
    // Route token shape, kept in sync with NotationParser.ProjectTokenRegex. The
    // route is interpolated into a shell-executed hook command, so anything
    // outside the plain notation token alphabet is refused outright.
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]*(?:/[a-zA-Z][a-zA-Z0-9_-]*)*$", RegexOptions.Compiled)]
    private static partial Regex RouteTokenRegex();

    private readonly WslBridge? _wsl;

    /// <param name="wsl">
    /// Shared WSL bridge, used only when a HookProfile declares
    /// <c>Host: AgentHost.Wsl</c> (currently grok). Bug found during task
    /// 10b's real end-to-end verification (2026-07-31): HookProfile has
    /// carried a Host field since it was added specifically for grok, but
    /// ResolvePath below called the single-arg PathTemplateExpander.Expand
    /// overload unconditionally, never consulting it - so a WSL-hosted
    /// agent's hook path always resolved to the Windows-side path even
    /// though its hooks file (like its MCP config and skills dir) actually
    /// lives under the distro's $HOME, reachable from Windows only via
    /// \\wsl.localhost\...  Optional and defaulted so existing callers keep
    /// compiling; a null bridge preserves the previous (Windows-only)
    /// behavior for every non-WSL framework and gracefully degrades grok's
    /// hook path the same way ConfigPathResolver/SkillsPathResolver already
    /// degrade when no bridge is available.
    /// </param>
    public HooksService(WslBridge? wsl = null) => _wsl = wsl;

    public WiringResult Install(string agentSlug, WireScope scope, string? projectDir, string cliExePath, string? route)
    {
        var profile = HookProfileRegistry.For(agentSlug);
        if (profile is null)
            return new WiringResult(false, $"No lifecycle hook support for agent '{agentSlug}'.");

        if (!string.IsNullOrEmpty(route) && !RouteTokenRegex().IsMatch(route))
            return new WiringResult(false, $"Route must be a plain route token (letters, digits, _ - /): '{route}'");

        var path = ResolvePath(profile, scope, projectDir);
        if (path is null)
            return new WiringResult(false, $"Agent '{agentSlug}' has no {scope} hook path.");

        var exe = ResolveHookCommandPath(profile, cliExePath);
        if (exe is null)
            return new WiringResult(false,
                $"Agent '{agentSlug}' runs inside WSL; its hook command needs the shim's Linux path, " +
                "which could not be resolved (no WSL bridge available).");

        var routeArg = string.IsNullOrEmpty(route) ? "" : $" --route {route}";
        var entries = new List<HookEntry>();
        if (profile.CanInject)
            entries.Add(new HookEntry(profile.InjectEvent,
                $"\"{exe}\" agent-context --framework {agentSlug}{routeArg}"));
        if (profile.CanCapture)
            entries.Add(new HookEntry(profile.CaptureEvent,
                $"\"{exe}\" agent-capture --framework {agentSlug}{routeArg}",
                profile.CaptureTimeoutSeconds));
        if (profile.CanCheckpoint && profile.CheckpointEvent is not null)
            entries.Add(new HookEntry(profile.CheckpointEvent,
                $"\"{exe}\" agent-capture --framework {agentSlug} --checkpoint{routeArg}"));

        return HookWriterFactory.Create(profile.Writer).Write(path, profile, entries);
    }

    public WiringResult Uninstall(string agentSlug, WireScope scope, string? projectDir)
    {
        var profile = HookProfileRegistry.For(agentSlug);
        if (profile is null) return new WiringResult(true);
        var path = ResolvePath(profile, scope, projectDir);
        if (path is null) return new WiringResult(true);
        return HookWriterFactory.Create(profile.Writer).Remove(path);
    }

    public bool IsInstalled(string agentSlug, WireScope scope, string? projectDir)
    {
        var profile = HookProfileRegistry.For(agentSlug);
        if (profile is null) return false;
        var path = ResolvePath(profile, scope, projectDir);
        return path is not null && HookWriterFactory.Create(profile.Writer).IsInstalled(path);
    }

    public string? ResolveHookPath(string agentSlug, WireScope scope, string? projectDir)
    {
        var profile = HookProfileRegistry.For(agentSlug);
        return profile is null ? null : ResolvePath(profile, scope, projectDir);
    }

    /// <summary>
    /// The executable path to interpolate into the hook command.
    ///
    /// CRITICAL 2 from the final whole-feature review (2026-07-31): callers
    /// pass <c>ShimWriter.CliShimPath()</c>, which on Windows is
    /// <c>...\LogMark\bin\logmark-run.cmd</c>. That is correct for the five
    /// Windows-hosted frameworks and must stay byte-identical for them. But a
    /// WSL-hosted framework runs its hooks INSIDE Linux, where that string is
    /// neither a valid path nor a valid filename to execute - grok's WSL-side
    /// hook file was getting the untranslated Windows .cmd path and exiting
    /// 127 on every SessionEnd and PreCompact. Design doc lines 199-201
    /// specify the Linux form of the extension-less sh twin:
    /// <c>/mnt/c/Users/&lt;user&gt;/AppData/Local/LogMark/bin/logmark-run</c>.
    ///
    /// Only rewritten when this process is running on Windows. On a native
    /// POSIX install there is no WSL boundary to cross, and CliShimPath()
    /// already returns the extension-less sh twin at its real local path -
    /// rewriting it there would break the very case it is meant to serve.
    ///
    /// A null return means "this framework needs a WSL translation this
    /// process cannot perform"; Install reports that as a failure rather than
    /// writing a hook that cannot run. Every production composition root
    /// passes a bridge (pinned by GrokCompositionRootTests).
    /// </summary>
    private string? ResolveHookCommandPath(HookProfile profile, string cliExePath)
    {
        if (profile.Host != AgentHost.Wsl || !OperatingSystem.IsWindows())
            return cliExePath;

        // Pure string translation for a drive-letter path - no wsl.exe probe,
        // so wiring a WSL agent's hooks does not boot the VM by itself.
        return _wsl?.ToLinuxPath(ShimWriter.ShShimPath());
    }

    private string? ResolvePath(HookProfile profile, WireScope scope, string? projectDir)
    {
        if (scope == WireScope.User)
            return PathTemplateExpander.Expand(profile.UserHookPath, profile.Host, _wsl);
        if (profile.ProjectHookPath is null) return null;
        var baseDir = projectDir ?? Environment.CurrentDirectory;
        return Path.Combine(baseDir, profile.ProjectHookPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
