using System.Security.Cryptography;
using System.Text;

namespace LogMark.Services.Hosting;

/// <summary>
/// Cross-process exclusive HOST ROLE per vault (design 4.3, amended
/// 2026-07-13). Exactly one process may run vault services + producer host +
/// events.db writer for a given vault; the serving role (HTTP/SSE) travels
/// with the holder.
///
/// Handoff semantics:
/// - The daemon acquires via a standby loop: poll TryAcquireDetached with
///   jittered backoff, host while held, release on shutdown or yield.
/// - The interactive app acquires at vault-open. If a daemon holds the role,
///   the app announces via the sibling VaultHostSignal and the daemon
///   auto-yields (shipped 2026-07-20, lmk-xkeg; protocol in
///   docs/plans/2026-07-20-daemon-auto-yield-implementation.md): announce ->
///   daemon cancels its host session and releases -> app polls TryAcquire
///   (250ms, 10s cap) -> app clears the signal -> daemon grace-waits (15s
///   cap, with a defensive stale-signal clear) and resumes standby.
/// - No abandoned-lock recovery is needed: a named mutex dies with its
///   process, so a crashed holder frees the vault automatically
///   (AbandonedMutexException is treated as a successful acquire).
/// - Dispose on the acquiring thread for plain TryAcquire: Mutex.ReleaseMutex
///   is owner-thread bound; a cross-thread Dispose is swallowed
///   (ApplicationException) and the OS releases at process exit - fine for
///   shutdown paths, but vault-switch flows must acquire and release on the
///   same thread. Async hosts that cannot guarantee a stable thread use
///   TryAcquireDetached, whose dedicated holder thread makes Dispose safe
///   from any thread.
/// </summary>
public sealed class VaultHostLock : IDisposable
{
    // Named mutexes are thread-reentrant, so a same-thread second TryAcquire
    // would silently succeed; this process-wide set keeps the guarantee
    // "one host per vault" honest within a single process too.
    private static readonly HashSet<string> HeldInProcess = new();
    private static readonly object Gate = new();

    private readonly Mutex? _mutex;
    private readonly string _name;
    private readonly ManualResetEventSlim? _releaseSignal;
    private readonly Thread? _holderThread;
    private bool _disposed;

    private VaultHostLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        _name = name;
    }

    private VaultHostLock(string name, ManualResetEventSlim releaseSignal, Thread holderThread)
    {
        _name = name;
        _releaseSignal = releaseSignal;
        _holderThread = holderThread;
    }

    /// <summary>Try to take the host role for a vault. Null when another holder exists.</summary>
    public static VaultHostLock? TryAcquire(string vaultPath)
    {
        var name = MutexNameFor(vaultPath);
        lock (Gate)
        {
            if (HeldInProcess.Contains(name)) return null;

            var mutex = new Mutex(initiallyOwned: false, name);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // Previous holder died without releasing; the mutex is ours.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }
            HeldInProcess.Add(name);
            return new VaultHostLock(mutex, name);
        }
    }

    /// <summary>
    /// Same acquire semantics as <see cref="TryAcquire"/> (including the
    /// <see cref="HeldInProcess"/> in-process guard and
    /// <see cref="AbandonedMutexException"/> handling), but the mutex
    /// WaitOne/ReleaseMutex pair runs on a dedicated background thread that
    /// this lock owns.
    ///
    /// This exists because the daemon's async standby loop disposes the
    /// lock on a continuation thread after an await - and Mutex.ReleaseMutex
    /// is owner-thread bound, so a cross-thread release is silently
    /// swallowed (see this type's header). That would leave the vault
    /// permanently locked after a mid-life yield. Here, a holder thread
    /// acquires the mutex, blocks on an internal signal, and only releases
    /// and disposes the mutex itself - from the same thread that acquired
    /// it - when <see cref="Dispose"/> (callable from any thread) tells it
    /// to stop.
    /// </summary>
    public static VaultHostLock? TryAcquireDetached(string vaultPath)
    {
        var name = MutexNameFor(vaultPath);
        lock (Gate)
        {
            if (HeldInProcess.Contains(name)) return null;

            var acquireResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSignal = new ManualResetEventSlim(false);
            var holderThread = new Thread(() => RunHolder(name, acquireResult, releaseSignal))
            {
                IsBackground = true,
                Name = "vault-host-lock",
            };
            holderThread.Start();

            bool acquired;
            try
            {
                acquired = acquireResult.Task.GetAwaiter().GetResult();
            }
            catch
            {
                // RunHolder already resolves AbandonedMutexException to
                // acquired=true (see below), so anything that reaches here
                // is a genuine construction/WaitOne failure - the same kind
                // of failure TryAcquire lets propagate to its caller. The
                // holder thread has already returned by the time the TCS
                // faulted (TrySetException runs right before its finally
                // disposes the mutex and the thread body exits), so this
                // join is bounded and does not leak the thread; it has not
                // been added to HeldInProcess yet, so there is nothing to
                // clean up there. Do not disguise this as ordinary
                // contention - rethrow so the caller sees the real failure.
                holderThread.Join(TimeSpan.FromSeconds(5));
                releaseSignal.Dispose();
                throw;
            }

            if (!acquired)
            {
                // Acquisition failed (or the holder thread faulted); it has
                // already returned by the time the TCS completes, so this
                // join is bounded and does not leak the thread.
                holderThread.Join(TimeSpan.FromSeconds(5));
                releaseSignal.Dispose();
                return null;
            }

            HeldInProcess.Add(name);
            return new VaultHostLock(name, releaseSignal, holderThread);
        }
    }

    /// <summary>
    /// Body of the dedicated holder thread for <see cref="TryAcquireDetached"/>.
    /// Acquires the named mutex, reports the result back via
    /// <paramref name="acquireResult"/>, then blocks until
    /// <paramref name="releaseSignal"/> is set before releasing and
    /// disposing the mutex on this same thread.
    /// </summary>
    private static void RunHolder(string name, TaskCompletionSource<bool> acquireResult, ManualResetEventSlim releaseSignal)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // Previous holder died without releasing; the mutex is ours.
                acquired = true;
            }

            if (!acquired)
            {
                acquireResult.TrySetResult(false);
                return;
            }

            acquireResult.TrySetResult(true);
            releaseSignal.Wait();

            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        catch (Exception ex)
        {
            // Unexpected failure before or after acquiring: make sure the
            // caller's wait on acquireResult never hangs.
            acquireResult.TrySetException(ex);
        }
        finally
        {
            mutex?.Dispose();
        }
    }

    /// <summary>
    /// True when some process currently holds the host role for
    /// <paramref name="vaultPath"/>, without taking or contending for the
    /// role itself. A named mutex object exists only while some process
    /// holds a handle to it - opening it here and immediately disposing the
    /// handle (never calling WaitOne) is purely a liveness probe; the host
    /// process keeps its own handle open for the duration of the host role,
    /// so existence of the named mutex implies a live holder.
    ///
    /// Windows-only (see <see cref="MutexNameFor"/> and this type's use of
    /// named <see cref="Mutex"/>): <see cref="Mutex.TryOpenExisting(string, out Mutex)"/>
    /// throws <see cref="PlatformNotSupportedException"/> for named mutexes
    /// on non-Windows runtimes. LogMark.CLI and LogMark.Daemon build
    /// cross-platform (the OSS distro ships osx/linux), so this returns
    /// false on non-Windows for now - named-mutex hosting is a Windows-only
    /// concept today, and POSIX hosting coordination is a future concern;
    /// <see cref="LogMark.Services.VaultResolutionPolicy"/> falls through to
    /// bootstrap resolution there, exactly as when nothing is hosted.
    /// </summary>
    public static bool IsCurrentlyHosted(string vaultPath)
    {
        if (!OperatingSystem.IsWindows()) return false;

        if (Mutex.TryOpenExisting(MutexNameFor(vaultPath), out var m))
        {
            m.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>Stable per-vault mutex name: normalized path, hashed.</summary>
    public static string MutexNameFor(string vaultPath)
    {
        return "LogMark.VaultHost." + NameSuffixFor(vaultPath);
    }

    /// <summary>
    /// Stable per-vault name suffix: canonicalized path, SHA256-hashed, first
    /// 16 hex chars. Shared with sibling handoff primitives (e.g.
    /// VaultHostSignal) so both derive the same vault identity from the same
    /// path.
    /// </summary>
    internal static string NameSuffixFor(string vaultPath)
    {
        var normalized = CanonicalizePath(vaultPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Resolves the on-disk identity of a directory - symlinks/junctions to
    /// their final target via <see cref="FileSystemInfo.ResolveLinkTarget"/>,
    /// and on Windows, drive-root and UNC forms (subst/mapped drives, 8.3
    /// spellings, and the <c>\\?\</c> / <c>\\?\UNC\</c> prefixes) to their
    /// final path via the open-handle <c>GetFinalPathNameByHandle</c> API.
    /// The two steps run independently with their own failure handling, so a
    /// link-resolution failure (e.g. a drive root, which
    /// <see cref="FileSystemInfo.ResolveLinkTarget"/> cannot handle and
    /// throws <see cref="DirectoryNotFoundException"/> for) never skips the
    /// handle-based step - both are attempted, in order, before falling back
    /// to the lexical path. Two products naming the same vault with
    /// different spellings MUST land on the same mutex name: this method is
    /// part of the cross-product wire protocol and its output for a given
    /// directory must never change once shipped. Falls back to GetFullPath
    /// normalization when the directory does not exist or every resolution
    /// step fails - which also means a plain, non-aliased directory (the
    /// common case for a single-product install today) canonicalizes to
    /// exactly the same string GetFullPath already produced, so existing
    /// vault paths see no name change from this method's introduction; only
    /// paths that ARE links/aliases/drive-roots resolve differently.
    /// </summary>
    internal static string CanonicalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            if (Directory.Exists(full))
            {
                try
                {
                    var link = new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true);
                    if (link is not null) full = link.FullName;
                }
                catch
                {
                    // Not a link, or resolution otherwise failed (drive
                    // roots - subst'd or mapped - throw
                    // DirectoryNotFoundException here). Fall through to the
                    // handle-based step below rather than abandoning
                    // canonicalization entirely: GetFinalPathNameByHandle
                    // handles drive roots correctly where ResolveLinkTarget
                    // does not.
                }

                if (OperatingSystem.IsWindows())
                    full = GetFinalPathName(full) ?? full;
            }
        }
        catch { /* genuinely unexpected failure - fall back to lexical normalization */ }
        return full;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? GetFinalPathName(string path)
    {
        using var handle = Interop.CreateFile(path, 0,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
            Interop.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new char[1024];
        var len = Interop.GetFinalPathNameByHandle(handle, buffer, buffer.Length, 0);
        // On overflow, the return value is the required buffer size
        // INCLUDING the null terminator, so a return equal to the buffer
        // length still means the path did not fit - only "<" is success.
        if (len <= 0 || len >= buffer.Length) return null;
        var result = new string(buffer, 0, (int)len);
        return StripFinalPathPrefix(result);
    }

    /// <summary>
    /// Strips the <c>\\?\</c> extended-length prefix that
    /// <c>GetFinalPathNameByHandle</c> always returns. The UNC form is
    /// <c>\\?\UNC\server\share\...</c> - stripping only the generic 4-char
    /// prefix would leave the invalid path <c>UNC\server\share\...</c>, so
    /// that longer prefix is matched first and rewritten to the plain UNC
    /// spelling <c>\\server\share\...</c>. Pulled out as a small pure
    /// helper so both forms are unit-testable without a real network share.
    /// </summary>
    internal static string StripFinalPathPrefix(string result)
    {
        if (result.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + result[8..];
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    private static class Interop
    {
        internal const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, FileShare dwShareMode,
            IntPtr lpSecurityAttributes, FileMode dwCreationDisposition,
            int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern uint GetFinalPathNameByHandle(
            Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
            [System.Runtime.InteropServices.Out] char[] lpszFilePath, int cchFilePath, uint dwFlags);
    }

    /// <summary>
    /// Safe from any thread, idempotent, and never throws. For a plain lock
    /// (<see cref="TryAcquire"/>), releases and disposes the mutex directly
    /// here, on the caller's thread - unchanged from the original behavior,
    /// so this must be the acquiring thread (see this type's header). For a
    /// detached lock (<see cref="TryAcquireDetached"/>), the actual
    /// ReleaseMutex must happen on the dedicated holder thread that
    /// acquired it; this method signals that thread and joins it with a
    /// bounded (5s) wait. Within that bound, the release has genuinely
    /// completed by the time Dispose returns, and only then is the
    /// <see cref="HeldInProcess"/> entry removed - so in-process bookkeeping
    /// never claims the vault is free before the OS mutex actually is. If
    /// the join times out, the holder thread is abandoned: the OS mutex may
    /// remain held until that thread eventually finishes releasing it, or
    /// until process exit if it never does. In that case this method
    /// deliberately leaves the <see cref="HeldInProcess"/> entry in place,
    /// so in-process callers stay blocked from re-acquiring this vault
    /// rather than being told it is free when the OS state may disagree.
    /// </summary>
    public void Dispose()
    {
        bool isPlainMutex;
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            isPlainMutex = _mutex is not null;

            if (isPlainMutex)
            {
                var mutex = _mutex!;
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
                mutex.Dispose();
                HeldInProcess.Remove(_name);
                return;
            }
        }

        // Detached mode: release runs on the holder thread, not here. Signal
        // it and wait for it to actually perform the OS-level release
        // before touching HeldInProcess (see this method's XML doc).
        _releaseSignal!.Set();
        var released = _holderThread!.Join(TimeSpan.FromSeconds(5));
        _releaseSignal.Dispose();

        lock (Gate)
        {
            if (released)
            {
                HeldInProcess.Remove(_name);
            }
            // else: join timed out. The holder thread may still be holding
            // the OS mutex, so the HeldInProcess entry stays - removing it
            // here would let another in-process caller believe the vault is
            // free while the OS state disagrees.
        }
    }
}
