using System.IO;
using System.Reflection;
using System.Text;
using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Services;

/// <summary>
/// Engine for creating entries with explicit dates, used for seeding vaults
/// with historical data during initialization.
/// </summary>
public class BackfillEngine
{
    private readonly string _vaultPath;
    private readonly RouteParser _routeParser;
    private readonly SeedParser _seedParser;
    private readonly EntryTypeMapper _mapper;
    private readonly IVaultFileWriter? _vaultWriter;
    private RouteConfig? _config;

    public BackfillEngine(string vaultPath, IVaultFileWriter? vaultWriter = null)
    {
        _vaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
        _routeParser = new RouteParser(vaultPath);
        _seedParser = new SeedParser();
        _mapper = new EntryTypeMapper();
        _vaultWriter = vaultWriter;
    }

    /// <summary>
    /// Add a single entry with explicit date.
    /// </summary>
    public void AddEntry(SeedEntry entry)
    {
        if (_config == null)
            _config = _routeParser.Parse();

        var destPath = GetDestinationPath(entry);
        var fullPath = Path.Combine(_vaultPath, destPath);

        // Create directory if needed
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Format and append entry
        var entryText = FormatEntry(entry);

        // Check if file has content - if so, add blank line before next entry
        var existingContent = File.Exists(fullPath) ? File.ReadAllText(fullPath).Trim('\uFEFF') : "";
        var hasContent = !string.IsNullOrWhiteSpace(existingContent);

        var sb = new StringBuilder();
        if (hasContent)
            sb.AppendLine();
        sb.Append(entryText);

        if (_vaultWriter != null)
            _vaultWriter.AppendText(fullPath, sb.ToString());
        else
            File.AppendAllText(fullPath, sb.ToString());
    }

    /// <summary>
    /// Add a page (wiki subpage) to the vault at the correct routed or unrouted location.
    /// Uses PathConstants for path resolution.
    /// </summary>
    /// <param name="route">Route path (null for unrouted/_inbox)</param>
    /// <param name="typeName">Entry type name (e.g., "diary", "ideas")</param>
    /// <param name="filename">Page filename (e.g., "2026-01-20-thread-14042.md")</param>
    /// <param name="content">Page content</param>
    public void AddPage(string? route, string typeName, string filename, string content)
    {
        var canonical = ResolveOrThrow(typeName);
        var folder = string.IsNullOrEmpty(route)
            ? PathConstants.GetUnroutedPagesFolder(canonical)
            : PathConstants.GetRoutedPagesFolder(route, canonical);

        var fullPath = Path.Combine(_vaultPath, folder, filename);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (_vaultWriter != null)
            _vaultWriter.WriteFile(fullPath, content);
        else
            File.WriteAllText(fullPath, content);
    }

    /// <summary>
    /// Get the vault-relative wiki path for a page (for use in wiki links).
    /// </summary>
    public static string GetPageWikiPath(string? route, string typeName, string fileNameWithoutExtension)
    {
        var canonical = ResolveOrThrow(typeName);
        var folder = string.IsNullOrEmpty(route)
            ? PathConstants.GetUnroutedPagesFolder(canonical)
            : PathConstants.GetRoutedPagesFolder(route, canonical);

        // Wiki paths use forward slashes
        return $"{folder.Replace('\\', '/')}/{fileNameWithoutExtension}";
    }

    /// <summary>
    /// Add all entries from seed markdown content.
    /// </summary>
    public void AddEntriesFromSeed(string seedMarkdown)
    {
        var entries = _seedParser.Parse(seedMarkdown);
        foreach (var entry in entries)
        {
            AddEntry(entry);
        }
    }

    /// <summary>
    /// Add entries from an embedded resource.
    /// </summary>
    /// <param name="resourceName">Full resource name (e.g., "LogMark.Resources.Templates.demo.seed.md")</param>
    public void AddEntriesFromResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new ArgumentException($"Resource not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        AddEntriesFromSeed(content);
    }

    private string GetDestinationPath(SeedEntry entry)
    {
        var mapping = _mapper.GetMapping(entry.Type);
        if (mapping == null)
            throw new InvalidOperationException($"No mapping for entry type: {entry.Type}");

        var typeName = mapping.TypeName;
        var year = entry.Date.Year;

        if (string.IsNullOrEmpty(entry.Route))
        {
            // Unrouted - goes to _inbox
            return PathConstants.GetUnroutedFilePath(typeName, year);
        }

        // Find route path from routes.md
        var route = _routeParser.FindRoute(_config!, entry.Route);
        if (route == null || string.IsNullOrEmpty(route.Path))
        {
            // Route not found or reference-only - go to _inbox
            return PathConstants.GetUnroutedFilePath(typeName, year);
        }

        // Routed entry
        return PathConstants.GetRoutedFilePath(route.Path, typeName, year);
    }

    private static string FormatEntry(SeedEntry entry)
    {
        var sb = new StringBuilder();

        // Header with date and optional context
        var header = $"## {entry.Date:yyyy-MM-dd HH:mm}";
        if (!string.IsNullOrEmpty(entry.Context))
            header += $" | {entry.Context}";
        sb.AppendLine(header);

        // Content (already includes tags)
        sb.AppendLine(entry.Content);

        // Metadata: ID line matching VaultService.AppendEntry format
        var shortId = TaskTracker.GenerateShortId();
        if (entry.Type == EntryType.Task)
        {
            sb.AppendLine($"_task: none | postponed: 0 | {shortId} ^{shortId}");
        }
        else
        {
            sb.AppendLine($"_id: {shortId} ^{shortId}");
        }

        // Source attribution
        if (!string.IsNullOrEmpty(entry.Source))
        {
            sb.AppendLine($"source: {entry.Source}");
        }

        return sb.ToString();
    }

    private static string ResolveOrThrow(string typeName)
    {
        var resolved = EntryTypeMapper.ResolveTypeName(typeName);
        if (resolved is null)
            throw new ArgumentException(
                $"Unknown entry type '{typeName}'. Valid types: tasks, ideas, blocks, decisions, diary, questions, gaps, analogies, workflow, timetrack. (Use 'diary' for log entries.)",
                nameof(typeName));
        return resolved;
    }
}
