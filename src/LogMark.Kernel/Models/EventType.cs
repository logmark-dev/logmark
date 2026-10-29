namespace LogMark.Models;

/// <summary>
/// Types of events logged in the event stream
/// </summary>
public enum EventType
{
    // Vault mutation events
    EntryCreated,
    TaskCompleted,
    TaskPostponed,
    SettingsChanged,
    RouteAdded,

    // Read/interaction events
    // TaskAlertDismissed mutates nothing: the task is untouched on disk and only
    // silenced for the session. It exists so dismiss-rate is measurable - the one
    // number that says whether the deadline alert earns its interruption.
    TaskAlertDismissed,
    CommandExecuted,
    ModalOpened,
    SearchHandoff,
    FileNavigationHandoff,
    FocusedModeEntered,
    FocusedModeExited,

    // Timetrack events
    TimetrackStarted,
    TimetrackStopped,
    TimetrackReportGenerated,

    // Protocol lifecycle events
    ProtocolStarted,
    ProtocolCompleted,
    ProtocolAction,
    ProtocolCreated,
    ProtocolSnoozed,
    ProtocolSkipped,
    ProtocolAdapted,
    ProtocolPaused,
    ProtocolResumed,

    // Vault lifecycle events
    VaultSwitched,

    // UX surface engagement events
    SurfaceOpened,
    SurfaceClosed,

    // Organize operations
    EntryRerouted,
    EntryAmended,
    EntryConverted,
    EntryCrossLinked,
    EntryDeleted,
    EntryRestored,
    TrashEmptied,

    // Privacy / vault security
    PasswordRotated,

    // Vault action runtime (VaultActionRunner)
    ActionExecuted,
    InsightGenerated,

    // Self-signal substrate (Phase 1: reactions + local diagnostics)
    FeedCardActioned,
    EntryDiscarded,
    PerfSample,
    Exception,

    // Self-signal substrate (alignment Phase A: agent-write reconciliation)
    AgentWrite,
    AgentWriteOutcome,

    // Concept intent signals (Cycle 2c)
    ConceptDecision,
    ConceptPositionEdited,
    ConceptAppetiteChanged,

    // Vault index reconcile completed (added/changed/removed counts in DataJson)
    IndexReconciled,
}
