namespace LogMark.Mcp;

/// <summary>
/// Polls a liveness predicate for the host (parent) process and fires a shutdown
/// callback when the host is gone.
///
/// Why this exists: MCP hosts on Windows (notably Claude Desktop) can abandon a
/// child server's stdin without closing the pipe on renderer reload or sleep/wake.
/// The server's blocking <c>ReadAsync</c> then never observes EOF, so it never hits
/// the loop exit in <see cref="McpServer.RunAsync"/> and the process orphans -
/// observed as multiple stale logmark-mcp.exe accumulating across a session while
/// the host keeps spawning fresh instances. Watching the host process guarantees the
/// server exits when the host does, regardless of pipe state.
///
/// The polling logic here is host-agnostic and unit-tested; the platform-specific
/// parent lookup lives in <see cref="ParentProcess"/>.
/// </summary>
public sealed class ParentProcessWatchdog
{
    private readonly Func<bool> _isParentAlive;
    private readonly Action _onParentExit;
    private readonly TimeSpan _pollInterval;
    private int _fired;

    public ParentProcessWatchdog(Func<bool> isParentAlive, Action onParentExit, TimeSpan pollInterval)
    {
        _isParentAlive = isParentAlive;
        _onParentExit = onParentExit;
        _pollInterval = pollInterval;
    }

    /// <summary>
    /// Begin polling on a background task. Returns immediately. The loop checks
    /// liveness before its first delay, fires the shutdown callback at most once
    /// when the parent is gone, then stops. It also stops when <paramref name="ct"/>
    /// is cancelled (normal server shutdown), without firing.
    /// </summary>
    public Task Start(CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!_isParentAlive())
                    {
                        Fire();
                        return;
                    }

                    await Task.Delay(_pollInterval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a normal stop (the server is shutting down for
                // its own reasons); do not treat it as host death.
            }
            // Note: ct is intentionally NOT passed to Task.Run. We want the
            // returned task to complete normally when cancelled (the loop catches
            // the OCE), not to surface as a canceled/faulted task to awaiters.
        });
    }

    private void Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0)
            _onParentExit();
    }
}
