using System.Text.Json;
using LogMark.Services.Agents;

namespace LogMark.CLI.Commands;

/// <summary>
/// Open capture-only subset of the closed SessionEnd/PreCompact hook executor:
/// on the OSS build (no LogMark.CLI.Producers, no transcript readers, no
/// distill enqueue) it still writes ONE deterministic session-end stub entry
/// through the normal Core save pipeline, so the hooks connect wires do not
/// exit 64 on a unified `logmark` binary. It carries none of the closed
/// command's transcript-window gating, turn/duration stats, or FeedMeta
/// distill enqueue -- those need TranscriptReaderRegistry/FeedMetaStore,
/// which are closed (LogMark.Core) and unavailable here. `--checkpoint` is a
/// deliberate no-op in the open build: checkpoints need a transcript reader
/// to measure the window since the last watermark, and the open build has
/// none, so it degrades to doing nothing rather than writing a misleading
/// stub. On lm.exe/desktop the closed AgentCaptureCommand (same Name,
/// registered after this one via ClosedCommandRegistrar) always overrides
/// this one -- CommandRegistry.Register is last-wins by Name -- so the full
/// distill-aware behavior is preserved wherever the closed producers are
/// present. MUST never damage session close: all failures exit 0 and stay
/// silent (no stderr - hosts treat hook stderr as failure) unless
/// LOGMARK_HOOK_DEBUG is set.
/// </summary>
public class OpenAgentCaptureCommand : ICommand
{
    public string Name => "agent-capture";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--route"] = true, ["--framework"] = true, ["--checkpoint"] = false,
    };

    public string Description => "Capture a session-end summary entry from an agent hook";
    public string Usage => "agent-capture [--route <route>] [--framework <slug>] [--checkpoint]";
    public bool RequiresVault => false;

    public static Func<bool> IsInputRedirected { get; set; } = () => Console.IsInputRedirected;

    public int Execute(CliContext context, string[] args)
    {
        try
        {
            // Automation drove this CLI (an inference call, the lab): the
            // session is LogMark's own traffic, not the user's work.
            if (AgentCaptureOptOut.IsOff())
                return 0;

            if (!IsInputRedirected())
                return 0; // not invoked as a hook; nothing to capture

            var raw = Console.In.ReadToEnd();

            string? route = null, framework = null;
            var checkpoint = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--route" && i + 1 < args.Length) route = args[++i];
                else if (args[i] == "--framework" && i + 1 < args.Length) framework = args[++i];
                else if (args[i] == "--checkpoint") checkpoint = true;
            }

            var shape = HookProfileRegistry.For(framework ?? "claude-code")
                        ?? HookProfileRegistry.For("claude-code")!;

            string? sessionId;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                sessionId = doc.RootElement.TryGetProperty(shape.CaptureSessionIdField, out var sid)
                    ? sid.GetString()
                    : null;
            }
            catch (JsonException)
            {
                HookDiagnostics.Warn("agent-capture: stdin was not valid hook JSON");
                return 0;
            }
            if (string.IsNullOrEmpty(sessionId))
                return 0;

            // Checkpoints need a transcript reader to measure the window since the
            // last watermark - a closed capability (TranscriptReaderRegistry lives
            // in LogMark.Core). The open build no-ops on --checkpoint rather than
            // write a misleading stub.
            if (checkpoint)
                return 0;

            var text = $"Agent session ended: {shape.AgentLabel}.";

            var captureRoute = route;
            if (captureRoute is null)
            {
                var scope = AgentHookScope.Resolve(context.VaultPath, raw, shape.CwdField);
                if (scope.Result.Kind == HookScopeKind.Route)
                    captureRoute = scope.Result.Route;
            }
            if (captureRoute != null) text += $" +{captureRoute}";

            var entry = context.NotationParser.Parse(text, "agent-hook");
            entry.Source = $"agent:{shape.AgentLabel}";
            entry.Session = sessionId;
            entry.Timestamp = DateTime.Now;

            context.VaultService.SaveEntryWithResultAsync(entry).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            // Silent unless LOGMARK_HOOK_DEBUG: stderr would make a host flag
            // the hook as failed. Always exit 0.
            HookDiagnostics.Warn($"agent-capture: {ex.Message}");
            return 0; // never block session close
        }
    }
}
