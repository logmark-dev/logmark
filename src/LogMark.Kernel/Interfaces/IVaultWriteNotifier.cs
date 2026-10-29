namespace LogMark.Interfaces;

/// <summary>
/// Notification contract for vault file mutations.
/// Implementors are notified after a vault file is written to disk.
/// </summary>
public interface IVaultWriteNotifier
{
    /// <summary>
    /// Called after a vault file has been written. Path must be absolute.
    /// </summary>
    void FileWritten(string absolutePath);
}
