using System.IO;
using LogMark.Models;

namespace LogMark.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Logs system events to _logmark/log.md
/// Phase 1: Single file, info level
/// Future: Rotation, configurable level
/// </summary>
public class SystemLogger
{
    private readonly string _logFilePath;
    private readonly LogLevel _minLevel;

    public SystemLogger(string vaultPath, LogLevel minLevel = LogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new ArgumentNullException(nameof(vaultPath));

        _logFilePath = Path.Combine(vaultPath, PathConstants.GetLogPath());
        _minLevel = minLevel;

        EnsureLogFileExists();
    }

    private void EnsureLogFileExists()
    {
        var dir = Path.GetDirectoryName(_logFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_logFilePath))
        {
            File.WriteAllText(_logFilePath, "# LogMark System Log\n\n");
        }
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel)
            return;

        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var levelStr = level.ToString().ToUpperInvariant();

            var logEntry = $"### {timestamp} [{levelStr}]\n{message}\n\n";

            // Check if we need to add today's date header
            var content = File.ReadAllText(_logFilePath);
            if (!content.Contains($"## {date}"))
            {
                File.AppendAllText(_logFilePath, $"## {date}\n\n");
            }

            File.AppendAllText(_logFilePath, logEntry);
        }
        catch (Exception)
        {
            // Silently fail - logging should never break the app
        }
    }

    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warn(string message) => Log(LogLevel.Warn, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Debug(string message) => Log(LogLevel.Debug, message);

    /// <summary>
    /// Log entry capture with Obsidian link to destination file
    /// </summary>
    public void LogEntryWithLink(string action, string filePath, string preview)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        // Convert path to Obsidian link format (remove .md extension)
        var linkPath = filePath.Replace("\\", "/");
        if (linkPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            linkPath = linkPath[..^3];

        // Create display name from path (e.g., "tasks/2025/tasks" -> "tasks")
        var displayName = GetDisplayName(filePath);

        // Truncate preview to 50 chars
        if (preview.Length > 50)
            preview = preview[..47] + "...";

        var message = $"{timestamp} - {action} to [[{linkPath}|{displayName}]]: \"{preview}\"";

        Info(message);
    }

    private static string GetDisplayName(string filePath)
    {
        // Use PathConstants for parsing, format for display
        // - Inbox: "logmark/_inbox/_tasks/2026.md" -> "tasks"
        // - Routed: "logmark/projects/shotqueue/_tasks/2026.md" -> "shotqueue tasks"
        var parsed = PathConstants.ParsePath(filePath);
        if (parsed == null)
            return "entry";

        if (parsed.Route != null)
        {
            // Routed: get last part of route path (e.g., "shotqueue" from "projects/shotqueue")
            var routeParts = parsed.Route.Split('/');
            var routeName = routeParts[^1];
            return $"{routeName} {parsed.TypeName}";
        }

        // Unrouted: just the type name
        return parsed.TypeName;
    }

    /// <summary>
    /// Create preview of content for logging (truncated to max length with ellipsis)
    /// </summary>
    public static string GetPreview(string content, int maxLength = 50)
    {
        if (string.IsNullOrEmpty(content))
            return "(empty)";

        return content.Length > maxLength
            ? content[..(maxLength - 3)] + "..."
            : content;
    }

    /// <summary>
    /// Get type label for logging (lowercase, "log" for null/empty)
    /// </summary>
    public static string GetTypeLabel(EntryType? type)
    {
        return type?.ToString().ToLowerInvariant() ?? "log";
    }

    /// <summary>
    /// Log API requests (POST/PUT/DELETE only) with status codes
    /// </summary>
    public void LogApiRequest(string method, string endpoint, int statusCode, bool success, string? errorMessage = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var status = success ? "SUCCESS" : "FAILED";

        string message;
        if (success)
        {
            message = $"API {method} {endpoint} - {status} ({statusCode})";
        }
        else
        {
            message = $"API {method} {endpoint} - {status} ({statusCode}): {errorMessage ?? "Unknown error"}";
        }

        if (success)
            Debug(message);
        else
            Error(message);
    }
}
