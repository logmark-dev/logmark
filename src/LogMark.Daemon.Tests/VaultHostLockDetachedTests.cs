using System.Threading;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// TryAcquireDetached exists because the daemon's async standby loop disposes
/// VaultHostLock on continuation threads (after an await), where
/// Mutex.ReleaseMutex is owner-thread bound and a cross-thread call is a
/// silent no-op (see VaultHostLock's header). These tests prove the holder
/// thread actually performs the OS-level release, not just the in-process
/// bookkeeping.
/// </summary>
public class VaultHostLockDetachedTests
{
    private static string TempVault() =>
        Path.Combine(Path.GetTempPath(), "lmk-vhld-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task Dispose_FromDifferentThread_ActuallyReleasesTheMutex()
    {
        var vault = TempVault();
        var detached = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(detached);

        await Task.Run(() => detached!.Dispose());

        // If the release were a no-op (owner-thread-bound ReleaseMutex
        // swallowed when called from a foreign thread), this plain acquire
        // would fail because the OS mutex would still be held.
        using var plain = VaultHostLock.TryAcquire(vault);
        Assert.NotNull(plain);
    }

    [Fact]
    public void DetachedAcquire_ReturnsNull_WhenPlainLockAlreadyHoldsTheVault()
    {
        var vault = TempVault();
        using var plain = VaultHostLock.TryAcquire(vault);
        Assert.NotNull(plain);

        var detached = VaultHostLock.TryAcquireDetached(vault);
        Assert.Null(detached);
    }

    [Fact]
    public void PlainAcquire_ReturnsNull_WhenDetachedLockAlreadyHoldsTheVault()
    {
        var vault = TempVault();
        var detached = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(detached);

        try
        {
            using var plain = VaultHostLock.TryAcquire(vault);
            Assert.Null(plain);
        }
        finally
        {
            detached!.Dispose();
        }
    }

    [Fact]
    public void HeldInProcess_BlocksASecondInProcessDetachedAcquire()
    {
        var vault = TempVault();
        var first = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(first);

        try
        {
            var second = VaultHostLock.TryAcquireDetached(vault);
            Assert.Null(second);
        }
        finally
        {
            first!.Dispose();
        }
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        var vault = TempVault();
        var handle = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(handle);

        handle!.Dispose();
        var exception = Record.Exception(() => handle.Dispose());

        Assert.Null(exception);
    }

    /// <summary>
    /// Proves parity between TryAcquire and TryAcquireDetached on a genuine
    /// construction failure (not contention, not an abandoned mutex). A
    /// second kernel object of the WRONG handle type under the exact same
    /// name makes `new Mutex(false, name)` throw
    /// WaitHandleCannotBeOpenedException - this is what a real "unexpected
    /// failure before or after acquiring" looks like on the holder thread.
    /// Plain TryAcquire lets this propagate; TryAcquireDetached must match,
    /// not disguise it as ordinary contention (returning null).
    /// </summary>
    // The fixture in the two tests below (a NAMED EventWaitHandle) is a
    // Windows-only OS primitive - .NET throws PlatformNotSupportedException
    // constructing it at all on Linux/macOS, before the scenario under test
    // even begins. There is no Unix equivalent of "a named kernel object of
    // the wrong handle type" to substitute, so this collision scenario is
    // Windows-only by nature (see VaultHostSignal.Open for the same
    // platform gap on the signal side) - both are [WindowsOnlyFact] rather
    // than [Fact], so they report as visible Skips on non-Windows CI.

    [WindowsOnlyFact]
    public void PlainAcquire_Throws_WhenNameIsHeldByWrongHandleType()
    {
        var vault = TempVault();
        var name = VaultHostLock.MutexNameFor(vault);
        using var wrongType = new EventWaitHandle(false, EventResetMode.ManualReset, name);

        Assert.Throws<WaitHandleCannotBeOpenedException>(() => VaultHostLock.TryAcquire(vault));
    }

    [WindowsOnlyFact]
    public void DetachedAcquire_Throws_WhenNameIsHeldByWrongHandleType_AndDoesNotLeakHeldInProcess()
    {
        // See PlainAcquire_Throws_WhenNameIsHeldByWrongHandleType above -
        // same Windows-only fixture limitation.
        var vault = TempVault();
        var name = VaultHostLock.MutexNameFor(vault);

        using (new EventWaitHandle(false, EventResetMode.ManualReset, name))
        {
            Assert.Throws<WaitHandleCannotBeOpenedException>(() => VaultHostLock.TryAcquireDetached(vault));
        }

        // The exception path must not have left an entry in HeldInProcess -
        // otherwise every future in-process acquire for this vault would
        // stay blocked forever even though nothing actually holds the
        // mutex. A follow-up acquire (now that the wrong-type handle is
        // disposed) must succeed.
        var recovered = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(recovered);
        recovered!.Dispose();
    }
}
