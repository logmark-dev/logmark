using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.TestUtilities;

/// <summary>
/// Helper for creating isolated test vaults with proper structure.
/// Creates real directories in temp path, auto-cleans on dispose.
/// </summary>
public class TestVaultBuilder : IDisposable
{
    private readonly string _vaultPath;
    private readonly List<string> _createdFiles = new();
    private bool _disposed;

    public string VaultPath => _vaultPath;
    public string LogMarkPath => Path.Combine(_vaultPath, "logmark");

    public TestVaultBuilder(string? nameHint = null)
    {
        var suffix = nameHint ?? Guid.NewGuid().ToString()[..8];
        _vaultPath = Path.Combine(Path.GetTempPath(), $"logmark-test-{suffix}");
        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(LogMarkPath);
    }

    /// <summary>
    /// Create the standard logmark folder structure.
    /// </summary>
    public TestVaultBuilder WithStandardStructure()
    {
        EnsureDirectory("_tasks");
        EnsureDirectory("_ideas");
        EnsureDirectory("_blocks");
        EnsureDirectory("_decisions");
        EnsureDirectory("_diary");
        EnsureDirectory("_system");
        return this;
    }

    /// <summary>
    /// Create a route folder with optional type subfolders.
    /// </summary>
    public TestVaultBuilder WithRoute(string route, bool withTypeFolders = false)
    {
        var routePath = Path.Combine(LogMarkPath, route);
        Directory.CreateDirectory(routePath);

        if (withTypeFolders)
        {
            EnsureDirectory(Path.Combine(route, "_tasks"));
            EnsureDirectory(Path.Combine(route, "_ideas"));
            EnsureDirectory(Path.Combine(route, "_diary"));
        }

        return this;
    }

    /// <summary>
    /// Writes a routes.md containing a flat bullet list under a single
    /// "# Routes" heading. Each argument becomes "- {arg}". Unsuitable
    /// for populating the parser's heading-based route format (## section
    /// + ### routeName + - path:/- private:). For that, use
    /// WithFile("_system/routes.md", rawMarkdown).
    /// </summary>
    public TestVaultBuilder WithRoutesFile(params string[] routeDefinitions)
    {
        var content = "# Routes\n\n" + string.Join("\n", routeDefinitions.Select(r => $"- {r}"));
        var path = Path.Combine(LogMarkPath, "_system", "routes.md");
        EnsureDirectory("_system");
        File.WriteAllText(path, content);
        _createdFiles.Add(path);
        return this;
    }

    /// <summary>
    /// Create a year file with entries.
    /// </summary>
    public TestVaultBuilder WithYearFile(string typeFolder, int year, string content)
    {
        var path = Path.Combine(LogMarkPath, typeFolder, $"{year}.md");
        EnsureDirectory(typeFolder);
        File.WriteAllText(path, content);
        _createdFiles.Add(path);
        return this;
    }

    /// <summary>
    /// Create an entry in the appropriate type folder.
    /// </summary>
    public TestVaultBuilder WithEntry(string typeFolder, int year, string entryContent, DateTime timestamp)
    {
        var path = Path.Combine(LogMarkPath, typeFolder, $"{year}.md");
        EnsureDirectory(typeFolder);

        var header = $"---\n\n## {timestamp:yyyy-MM-dd HH:mm}\n\n";
        var fullContent = header + entryContent + "\n";

        if (File.Exists(path))
        {
            File.AppendAllText(path, "\n" + fullContent);
        }
        else
        {
            File.WriteAllText(path, fullContent);
            _createdFiles.Add(path);
        }

        return this;
    }

    /// <summary>
    /// Write a single entry of the given type into a route's routed file
    /// (logmark/{route}/_{typeFolder}/{year}.md), stamped with a fresh ShortId
    /// so callers can round-trip through EntryCache/EntryOrganizer lookups.
    /// Task entries get a "_task: none | postponed: 0 | {shortId} ^{shortId}"
    /// metadata line (matches EntryParser's TaskMetadataPattern); every other
    /// type gets "_id: {shortId} ^{shortId}" (matches the non-task ShortId
    /// regex). Appends to the year file if it already has content.
    /// </summary>
    public TestVaultBuilder WithEntry(string route, EntryType type, string content, out string shortId)
    {
        shortId = Guid.NewGuid().ToString("N")[..8];
        var timestamp = DateTime.Now;
        var typeFolder = EntryTypeMapper.GetFolderName(type);
        var relativePath = PathConstants.GetRoutedFilePath(route, typeFolder, timestamp.Year);
        var absolutePath = Path.Combine(_vaultPath, relativePath);
        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var prefix = ShortPrefix(type);
        var contentLine = prefix == null ? content : $"{prefix}: {content}";
        var metaLine = type == EntryType.Task
            ? $"_task: none | postponed: 0 | {shortId} ^{shortId}"
            : $"_id: {shortId} ^{shortId}";

        var entryText = $"## {timestamp:yyyy-MM-dd HH:mm}\n\n{contentLine}\n{metaLine}\n";

        if (File.Exists(absolutePath))
        {
            File.AppendAllText(absolutePath, "\n---\n\n" + entryText);
        }
        else
        {
            File.WriteAllText(absolutePath, entryText);
            _createdFiles.Add(absolutePath);
        }

        return this;
    }

    private static string? ShortPrefix(EntryType type) => type switch
    {
        EntryType.Task => "t",
        EntryType.Idea => "i",
        EntryType.Block => "b",
        EntryType.Decision => "d",
        EntryType.Question => "q",
        EntryType.Gap => "g",
        EntryType.Analogy => "a",
        _ => null
    };

    /// <summary>
    /// Completion marker for fluent chains. TestVaultBuilder performs its file
    /// writes eagerly on each With* call, so this is a no-op that exists to give
    /// callers an explicit terminal step before deriving further test objects
    /// (e.g. BuildEntryCache()).
    /// </summary>
    public TestVaultBuilder Build() => this;

    /// <summary>
    /// Construct an EntryCache bound to this vault and synchronously warm it,
    /// so the first read after this call already reflects everything written
    /// via the With* methods above.
    /// </summary>
    public EntryCache BuildEntryCache()
    {
        var cache = new EntryCache(_vaultPath);
        cache.StartWarmup();
        cache.WarmupTask?.Wait();
        return cache;
    }

    /// <summary>
    /// Create a config file with settings.
    /// </summary>
    public TestVaultBuilder WithConfig(string content)
    {
        var configPath = Path.Combine(LogMarkPath, PathConstants.GetConfigPath());
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir))
            Directory.CreateDirectory(configDir);
        File.WriteAllText(configPath, content);
        _createdFiles.Add(configPath);
        return this;
    }

    /// <summary>
    /// Write arbitrary file content relative to logmark folder.
    /// </summary>
    public TestVaultBuilder WithFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(LogMarkPath, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        _createdFiles.Add(fullPath);
        return this;
    }

    /// <summary>
    /// Write arbitrary file content relative to the vault root (not the logmark subfolder).
    /// Use this for files like "logmark/research/pages/overview.md" whose paths start
    /// with "logmark/" and must be placed directly under the vault root.
    /// </summary>
    public TestVaultBuilder WithRawFile(string vaultRelativePath, string content)
    {
        var fullPath = Path.Combine(_vaultPath, vaultRelativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        _createdFiles.Add(fullPath);
        return this;
    }

    private void EnsureDirectory(string relativePath)
    {
        var fullPath = Path.Combine(LogMarkPath, relativePath);
        Directory.CreateDirectory(fullPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(_vaultPath))
                Directory.Delete(_vaultPath, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
