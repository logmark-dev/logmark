namespace LogMark.Models;

/// <summary>
/// Private-route preset selection.
/// Maps to Shroud presets (financial, paranoid, dev).
/// Default means "use the vault-level default preset from settings".
/// </summary>
public enum PrivatePreset
{
    Default,
    Paranoid,
    Financial,
    Dev
}
