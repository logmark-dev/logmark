using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Tests the pure liveness-polling logic of <see cref="ParentProcessWatchdog"/>.
/// The platform-specific parent lookup (<see cref="ParentProcess"/>) is glue and
/// is exercised indirectly; the shutdown decision is what must be correct.
/// </summary>
public class ParentProcessWatchdogTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Start_FiresOnParentExit_WhenLivenessGoesFalse()
    {
        var alive = true;
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchdog = new ParentProcessWatchdog(
            isParentAlive: () => alive,
            onParentExit: () => fired.TrySetResult(true),
            pollInterval: Poll);

        using var cts = new CancellationTokenSource();
        _ = watchdog.Start(cts.Token);

        alive = false; // simulate host process death

        var completed = await Task.WhenAny(fired.Task, Task.Delay(2000));
        completed.Should().Be(fired.Task, "watchdog must fire once liveness goes false");
    }

    [Fact]
    public async Task Start_DoesNotFire_WhileParentAlive()
    {
        var count = 0;

        var watchdog = new ParentProcessWatchdog(
            isParentAlive: () => true,
            onParentExit: () => Interlocked.Increment(ref count),
            pollInterval: Poll);

        using var cts = new CancellationTokenSource();
        _ = watchdog.Start(cts.Token);

        await Task.Delay(300);
        count.Should().Be(0, "an alive host must never trigger shutdown");
    }

    [Fact]
    public async Task Start_FiresExactlyOnce_WhenParentStaysDead()
    {
        var count = 0;

        var watchdog = new ParentProcessWatchdog(
            isParentAlive: () => false,
            onParentExit: () => Interlocked.Increment(ref count),
            pollInterval: Poll);

        using var cts = new CancellationTokenSource();
        _ = watchdog.Start(cts.Token);

        await Task.Delay(300);
        count.Should().Be(1, "shutdown must fire once and the loop must then stop");
    }

    [Fact]
    public async Task Start_StopsPolling_WhenCancelled()
    {
        var alive = true;
        var count = 0;

        var watchdog = new ParentProcessWatchdog(
            isParentAlive: () => alive,
            onParentExit: () => Interlocked.Increment(ref count),
            pollInterval: Poll);

        using var cts = new CancellationTokenSource();
        var loop = watchdog.Start(cts.Token);

        cts.Cancel();
        await loop; // completes promptly, does not throw

        alive = false; // host "dies" after we stopped watching
        await Task.Delay(100);
        count.Should().Be(0, "a cancelled watchdog must not fire");
    }
}
