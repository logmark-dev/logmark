using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// VaultHostSignal is the manual-reset named-event want-signal sibling to
/// VaultHostLock (see VaultHostLock's header for the handoff protocol). Two
/// Open() handles per test simulate the app process and the daemon process
/// sharing one named event; each test uses a unique temp vault path so
/// parallel test runs never collide on the same event name.
/// </summary>
public class VaultHostSignalTests
{
    private static string UniqueVaultPath(string label)
    {
        return Path.Combine(Path.GetTempPath(), "lmk-vhs-" + label + "-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Announce_OnOneHandle_IsAnnouncedOnAnother()
    {
        var vault = UniqueVaultPath(nameof(Announce_OnOneHandle_IsAnnouncedOnAnother));
        using var app = VaultHostSignal.Open(vault);
        using var daemon = VaultHostSignal.Open(vault);

        Assert.False(daemon.IsAnnounced);

        app.Announce();

        Assert.True(daemon.IsAnnounced);
    }

    [Fact]
    public async Task WaitForAnnounceAsync_CompletesAfterLaterAnnounce()
    {
        var vault = UniqueVaultPath(nameof(WaitForAnnounceAsync_CompletesAfterLaterAnnounce));
        using var app = VaultHostSignal.Open(vault);
        using var daemon = VaultHostSignal.Open(vault);

        var waitTask = daemon.WaitForAnnounceAsync(CancellationToken.None);
        Assert.False(waitTask.IsCompleted);

        app.Announce();

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waitTask, completed);
        await waitTask;
    }

    [Fact]
    public async Task WaitForAnnounceAsync_AlreadyAnnounced_CompletesImmediately()
    {
        var vault = UniqueVaultPath(nameof(WaitForAnnounceAsync_AlreadyAnnounced_CompletesImmediately));
        using var signal = VaultHostSignal.Open(vault);

        signal.Announce();

        var waitTask = signal.WaitForAnnounceAsync(CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waitTask, completed);
        await waitTask;
    }

    [Fact]
    public void Clear_FlipsIsAnnouncedFalse_OnBothHandles()
    {
        var vault = UniqueVaultPath(nameof(Clear_FlipsIsAnnouncedFalse_OnBothHandles));
        using var app = VaultHostSignal.Open(vault);
        using var daemon = VaultHostSignal.Open(vault);

        app.Announce();
        Assert.True(daemon.IsAnnounced);

        app.Clear();

        Assert.False(app.IsAnnounced);
        Assert.False(daemon.IsAnnounced);
    }

    [Fact]
    public async Task WaitForAnnounceAsync_Cancelled_DoesNotThrowUnhandledAndDisposesCleanly()
    {
        var vault = UniqueVaultPath(nameof(WaitForAnnounceAsync_Cancelled_DoesNotThrowUnhandledAndDisposesCleanly));
        using var signal = VaultHostSignal.Open(vault);

        using var cts = new CancellationTokenSource();
        var waitTask = signal.WaitForAnnounceAsync(cts.Token);

        cts.Cancel();

        var bounded = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waitTask, bounded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);

        // Dispose after cancel must not crash or throw, and must not leave
        // the registered wait dangling.
        var exception = Record.Exception(() => signal.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task Dispose_WhilePendingWait_DoesNotThrow_AndCancelStillCompletes()
    {
        var vault = UniqueVaultPath(nameof(Dispose_WhilePendingWait_DoesNotThrow_AndCancelStillCompletes));
        var signal = VaultHostSignal.Open(vault);

        using var cts = new CancellationTokenSource();
        var waitTask = signal.WaitForAnnounceAsync(cts.Token);

        // Contract (see VaultHostSignal.Dispose doc): disposing with a wait
        // still pending is benign but unsupported - it must not throw, and
        // the caller-owned token must still be able to complete the wait.
        var exception = Record.Exception(() => signal.Dispose());
        Assert.Null(exception);

        cts.Cancel();
        var bounded = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waitTask, bounded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var vault = UniqueVaultPath(nameof(Dispose_IsIdempotent));
        var signal = VaultHostSignal.Open(vault);

        signal.Dispose();
        var exception = Record.Exception(() => signal.Dispose());

        Assert.Null(exception);
    }

    // VaultHostSignal.SharedEventRegistry backs Open() on non-Windows, but
    // its contract is platform-agnostic (anonymous handles + refcounts), so
    // it is driven directly here to run on every platform including
    // Windows, where the public Open() path never touches it.

    private static string UniqueRegistryName(string label) =>
        "lmk-vhs-registry-" + label + "-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void SharedEventRegistry_AcquireTwice_SameName_ReturnsSameHandleInstance_ObservableViaSet()
    {
        var name = UniqueRegistryName(nameof(SharedEventRegistry_AcquireTwice_SameName_ReturnsSameHandleInstance_ObservableViaSet));

        var first = VaultHostSignal.SharedEventRegistry.Acquire(name);
        var second = VaultHostSignal.SharedEventRegistry.Acquire(name);

        Assert.Same(first, second);

        first.Set();
        Assert.True(second.WaitOne(0));

        VaultHostSignal.SharedEventRegistry.Release(name);
        VaultHostSignal.SharedEventRegistry.Release(name);
    }

    [Fact]
    public void SharedEventRegistry_AcquireTwoDifferentNames_ReturnsIndependentHandles()
    {
        var nameA = UniqueRegistryName(nameof(SharedEventRegistry_AcquireTwoDifferentNames_ReturnsIndependentHandles) + "-a");
        var nameB = UniqueRegistryName(nameof(SharedEventRegistry_AcquireTwoDifferentNames_ReturnsIndependentHandles) + "-b");

        var handleA = VaultHostSignal.SharedEventRegistry.Acquire(nameA);
        var handleB = VaultHostSignal.SharedEventRegistry.Acquire(nameB);

        Assert.NotSame(handleA, handleB);

        handleA.Set();
        Assert.False(handleB.WaitOne(0));

        VaultHostSignal.SharedEventRegistry.Release(nameA);
        VaultHostSignal.SharedEventRegistry.Release(nameB);
    }

    [Fact]
    public void SharedEventRegistry_ReleaseOneOfTwoHolders_HandleStillFunctionalForSurvivor()
    {
        var name = UniqueRegistryName(nameof(SharedEventRegistry_ReleaseOneOfTwoHolders_HandleStillFunctionalForSurvivor));

        var first = VaultHostSignal.SharedEventRegistry.Acquire(name);
        var second = VaultHostSignal.SharedEventRegistry.Acquire(name);

        VaultHostSignal.SharedEventRegistry.Release(name);

        second.Set();
        Assert.True(second.WaitOne(0));

        VaultHostSignal.SharedEventRegistry.Release(name);
    }

    [Fact]
    public void SharedEventRegistry_ReleaseLastHolder_EntryRemoved_FreshAcquireReturnsNewUnsignaledHandle()
    {
        var name = UniqueRegistryName(nameof(SharedEventRegistry_ReleaseLastHolder_EntryRemoved_FreshAcquireReturnsNewUnsignaledHandle));

        var first = VaultHostSignal.SharedEventRegistry.Acquire(name);
        first.Set();
        Assert.True(first.WaitOne(0));

        VaultHostSignal.SharedEventRegistry.Release(name);

        var fresh = VaultHostSignal.SharedEventRegistry.Acquire(name);

        Assert.NotSame(first, fresh);
        Assert.False(fresh.WaitOne(0));

        VaultHostSignal.SharedEventRegistry.Release(name);
    }

    [Fact]
    public void Open_Twice_SameVaultPath_DisposeOneInstanceTwice_TheOtherInstanceStillWorks()
    {
        // Exercised through the public VaultHostSignal type (not the
        // registry directly) so the assertion is valid on both platforms:
        // on Windows, Open() is named-event backed and each instance owns
        // an independently-closeable OS handle to the same shared kernel
        // object, so disposing one instance (even twice - Dispose is
        // idempotent, see Dispose_IsIdempotent) never affects the other's
        // handle. On non-Windows, Open() resolves both instances to the
        // SAME underlying anonymous handle via SharedEventRegistry, so this
        // also proves a redundant Dispose (blocked by VaultHostSignal's own
        // _disposed guard before it ever reaches the registry) does not
        // decrement the refcount a second time and tear the shared handle
        // down out from under the surviving instance.
        var vault = UniqueVaultPath(nameof(Open_Twice_SameVaultPath_DisposeOneInstanceTwice_TheOtherInstanceStillWorks));

        var instanceA = VaultHostSignal.Open(vault);
        using var instanceB = VaultHostSignal.Open(vault);

        instanceA.Dispose();
        var exception = Record.Exception(() => instanceA.Dispose());
        Assert.Null(exception);

        instanceB.Announce();
        Assert.True(instanceB.IsAnnounced);
    }
}
