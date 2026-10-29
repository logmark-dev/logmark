using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Marks a test that depends on Windows-only named-handle vault host
/// signaling (VaultHostSignal / VaultHostLock's use of named
/// EventWaitHandle / Mutex constructs that throw
/// PlatformNotSupportedException on Linux/macOS). Reports as a visible
/// xUnit Skip on non-Windows runners instead of an early `return;` inside
/// the test body, which xUnit would otherwise report as PASSED without
/// asserting anything. See docs/operations/oss-launch-runbook.md gate 0 for
/// the cross-platform fix this is standing in for.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only: named-handle vault host signaling (see oss-launch-runbook gate 0)";
        }
    }
}
