// src/LogMark.Core/Services/Export/VaultArchiveResult.cs
namespace LogMark.Services.Export;

/// <summary>
/// Result of a whole-vault .logmark export.
/// </summary>
/// <param name="ArchivePath">Absolute path to the produced archive.</param>
/// <param name="EntriesCount">
/// Approximate count of entries in the exported vault, computed by scanning
/// for "## YYYY-MM-DD HH:mm" header lines across all .md files. Suitable for
/// post-export toasts and order-of-magnitude reporting; not a canonical count.
/// May over-count when entry bodies contain code fences with lines starting
/// "## 20...", and includes subpage headers in addition to entry headers.
/// </param>
public sealed record VaultArchiveResult(string ArchivePath, int EntriesCount);
