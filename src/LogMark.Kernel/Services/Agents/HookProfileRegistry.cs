namespace LogMark.Services.Agents;

/// <summary>
/// The six frameworks whose lifecycle hooks LogMark can wire. Slugs match
/// AgentRegistry where an MCP entry exists. Schemas pinned 2026-06-25 from each
/// host's hook-reference doc (see the design doc for source URLs); codex
/// re-pinned 2026-09-06.
///
/// These pins do not expire on their own and the hosts do move under them - the
/// codex re-pin corrected a capture event that had been wrong for two months.
/// Before trusting a capability here, check it against the host's current doc.
/// Capability drift and the validation debt are tracked in
/// lmk-hook-fidelity-validation-debt-4if7.
/// </summary>
public static class HookProfileRegistry
{
    private static readonly HookProfile[] Profiles =
    [
        new("claude-code", "Claude Code",
            UserHookPath: "%USERPROFILE%\\.claude\\settings.json",
            ProjectHookPath: ".claude/settings.json",
            Writer: HookWriterKind.GroupedJson,
            InjectEvent: "SessionStart", CaptureEvent: "SessionEnd",
            CanInject: true, CanCapture: true,
            CaptureSessionIdField: "session_id", CaptureTranscriptField: "transcript_path",
            InjectOutput: HookOutputKind.ClaudeStyle, AgentLabel: "claude-code",
            CheckpointEvent: "PreCompact", CanCheckpoint: true,
            ContextBudgetChars: 8000),

        // Codex's June pin declared CaptureEvent "Stop" on the premise that
        // "Codex has no SessionEnd". It has one now, and Stop was never a
        // session boundary: it fires at the end of EVERY TURN. Because codex
        // is one of the three frameworks with a transcript reader, the window
        // gate actually engaged, and Final mode never advances the watermark -
        // so from stayed 0, the window stayed the whole transcript, and the
        // gate passed again on every subsequent turn. That wrote one "Agent
        // session ended: codex" entry per turn, and closed the LogMark session
        // record at the FIRST turn end (the EndSession call sits ahead of the
        // gate). Latent the whole time only because codex hooks were never
        // live-validated. SessionEnd is the real boundary; use it.
        //
        // SessionEnd on codex is capped at 3s (1s default), synchronous and
        // advisory - stdout is ignored - so the capture hook declares 3, not
        // the shared 10. PreCompact carries no such cap (600s), which is what
        // makes the checkpoint path viable here at all: its bounded 3s
        // HandoffWarmupTimeoutMs wait fits with room to spare.
        //
        // PostCompact is deliberately NOT wired. LogMark has no
        // post-compaction concept, and codex re-fires SessionStart with
        // source=compact, so re-injection after a compaction is already
        // covered by the inject wiring above.
        new("codex", "Codex CLI",
            UserHookPath: "%USERPROFILE%\\.codex\\hooks.json",
            ProjectHookPath: ".codex/hooks.json",
            Writer: HookWriterKind.GroupedJson,
            InjectEvent: "SessionStart", CaptureEvent: "SessionEnd",
            CanInject: true, CanCapture: true,
            CaptureSessionIdField: "session_id", CaptureTranscriptField: "transcript_path",
            InjectOutput: HookOutputKind.ClaudeStyle, AgentLabel: "codex",
            CheckpointEvent: "PreCompact", CanCheckpoint: true,
            CaptureTimeoutSeconds: 3),

        new("gemini-cli", "Gemini CLI",
            UserHookPath: "%USERPROFILE%\\.gemini\\settings.json",
            ProjectHookPath: ".gemini/settings.json",
            Writer: HookWriterKind.GroupedJson,
            InjectEvent: "SessionStart", CaptureEvent: "SessionEnd",
            CanInject: true, CanCapture: true,
            CaptureSessionIdField: "session_id", CaptureTranscriptField: "transcript_path",
            InjectOutput: HookOutputKind.GeminiStyle, AgentLabel: "gemini-cli"),

        new("cursor", "Cursor",
            UserHookPath: "%USERPROFILE%\\.cursor\\hooks.json",
            ProjectHookPath: ".cursor/hooks.json",
            Writer: HookWriterKind.CursorJson,
            InjectEvent: "sessionStart", CaptureEvent: "sessionEnd",
            CanInject: true, CanCapture: true,
            CaptureSessionIdField: "conversation_id", CaptureTranscriptField: "transcript_path",
            InjectOutput: HookOutputKind.CursorStyle, AgentLabel: "cursor",
            CwdField: "workspace_roots"),

        // Copilot ships capture-only (CanInject=false); Task 9 validates whether
        // sessionStart additionalContext injection actually works and may flip it.
        new("copilot-cli", "GitHub Copilot CLI",
            UserHookPath: "%USERPROFILE%\\.copilot\\hooks\\logmark.json",
            ProjectHookPath: ".github/hooks/logmark.json",
            Writer: HookWriterKind.CopilotJson,
            InjectEvent: "sessionStart", CaptureEvent: "sessionEnd",
            CanInject: false, CanCapture: true,
            CaptureSessionIdField: "sessionId", CaptureTranscriptField: "transcriptPath",
            InjectOutput: HookOutputKind.CopilotStyle, AgentLabel: "copilot-cli"),

        // Grok ignores hook stdout on every passive event (SessionStart,
        // UserPromptSubmit), so CanInject is false - verified 2026-07-31, the
        // shim emitted correct additionalContext and grok discarded it. Only
        // the blocking events (PreToolUse, Stop) honor stdout, and injecting
        // vault context at Stop is semantically backwards. Do NOT wire it.
        // Capture and checkpoint only need the hook to RUN, so both work.
        new("grok", "Grok CLI",
            UserHookPath: "%USERPROFILE%\\.grok\\hooks\\logmark.json",
            ProjectHookPath: ".grok/hooks/logmark.json",
            Writer: HookWriterKind.GroupedJson,
            InjectEvent: "SessionStart", CaptureEvent: "SessionEnd",
            CanInject: false, CanCapture: true,
            // CRITICAL 3 from the final whole-feature review (2026-07-31):
            // these declared "sessionId" / "transcriptPath" - grok's own
            // camelCase envelope - while the WSL shim NORMALIZES that envelope
            // to the snake_case keys every other GroupedJson framework
            // declares (claude-code, codex, gemini-cli all use session_id /
            // transcript_path). The shim's jq filter ADDS the snake_case keys;
            // AgentCaptureCommand read the declared camelCase ones, so it read
            // the untranslated Linux transcript path, File.Exists was false,
            // the window gate never passed, and capture exited 0 having
            // written nothing - the exact "ran, exited 0, wrote nothing"
            // symptom the design doc recorded from the original manual probe.
            // The profile is the smaller side to move, and moving it makes
            // grok consistent with its three GroupedJson siblings.
            CaptureSessionIdField: "session_id", CaptureTranscriptField: "transcript_path",
            InjectOutput: HookOutputKind.ClaudeStyle, AgentLabel: "grok",
            CheckpointEvent: "PreCompact", CanCheckpoint: true,
            Host: AgentHost.Wsl),
    ];

    public static HookProfile? For(string slug) =>
        Profiles.FirstOrDefault(p => p.Slug == slug);

    public static IReadOnlyList<string> SupportedSlugs =>
        Profiles.Select(p => p.Slug).ToList();
}
