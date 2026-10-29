using System.Collections.Generic;

namespace LogMark.Models.Sensitivity;

/// <summary>
/// Outcome of a vault write through IVaultWriteGate.
/// For non-private routes and successful Pro masks, Unmasked is false and UnmaskedSpans is empty.
/// For free-tier writes to a private route, Unmasked is true and UnmaskedSpans lists the values
/// the shroud scanner detected in the entry content.
/// </summary>
public sealed record VaultWriteResult(
    bool Unmasked,
    IReadOnlyList<SensitiveSpan> UnmaskedSpans)
{
    public static VaultWriteResult Clean { get; } =
        new(Unmasked: false, UnmaskedSpans: System.Array.Empty<SensitiveSpan>());
}
