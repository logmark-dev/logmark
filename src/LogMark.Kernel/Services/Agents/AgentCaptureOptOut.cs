namespace LogMark.Services.Agents;

/// <summary>
/// Environment opt-out for the agent-capture hooks (SessionEnd / PreCompact).
///
/// LogMark drives agent CLIs itself - grok as an inference provider, the
/// inference lab running every consumer against a CLI column - and each of
/// those headless calls is, to the CLI, a session. Its SessionEnd hook then
/// fires LogMark's own capture against the ACTIVE vault, one stub entry and
/// one pending session per call (49 of them in the 2026-08-26 lab run).
/// Claude CLI has --setting-sources "" to keep hooks out of such calls; grok
/// has no flag, so the process environment is the one channel that reaches
/// the hook through any host: the CLI inherits it, the hook it spawns
/// inherits it again.
///
/// Set <see cref="EnvVar"/> to <see cref="OffValue"/> (case-insensitive) on
/// the CLI process and both hook commands return before reading stdin.
/// </summary>
public static class AgentCaptureOptOut
{
    public const string EnvVar = "LOGMARK_AGENT_CAPTURE";
    public const string OffValue = "off";

    public static bool IsOff(Func<string, string?>? getEnv = null)
    {
        var value = (getEnv ?? Environment.GetEnvironmentVariable)(EnvVar);
        return string.Equals(value?.Trim(), OffValue, StringComparison.OrdinalIgnoreCase);
    }
}
