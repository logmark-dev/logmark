namespace LogMark.Services.EntryParser;

/// <summary>
/// A trashed entry surfaced to the Trash view. The trash store is intentionally
/// out of EntryCache (it lives under _system), so this is read directly.
/// </summary>
/// <param name="Entry">The parsed entry as stored in the trash file.</param>
/// <param name="TrashFilePath">Vault-relative path to the trash file holding it.</param>
/// <param name="OriginLabel">Human route label of where it came from (for display).</param>
/// <param name="OriginPath">Vault-relative origin file path (for restore).</param>
public record TrashedEntry(
    ParsedEntry Entry,
    string TrashFilePath,
    string OriginLabel,
    string OriginPath);
