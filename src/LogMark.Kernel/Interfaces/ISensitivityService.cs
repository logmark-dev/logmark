using LogMark.Models;
using LogMark.Models.Sensitivity;

namespace LogMark.Interfaces;

/// <summary>
/// Minimal interface for the sensitivity (shroud) service as seen by FileRouter.
/// Exposes only the methods needed on the write path.
/// </summary>
public interface ISensitivityService
{
    /// <summary>
    /// True when the session is unlocked and reveal operations will return original values.
    /// False when locked or no service is wired.
    /// </summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// Mask sensitive tokens in text for a private route and register them in the
    /// encrypted vault.shroud registry.
    /// </summary>
    Task<MaskResult> MaskAndRegisterAsync(string text, Route route);

    /// <summary>
    /// Reveal masked tokens in text using the unlocked session.
    /// Returns the original text if the session is not unlocked.
    /// </summary>
    string Reveal(string text);

    /// <summary>
    /// Scan text for sensitive spans using the given preset without masking.
    /// Used by the capture panel live indicator to count sensitive values as the user types.
    /// </summary>
    ScanResult Scan(string text, PrivatePreset routePreset);

    /// <summary>
    /// Ensure the sensitivity session is unlocked, prompting the user (via the injected
    /// UI prompt) for setup-or-unlock if it is not. Returns true if unlocked (already or
    /// after user input), false if the user cancelled or no prompt is wired (headless host).
    /// Callers use this when a user action creates a private route, so password setup
    /// happens at route creation rather than deferred to the first capture.
    /// </summary>
    Task<bool> EnsureUnlockedAsync();
}
