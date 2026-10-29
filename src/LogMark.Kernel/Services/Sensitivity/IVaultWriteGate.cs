using System.Threading.Tasks;
using LogMark.Models;
using LogMark.Models.Sensitivity;

namespace LogMark.Services.Sensitivity;

/// <summary>
/// Sole public surface for writing vault content. Performs route-aware
/// private masking before delegating to IVaultFileWriter.
///
/// All capture, paste, amendment, reroute, and type-conversion write
/// paths MUST go through this gate so that the paranoid/financial/dev
/// privacy guarantees cannot be silently bypassed by a new call site.
///
/// System files (registry, stats, config, logs) use IVaultFileWriter
/// directly -- they are not vault content and must not be masked.
/// </summary>
public interface IVaultWriteGate
{
    /// <summary>
    /// Append or write an entry to its routed destination. Masks
    /// entry.RawContent when `route.Private` is set. Used by
    /// FileRouter for capture-time routed writes.
    /// </summary>
    Task<VaultWriteResult> WriteEntryAsync(string absoluteDestFile, Entry entry, Route route,
                                           string? overrideContent = null);

    /// <summary>
    /// Write a paste subpage. Masks `content` when `route` is private.
    /// Used by VaultService.CreateSubpageAndReplaceContent.
    /// </summary>
    Task<VaultWriteResult> WriteSubpageAsync(string absoluteSubpagePath, string content, Route? route);

    /// <summary>
    /// Rewrite a whole YYYY.md file after an in-place edit (amend,
    /// reroute, convert-type). Route is inferred from the file path.
    /// When the inferred route is private, the full file content is
    /// rescanned and masked. Existing `[TYPE:ID]` tokens pass through
    /// unchanged (see the closed sensitivity provider's Scan).
    /// </summary>
    Task<VaultWriteResult> RewriteFileAsync(string absoluteFilePath, string content);
}
