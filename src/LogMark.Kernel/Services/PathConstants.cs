using System.IO;
using System.Text.RegularExpressions;

namespace LogMark.Services;

/// <summary>
/// Parsed vault path components.
/// </summary>
/// <param name="Route">Route path (null for unrouted, e.g., "projects/shotqueue" for routed)</param>
/// <param name="TypeName">Entry type folder name without underscore (e.g., "tasks", "ideas")</param>
/// <param name="Year">Year from filename</param>
public record ParsedPath(string? Route, string TypeName, int Year);

/// <summary>
/// Centralized path construction for vault structure.
/// Single source of truth for LogMark namespace organization.
///
/// Structure:
/// - logmark/_inbox/_{entryType}/{year}.md (unrouted/inbox)
/// - logmark/{route}/_{entryType}/{year}.md (routed)
/// - logmark/_system/ (config, logs, routes, assets, db)
/// </summary>
public static partial class PathConstants
{
    // Root namespace
    private const string LogMarkNamespace = "logmark";
    private const string SystemFolder = "_system";
    private const string InboxFolder = "_inbox";
    private const string TrashFolder = "_trash";

    /// <summary>
    /// Top-level folder names directly under logmark/ that are LogMark-managed
    /// but don't follow the underscore convention. Consumers (e.g. OrphanedEntryLint)
    /// must treat these as system namespaces, not user content awaiting a route.
    /// </summary>
    public static readonly IReadOnlySet<string> ManagedNonUnderscoreFolders =
        new HashSet<string>(StringComparer.Ordinal) { "feed", "thinkers" };

    // Regex to extract year from filename (e.g., "2026.md" -> 2026)
    [GeneratedRegex(@"^(\d{4})\.md$", RegexOptions.Compiled)]
    private static partial Regex YearFilePattern();

    /// <summary>
    /// Get unrouted (inbox) file path: logmark/_inbox/_{typeName}/{year}.md
    /// </summary>
    public static string GetUnroutedFilePath(string typeName, int year)
    {
        return Path.Combine(LogMarkNamespace, InboxFolder, $"_{typeName}", $"{year}.md");
    }

    /// <summary>
    /// Get routed file path: logmark/{routePath}/_{typeName}/{year}.md
    /// </summary>
    public static string GetRoutedFilePath(string routePath, string typeName, int year)
    {
        // Normalize route path separators by splitting and recombining
        var routeParts = routePath.Split('/', '\\');
        var parts = new[] { LogMarkNamespace }.Concat(routeParts).Concat(new[] { $"_{typeName}", $"{year}.md" });
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// Get unrouted (inbox) type folder: logmark/_inbox/_{typeName}
    /// </summary>
    public static string GetUnroutedTypeFolder(string typeName)
    {
        return Path.Combine(LogMarkNamespace, InboxFolder, $"_{typeName}");
    }

    /// <summary>
    /// Get routed type folder: logmark/{routePath}/_{typeName}
    /// </summary>
    public static string GetRoutedTypeFolder(string routePath, string typeName)
    {
        // Normalize route path separators by splitting and recombining
        var routeParts = routePath.Split('/', '\\');
        var parts = new[] { LogMarkNamespace }.Concat(routeParts).Concat(new[] { $"_{typeName}" });
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// Get unrouted (inbox) pages folder: logmark/_inbox/_{typeName}/pages
    /// </summary>
    public static string GetUnroutedPagesFolder(string typeName)
    {
        return Path.Combine(LogMarkNamespace, InboxFolder, $"_{typeName}", "pages");
    }

    /// <summary>
    /// Get the trash file for a given year: logmark/_system/_trash/{year}.md.
    /// Lives under _system so trashed entries are excluded from EntryCache
    /// warmup and the sidebar vault tree.
    /// </summary>
    public static string GetTrashFilePath(int year)
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, TrashFolder, $"{year}.md");
    }

    /// <summary>
    /// Get the trash folder: logmark/_system/_trash.
    /// </summary>
    public static string GetTrashFolder()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, TrashFolder);
    }

    /// <summary>
    /// Get the trash subpages folder: logmark/_system/_trash/_pages.
    /// Trashed paste-flow subpages move here.
    /// </summary>
    public static string GetTrashPagesFolder()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, TrashFolder, "_pages");
    }

    /// <summary>
    /// Get routed pages folder: logmark/{routePath}/_{typeName}/pages
    /// </summary>
    public static string GetRoutedPagesFolder(string routePath, string typeName)
    {
        // Normalize route path separators by splitting and recombining
        var routeParts = routePath.Split('/', '\\');
        var parts = new[] { LogMarkNamespace }.Concat(routeParts).Concat(new[] { $"_{typeName}", "pages" });
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// Get config file path: logmark/_system/config.md
    /// </summary>
    public static string GetConfigPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "config.md");
    }

    /// <summary>
    /// Get log file path: logmark/_system/log.md
    /// </summary>
    public static string GetLogPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "log.md");
    }

    /// <summary>
    /// Get routes file path: logmark/_system/routes.md
    /// </summary>
    public static string GetRoutesPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "routes.md");
    }

    /// <summary>
    /// Get system folder path: logmark/_system
    /// </summary>
    public static string GetSystemFolder()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder);
    }

    /// <summary>
    /// Get assets directory: logmark/_system/assets
    /// </summary>
    public static string GetAssetsDir()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "assets");
    }

    /// <summary>
    /// Get asset file path: logmark/_system/assets/{filename}
    /// </summary>
    public static string GetAssetPath(string filename)
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "assets", filename);
    }

    /// <summary>
    /// Get events database path: logmark/_system/events.db
    /// </summary>
    public static string GetEventsDbPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "events.db");
    }

    /// <summary>
    /// Get palettes directory: logmark/_system/palettes
    /// </summary>
    public static string GetPalettesDir()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "palettes");
    }

    /// <summary>
    /// Get tags file path: logmark/_system/tags.md
    /// </summary>
    public static string GetTagsPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "tags.md");
    }

    /// <summary>
    /// Get lint state file path: logmark/_system/lint-state.json
    /// </summary>
    public static string GetLintStatePath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "lint-state.json");
    }

    /// <summary>
    /// Get projects file path: logmark/_system/projects.md
    /// </summary>
    public static string GetProjectsPath()
    {
        return Path.Combine(LogMarkNamespace, SystemFolder, "projects.md");
    }

    /// <summary>
    /// DEPRECATED: Daily log path (Phase 1.8 daily structure)
    /// Will be removed when daily logs are migrated to new structure
    /// </summary>
    public static string GetDailyLogPath(DateTime date)
    {
        return Path.Combine(LogMarkNamespace, $"{date:yyyy}", $"{date:MM}", $"{date:yyyy-MM-dd}.md");
    }

    /// <summary>
    /// Parse a vault-relative path into semantic components.
    /// Inverse of GetRoutedFilePath/GetUnroutedFilePath.
    /// </summary>
    /// <param name="relativePath">Vault-relative path (e.g., "logmark/projects/shotqueue/_tasks/2026.md")</param>
    /// <returns>Parsed components, or null if path doesn't match LogMark entry structure</returns>
    public static ParsedPath? ParsePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Normalize path separators to forward slashes
        var normalized = relativePath.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Minimum valid path: logmark/_inbox/_type/year.md (4 parts for inbox) or logmark/route/_type/year.md
        if (parts.Length < 3)
            return null;

        // Must start with logmark namespace
        if (!parts[0].Equals(LogMarkNamespace, StringComparison.OrdinalIgnoreCase))
            return null;

        // Find the type folder (starts with _ but not _system or _inbox)
        var typeIndex = -1;
        for (int i = 1; i < parts.Length - 1; i++) // Skip first (logmark) and last (filename)
        {
            if (parts[i].StartsWith('_') &&
                !parts[i].Equals(SystemFolder, StringComparison.OrdinalIgnoreCase) &&
                !parts[i].Equals(InboxFolder, StringComparison.OrdinalIgnoreCase))
            {
                typeIndex = i;
                break;
            }
        }

        if (typeIndex < 0)
            return null;

        // Type folder at index 1 means legacy root structure (logmark/_tasks) - not supported
        // Valid structures: logmark/_inbox/_tasks or logmark/route/_tasks
        if (typeIndex == 1)
            return null;

        // Extract type name (remove underscore prefix)
        var typeName = parts[typeIndex][1..];
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Extract year from filename
        var filename = parts[^1];
        var yearMatch = YearFilePattern().Match(filename);
        if (!yearMatch.Success)
            return null;

        var year = int.Parse(yearMatch.Groups[1].Value);

        // Extract route (parts between logmark and type folder, excluding _inbox)
        string? route = null;
        // Check if this is inbox (unrouted) or a real route
        var potentialRoute = parts[1..typeIndex];
        if (potentialRoute.Length == 1 && potentialRoute[0].Equals(InboxFolder, StringComparison.OrdinalIgnoreCase))
        {
            // This is inbox, so no route (unrouted entry)
            route = null;
        }
        else
        {
            // Real route: join parts between logmark (index 0) and type folder
            route = string.Join("/", potentialRoute);
        }

        return new ParsedPath(route, typeName, year);
    }
}
