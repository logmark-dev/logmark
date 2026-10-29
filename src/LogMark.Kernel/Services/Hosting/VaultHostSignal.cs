namespace LogMark.Services.Hosting;

/// <summary>
/// Cross-process want-signal for vault host handoff (design 4.3 amended,
/// daemon auto-yield follow-up). A manual-reset named event, one per vault,
/// that lets the interactive app announce "I want the host role" and the
/// daemon watch for it event-driven instead of polling.
///
/// Sibling primitive to VaultHostLock: shares the vault-path name derivation
/// (VaultHostLock.NameSuffixFor) so both independently resolve the same
/// per-vault identity from the same path, without either owning the other.
///
/// Protocol (v1): the app calls Announce() when it wants the host role back;
/// the daemon awaits WaitForAnnounceAsync, releases VaultHostLock, and the
/// app clears the signal (Clear()) once it has reacquired the role, so the
/// event does not stay latched for the next handoff cycle.
/// </summary>
public sealed class VaultHostSignal : IDisposable
{
    private readonly EventWaitHandle _event;
    private readonly string? _registryKey;
    private bool _disposed;

    private VaultHostSignal(EventWaitHandle waitHandle, string? registryKey)
    {
        _event = waitHandle;
        _registryKey = registryKey;
    }

    /// <summary>
    /// Opens or creates the per-vault want-signal event. Never throws when
    /// it already exists.
    /// </summary>
    /// <remarks>
    /// Platform split: named EventWaitHandle is a Windows-only OS primitive
    /// (.NET throws PlatformNotSupportedException constructing the named
    /// overload on Linux/macOS), so the two platforms resolve the underlying
    /// handle differently while every other member of this type (Announce,
    /// Clear, IsAnnounced, WaitForAnnounceAsync, Dispose) operates on
    /// whatever <see cref="EventWaitHandle"/> it was handed, named or not.
    ///
    /// On Windows, each Open() call creates its own named handle - the OS
    /// shares the underlying kernel object across handles (and processes)
    /// with the same name, so repeat Open() calls for the same vault path
    /// observe the same signal without any in-process bookkeeping.
    ///
    /// On non-Windows, Open() resolves through <see cref="SharedEventRegistry"/>,
    /// a process-local, ref-counted, name-keyed registry of anonymous
    /// manual-reset events (the unnamed constructor overload is supported on
    /// every platform). Repeat Open() calls for the same name share the same
    /// anonymous handle within this process; the registry tears the handle
    /// down and forgets the name only once every holder has disposed.
    ///
    /// PROCESS-LOCAL on non-Windows, deliberately (v1): a second OS process
    /// opening the same vault's signal gets an independent anonymous event
    /// and will NOT observe another process's Announce - anonymous handles
    /// cannot be shared across processes the way a named handle can. This is
    /// acceptable for v1 because the only announcer in the product is the
    /// Windows-only WPF desktop app; on Unix the daemon is the only
    /// host-role participant, and the app side of the handoff protocol
    /// already has a warn-and-continue fallback if a daemon never yields.
    /// A future Unix announcer would need a genuinely cross-process signal
    /// (e.g. a filesystem-watched marker) - this registry does not provide
    /// one, by design.
    /// </remarks>
    public static VaultHostSignal Open(string vaultPath)
    {
        var name = "LogMark.VaultWant." + VaultHostLock.NameSuffixFor(vaultPath);

        if (OperatingSystem.IsWindows())
        {
            var waitHandle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, name);
            return new VaultHostSignal(waitHandle, registryKey: null);
        }

        var sharedHandle = SharedEventRegistry.Acquire(name);
        return new VaultHostSignal(sharedHandle, registryKey: name);
    }

    /// <summary>Raises the want-signal. Manual-reset: stays announced until Clear().</summary>
    public void Announce() => _event.Set();

    /// <summary>Lowers the want-signal.</summary>
    public void Clear() => _event.Reset();

    /// <summary>True if the signal is currently announced. Non-consuming (manual-reset makes WaitOne(0) safe to poll).</summary>
    public bool IsAnnounced => _event.WaitOne(0);

    /// <summary>
    /// Completes when the signal is announced - immediately if already
    /// announced, otherwise event-driven via the thread pool's wait registry
    /// (no polling). Cancelling <paramref name="ct"/> completes the returned
    /// task as cancelled and unregisters the pending wait; it does not throw
    /// synchronously and does not leak the registration.
    /// </summary>
    public Task WaitForAnnounceAsync(CancellationToken ct = default)
    {
        if (_event.WaitOne(0))
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _event,
            (_, _) => tcs.TrySetResult(),
            state: null,
            timeout: Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);

        var ctRegistration = ct.Register(() => tcs.TrySetCanceled(ct));

        // Fire-and-forget cleanup: runs on any completion (set or cancelled)
        // and unregisters the pooled wait plus the cancellation callback so
        // neither leaks past this call.
        tcs.Task.ContinueWith(
            _ =>
            {
                registeredWait.Unregister(null);
                ctRegistration.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return tcs.Task;
    }

    /// <summary>
    /// Releases this handle to the underlying event (on Windows, the named
    /// handle directly; on non-Windows, a refcount decrement against
    /// <see cref="SharedEventRegistry"/> - the underlying anonymous handle is
    /// only disposed once every holder has released). Does NOT complete or
    /// cancel an in-flight WaitForAnnounceAsync: callers own their
    /// CancellationToken and must cancel before disposing. A wait left
    /// pending across Dispose is benign (the registered wait holds its own
    /// reference to the underlying handle) but is not a supported pattern.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registryKey is not null)
        {
            SharedEventRegistry.Release(_registryKey);
        }
        else
        {
            _event.Dispose();
        }
    }

    /// <summary>
    /// Process-local, ref-counted, name-keyed registry of anonymous
    /// manual-reset <see cref="EventWaitHandle"/> instances. Backs
    /// <see cref="Open"/> on non-Windows (named events are unavailable
    /// there), standing in for the OS-level name sharing that a named
    /// EventWaitHandle gives Windows for free. Platform-agnostic by design
    /// (it only manages anonymous handles and reference counts) so its
    /// contract can be unit-tested on any platform, including Windows.
    /// </summary>
    internal static class SharedEventRegistry
    {
        private sealed class Entry
        {
            public required EventWaitHandle Handle { get; init; }
            public int RefCount;
        }

        private static readonly object Gate = new();
        private static readonly Dictionary<string, Entry> Entries = new();

        /// <summary>
        /// Returns the shared anonymous handle for <paramref name="name"/>,
        /// creating a fresh unsignaled one if no holder currently references
        /// this name, and incrementing its reference count either way.
        /// </summary>
        public static EventWaitHandle Acquire(string name)
        {
            lock (Gate)
            {
                if (Entries.TryGetValue(name, out var entry))
                {
                    entry.RefCount++;
                    return entry.Handle;
                }

                var handle = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
                Entries[name] = new Entry { Handle = handle, RefCount = 1 };
                return handle;
            }
        }

        /// <summary>
        /// Decrements the reference count for <paramref name="name"/>.
        /// Disposes the underlying handle and forgets the name only when the
        /// last holder releases; a subsequent Acquire for the same name then
        /// creates a fresh, unsignaled handle. Safe to call at most once per
        /// Acquire - callers (VaultHostSignal.Dispose) are responsible for
        /// their own idempotence, matching Acquire/Release 1:1.
        /// </summary>
        public static void Release(string name)
        {
            lock (Gate)
            {
                if (!Entries.TryGetValue(name, out var entry))
                {
                    return;
                }

                entry.RefCount--;
                if (entry.RefCount <= 0)
                {
                    Entries.Remove(name);
                    entry.Handle.Dispose();
                }
            }
        }
    }
}
