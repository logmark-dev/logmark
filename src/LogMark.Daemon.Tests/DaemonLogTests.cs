using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

public class DaemonLogTests
{
    [Fact]
    public void WritesTimestampedLinesUnderVaultDotLogmark()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-daemonlog").FullName;
        try
        {
            DaemonLog.Init(vault);
            DaemonLog.Write("test", "hello world");
            DaemonLog.WriteException("test", new InvalidOperationException("boom"));

            var path = Path.Combine(vault, ".logmark", "daemon.log");
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("hello world", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("boom", text);

            var lines = File.ReadAllLines(path);
            var stamped = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[[^\]]+\] ";
            Assert.Matches(stamped, lines[0]);
            Assert.Contains(lines, line =>
                System.Text.RegularExpressions.Regex.IsMatch(line, stamped) && line.Contains("hello world"));
        }
        finally
        {
            Directory.Delete(vault, recursive: true);
        }
    }
}
