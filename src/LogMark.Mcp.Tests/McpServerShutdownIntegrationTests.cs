using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using LogMark.Models;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

// The defect this proves fixed lives in process shutdown: the MCP server used to
// exit without closing its session record. That is not reachable from a unit test
// against SessionService in isolation - it requires the real stdio server process
// to actually receive stdin EOF the way a host quitting sends it. This test spawns
// the built logmark-mcp binary, drives it over its real newline-delimited-JSON
// stdio protocol, closes stdin, and asserts the session record on disk says
// Completed. If the shutdown fix regresses, this is the test that catches it.
[Trait("Category", "Integration")]
public class McpServerShutdownIntegrationTests : IDisposable
{
    private readonly string _vaultPath;

    public McpServerShutdownIntegrationTests()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-shutdown-{Guid.NewGuid()}");
        Directory.CreateDirectory(_vaultPath);

        // Seed the system folder the way SessionToolTests does. A bare temp dir is
        // not a usable vault, and create_entry failing would surface here as "no
        // session was created", which looks like the shutdown bug rather than setup.
        Directory.CreateDirectory(Path.Combine(_vaultPath, PathConstants.GetSystemFolder()));
    }

    public void Dispose()
    {
        // Best-effort: if a prior assertion failure left the server process alive
        // (see the try/finally in ClosingStdin_ClosesTheSessionRecord), the
        // directory can still be locked here despite that cleanup attempt. Losing
        // the real assertion failure underneath an IOException from teardown would
        // be strictly worse than leaving a temp directory behind.
        try
        {
            if (Directory.Exists(_vaultPath))
                Directory.Delete(_vaultPath, true);
        }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ClosingStdin_ClosesTheSessionRecord()
    {
        var exe = ResolveServerPath();
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--vault-override");
        psi.ArgumentList.Add(_vaultPath);
        psi.ArgumentList.Add("--agent");
        psi.ArgumentList.Add("shutdown-test");
        psi.ArgumentList.Add("--route");
        psi.ArgumentList.Add("logmark");

        var proc = Process.Start(psi)!;
        try
        {
            await SendAsync(proc, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "shutdown-test", version = "1.0" }
                }
            });
            await proc.StandardOutput.ReadLineAsync();

            // The server accepted tool calls without an explicit notifications/initialized
            // message during manual protocol discovery, so it is not sent here. Kept as a
            // documented fallback: it is a notification and gets no response line, so do
            // not try to read one if it is ever needed.
            //   await SendAsync(proc, new { jsonrpc = "2.0", method = "notifications/initialized" });

            await SendAsync(proc, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "create_entry",
                    arguments = new { content = "t: shutdown probe +logmark" }
                }
            });
            await proc.StandardOutput.ReadLineAsync();

            // Close stdin: this is what /exit does to a stdio MCP server.
            proc.StandardInput.Close();
            var exited = proc.WaitForExit(20_000);
            exited.Should().BeTrue("the server must exit when stdin closes");

            var sessions = new SessionService(_vaultPath, new SystemLogger(_vaultPath));
            var all = sessions.GetSessions(new SessionQuery { Limit = 50 });
            all.Should().NotBeEmpty("the create_entry call must have materialized a session");
            all.Should().OnlyContain(s => s.Status == SessionStatus.Completed);
        }
        finally
        {
            // If the assertion above threw because the server hung instead of
            // exiting - the exact regression this test exists to catch - the
            // process would otherwise leak past this test and hold handles
            // inside _vaultPath, turning Dispose()'s directory delete into an
            // IOException that masks the real failure. Kill is tolerant of the
            // process having already exited normally, which is the expected path.
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            proc.Dispose();
        }
    }

    private static async Task SendAsync(Process proc, object message)
    {
        // MCP stdio is newline-delimited JSON. No Content-Length framing.
        await proc.StandardInput.WriteAsync(JsonSerializer.Serialize(message) + "\n");
        await proc.StandardInput.FlushAsync();
    }

    private static string ResolveServerPath()
    {
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var root = AppContext.BaseDirectory;
        // Walk up out of the test's own bin folder to src/, then across to the server.
        var srcDir = new DirectoryInfo(root);
        while (srcDir != null && srcDir.Name != "src")
            srcDir = srcDir.Parent;
        srcDir.Should().NotBeNull("the test must run from inside the src tree");

        // AssemblyName in LogMark.Mcp.csproj is conditioned on the OssBuild property:
        // "logmark-mcp" only when OssBuild=true; a plain `dotnet build` (OssBuild
        // unset) produces LogMark.Mcp.exe. Try the OSS name first, then the default.
        // The apphost has no extension off Windows (public CI's ubuntu runner).
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        var candidates = new[] { "logmark-mcp" + ext, "LogMark.Mcp" + ext };
        foreach (var name in candidates)
        {
            var candidate = Path.Combine(
                srcDir!.FullName, "LogMark.Mcp", "bin", configuration, "net8.0", name);
            if (File.Exists(candidate))
                return candidate;
        }

        var expected = Path.Combine(
            srcDir!.FullName, "LogMark.Mcp", "bin", configuration, "net8.0", candidates[0]);
        File.Exists(expected).Should().BeTrue($"build LogMark.Mcp first; expected one of: {string.Join(", ", candidates)}");
        return expected;
    }
}
