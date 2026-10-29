namespace LogMark.Services.Agents;

/// <summary>
/// Expands a config/hook/skills path template (e.g. "%USERPROFILE%\.claude\settings.json"
/// from HookProfileRegistry/AgentRegistry) into a real filesystem path.
/// </summary>
/// <remarks>
/// Task 11 OSS dry-run fix (2026-07-23): every path template in this
/// namespace is authored with Windows-style backslash separators, because
/// they were written against %USERPROFILE% (a Windows environment-variable
/// convention). <see cref="Environment.ExpandEnvironmentVariables"/> itself
/// is cross-platform (it substitutes literal env-var names regardless of
/// OS), but it does NOT touch path separators - so on Linux/macOS the
/// expanded string still contains literal backslashes, which are ordinary
/// filename characters there, not directory separators. A hook/config write
/// then lands at a single oddly-named file (or fails to resolve a parent
/// directory at all) instead of the intended nested path, and callers whose
/// error handling treats "the target isn't there" as a soft failure (see
/// ConnectService's per-capability isolation) can silently no-op instead of
/// throwing - confirmed via a live ubuntu-latest CI run of
/// ConnectCommandTests (`connect claude-code --hooks` reported success but
/// wrote nothing to the expected directory).
/// </remarks>
public static class PathTemplateExpander
{
    /// <summary>
    /// Expands environment-variable tokens (e.g. %USERPROFILE%) in
    /// <paramref name="template"/>, then normalizes any backslash path
    /// separators to <see cref="Path.DirectorySeparatorChar"/> for the
    /// current OS. A no-op on Windows (where backslash already is the
    /// separator); on Linux/macOS it turns literal backslashes into real
    /// directory separators.
    /// </summary>
    public static string Expand(string template)
    {
        var expanded = Environment.ExpandEnvironmentVariables(WithUserProfile(template));
        return Path.DirectorySeparatorChar == '\\' ? expanded : expanded.Replace('\\', Path.DirectorySeparatorChar);
    }

    // macOS and Linux never set USERPROFILE, so ExpandEnvironmentVariables
    // left the token in place and every user-scope path resolved to
    // ./%USERPROFILE%/... under the working directory (rc7 osx QA,
    // 2026-10-22). When the variable is unset, the token means the OS user
    // profile (HOME on Unix). A set USERPROFILE still wins, which is how the
    // test sandboxes redirect it.
    private static string WithUserProfile(string template)
    {
        if (Environment.GetEnvironmentVariable("USERPROFILE") is not null) return template;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length == 0
            ? template
            : template.Replace("%USERPROFILE%", profile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Host-aware expansion. Windows delegates to <see cref="Expand(string)"/>
    /// verbatim. Wsl maps the %USERPROFILE% token onto the distro's Linux home
    /// and returns the \\wsl.localhost\... UNC form, which ordinary File I/O
    /// can read and write - that is what lets every downstream writer stay
    /// host-agnostic.
    ///
    /// A null or unavailable bridge falls back to Windows expansion so a
    /// machine without WSL degrades to "agent not found" rather than throwing
    /// inside a wiring operation.
    /// </summary>
    public static string Expand(string template, AgentHost host, WslBridge? bridge)
    {
        if (host != AgentHost.Wsl || bridge is null) return Expand(template);

        var home = bridge.LinuxHome;
        var root = bridge.UncRoot;
        if (home is null || root is null) return Expand(template);

        // Strip %USERPROFILE% first - it maps to the distro's Linux home
        // below, not to Environment.ExpandEnvironmentVariables' own Windows
        // substitution. Any OTHER token (e.g. %APPDATA%) in the remainder is
        // then expanded so no literal "%TOKEN%" can survive into the
        // returned UNC path - a stray "%" is a worse failure mode (mangled
        // path, wrong directory created) than a Windows-side value embedded
        // in a WSL path, and templates are authored, not user input.
        var withoutUserProfile = template.Replace("%USERPROFILE%", "", StringComparison.OrdinalIgnoreCase);
        var rest = Environment.ExpandEnvironmentVariables(withoutUserProfile)
            .Replace('/', '\\')
            .TrimStart('\\');

        var linuxHomeAsWindows = home.Replace('/', '\\').TrimEnd('\\');
        return rest.Length == 0
            ? root + linuxHomeAsWindows
            : root + linuxHomeAsWindows + "\\" + rest;
    }
}
