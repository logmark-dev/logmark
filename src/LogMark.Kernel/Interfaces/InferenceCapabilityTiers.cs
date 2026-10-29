namespace LogMark.Interfaces;

// Split from LogMark.Core/Interfaces/InferenceCapabilities.cs during the
// kernel wiring extraction (docs/plans/2026-07-11-kernel-extraction-implementation.md):
// AgentDefinition/AgentRegistry (now in LogMark.Kernel) describe each agent's
// assumed inference capability using these tiers, and the closed inference
// cluster (InferenceRouter, InferenceAdapter, etc.) in LogMark.Core also
// consumes them. A utility used by both clusters lives in the kernel; Core
// references it Core -> Kernel like everything else.
public enum QualityTier
{
    Small,
    Mid,
    Large,
    Frontier
}

public enum CostProfile
{
    FreeLocal,
    CloudQuota,
    UserPaid
}
