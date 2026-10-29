using System.Collections.Concurrent;
using System.IO;
using LogMark.Services.EntryParser;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Reverse index mapping target paths to entries that reference them.
/// Thread-safe for use during cache warmup and invalidation.
/// </summary>
public class BacklinkIndex
{
    // Maps normalized target path -> list of entries that reference it (wiki links)
    private readonly ConcurrentDictionary<string, List<BacklinkEntry>> _index = new();

    // Maps target ShortId -> list of entries that reference it (refs: metadata)
    private readonly ConcurrentDictionary<string, List<BacklinkEntry>> _shortIdBacklinks = new();

    // Maps source file path -> list of targets it references (for efficient removal)
    private readonly ConcurrentDictionary<string, HashSet<string>> _sourceToTargets = new();

    // Maps source file path -> list of ShortId targets it references (for efficient removal)
    private readonly ConcurrentDictionary<string, HashSet<string>> _sourceToShortIds = new();

    private readonly object _lock = new();
    private readonly string _logmarkPath;

    public BacklinkIndex(string logmarkPath)
    {
        _logmarkPath = logmarkPath;
    }

    /// <summary>
    /// Get all entries that reference the given target path.
    /// </summary>
    public IReadOnlyList<BacklinkEntry> GetBacklinks(string targetPath)
    {
        var normalizedTarget = NormalizePath(targetPath);

        if (_index.TryGetValue(normalizedTarget, out var backlinks))
        {
            lock (_lock)
            {
                // Return a copy sorted by timestamp (newest first)
                return backlinks
                    .OrderByDescending(b => b.Timestamp)
                    .ToList();
            }
        }

        return Array.Empty<BacklinkEntry>();
    }

    /// <summary>
    /// Add an entry to the index, extracting wiki links from its content.
    /// </summary>
    public void AddEntry(ParsedEntry entry, string sourceFilePath)
    {
        var wikiLinks = ContentParser.GetWikiLinks(entry.Content);
        if (wikiLinks.Count == 0)
            return;

        var normalizedSource = NormalizePath(sourceFilePath);
        var preview = GetContentPreview(entry.Content);
        var backlink = new BacklinkEntry(sourceFilePath, entry.Timestamp, entry.Type, preview, entry.ShortId);

        lock (_lock)
        {
            // Track which targets this source references
            if (!_sourceToTargets.TryGetValue(normalizedSource, out var targets))
            {
                targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _sourceToTargets[normalizedSource] = targets;
            }

            foreach (var link in wikiLinks)
            {
                var resolvedPath = ResolveWikiLinkPath(link.Path, sourceFilePath);
                if (resolvedPath == null)
                    continue;

                var normalizedTarget = NormalizePath(resolvedPath);
                targets.Add(normalizedTarget);

                if (!_index.TryGetValue(normalizedTarget, out var backlinks))
                {
                    backlinks = new List<BacklinkEntry>();
                    _index[normalizedTarget] = backlinks;
                }

                // Avoid duplicates (same source file and timestamp)
                if (!backlinks.Any(b =>
                    b.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase) &&
                    b.Timestamp == entry.Timestamp))
                {
                    backlinks.Add(backlink);
                }
            }
        }
    }

    /// <summary>
    /// Index refs: metadata for an entry, creating backlinks by ShortId.
    /// Call after all ShortIds are indexed (pass 2 of warmup).
    /// </summary>
    public void AddEntryRefs(ParsedEntry entry, string sourceFilePath, Func<string, string?> resolveShortId)
    {
        if (entry.Refs.Count == 0)
            return;

        var normalizedSource = NormalizePath(sourceFilePath);
        var preview = GetContentPreview(entry.Content);
        var backlink = new BacklinkEntry(sourceFilePath, entry.Timestamp, entry.Type, preview, entry.ShortId);

        lock (_lock)
        {
            if (!_sourceToShortIds.TryGetValue(normalizedSource, out var targetIds))
            {
                targetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _sourceToShortIds[normalizedSource] = targetIds;
            }

            foreach (var entryRef in entry.Refs)
            {
                // Only index refs that resolve to existing entries
                var resolved = resolveShortId(entryRef.Id);
                if (resolved == null)
                    continue;

                targetIds.Add(entryRef.Id);

                if (!_shortIdBacklinks.TryGetValue(entryRef.Id, out var backlinks))
                {
                    backlinks = new List<BacklinkEntry>();
                    _shortIdBacklinks[entryRef.Id] = backlinks;
                }

                // Avoid duplicates
                if (!backlinks.Any(b =>
                    b.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase) &&
                    b.Timestamp == entry.Timestamp))
                {
                    backlinks.Add(backlink);
                }
            }
        }
    }

    /// <summary>
    /// Get all entries that reference the given ShortId via refs: metadata.
    /// </summary>
    public IReadOnlyList<BacklinkEntry> GetBacklinksByShortId(string shortId)
    {
        if (_shortIdBacklinks.TryGetValue(shortId, out var backlinks))
        {
            lock (_lock)
            {
                return backlinks
                    .OrderByDescending(b => b.Timestamp)
                    .ToList();
            }
        }

        return Array.Empty<BacklinkEntry>();
    }

    /// <summary>
    /// Remove all backlinks originating from a source file.
    /// Call this before re-indexing a modified file.
    /// </summary>
    public void RemoveFile(string sourceFilePath)
    {
        var normalizedSource = NormalizePath(sourceFilePath);

        lock (_lock)
        {
            // Remove wiki link backlinks
            if (_sourceToTargets.TryRemove(normalizedSource, out var targets))
            {
                foreach (var target in targets)
                {
                    if (_index.TryGetValue(target, out var backlinks))
                    {
                        backlinks.RemoveAll(b =>
                            b.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase));

                        if (backlinks.Count == 0)
                            _index.TryRemove(target, out _);
                    }
                }
            }

            // Remove ShortId backlinks
            if (_sourceToShortIds.TryRemove(normalizedSource, out var shortIds))
            {
                foreach (var shortId in shortIds)
                {
                    if (_shortIdBacklinks.TryGetValue(shortId, out var backlinks))
                    {
                        backlinks.RemoveAll(b =>
                            b.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase));

                        if (backlinks.Count == 0)
                            _shortIdBacklinks.TryRemove(shortId, out _);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clear the entire index.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _index.Clear();
            _sourceToTargets.Clear();
            _shortIdBacklinks.Clear();
            _sourceToShortIds.Clear();
        }
    }

    /// <summary>
    /// Get the number of targets being tracked.
    /// </summary>
    public int TargetCount => _index.Count;

    /// <summary>
    /// Get the total number of backlinks across all targets.
    /// </summary>
    public int TotalBacklinks
    {
        get
        {
            lock (_lock)
            {
                return _index.Values.Sum(list => list.Count);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        // Normalize to lowercase with forward slashes for consistent comparison
        return Path.GetFullPath(path)
            .Replace('\\', '/')
            .ToLowerInvariant();
    }

    private string? ResolveWikiLinkPath(string linkPath, string sourceFilePath)
    {
        // The #^shortid (or #heading) anchor selects inside the target file;
        // the backlink belongs to the file itself (bead lmk-nziz).
        var hashIdx = linkPath.IndexOf('#');
        if (hashIdx >= 0) linkPath = linkPath[..hashIdx];
        if (linkPath.Length == 0) return null;

        // Add .md extension if missing
        if (!linkPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            linkPath += ".md";

        // Try resolving relative to source file's directory
        var sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (sourceDir != null)
        {
            var relativePath = Path.Combine(sourceDir, linkPath);
            if (File.Exists(relativePath))
                return Path.GetFullPath(relativePath);

            // A bare name from a year file points at its sibling pages/ folder,
            // where page write puts pages (bead lmk-m39p). No vault-wide
            // basename search here: this runs for every link at warmup.
            if (LinkTargetResolver.BareName(linkPath) is not null)
            {
                var pagesPath = LinkTargetResolver.SiblingPagesPath(linkPath, sourceFilePath);
                if (File.Exists(pagesPath))
                    return pagesPath;
            }
        }

        // Try resolving relative to logmark folder
        var logmarkRelative = Path.Combine(_logmarkPath, linkPath);
        if (File.Exists(logmarkRelative))
            return Path.GetFullPath(logmarkRelative);

        // Try resolving relative to vault root (one level up from logmark)
        var vaultPath = Path.GetDirectoryName(_logmarkPath);
        if (vaultPath != null)
        {
            var vaultRelative = Path.Combine(vaultPath, linkPath);
            if (File.Exists(vaultRelative))
                return Path.GetFullPath(vaultRelative);
        }

        // Target file doesn't exist yet - still track it for when it's created
        // Resolve relative to logmark as default
        return Path.GetFullPath(Path.Combine(_logmarkPath, linkPath));
    }

    public static string GetContentPreview(string content)
    {
        // Strip leading type prefix (task:, idea:, etc.)
        var stripped = content.TrimStart();
        var prefixes = new[] { "task:", "t:", "idea:", "i:", "block:", "b:", "decision:", "d:" };

        foreach (var prefix in prefixes)
        {
            if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                stripped = stripped.Substring(prefix.Length).TrimStart();
                break;
            }
        }

        // Take first line, truncate to ~60 chars
        var firstLine = stripped.Split('\n')[0].Trim();
        if (firstLine.Length > 60)
            return firstLine.Substring(0, 57) + "...";

        return firstLine;
    }
}
