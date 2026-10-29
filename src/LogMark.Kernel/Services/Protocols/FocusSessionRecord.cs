namespace LogMark.Services.Protocols;

/// <summary>
/// Pure DTO for a completed focus session, parsed from the event log.
/// Lives in its own file so the kernel-side EventQueryService can consume it
/// without dragging FocusProtocolService (protocol machinery, non-kernel).
/// </summary>
public record FocusSessionRecord(
    int DurationMinutes,
    int PostponeCount,
    string Outcome,
    double ActualMinutes);
