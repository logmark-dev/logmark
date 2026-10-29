using System.Diagnostics;
using System.Globalization;

namespace LogMark.Services;

/// <summary>
/// Encodes which process owns a session record, so a sweeper can ask whether that
/// process is still alive instead of guessing from elapsed time.
///
/// Format: "{machine}:{pid}:{process-start-utc-iso}".
///
/// The machine name is part of the identity because the vault is synced across
/// nodes: a pid written on one machine says nothing about any process on another,
/// and a liveness check that ignored the machine would mark live remote sessions
/// dead. The process start time is part of it because pids are recycled, and a
/// recycled pid would make a dead session look alive forever.
///
/// Times are UTC on both sides of the comparison. Session records round-trip
/// `started` as local while other stamps in this codebase parse as UTC, and that
/// exact mismatch has produced real bugs before (see the timestamp note in
/// AgentCaptureCommand.ResolveClaimableSession).
/// </summary>
public static class SessionOwner
{
    public static string Current()
    {
        using var self = Process.GetCurrentProcess();
        return Format(Environment.MachineName, self.Id, self.StartTime.ToUniversalTime());
    }

    public static string Format(string machine, int pid, DateTime startUtc)
        => $"{machine}:{pid}:{startUtc.ToUniversalTime():O}";

    public static bool TryParse(string? value, out string machine, out int pid, out DateTime startUtc)
    {
        machine = "";
        pid = 0;
        startUtc = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Split on the first two separators only: the ISO timestamp contains colons.
        var firstSep = value.IndexOf(':');
        if (firstSep <= 0)
            return false;

        var secondSep = value.IndexOf(':', firstSep + 1);
        if (secondSep <= firstSep + 1)
            return false;

        machine = value.Substring(0, firstSep);

        if (!int.TryParse(value.Substring(firstSep + 1, secondSep - firstSep - 1), out pid))
            return false;

        var startText = value.Substring(secondSep + 1);
        if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;

        startUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }
}
