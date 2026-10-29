using LogMark.Services.EntryParser;

namespace LogMark.Services.Organize;

/// <summary>
/// Appends amend lines to source entries after an export operation,
/// leaving an audit trail in the vault.
/// </summary>
public static class ExportAuditTrail
{
    /// <summary>
    /// For each exported entry, append an amend line recording the export target,
    /// timestamp, and scope size. Groups by file path for efficient I/O.
    /// </summary>
    public static void Apply(
        IReadOnlyList<ScopedEntry> entries,
        string target,
        int totalEntryCount,
        IVaultFileWriter vaultWriter)
    {
        var byFile = entries.GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byFile)
        {
            var filePath = group.Key;

            if (!File.Exists(filePath))
                continue;

            var markdown = File.ReadAllText(filePath);
            var now = DateTime.Now;
            var amendContent = $"exported to {target}, scope: {totalEntryCount} entries";

            foreach (var scoped in group)
            {
                markdown = EntrySerializer.AmendEntry(
                    markdown,
                    scoped.Entry.Timestamp,
                    amendContent,
                    "system",
                    amendTimestamp: now,
                    shortId: scoped.Entry.ShortId);
            }

            vaultWriter.WriteFile(filePath, markdown);
        }
    }
}
