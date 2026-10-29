namespace LogMark.Services.Agents;

/// <summary>Selects the per-host hook config schema writer.</summary>
public enum HookWriterKind { GroupedJson, CursorJson, CopilotJson }

/// <summary>Selects the SessionStart stdout wrapper the agent-context command emits.</summary>
public enum HookOutputKind { ClaudeStyle, GeminiStyle, CursorStyle, CopilotStyle }

/// <summary>
/// One LogMark-owned hook command to register under an event.
///
/// TimeoutSeconds is the per-hook budget written into the host config. It is
/// not cosmetic on either side of its range: Claude Code's SessionEnd hooks
/// share a 1.5s budget that an explicit per-hook timeout RAISES (up to 60s),
/// so the value is what buys claude-code's capture enough room to run; Codex's
/// SessionEnd caps at 3s, so the same value must shrink there rather than
/// declare a budget the host will not grant. See HookProfile.CaptureTimeoutSeconds.
/// </summary>
public sealed record HookEntry(string Event, string Command, int TimeoutSeconds = HookProfile.DefaultTimeoutSeconds);

/// <summary>
/// Declarative per-framework description of how LogMark wires and runs its
/// lifecycle hooks. All host divergence (config path, schema family, event
/// names, capabilities, runtime stdin field names, stdout wrapper, agent
/// label) lives here so the writers, orchestrator, and CLI executors stay
/// host-agnostic.
/// </summary>
public sealed record HookProfile(
    string Slug,
    string DisplayName,
    string UserHookPath,            // env-var form, expanded at resolve time
    string? ProjectHookPath,        // relative to the project dir, or null
    HookWriterKind Writer,
    string InjectEvent,             // host event name for context injection
    string CaptureEvent,            // host event name for session-end capture
    bool CanInject,
    bool CanCapture,
    string CaptureSessionIdField,   // stdin field carrying the session id
    string CaptureTranscriptField,  // stdin field carrying the transcript path
    HookOutputKind InjectOutput,    // SessionStart stdout wrapper
    string AgentLabel,              // entry Source label, e.g. "claude-code"
    string CwdField = "cwd",        // stdin field carrying the working directory
    string? CheckpointEvent = null, // host event name for compaction-boundary capture (null = unsupported)
    bool CanCheckpoint = false,     // whether this framework fires a compaction-boundary event
    AgentHost Host = AgentHost.Windows,     // which OS surface this framework's config lives on
    int ContextBudgetChars = 3000,   // per-host injected-context budget
    int CaptureTimeoutSeconds = HookProfile.DefaultTimeoutSeconds)  // host-imposed ceiling on the capture hook
{
    /// <summary>
    /// Per-hook timeout written for every event that has no host-specific
    /// ceiling. Ten seconds is deliberately above Claude Code's 1.5s default
    /// SessionEnd budget: on that host an explicit per-hook timeout raises the
    /// shared budget to match, so lowering this silently starves capture.
    /// </summary>
    public const int DefaultTimeoutSeconds = 10;
}
