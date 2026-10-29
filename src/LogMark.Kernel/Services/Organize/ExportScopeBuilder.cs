using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Organize;

/// <summary>
/// A single entry scoped for export, with resolved route and type metadata.
/// </summary>
public record ScopedEntry(ParsedEntry Entry, string FilePath, string? Route, EntryType Type);

/// <summary>
/// The result of resolving checked tree paths into a flat, deduplicated entry list.
/// </summary>
public class ExportScope
{
    public IReadOnlyList<ScopedEntry> Entries { get; init; } = Array.Empty<ScopedEntry>();
    public int EntryCount { get; init; }
    public int RouteCount { get; init; }
    public int TypeCount { get; init; }

    public static ExportScope Empty => new()
    {
        Entries = Array.Empty<ScopedEntry>(),
        EntryCount = 0,
        RouteCount = 0,
        TypeCount = 0
    };
}

/// <summary>
/// Resolves a set of checked tree paths (files and folders) into a flat,
/// deduplicated list of entries for export.
/// </summary>
public class ExportScopeBuilder
{
    private readonly string _vaultPath;

    public ExportScopeBuilder(string vaultPath)
    {
        _vaultPath = vaultPath;
    }

    /// <summary>
    /// Build an ExportScope from the given checked paths.
    /// Each path is either a .md file or a directory. Directories are expanded
    /// recursively, excluding _system/. Entries are deduplicated and sorted by
    /// timestamp descending.
    /// </summary>
    public ExportScope BuildScope(IReadOnlyList<string> checkedPaths)
    {
        if (checkedPaths == null || checkedPaths.Count == 0)
            return ExportScope.Empty;

        // Collect all .md files from checked paths
        var mdFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in checkedPaths)
        {
            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(_vaultPath, path);

            if (File.Exists(fullPath) && fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                mdFiles.Add(Path.GetFullPath(fullPath));
            }
            else if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*.md", SearchOption.AllDirectories))
                {
                    // Exclude _system/ directories
                    var normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/_system/", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Contains("\\_system\\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    mdFiles.Add(Path.GetFullPath(file));
                }
            }
        }

        // Parse entries from each file, deduplicating by "filepath|timestamp"
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scopedEntries = new List<ScopedEntry>();

        foreach (var filePath in mdFiles)
        {
            var markdown = File.ReadAllText(filePath);

            // Infer entry type hint from path
            var typeHint = EntryTypeMapper.InferTypeFromPath(filePath);
            var entries = EntryParser.EntryParser.Parse(markdown, typeHint);

            // Get vault-relative path for ParsePath
            var relativePath = GetRelativePath(filePath);
            var parsedPath = PathConstants.ParsePath(relativePath);
            var route = parsedPath?.Route;

            foreach (var entry in entries)
            {
                var dedupeKey = $"{filePath}|{entry.Timestamp:O}";
                if (!seen.Add(dedupeKey))
                    continue;

                var entryType = entry.Type;

                scopedEntries.Add(new ScopedEntry(entry, filePath, route, entryType));
            }
        }

        // Sort by timestamp descending
        scopedEntries.Sort((a, b) => b.Entry.Timestamp.CompareTo(a.Entry.Timestamp));

        var distinctRoutes = scopedEntries
            .Select(e => e.Route)
            .Where(r => r != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var distinctTypes = scopedEntries
            .Select(e => e.Type)
            .Distinct()
            .Count();

        return new ExportScope
        {
            Entries = scopedEntries,
            EntryCount = scopedEntries.Count,
            RouteCount = distinctRoutes,
            TypeCount = distinctTypes
        };
    }

    private string GetRelativePath(string fullPath)
    {
        var vaultFull = Path.GetFullPath(_vaultPath);
        var fileFull = Path.GetFullPath(fullPath);

        if (fileFull.StartsWith(vaultFull, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fileFull.Substring(vaultFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative;
        }

        return fileFull;
    }
}
