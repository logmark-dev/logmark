using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LogMark.Mcp;

/// <summary>
/// Resolves the parent (host) process so <see cref="ParentProcessWatchdog"/> can
/// detect host death. Windows-only; returns null on other platforms or on any
/// failure, in which case the server falls back to plain stdin-EOF shutdown.
/// </summary>
public static class ParentProcess
{
    /// <summary>
    /// Returns the process that launched this one, or null if it cannot be
    /// resolved (non-Windows, access denied, or the parent already exited and
    /// its PID was reused). A non-null result holds an open handle, so the PID
    /// stays valid for liveness checks until the returned Process is disposed.
    /// </summary>
    public static Process? TryGetParent()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            return TryGetParentWindows();
        }
        catch
        {
            // Any failure (P/Invoke error, access denied, race) degrades to the
            // pre-existing EOF-only behavior. The watchdog is a safety net, not a
            // correctness dependency.
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Process? TryGetParentWindows()
    {
        using var self = Process.GetCurrentProcess();

        var info = new PROCESS_BASIC_INFORMATION();
        int status = NtQueryInformationProcess(
            self.Handle, ProcessBasicInformation, ref info, (uint)Marshal.SizeOf(info), out _);
        if (status != 0)
            return null;

        int parentPid = (int)info.InheritedFromUniqueProcessId.ToUInt64();
        if (parentPid <= 0)
            return null;

        var parent = Process.GetProcessById(parentPid);

        // Guard against PID reuse: if the candidate started after we did, our real
        // parent already exited and the OS recycled its PID. Treat as no parent so
        // we do not watch (and later kill ourselves over) an unrelated process.
        try
        {
            if (parent.StartTime > self.StartTime)
            {
                parent.Dispose();
                return null;
            }
        }
        catch
        {
            // StartTime can be inaccessible for some processes; accept the parent
            // rather than lose the watchdog entirely.
        }

        return parent;
    }

    private const int ProcessBasicInformation = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public UIntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        uint processInformationLength,
        out uint returnLength);
}
