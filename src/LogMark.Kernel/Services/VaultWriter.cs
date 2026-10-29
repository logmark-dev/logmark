using System.Collections.Concurrent;
using System.Text;
using LogMark.Interfaces;

namespace LogMark.Services;

/// <summary>
/// Thin write gateway for all vault file mutations.
/// Wraps File I/O with absolute path validation, per-file semaphore
/// locking, and IVaultWriteNotifier notification.
/// </summary>
/// <remarks>
/// Uses SemaphoreSlim(1,1) instead of named Mutex for file locking.
/// SemaphoreSlim is thread-agnostic (any thread can release), which
/// avoids abandoned mutex errors when sync methods are called from
/// async contexts (MCP server, feed detectors). Cross-process write
/// coordination relies on NTFS atomic appends for short content.
/// </remarks>
public sealed class VaultWriter : IVaultFileWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _lockCache = new();

    // Encoding.UTF8 is the BOM-emitting singleton (UTF8Encoding(emitBOM: true)). Writing vault
    // files with a BOM breaks YAML frontmatter parsers (the `---` opener no longer starts at byte 0)
    // and silently corrupts any tool that scans the file head as ASCII.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IVaultWriteNotifier? _notifier;

    public VaultWriter(IVaultWriteNotifier? notifier = null)
    {
        _notifier = notifier;
    }

    // --- Sync methods ---

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public void AppendText(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var sem = GetFileLock(absolutePath);
        sem.Wait();
        try
        {
            File.AppendAllText(absolutePath, content, Utf8NoBom);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public void WriteFile(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var sem = GetFileLock(absolutePath);
        sem.Wait();
        try
        {
            File.WriteAllText(absolutePath, content, Utf8NoBom);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public void EnsureAndWrite(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var dir = Path.GetDirectoryName(absolutePath);
        if (dir != null)
            Directory.CreateDirectory(dir);
        var sem = GetFileLock(absolutePath);
        sem.Wait();
        try
        {
            File.WriteAllText(absolutePath, content, Utf8NoBom);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    public void EnsureFile(string absolutePath)
    {
        ValidateAbsolutePath(absolutePath);
        if (File.Exists(absolutePath))
            return;
        var dir = Path.GetDirectoryName(absolutePath);
        if (dir != null)
            Directory.CreateDirectory(dir);
        var sem = GetFileLock(absolutePath);
        sem.Wait();
        try
        {
            // Re-check inside the lock to avoid a race between the outer check and lock acquisition.
            if (File.Exists(absolutePath))
                return;
            File.WriteAllText(absolutePath, string.Empty, Utf8NoBom);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    // --- Async methods ---

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public async Task AppendTextAsync(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var sem = GetFileLock(absolutePath);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(absolutePath, content, Utf8NoBom).ConfigureAwait(false);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public async Task WriteFileAsync(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var sem = GetFileLock(absolutePath);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(absolutePath, content, Utf8NoBom).ConfigureAwait(false);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    [Obsolete("For vault content use IVaultWriteGate so private-route masking is enforced. " +
              "IVaultFileWriter remains the low-level interface for system files " +
              "(registry, stats, config).", error: false)]
    public async Task EnsureAndWriteAsync(string absolutePath, string content)
    {
        ValidateAbsolutePath(absolutePath);
        var dir = Path.GetDirectoryName(absolutePath);
        if (dir != null)
            Directory.CreateDirectory(dir);
        var sem = GetFileLock(absolutePath);
        await sem.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(absolutePath, content, Utf8NoBom).ConfigureAwait(false);
            _notifier?.FileWritten(absolutePath);
        }
        finally
        {
            sem.Release();
        }
    }

    // --- Internal helpers ---

    private static void ValidateAbsolutePath(string path)
    {
        if (!Path.IsPathRooted(path))
            throw new ArgumentException($"Path must be absolute. Got: {path}", nameof(path));
    }

    private static SemaphoreSlim GetFileLock(string absolutePath)
    {
        var key = absolutePath.Replace('\\', '/').ToUpperInvariant();
        return _lockCache.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }
}
