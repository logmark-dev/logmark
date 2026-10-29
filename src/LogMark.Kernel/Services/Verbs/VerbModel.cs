namespace LogMark.Services.Verbs;

public enum VerbBand { Read, Capture, Acquire, Organize, Curate, Analyze, Synthesize, Surface, Reflect, Publish, Run }

public enum VerbKind { Operation, Atom, Program }

public enum VerbTier { Free, Pro }

public enum VerbGating { Auto, Confirm, ConfirmDestructive }

/// <summary>
/// One declared vault verb. The single semantic unit every surface dispatches.
/// Kind carries tier and gating defaults; factories apply them so a verb only
/// overrides when it deviates.
/// </summary>
public record Verb
{
    public required string Name { get; init; }
    public required VerbBand Band { get; init; }
    public required VerbKind Kind { get; init; }
    public required string Summary { get; init; }
    public VerbTier Tier { get; init; } = VerbTier.Free;
    public VerbGating Gating { get; init; } = VerbGating.Confirm;
    public bool ReadOnly { get; init; }
    public bool Destructive { get; init; }
    public bool Idempotent { get; init; }

    /// <summary>Declare a deterministic operation verb (free, confirm-unless-readonly).</summary>
    public static Verb Operation(string name, VerbBand band, string summary,
        bool readOnly = false, bool destructive = false, bool idempotent = false)
        => new()
        {
            Name = name,
            Band = band,
            Kind = VerbKind.Operation,
            Summary = summary,
            Tier = VerbTier.Free,
            Gating = readOnly ? VerbGating.Auto
                : destructive ? VerbGating.ConfirmDestructive
                : VerbGating.Confirm,
            ReadOnly = readOnly,
            Destructive = destructive,
            Idempotent = idempotent,
        };
}
