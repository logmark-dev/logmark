// src/LogMark.Core/Services/Export/VaultArchiveExporter.cs
using System.IO;
using System.IO.Compression;

namespace LogMark.Services.Export;

/// <summary>
/// Composes a whole-vault .logmark archive. Excludes telemetry
/// (events.db), vector stores (vectors*.db), and generated indexes
/// (_indexes/). Everything else under logmark/ is included verbatim.
/// Appends a single line to _system/log.md via SystemLogger.Info on
/// successful export.
/// </summary>
public sealed class VaultArchiveExporter
{
    private static readonly string[] ExcludedFileNames =
        { "events.db", "vectors.db", "vectors-log.db" };

    public VaultArchiveResult Export(string vaultPath, string archivePath)
    {
        if (!Directory.Exists(vaultPath))
            throw new DirectoryNotFoundException($"Vault folder not found: {vaultPath}");

        var vaultFull = Path.GetFullPath(vaultPath);
        var archiveFull = Path.GetFullPath(archivePath);
        if (string.Equals(archiveFull, vaultFull, StringComparison.OrdinalIgnoreCase) ||
            archiveFull.StartsWith(vaultFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot export into the vault being exported");

        var logmarkRoot = Path.Combine(vaultFull, "logmark");
        if (!Directory.Exists(logmarkRoot))
            throw new InvalidOperationException($"Vault is missing logmark/ folder: {vaultPath}");

        // Compose entry-by-entry so we can skip excluded files.
        try
        {
            using (var fs = File.Create(archiveFull))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var file in EnumerateIncludedFiles(logmarkRoot))
                {
                    var rel = Path.GetRelativePath(vaultFull, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, rel);
                }
            }
        }
        catch
        {
            try { File.Delete(archiveFull); } catch { }
            throw;
        }

        // Append log marker via SystemLogger (same path the merge engine uses).
        try
        {
            var logger = new SystemLogger(vaultFull);
            logger.Info($"Exported vault to {Path.GetFileName(archiveFull)}");
        }
        catch
        {
            // SystemLogger is best-effort by design (per its source comment).
        }

        var entriesCount = CountEntries(logmarkRoot);
        return new VaultArchiveResult(archiveFull, entriesCount);
    }

    private static IEnumerable<string> EnumerateIncludedFiles(string logmarkRoot)
    {
        foreach (var file in Directory.EnumerateFiles(logmarkRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(logmarkRoot, file).Replace('\\', '/');
            if (rel.StartsWith("_indexes/", StringComparison.OrdinalIgnoreCase)) continue;
            var fileName = Path.GetFileName(file);
            if (ExcludedFileNames.Any(x => string.Equals(x, fileName, StringComparison.OrdinalIgnoreCase))) continue;
            yield return file;
        }
    }

    private static int CountEntries(string logmarkRoot)
    {
        // Count "## YYYY-MM-DD HH:mm" headers across every YYYY.md in the vault.
        // This matches the engine's EntryParser shape; cheap rough count for the toast.
        var files = Directory.EnumerateFiles(logmarkRoot, "*.md", SearchOption.AllDirectories);
        var headerStart = "## 20"; // matches "## YYYY..." for 2000-2099
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith(headerStart, StringComparison.Ordinal) &&
                    line.Length >= "## YYYY-MM-DD HH:mm".Length)
                    count++;
            }
        }
        return count;
    }
}
