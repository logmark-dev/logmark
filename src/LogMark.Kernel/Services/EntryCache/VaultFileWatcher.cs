using System.Collections.Concurrent;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Watches the logmark/ directory for external file changes (from MCP, CLI, or Obsidian).
/// Debounces rapid events and invokes a callback for each changed file.
/// Thread-safe: callbacks fire on thread pool threads and use only lock-free structures.
/// Does NOT acquire any VaultWriter named mutexes, so cannot deadlock with
/// concurrent writers across app, MCP, and CLI processes.
/// </summary>
internal sealed class VaultFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _onFileChanged;
    private readonly Action? _onOverflow;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
    private bool _disposed;

    public VaultFileWatcher(string logmarkPath, Action<string> onFileChanged, Action? onOverflow = null)
    {
        _onFileChanged = onFileChanged;
        _onOverflow = onOverflow;

        _watcher = new FileSystemWatcher(logmarkPath, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            InternalBufferSize = 65536 // 64KB buffer (default 8KB overflows on burst writes)
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnChanged;
        _watcher.Error += OnError;
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        ScheduleInvalidation(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldIgnore(e.OldFullPath))
            ScheduleInvalidation(e.OldFullPath);
        if (!ShouldIgnore(e.FullPath))
            ScheduleInvalidation(e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow -- too many rapid changes. Signal full re-verify.
        _onOverflow?.Invoke();
    }

    private void ScheduleInvalidation(string absolutePath)
    {
        if (_disposed) return;

        var key = absolutePath.ToUpperInvariant();

        if (_debounceTimers.TryGetValue(key, out var existing))
        {
            // Reset existing timer
            try { existing.Change(_debounceDelay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { /* Timer disposed between TryGet and Change */ }
            return;
        }

        var timer = new Timer(_ =>
        {
            if (_debounceTimers.TryRemove(key, out var removed))
                removed.Dispose();

            if (!_disposed)
                _onFileChanged(absolutePath);
        }, null, _debounceDelay, Timeout.InfiniteTimeSpan);

        if (!_debounceTimers.TryAdd(key, timer))
        {
            // Race: another thread added first. Dispose ours, reset theirs.
            timer.Dispose();
            if (_debounceTimers.TryGetValue(key, out var other))
            {
                try { other.Change(_debounceDelay, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { /* disposed between get and change */ }
            }
        }
    }

    private static bool ShouldIgnore(string path)
    {
        return path.Contains("_system", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();

        foreach (var kvp in _debounceTimers)
        {
            if (_debounceTimers.TryRemove(kvp.Key, out var timer))
                timer.Dispose();
        }
    }
}
