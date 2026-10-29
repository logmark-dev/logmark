namespace LogMark.Services.Agents;

/// <summary>
/// The two content shapes the session hooks write for THEMSELVES: a
/// compaction-boundary checkpoint and a session-end marker. Both are anchors -
/// the distill pipeline, the staged batch and the run ledger key on the
/// checkpoint stub's id - and neither carries content a reader can orient from.
///
/// This is the single definition. It was already written down twice with the
/// same words and the same reason (ProducerCliContext.SessionAlreadyCaptured
/// called them "hook internals, not genuine captures"), and HandoffResolver
/// needed a third; three copies of a literal prefix in three assemblies is how
/// they drift apart with no compiler signal.
///
/// Deliberately prefix-only. Matching on entry Source as well would broaden it:
/// "agent:session-distill" writes real, promoted content under the same
/// "agent:" family, and SessionAlreadyCaptured's existing behaviour depends on
/// the prefix test alone.
/// </summary>
public static class AgentStubEntry
{
    public const string EndedPrefix = "Agent session ended:";
    public const string CheckpointPrefix = "Agent session checkpoint:";

    public static bool IsStub(string? content) =>
        content is not null
        && (content.StartsWith(EndedPrefix, StringComparison.Ordinal)
            || content.StartsWith(CheckpointPrefix, StringComparison.Ordinal));
}
