using System.Text;

namespace LogMark.Services.Agents;

/// <summary>
/// The only type in the codebase that knows the string "wsl.exe". Owns distro
/// discovery, both path-translation directions, command probing, and process
/// launch for WSL-hosted agents (see AgentHost.Wsl).
///
/// Translation rules, verified 2026-07-31 against a live Ubuntu distro:
///   - Linux paths under /mnt/&lt;drive&gt;/ are DrvFs mounts of Windows drives
///     and map to &lt;DRIVE&gt;:\...
///   - All other Linux paths are native to the distro and are reachable from
///     Windows at \\wsl.localhost\&lt;distro&gt;\...  Ordinary File I/O can read
///     AND write that UNC form, which is why the config writers and skills
///     installer need no WSL awareness of their own.
///
/// Never throws on translation: unresolvable input yields null so callers can
/// degrade rather than fail a whole wiring operation.
/// </summary>
public sealed class WslBridge
{
    private const string UncPrefix = @"\\wsl.localhost\";
    private readonly IProcessRunner _runner;
    private readonly string? _forcedDistro;
    private readonly string? _forcedHome;

    public WslBridge(IProcessRunner runner) => _runner = runner;

    private WslBridge(string distro, string linuxHome, IProcessRunner runner)
    {
        _runner = runner;
        _forcedDistro = distro;
        _forcedHome = linuxHome;
    }

    /// <summary>Test seam: fixes distro and home so translation is pure.</summary>
    public static WslBridge ForTesting(string distro, string linuxHome, IProcessRunner runner) =>
        new(distro, linuxHome, runner);

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private string? _cachedDistro;
    private bool _distroResolved;
    private string? _cachedHome;
    private bool _homeResolved;

    public bool IsAvailable => _runner.IsOnPath("wsl.exe") && DefaultDistro is not null;

    public string? DefaultDistro
    {
        get
        {
            if (_forcedDistro is not null) return _forcedDistro;
            if (_distroResolved) return _cachedDistro;

            var (timedOut, distro) = ResolveDistro();
            if (timedOut) return null; // Unknown: never latched, re-probe next access.

            _distroResolved = true;
            return _cachedDistro = distro;
        }
    }

    public string? LinuxHome
    {
        get
        {
            if (_forcedHome is not null) return _forcedHome;
            if (_homeResolved) return _cachedHome;

            var distro = DefaultDistro;
            if (distro is null)
            {
                // DefaultDistro itself only returns null without latching when
                // its probe timed out (Unknown). Mirror that here: only cache
                // "no home" when DefaultDistro reached a definitive negative.
                if (_distroResolved) { _homeResolved = true; _cachedHome = null; }
                return null;
            }

            var r = _runner.Run("wsl.exe",
                ["-d", distro, "bash", "-lc", "echo $HOME"], ProbeTimeout);
            if (r.TimedOut) return null; // Unknown: never latched, re-probe next access.

            _homeResolved = true;
            if (r.ExitCode != 0) return _cachedHome = null;
            var home = DecodeMaybeUtf16(r.Stdout).Trim();
            return _cachedHome = string.IsNullOrEmpty(home) ? null : home;
        }
    }

    public string? UncRoot => DefaultDistro is null ? null : UncPrefix + DefaultDistro;

    /// <summary>
    /// Three-state distro resolution: (TimedOut, Distro). TimedOut=true means
    /// Unknown - the caller must not cache a negative. TimedOut=false with a
    /// null Distro is a definitive negative (wsl.exe answered; no acceptable
    /// distro was in the list) and IS safe to cache.
    /// </summary>
    private (bool TimedOut, string? Distro) ResolveDistro()
    {
        if (!_runner.IsOnPath("wsl.exe")) return (false, null);
        var r = _runner.Run("wsl.exe", ["-l", "-q"], ProbeTimeout);
        if (r.TimedOut) return (true, null);
        if (r.ExitCode != 0) return (false, null);

        foreach (var raw in DecodeMaybeUtf16(r.Stdout)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim().Trim('﻿', '\0', '\r');
            if (name.Length == 0) continue;
            // docker-desktop is a utility distro with no user home.
            if (name.Equals("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("docker-desktop-", StringComparison.OrdinalIgnoreCase)) continue;
            return (false, name);
        }
        return (false, null);
    }

    /// <summary>
    /// wsl.exe emits UTF-16LE. IProcessRunner decodes stdout byte-for-byte as
    /// Latin1, which turns each ASCII character into "&lt;char&gt;\0". Detect
    /// that shape and re-decode rather than changing the shared runner's
    /// contract.
    ///
    /// The re-encode MUST use the same single-byte encoding the runner decoded
    /// with (SystemProcessRunner.DecodeRawBytes). It was UTF8.GetBytes here
    /// while the runner used UTF8.GetString - a pairing that only round-trips
    /// for bytes below 0x80, so any non-ASCII byte (a non-English distro name,
    /// a non-ASCII $HOME) was already destroyed at decode time and then
    /// re-expanded to two bytes here, desynchronizing the UTF-16LE pairing for
    /// everything after it. Latin1 on both sides is byte-lossless.
    /// </summary>
    private static string DecodeMaybeUtf16(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('\0')) return s;
        var bytes = Encoding.Latin1.GetBytes(s);
        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    public bool ProbeCommand(string command) => ProbeCommandStatus(command).Found;

    /// <summary>
    /// True when the last probe of this command timed out rather than
    /// returning a definitive answer. Callers must map this to
    /// AgentConnectionStatus.Unknown and must not cache it.
    ///
    /// This is a convenience wrapper around <see cref="ProbeCommandStatus"/>
    /// for callers that only need the timeout fact. A caller that needs BOTH
    /// Found and TimedOut (AgentDetector.DetectOutbound does) should call
    /// ProbeCommandStatus directly instead of chaining ProbeCommand +
    /// ProbeTimedOut - each call here is its own wsl.exe invocation, so
    /// chaining the two doubles the cost of every WSL detection for no
    /// extra information.
    /// </summary>
    public bool ProbeTimedOut(string command) => ProbeCommandStatus(command).TimedOut;

    /// <summary>
    /// Probes whether <paramref name="command"/> resolves inside the default
    /// distro's login shell, in a single wsl.exe invocation that reports both
    /// facts a caller might need: whether the command was Found, and whether
    /// the probe TimedOut instead of answering. TimedOut=true means Unknown
    /// (see AgentConnectionStatus.Unknown) - Found is meaningless in that
    /// case and must not be trusted or cached.
    /// </summary>
    public (bool Found, bool TimedOut) ProbeCommandStatus(string command)
    {
        var distro = DefaultDistro;
        if (distro is null || string.IsNullOrWhiteSpace(command)) return (false, false);
        var r = _runner.Run("wsl.exe",
            ["-d", distro, "bash", "-lc", $"command -v {command}"], ProbeTimeout);
        if (r.TimedOut) return (false, true);
        return (r.ExitCode == 0 && !string.IsNullOrWhiteSpace(DecodeMaybeUtf16(r.Stdout)), false);
    }

    public ProcessResult Run(string command, string[] args, TimeSpan timeout)
        => Run(command, args, timeout, env: null);

    /// <summary>
    /// Runs a command through the distro's login shell. <paramref name="env"/>
    /// entries are prefixed as shell assignments (NAME='value' command ...),
    /// so they reach the command and every process it spawns - the channel
    /// the inference path uses to tell LogMark's own agent-capture hook to
    /// stand down (<see cref="AgentCaptureOptOut"/>).
    /// </summary>
    public ProcessResult Run(string command, string[] args, TimeSpan timeout,
        IReadOnlyDictionary<string, string>? env)
    {
        var distro = DefaultDistro;
        if (distro is null)
            return new ProcessResult(1, "", "no WSL distro available", false);
        var inner = args.Length == 0 ? command : $"{command} {string.Join(' ', args)}";
        if (env is { Count: > 0 })
            inner = string.Join(' ', env.Select(kv => $"{kv.Key}='{kv.Value.Replace("'", "'\\''")}'")) + " " + inner;
        var r = _runner.Run("wsl.exe", ["-d", distro, "bash", "-lc", inner], timeout);
        return r with { Stdout = DecodeMaybeUtf16(r.Stdout) };
    }

    public string? ToWindowsPath(string linuxPath)
    {
        if (string.IsNullOrWhiteSpace(linuxPath)) return null;
        var p = linuxPath.Replace('\\', '/').TrimEnd('/');
        if (p.Length == 0) p = "/";

        if (p.StartsWith("/mnt/", StringComparison.Ordinal) && p.Length >= 6)
        {
            var drive = char.ToUpperInvariant(p[5]);
            if (char.IsLetter(drive) && (p.Length == 6 || p[6] == '/'))
            {
                var rest = p.Length > 7 ? p[7..].Replace('/', '\\') : "";
                return rest.Length == 0 ? $"{drive}:\\" : $"{drive}:\\{rest}";
            }
        }

        var root = UncRoot;
        if (root is null) return null;
        return root + p.Replace('/', '\\');
    }

    public string? ToLinuxPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath)) return null;

        if (windowsPath.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = windowsPath[UncPrefix.Length..];
            var slash = afterPrefix.IndexOf('\\');
            if (slash < 0) return "/";
            return "/" + afterPrefix[(slash + 1)..].Replace('\\', '/').TrimEnd('/');
        }

        if (windowsPath.Length >= 2 && windowsPath[1] == ':' && char.IsLetter(windowsPath[0]))
        {
            var drive = char.ToLowerInvariant(windowsPath[0]);
            var rest = windowsPath.Length > 2 ? windowsPath[2..].Replace('\\', '/') : "";
            var trimmed = rest.TrimEnd('/');
            return trimmed.Length == 0 ? $"/mnt/{drive}" : $"/mnt/{drive}{trimmed}";
        }

        return null;
    }
}

/// <summary>
/// Documents, and pins by test, that arguments to a WSL-launched Windows
/// binary are NOT path-translated. WSL interop hands argv to the Windows
/// process unmodified, so the process interprets them as Windows paths. Only
/// the executable itself is translated, because WSL is what resolves it.
/// </summary>
public static class WslArgPolicy
{
    public static string[] Translate(string[] args) => args;
}
