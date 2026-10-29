using System.Threading.Tasks;

namespace LogMark.Services;

/// <summary>
/// Low-level IO primitive for writing bytes to disk under the vault.
///
/// Used by:
/// - The sensitivity subsystem for registry and stats files
///   (vault.shroud, vault.stats.json).
/// - IVaultWriteGate internals after masking decisions have been made.
/// - General consumers that write non-route-gated files (logs, config,
///   tags.md, routes.md, event DBs) and accept that this is a pure IO
///   primitive with no privacy awareness.
///
/// IMPORTANT: Callers writing content that belongs to a user-visible
/// route (anything under logmark/&lt;route&gt;/ that is part of the entry
/// stream) should NOT use this interface directly -- go through
/// IVaultWriteGate so private-route masking is enforced.
/// </summary>
public interface IVaultFileWriter
{
    void AppendText(string absolutePath, string content);
    void WriteFile(string absolutePath, string content);
    void EnsureAndWrite(string absolutePath, string content);
    void EnsureFile(string absolutePath);
    Task AppendTextAsync(string absolutePath, string content);
    Task WriteFileAsync(string absolutePath, string content);
    Task EnsureAndWriteAsync(string absolutePath, string content);
}
