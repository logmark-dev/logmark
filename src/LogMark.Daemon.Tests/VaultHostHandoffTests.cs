using LogMark.Daemon;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Protocol integration tests for the auto-yield handoff (daemon auto-yield
/// follow-up to design 4.3). These simulate both sides of the handoff using
/// the primitives directly - VaultHostLock.TryAcquireDetached +
/// VaultHostSignal.WaitForAnnounceAsync for the daemon side, VaultHostSignal
/// Open/Announce/Clear + VaultHostLock.TryAcquire polling for the app side -
/// rather than invoking Program.cs (which is not a testable seam: top-level
/// Main, no DI). Every wait here is bounded; none sleeps the real 15s grace
/// cap (see WaitForHandoffGraceAsync in Program.cs for why that duration is
/// a parameter, not a hardcoded constant).
/// </summary>
public class VaultHostHandoffTests
{
    private static string UniqueVaultPath(string label) =>
        Path.Combine(Path.GetTempPath(), "lmk-vhh-" + label + "-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task FullHappyHandoff_DaemonObservesAnnounceAndYields_AppReacquiresAndClears()
    {
        var vault = UniqueVaultPath(nameof(FullHappyHandoff_DaemonObservesAnnounceAndYields_AppReacquiresAndClears));

        // Daemon side: standby-acquires the detached lock and opens its own
        // signal handle, exactly as Program.cs's loop does.
        using var daemonSignal = VaultHostSignal.Open(vault);
        var daemonLock = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(daemonLock);

        var daemonWait = daemonSignal.WaitForAnnounceAsync(CancellationToken.None);
        Assert.False(daemonWait.IsCompleted);

        // App side: opens its own handle to the same named event and
        // announces it wants the role back.
        using var appSignal = VaultHostSignal.Open(vault);
        appSignal.Announce();

        // Daemon-side observes within 1s (event-driven, not polling).
        var observed = await Task.WhenAny(daemonWait, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(daemonWait, observed);
        await daemonWait;

        // Daemon disposes its lock (the yield) - from a different thread,
        // exactly the scenario TryAcquireDetached exists for.
        await Task.Run(() => daemonLock!.Dispose());

        // App-side polls TryAcquire (as the real app would while waiting for
        // the daemon to actually let go) and must succeed within 2s.
        VaultHostLock? appLock = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (appLock is null && DateTime.UtcNow < deadline)
        {
            appLock = VaultHostLock.TryAcquire(vault);
            if (appLock is null)
            {
                await Task.Delay(50);
            }
        }
        Assert.NotNull(appLock);

        try
        {
            appSignal.Clear();

            // Daemon-side handle must observe the clear too.
            Assert.False(daemonSignal.IsAnnounced);
        }
        finally
        {
            appLock!.Dispose();
        }
    }

    [Fact]
    public async Task GraceTimeoutPath_AppNeverAcquiresOrClears_SignalStaysAnnouncedPastOneSecond()
    {
        var vault = UniqueVaultPath(nameof(GraceTimeoutPath_AppNeverAcquiresOrClears_SignalStaysAnnouncedPastOneSecond));

        using var daemonSignal = VaultHostSignal.Open(vault);
        var daemonLock = VaultHostLock.TryAcquireDetached(vault);
        Assert.NotNull(daemonLock);

        var daemonWait = daemonSignal.WaitForAnnounceAsync(CancellationToken.None);

        using var appSignal = VaultHostSignal.Open(vault);
        appSignal.Announce();

        await Task.WhenAny(daemonWait, Task.Delay(TimeSpan.FromSeconds(1)));
        await daemonWait;

        // Daemon yields (disposes the lock) exactly as the real loop does...
        await Task.Run(() => daemonLock!.Dispose());

        // ...but the app never shows up to acquire or clear. A real daemon
        // obeying the 15s/250ms grace cap would still be inside its grace
        // window at +1s - this pins that the signal genuinely stays
        // announced (does not self-clear, does not time out early) without
        // this test ever sleeping the real 15s. Program.cs's
        // WaitForHandoffGraceAsync takes graceDuration/pollInterval as
        // parameters for exactly this reason.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.True(daemonSignal.IsAnnounced);
    }

    [Fact]
    public async Task AppTimeoutPath_NoDaemonPresent_AppPollFailsAndHolderKeepsTheVault()
    {
        var vault = UniqueVaultPath(nameof(AppTimeoutPath_NoDaemonPresent_AppPollFailsAndHolderKeepsTheVault));

        // No daemon in this scenario: someone else (simulating a stuck /
        // unresponsive holder) holds the plain lock and never releases it.
        using var stuckHolder = VaultHostLock.TryAcquire(vault);
        Assert.NotNull(stuckHolder);

        using var appSignal = VaultHostSignal.Open(vault);
        appSignal.Announce();

        // App polls for up to 300ms (bounded) - must never acquire, because
        // the holder never yields.
        VaultHostLock? appLock = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
        while (DateTime.UtcNow < deadline)
        {
            appLock = VaultHostLock.TryAcquire(vault);
            if (appLock is not null)
            {
                break;
            }
            await Task.Delay(25);
        }
        Assert.Null(appLock);

        // App gives up and clears its own want-signal.
        appSignal.Clear();
        Assert.False(appSignal.IsAnnounced);

        // The stuck holder is untouched by any of this - still holds the
        // vault, proving the app's timeout path did not corrupt lock state.
        var stillBlocked = VaultHostLock.TryAcquire(vault);
        Assert.Null(stillBlocked);
    }

    /// <summary>
    /// Reproduces the fault-iteration race the review flagged in
    /// WatchForYieldAsync (now YieldWatch.WatchAsync): a HostAsync exit that
    /// is neither a yield nor a shutdown (a genuine fault) used to leave the
    /// iteration's watch pending against the shared want-signal after that
    /// iteration's yieldCts was disposed. A later announce would then drive
    /// the STALE watch to call Cancel() on the disposed CTS.
    ///
    /// This test drives the fixed sequence: start a watch, retire it
    /// (Program.cs now does this on every HostAsync exit path, including
    /// fault) with no announce having arrived, dispose its yieldCts, start a
    /// second iteration's watch, THEN announce. Only the live (second)
    /// watch may react; the retired (first) watch must have already ended
    /// silently and its task must never throw when observed.
    /// </summary>
    [Fact]
    public async Task FaultIteration_RetiredWatchNeverThrows_OnlyLiveWatchCancelsItsCts()
    {
        var vault = UniqueVaultPath(nameof(FaultIteration_RetiredWatchNeverThrows_OnlyLiveWatchCancelsItsCts));
        using var signal = VaultHostSignal.Open(vault);
        using var shutdown = new CancellationTokenSource();

        // Iteration 1: HostAsync "faults" (neither yield nor shutdown). The
        // loop retires the watch deterministically before disposing yieldCts.
        using var retire1 = new CancellationTokenSource();
        var yieldCts1 = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        var watch1 = YieldWatch.WatchAsync(signal, yieldCts1, shutdown.Token, retire1.Token);

        await YieldWatch.RetireAsync(retire1, watch1, TimeSpan.FromSeconds(2));
        yieldCts1.Dispose();

        // Iteration 2 starts exactly as the loop would after a fault.
        using var retire2 = new CancellationTokenSource();
        var yieldCts2 = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        var watch2 = YieldWatch.WatchAsync(signal, yieldCts2, shutdown.Token, retire2.Token);

        // The desktop app announces now. Only watch2 is still listening -
        // watch1 already ended (silently) when it was retired above.
        signal.Announce();

        var completed = await Task.WhenAny(watch2, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(watch2, completed);
        await watch2; // must not throw
        Assert.True(yieldCts2.IsCancellationRequested);

        // The retired first watch must never throw - observe its task
        // directly. Before the fix this is exactly where an unobserved
        // ObjectDisposedException would have surfaced.
        await watch1;

        yieldCts2.Dispose();
        signal.Clear();
    }

    /// <summary>
    /// Directly exercises the belt-and-suspenders guard in
    /// YieldWatch.WatchAsync: even if a yieldCts is disposed WITHOUT the
    /// watch being retired first (the exact pre-fix scenario, where
    /// WatchForYieldAsync only ever awaited the shutdown token and had no
    /// way to be told to stand down), the guarded Cancel() call must swallow
    /// ObjectDisposedException rather than let it escape into the
    /// fire-and-forget watch task.
    /// </summary>
    [Fact]
    public async Task WatchAsync_YieldCtsDisposedBeforeAnnounce_GuardedCancelDoesNotThrow()
    {
        var vault = UniqueVaultPath(nameof(WatchAsync_YieldCtsDisposedBeforeAnnounce_GuardedCancelDoesNotThrow));
        using var signal = VaultHostSignal.Open(vault);
        using var shutdown = new CancellationTokenSource();
        using var retire = new CancellationTokenSource();

        var yieldCts = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        var watch = YieldWatch.WatchAsync(signal, yieldCts, shutdown.Token, retire.Token);

        // Dispose the CTS WITHOUT retiring the watch - the unsafe pre-fix
        // sequence. The watch is still pending against the signal.
        yieldCts.Dispose();

        signal.Announce();

        var completed = await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(watch, completed);
        await watch; // must not throw despite the disposed yieldCts

        signal.Clear();
    }

    [Fact]
    public async Task GraceExpiry_WithStaleAnnounce_ClearsTheSignal_BreakingTheYieldOscillation()
    {
        var vault = UniqueVaultPath(nameof(GraceExpiry_WithStaleAnnounce_ClearsTheSignal_BreakingTheYieldOscillation));
        using var daemonSignal = VaultHostSignal.Open(vault);
        using var announcerSignal = VaultHostSignal.Open(vault);

        // A hard-killed announcer leaves the manual-reset event latched set
        // (the daemon's own handle keeps the kernel object alive). The grace
        // wait must clear it once the window fully expires, or the next
        // acquire would spuriously yield again, forever.
        announcerSignal.Announce();

        await Program.WaitForHandoffGraceAsync(
            daemonSignal,
            CancellationToken.None,
            graceDuration: TimeSpan.FromMilliseconds(400),
            pollInterval: TimeSpan.FromMilliseconds(50));

        Assert.False(daemonSignal.IsAnnounced);
    }

    [Fact]
    public async Task GraceClearedEarly_ByLiveApp_ReturnsPromptly_WithoutReclearing()
    {
        var vault = UniqueVaultPath(nameof(GraceClearedEarly_ByLiveApp_ReturnsPromptly_WithoutReclearing));
        using var daemonSignal = VaultHostSignal.Open(vault);
        using var appSignal = VaultHostSignal.Open(vault);

        appSignal.Announce();
        var clearer = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            appSignal.Clear();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Program.WaitForHandoffGraceAsync(
            daemonSignal,
            CancellationToken.None,
            graceDuration: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50));
        sw.Stop();

        await clearer;
        Assert.False(daemonSignal.IsAnnounced);
        // Early exit on the app's Clear, not the full 5s grace.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"grace wait took {sw.Elapsed}");
    }
}
