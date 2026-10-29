using System.Diagnostics;
using System.Text;

namespace LogMark.Services.Agents;

public class SystemProcessRunner : IProcessRunner
{
    /// <summary>
    /// Bug found during task 10b's real end-to-end verification (2026-07-31):
    /// wsl.exe writes UTF-16LE to a redirected stdout pipe (verified against a
    /// live Ubuntu distro). The previous implementation read output via
    /// Process.OutputDataReceived (line-based, using .NET's default
    /// StandardOutputEncoding) and reassembled lines with
    /// StringBuilder.AppendLine, which inserts Environment.NewLine - real "\r\n"
    /// characters with no interleaved NUL bytes - between each already-decoded
    /// line. WslBridge.DecodeMaybeUtf16 (and ResolveDistro's line-splitting)
    /// depend on the accumulated string being a faithful one-char-per-byte
    /// rendering of the ORIGINAL byte stream so it can re-encode-then-redecode
    /// it as UTF-16LE; the injected separator bytes desynchronize that
    /// alignment partway through multi-line output, corrupting everything
    /// after the first line (confirmed live: DefaultDistro resolved to
    /// "Ubuntu" followed by two garbage characters instead of a clean name,
    /// which made the subsequent `wsl.exe -d "Ubuntu&lt;garbage&gt;" ...` probe
    /// fail and LinuxHome/UncRoot silently degrade to the Windows-path
    /// fallback - the whole grok-in-WSL feature never actually worked end to
    /// end despite full unit coverage, because every existing test drives
    /// WslBridge through a FakeProcessRunner and never exercises this real
    /// byte-capture path).
    ///
    /// Fix: read the raw stdout/stderr bytes directly off the process's
    /// underlying stream (no line splitting, no text encoding applied yet, no
    /// separator characters spliced in) and decode the complete byte buffer
    /// exactly once. This preserves byte order losslessly, so
    /// DecodeMaybeUtf16's re-encode/re-decode trick works for output of any
    /// length, not just a single line.
    /// </summary>
    public ProcessResult Run(string executable, string[] args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var overallTimer = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();

        // Start draining both pipes concurrently via async raw-byte reads
        // BEFORE blocking on WaitForExit, so a full output buffer can never
        // deadlock the child process (the classic redirected-output hazard).
        //
        // Bug found during fix-round 1 review (2026-07-31): the initial raw-byte
        // rewrite waited on these two tasks with Task.WaitAll and NO timeout.
        // A child that exits within budget does not guarantee its stdout/stderr
        // pipes see EOF promptly - a surviving grandchild that inherited the
        // redirected handle (plausible for the npm/node-launched CLIs
        // ProcessRunnerNativeCliDelegate drives) keeps the write end open, so
        // CopyToAsync never completes and Run() blocks forever. Bound the read
        // wait to whatever timeout budget remains after WaitForExit, via a
        // CancellationToken threaded into each read - falling back to
        // whatever bytes arrived before the cutoff rather than hanging.
        using var readCts = new CancellationTokenSource();
        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, readCts.Token);
        var stderrTask = ReadAllBytesAsync(process.StandardError.BaseStream, readCts.Token);

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            readCts.Cancel();
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, "", "", TimedOut: true);
        }

        var remaining = timeout - overallTimer.Elapsed;
        readCts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);

        // Both tasks always complete (successfully) once cancelled -
        // ReadAllBytesAsync catches OperationCanceledException internally and
        // returns whatever was captured so far - so this wait is bounded by
        // the CancelAfter above and never blocks indefinitely.
        Task.WaitAll(stdoutTask, stderrTask);
        return new ProcessResult(
            process.ExitCode,
            DecodeRawBytes(stdoutTask.Result),
            DecodeRawBytes(stderrTask.Result),
            TimedOut: false);
    }

    /// <summary>
    /// Bug found during the final whole-feature review (2026-07-31): the await
    /// below carried no ConfigureAwait(false). Run() is a SYNCHRONOUS method
    /// that blocks on Task.WaitAll, and it is reachable from the WPF dispatcher
    /// thread (App.Startup -&gt; ProviderRegistration.Refresh(selfHeal: true) -&gt;
    /// WiringSelfHeal -&gt; InboundWirer.HealUserScopeEntry -&gt;
    /// ProcessRunnerNativeCliDelegate, and SettingsMcpView.CommitWiring). On a
    /// thread carrying a single-threaded SynchronizationContext the
    /// continuation after CopyToAsync is POSTED back to that context, which
    /// cannot pump while Run() is blocked inside Task.WaitAll - so the read
    /// tasks never complete and the app hangs at startup, before the tray icon
    /// appears, for any user with a native claude.exe / codex.exe / q.exe on
    /// PATH and an existing user-scope MCP entry. Nothing WSL- or grok-specific
    /// about it. ConfigureAwait(false) pushes the continuation to the thread
    /// pool, where it runs regardless of what the calling thread is doing.
    /// Pinned by Run_DoesNotDeadlock_OnThreadWithSynchronizationContext.
    /// </summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timed out draining the pipe (see the caller's comment) - return
            // whatever bytes arrived before the cutoff rather than losing them
            // or hanging further.
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Latin1 maps every byte 0x00-0xFF to the character of the same value,
    /// 1:1 and reversibly - exactly the "naive" single-byte rendering
    /// WslBridge.DecodeMaybeUtf16 detects and repairs by re-encoding and
    /// re-decoding as UTF-16LE.
    ///
    /// Was Encoding.UTF8.GetString until the final whole-feature review
    /// (2026-07-31). UTF-8 is only lossless here for bytes below 0x80: any
    /// byte at or above that is not valid standalone UTF-8, so it decodes to
    /// U+FFFD and the original value is gone for good. That silently breaks
    /// the DecodeMaybeUtf16 round-trip for a non-English `claude mcp add`
    /// stderr, a non-ASCII distro name, and a non-ASCII $HOME. Latin1 has no
    /// such hole and satisfies the same byte-exactness tests.
    /// </summary>
    private static string DecodeRawBytes(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    public bool IsOnPath(string executable)
    {
        // Only probe for extensions Run() can actually launch with UseShellExecute=false:
        // real executable images (.exe / extensionless PE). .cmd/.bat shims -- how npm-installed
        // CLIs like codex and claude land on Windows -- are NOT launchable this way; starting one
        // throws Win32Exception. Reporting them "available" used to send Connect/Disconnect down a
        // native-CLI path that always threw and crashed the app; both callers fall back to the
        // file-writer instead, which is the correct behaviour for shim-only installs.
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
        foreach (var dir in paths)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in new[] { "", ".exe" })
            {
                if (File.Exists(Path.Combine(dir, executable + ext))) return true;
            }
        }
        return false;
    }
}
