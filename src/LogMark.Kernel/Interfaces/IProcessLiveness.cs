namespace LogMark.Interfaces;

/// <summary>
/// Answers whether a specific process is still running. A seam because the
/// sweeper's rules must be testable without spawning real processes.
/// </summary>
public interface IProcessLiveness
{
    /// <summary>
    /// True when a process with this pid is running AND started at the given UTC
    /// time. Both halves matter: pids are recycled, so a pid match alone would
    /// report a long-dead session's owner as alive.
    /// </summary>
    bool IsAlive(int pid, DateTime startUtc);
}
