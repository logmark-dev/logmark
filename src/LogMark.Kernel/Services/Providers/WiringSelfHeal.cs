using System.Text.RegularExpressions;
using LogMark.Services.Agents;

namespace LogMark.Services.Providers;

/// <summary>
/// Startup repair pass: for every framework whose user-scope wiring exists,
/// rewrite hook entries that do not reference the current shim, and rewrite the
/// MCP entry when its command is dead or is not the current resolver winner.
/// Idempotent; user scope only (project scopes are not enumerable). Every step
/// is best-effort - one framework's malformed config never aborts the rest, and
/// the whole pass never throws into a product's startup.
/// </summary>
public static class WiringSelfHeal
{
    public static void Run(ProviderResolver resolver, HooksService hooks,
        Func<AgentDefinition, InboundWirer> wirerFactory)
    {
        // Hooks: any installed user-scope hook file that predates the stable shim
        // (references an absolute build path instead of logmark-run) is rewritten
        // to point at the shim, preserving a pinned --route if one was set.
        foreach (var slug in HookProfileRegistry.SupportedSlugs)
        {
            try
            {
                // Bug found during task 10b (2026-07-31): this loop had no
                // AgentHost.Wsl skip even though the MCP loop below does (see
                // its comment). Once HooksService became WSL-aware (host-aware
                // PathTemplateExpander call in HooksService.ResolvePath), a
                // caller that passes it a real bridge would have this loop
                // read HookProfile.Host -> WslBridge.LinuxHome/UncRoot for
                // grok on every startup, booting the WSL VM. Mirror the MCP
                // loop's guard so hooks self-heal stays startup-safe too;
                // grok's hooks wiring is validated lazily when the Settings
                // Hooks surface opens or `lm connect --status` runs, exactly
                // like its MCP wiring already is.
                if (HookProfileRegistry.For(slug)?.Host == AgentHost.Wsl) continue;

                if (!hooks.IsInstalled(slug, WireScope.User, null)) continue;
                var path = hooks.ResolveHookPath(slug, WireScope.User, null);
                if (path is null || !File.Exists(path)) continue;

                var content = File.ReadAllText(path);
                if (content.Contains("logmark-run")) continue; // already on the shim

                // Recover a pinned route from the stale command so healing does not
                // silently drop `--route X` (not otherwise reconstructable).
                var routeMatch = Regex.Match(content, @"--route\s+([A-Za-z][A-Za-z0-9_\-/]*)");
                var route = routeMatch.Success ? routeMatch.Groups[1].Value : null;
                hooks.Install(slug, WireScope.User, null, ShimWriter.CliShimPath(), route);
            }
            catch { /* best-effort per framework */ }
        }

        // MCP: delegate per-agent healing to the wirer, which owns config formats.
        foreach (var agent in AgentRegistry.GetAll())
        {
            // WSL-hosted agents are deliberately excluded from startup
            // self-heal. Probing wsl.exe, and even reading \\wsl.localhost,
            // boots the WSL VM - which has nothing to do with capture and
            // would put a multi-second VM start on every LogMark launch.
            // Their wiring is validated lazily when the Settings agent
            // surface opens or `lm connect --status` runs. This guard must
            // stay ahead of wirer construction and config-path resolution
            // (both can end up touching WslBridge via a host-aware
            // ConfigPathResolver) - do NOT "fix" this asymmetry with the
            // other five frameworks.
            if (agent.Host == AgentHost.Wsl) continue;

            try
            {
                var wirer = wirerFactory(agent);
                wirer.HealUserScopeEntry(agent, resolver);
            }
            catch { /* best-effort per agent */ }
        }
    }
}
