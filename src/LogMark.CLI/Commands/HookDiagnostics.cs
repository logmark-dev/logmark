namespace LogMark.CLI.Commands;

/// <summary>
/// Diagnostics gate for lifecycle-hook commands (agent-context, agent-capture).
/// Hosts treat ANY stderr output from a hook as a failure - Codex, for one,
/// reports "hook exited with code 1" when a SessionStart hook writes to stderr
/// even though the process exits 0. So the never-break-the-session contract
/// means TRUE silence on degraded paths: diagnostics are suppressed unless
/// LOGMARK_HOOK_DEBUG is set (any non-empty value), which a user can export in
/// the host's hook environment without editing the wired command.
/// </summary>
internal static class HookDiagnostics
{
    // Indirection so tests can toggle the gate without mutating the real
    // process environment (which is flaky under parallel test runs).
    internal static Func<string?> DebugVar { get; set; } =
        () => Environment.GetEnvironmentVariable("LOGMARK_HOOK_DEBUG");

    public static bool DebugEnabled => !string.IsNullOrEmpty(DebugVar());

    /// <summary>Write a diagnostic to stderr only when LOGMARK_HOOK_DEBUG is set.</summary>
    public static void Warn(string message)
    {
        if (DebugEnabled)
            Console.Error.WriteLine(message);
    }
}
