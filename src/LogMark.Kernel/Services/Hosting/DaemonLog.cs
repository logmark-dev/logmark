namespace LogMark.Services.Hosting;

/// <summary>
/// Append-only daemon trail at `<vault>/.logmark/daemon.log` (release
/// review P0-5: the daemon once died with exit 1 and zero diagnostics).
/// Logging must never take the daemon down: every IO failure here is
/// swallowed. Also mirrored to stderr so foreground runs stay readable.
/// </summary>
public static class DaemonLog
{
    private static string? _path;
    private static readonly object Gate = new();

    public static void Init(string vaultPath)
    {
        try
        {
            var dir = Path.Combine(vaultPath, ".logmark");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "daemon.log");
            lock (Gate) { _path = path; }
        }
        catch
        {
            lock (Gate) { _path = null; }
        }
    }

    public static void Write(string category, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
        try { Console.Error.WriteLine(line); } catch { }
        lock (Gate)
        {
            if (_path is null) return;
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch { }
        }
    }

    public static void WriteException(string category, Exception ex)
    {
        Write(category, ex.ToString());
    }
}
