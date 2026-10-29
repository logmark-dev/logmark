using System.Collections.Concurrent;
using System.IO;
using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Services.EntryParser;
using LogMark.Services.Routes;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Entry cache for the active vault.
/// Caching layer for parsed entries. Sits between views and file system.
/// Provides file-level caching with modification time-based invalidation.
///
/// Thinker-namespace filtering: GetAllEntries and GetRecent default-exclude
/// entries under logmark/thinkers/ via the includeThinkers flag. Other
/// methods (GetByType, GetPendingTasks, GetEntriesUnderPath) do NOT currently
/// filter; callers that need to exclude thinkers from those reads must do so
/// explicitly. Follow-up to extend the flag systematically is tracked
/// separately.
/// </summary>
public class EntryCache : IVaultWriteNotifier, IDisposable
{
    private readonly ConcurrentDictionary<string, CachedFile> _cache = new();
    private readonly ConcurrentDictionary<string, (string filePath, ParsedEntry entry)> _shortIdIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFileAccessor _fileAccessor;
    private readonly string _logmarkPath;
    private readonly string _vaultPath;
    private readonly BacklinkIndex _backlinkIndex;
    private VaultFileWatcher? _fileWatcher;

    /// <summary>
    /// Path-to-route resolution built from routes.md. Rebuilt when routes.md
    /// changes (checked at most every RoutesRecheckMs), so a route added by
    /// update_routes or the settings editor reads back without a restart.
    /// </summary>
    private volatile RouteLocator _routeLocator = RouteLocator.Empty;
    private DateTime _routesFileStamp = DateTime.MinValue;
    private long _routesCheckedAt = long.MinValue / 2;
    private readonly object _routesLock = new();
    private const int RoutesRecheckMs = 2000;

    private bool _isWarmedUp;
    private Task? _warmupTask;
    private DateTime _lastVerified = DateTime.MinValue;
    private readonly TimeSpan _verifyDebounce = TimeSpan.FromSeconds(5);

    public bool IsReady => _isWarmedUp;
    public Task? WarmupTask => _warmupTask;
    internal string LogmarkPath => _logmarkPath;
    internal IFileAccessor FileAccessor => _fileAccessor;

    /// <summary>Vault root this cache was constructed against (parent of logmark/).</summary>
    public string VaultPath => _vaultPath;

    public EntryCache(string vaultPath, IFileAccessor? fileAccessor = null)
    {
        _fileAccessor = fileAccessor ?? new FileAccessor();
        _vaultPath = vaultPath;
        _logmarkPath = Path.Combine(vaultPath, "logmark");
        _backlinkIndex = new BacklinkIndex(_logmarkPath);
    }

    /// <summary>
    /// Get entries for a file. Uses cache if fresh, parses and caches if stale/missing.
    /// </summary>
    public IReadOnlyList<ParsedEntry> GetEntries(string filePath, EntryType? typeHint = null)
    {
        if (!_fileAccessor.Exists(filePath))
            return Array.Empty<ParsedEntry>();

        var normalizedPath = NormalizePath(filePath);
        var lastWrite = _fileAccessor.GetLastWriteTimeUtc(filePath);

        if (_cache.TryGetValue(normalizedPath, out var cached) && cached.LastModified >= lastWrite)
            return cached.Entries;

        // Cache miss or stale - parse and cache
        var content = _fileAccessor.ReadAllText(filePath);
        var entries = EntryParser.EntryParser.Parse(content, typeHint);
        var cachedFile = new CachedFile(lastWrite, entries, typeHint);
        _cache[normalizedPath] = cachedFile;

        return entries;
    }

    /// <summary>
    /// Get recent entries across all cached files, sorted by timestamp (newest first).
    /// If cache is not warmed up, returns empty list with IsReady = false.
    /// By default, entries under logmark/thinkers/ are excluded. Pass includeThinkers: true
    /// to include them (e.g. thinker-specific views).
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetRecent(int count, bool includeThinkers = false)
    {
        if (_cache.IsEmpty)
            return Array.Empty<IndexedEntry>();

        var source = includeThinkers
            ? _cache.AsEnumerable()
            : _cache.Where(kvp => !IsInThinkersNamespace(kvp.Key));

        return source
            .SelectMany(kvp => kvp.Value.Entries.Select(e =>
            {
                var route = ExtractRoute(kvp.Key);
                return new IndexedEntry(e, kvp.Key, route, ExtractScope(kvp.Key, route));
            }))
            .OrderByDescending(ie => ie.Entry.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Get entries filtered by type across all cached files.
    /// Optional <paramref name="since"/> excludes entries with timestamp earlier
    /// than the cutoff (inclusive lower bound, matching SearchQuery.After semantics).
    /// Optional <paramref name="limit"/> caps the result count after the since filter
    /// and the descending-timestamp sort, giving "top N most-recent matches" semantics.
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetByType(EntryType type, DateTime? since = null, int? limit = null)
    {
        var query = _cache
            .SelectMany(kvp =>
            {
                var route = ExtractRoute(kvp.Key);
                var scope = ExtractScope(kvp.Key, route);
                return kvp.Value.Entries
                    .Where(e => e.Type == type && (since is null || e.Timestamp >= since.Value))
                    .Select(e => new IndexedEntry(e, kvp.Key, route, scope));
            })
            .OrderByDescending(ie => ie.Entry.Timestamp);

        return limit.HasValue ? query.Take(limit.Value).ToList() : query.ToList();
    }

    /// <summary>
    /// Get pending tasks: neither completed nor dismissed, optionally filtered
    /// by deadline.
    ///
    /// The dismissal exclusion is what makes the task dismissal exit visible.
    /// Without it a dismissed task keeps appearing in every census, feed count,
    /// and closure candidate list, and the verb changes nothing any consumer
    /// can observe. Mirrors WorkHygieneDetector.IsUnresolvedBlock, which has
    /// filtered the same tag on the block side all along.
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetPendingTasks(TaskFilter? filter = null)
    {
        var now = DateTime.Now;
        var tasks = _cache
            .SelectMany(kvp =>
            {
                var route = ExtractRoute(kvp.Key);
                var scope = ExtractScope(kvp.Key, route);
                return kvp.Value.Entries
                    .Where(e => e.Type == EntryType.Task && !e.IsCompleted && !IsDismissed(e))
                    .Select(e => new IndexedEntry(e, kvp.Key, route, scope));
            });

        if (filter == TaskFilter.Overdue)
        {
            tasks = tasks.Where(ie => ie.Entry.TaskMeta?.Deadline != null && ie.Entry.TaskMeta.Deadline < now);
        }
        else if (filter == TaskFilter.Today)
        {
            var todayStart = DateTime.Today;
            var todayEnd = todayStart.AddDays(1);
            tasks = tasks.Where(ie =>
                ie.Entry.TaskMeta?.Deadline != null &&
                ie.Entry.TaskMeta.Deadline >= todayStart &&
                ie.Entry.TaskMeta.Deadline < todayEnd);
        }

        return tasks
            .OrderBy(ie => ie.Entry.TaskMeta?.Deadline ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// True when an entry carries the "#dismissed" lifecycle tag - a closed
    /// state that, unlike completion, leaves no strikethrough to read.
    /// </summary>
    public static bool IsDismissed(ParsedEntry entry) =>
        NotationParser.ExtractTags(entry.Content)
            .Contains("dismissed", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get entries that reference the given target path via wiki links.
    /// Returns entries sorted by timestamp (newest first).
    /// </summary>
    public IReadOnlyList<BacklinkEntry> GetBacklinks(string targetPath)
    {
        return _backlinkIndex.GetBacklinks(targetPath);
    }

    /// <summary>
    /// Resolve a ShortId to a file path and parsed entry.
    /// Returns null if the ShortId is not found.
    /// </summary>
    public (string filePath, ParsedEntry entry)? ResolveShortId(string shortId)
    {
        if (_shortIdIndex.TryGetValue(shortId, out var result))
            return result;
        return null;
    }

    /// <summary>
    /// Get entries that reference the given ShortId via refs: metadata.
    /// Returns entries sorted by timestamp (newest first).
    /// </summary>
    public IReadOnlyList<BacklinkEntry> GetBacklinksForEntry(string shortId)
    {
        return _backlinkIndex.GetBacklinksByShortId(shortId);
    }

    /// <summary>
    /// Entries under a route, given its name ("fin-inv", resolved through
    /// routes.md to its folder) or a folder path. Includes routes nested in its
    /// folder, like every route filter. Callers holding a VaultScope.Route use
    /// this rather than passing a route name to GetEntriesUnderPath as a path.
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetEntriesForRoute(string routeOrFolder) =>
        GetEntriesUnderPath(GetPathForRoute(routeOrFolder.TrimStart('+')) ?? routeOrFolder);

    /// <summary>
    /// Get entries from files under a specific folder path, sorted by timestamp (newest first).
    /// Useful for folder-scoped views (e.g., only entries from _inbox/_diary/).
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetEntriesUnderPath(string folderPath)
    {
        // Relative input is vault content, never the process working directory:
        // "logmark/x" is vault-relative, anything else logmark-relative.
        if (!Path.IsPathRooted(folderPath))
        {
            var rel = folderPath.Replace('\\', '/').Trim('/');
            folderPath = rel.StartsWith("logmark/", StringComparison.OrdinalIgnoreCase) || rel.Equals("logmark", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(_vaultPath, rel)
                : Path.Combine(_logmarkPath, rel);
        }

        // Whole-folder match: "projects/foo" must not claim "projects/foobar".
        var normalizedFolder = NormalizePath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        var folderPrefix = normalizedFolder + Path.DirectorySeparatorChar;

        return _cache
            .Where(kvp => kvp.Key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kvp =>
            {
                var route = ExtractRoute(kvp.Key);
                return kvp.Value.Entries.Select(e => new IndexedEntry(e, kvp.Key, route, ExtractScope(kvp.Key, route)));
            })
            .OrderByDescending(ie => ie.Entry.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Get all cached entries across all files, sorted by timestamp (newest first).
    /// By default, entries under logmark/thinkers/ are excluded. Pass includeThinkers: true
    /// to include them. GetEntriesUnderPath is unaffected -- explicit paths always return entries.
    /// </summary>
    public IReadOnlyList<IndexedEntry> GetAllEntries(bool includeThinkers = false)
    {
        if (_cache.IsEmpty)
            return Array.Empty<IndexedEntry>();

        return _cache
            .Where(kvp => includeThinkers || !IsInThinkersNamespace(kvp.Key))
            .SelectMany(kvp =>
            {
                var route = ExtractRoute(kvp.Key);
                return kvp.Value.Entries.Select(e => new IndexedEntry(e, kvp.Key, route, ExtractScope(kvp.Key, route)));
            })
            .OrderByDescending(ie => ie.Entry.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Finds the file path that contains the given entry, matched by Timestamp and ShortId.
    /// Returns null if the entry is not found in any cached file.
    /// Used as a last-resort lookup when the caller does not know which file hosts an entry.
    /// </summary>
    public string? FindFileContaining(ParsedEntry entry)
    {
        foreach (var kvp in _cache)
        {
            foreach (var e in kvp.Value.Entries)
            {
                if (e.Timestamp == entry.Timestamp &&
                    string.Equals(e.ShortId, entry.ShortId, StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;
            }
        }
        return null;
    }

    private bool IsInThinkersNamespace(string filePath)
    {
        var normalizedVault = _vaultPath.Replace('\\', '/').TrimEnd('/');
        var normalizedFile = filePath.Replace('\\', '/');
        if (!normalizedFile.StartsWith(normalizedVault, StringComparison.OrdinalIgnoreCase))
            return false;
        var relative = normalizedFile[normalizedVault.Length..].TrimStart('/');
        return relative.StartsWith("logmark/thinkers/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the path sits under logmark/_system/ - the vault's machine
    /// namespace (trash, config, routes, logs). Same exclusion WarmupAsync and
    /// VaultFileWatcher.ShouldIgnore apply, matched relative to the vault root
    /// so a vault whose own path happens to contain "_system" is not excluded
    /// wholesale.
    /// </summary>
    private bool IsInSystemNamespace(string filePath)
    {
        var normalizedVault = _vaultPath.Replace('\\', '/').TrimEnd('/');
        var normalizedFile = filePath.Replace('\\', '/');
        if (!normalizedFile.StartsWith(normalizedVault, StringComparison.OrdinalIgnoreCase))
            return false;
        var relative = normalizedFile[normalizedVault.Length..].TrimStart('/');
        return relative.StartsWith("logmark/_system/", StringComparison.OrdinalIgnoreCase);
    }

    #region Invalidation

    /// <summary>
    /// Fires after InvalidateFile completes. Carries the absolute path that was invalidated.
    /// Subscribers may run on any thread (VaultWriter -> in-process writer thread,
    /// VaultFileWatcher -> thread pool). Marshal to UI thread before touching WPF state.
    /// </summary>
    public event Action<string>? FileInvalidated;

    /// <summary>
    /// IVaultWriteNotifier implementation. Called by VaultWriter after a file is written.
    /// </summary>
    public void FileWritten(string absolutePath)
    {
        InvalidateFile(absolutePath);
    }

    /// <summary>
    /// Invalidate a specific file, forcing re-parse on next access.
    /// Call this after saving an entry.
    ///
    /// Files under logmark/_system/ are evicted but never re-admitted: this is
    /// the third cache admission path (alongside WarmupAsync and
    /// VaultFileWatcher) and the only one that used to lack the _system
    /// exclusion. Because VaultWriter notifies on every write, trashing an
    /// entry (which writes logmark/_system/_trash/{year}.md) re-admitted the
    /// entry it had just removed - a trashed task keeps its _task: line, so it
    /// still parses as EntryType.Task and reappeared in the dashboard
    /// Attention section until the next restart. Eviction still runs on the
    /// _system path so an already-polluted cache heals on the next write.
    /// </summary>
    public void InvalidateFile(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        _cache.TryGetValue(normalizedPath, out var oldCached);

        // Re-read before swapping: dropping the cached file first left a window
        // in which a concurrent read saw none of its entries or ids.
        IReadOnlyList<ParsedEntry> entries = Array.Empty<ParsedEntry>();
        var keep = _fileAccessor.Exists(filePath) && !IsInSystemNamespace(filePath);
        if (keep)
        {
            var typeHint = InferTypeFromPath(filePath);
            var lastWrite = _fileAccessor.GetLastWriteTimeUtc(filePath);
            entries = EntryParser.EntryParser.Parse(_fileAccessor.ReadAllText(filePath), typeHint);
            _cache[normalizedPath] = new CachedFile(lastWrite, entries, typeHint);
        }
        else
        {
            _cache.TryRemove(normalizedPath, out _);
        }

        // Retire only the ShortIds the edit removed; surviving ones are re-pointed below.
        if (oldCached is not null)
        {
            var current = new HashSet<string>(entries.Select(e => e.ShortId).Where(id => !string.IsNullOrEmpty(id))!);
            foreach (var oldEntry in oldCached.Entries)
            {
                if (!string.IsNullOrEmpty(oldEntry.ShortId) && !current.Contains(oldEntry.ShortId))
                    _shortIdIndex.TryRemove(oldEntry.ShortId, out _);
            }
        }

        // Re-index backlinks and ShortIds for this file
        _backlinkIndex.RemoveFile(filePath);
        if (keep)
        {
            foreach (var entry in entries)
            {
                _backlinkIndex.AddEntry(entry, filePath);
                IndexShortId(entry, filePath);
            }

            // Re-index refs (ShortIds are available)
            foreach (var entry in entries)
            {
                if (entry.Refs.Count > 0)
                    _backlinkIndex.AddEntryRefs(entry, filePath, id => ResolveShortId(id)?.filePath);
            }
        }

        FileInvalidated?.Invoke(filePath);
    }

    /// <summary>
    /// Invalidate all files under a route path.
    /// </summary>
    public void InvalidateRoute(string routePath)
    {
        var normalizedRoute = NormalizePath(routePath);
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(normalizedRoute, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Clear entire cache. Use sparingly.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _shortIdIndex.Clear();
        _backlinkIndex.Clear();
        _isWarmedUp = false;
    }

    /// <summary>
    /// Verify cache freshness against file system. Call on window focus.
    /// Checks modification times and removes stale/deleted entries.
    /// </summary>
    public void VerifyOnFocus()
    {
        var now = DateTime.UtcNow;
        if (now - _lastVerified < _verifyDebounce)
            return;

        _lastVerified = now;

        var keysToRemove = new List<string>();

        foreach (var kvp in _cache)
        {
            if (!_fileAccessor.Exists(kvp.Key))
            {
                // File deleted
                keysToRemove.Add(kvp.Key);
            }
            else
            {
                var currentModified = _fileAccessor.GetLastWriteTimeUtc(kvp.Key);
                if (currentModified > kvp.Value.LastModified)
                {
                    // File modified externally
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    #endregion

    #region Warmup

    /// <summary>
    /// Start background warmup of cache. Non-blocking.
    /// </summary>
    public void StartWarmup()
    {
        if (_warmupTask != null)
            return;

        _warmupTask = Task.Run(WarmupAsync);
    }

    private async Task WarmupAsync()
    {
        try
        {
            RefreshRoutesIfChanged();

            if (!Directory.Exists(_logmarkPath))
            {
                _isWarmedUp = true;
                return;
            }

            var files = _fileAccessor.EnumerateFiles(_logmarkPath, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains("_system"));

            // Track entries with refs for pass 2
            var entriesWithRefs = new List<(ParsedEntry entry, string filePath)>();

            // Pass 1: Parse files, populate cache, build ShortId index, index wiki links
            foreach (var file in files)
            {
                var typeHint = InferTypeFromPath(file);
                var entries = GetEntries(file, typeHint); // Populates cache

                foreach (var entry in entries)
                {
                    _backlinkIndex.AddEntry(entry, file);
                    IndexShortId(entry, file);

                    if (entry.Refs.Count > 0)
                        entriesWithRefs.Add((entry, file));
                }

                await Task.Yield(); // Yield to avoid blocking UI thread
            }

            // Pass 2: Index refs (all ShortIds are now available for resolution)
            foreach (var (entry, filePath) in entriesWithRefs)
            {
                _backlinkIndex.AddEntryRefs(entry, filePath, id => ResolveShortId(id)?.filePath);
            }

            _isWarmedUp = true;
            StartFileWatcher();
        }
        catch
        {
            // Warmup failure is non-fatal - cache will populate on demand
            _isWarmedUp = true;
            StartFileWatcher();
        }
    }

    /// <summary>
    /// Start watching logmark/ for external file changes (MCP, CLI, Obsidian).
    /// Called after warmup so the initial scan does not trigger spurious invalidations.
    /// Watcher callbacks fire on thread pool threads and never acquire VaultWriter
    /// named mutexes, so no cross-process deadlocks with concurrent app/MCP/CLI writers.
    /// </summary>
    private void StartFileWatcher()
    {
        if (!Directory.Exists(_logmarkPath)) return;

        _fileWatcher = new VaultFileWatcher(
            _logmarkPath,
            onFileChanged: OnExternalFileChanged,
            onOverflow: VerifyOnFocus
        );
        _fileWatcher.Start();
    }

    /// <summary>
    /// Callback from VaultFileWatcher. Fires on thread pool -- no mutex held.
    /// Skips files whose LastWriteTime matches the cached version (avoids
    /// redundant re-parse when the write originated from this process).
    /// </summary>
    private void OnExternalFileChanged(string absolutePath)
    {
        var normalizedPath = NormalizePath(absolutePath);

        if (_cache.TryGetValue(normalizedPath, out var cached))
        {
            try
            {
                var currentWrite = _fileAccessor.GetLastWriteTimeUtc(absolutePath);
                if (cached.LastModified >= currentWrite)
                    return; // Already up to date (self-written via VaultWriter)
            }
            catch
            {
                // File deleted between event and check -- fall through to invalidate
            }
        }

        InvalidateFile(absolutePath);
    }

    private void IndexShortId(ParsedEntry entry, string filePath)
    {
        if (!string.IsNullOrEmpty(entry.ShortId))
        {
            _shortIdIndex[entry.ShortId] = (filePath, entry);
        }
    }

    #endregion

    #region Helpers

    // The key is also every entry's FilePath, so it folds case only where the
    // file system does (Windows, macOS - the split .NET's own Path makes).
    // Folding on Linux handed out paths that name no file once the vault path
    // has a capital letter, and route lookups put every entry outside its
    // route (rc7 QA: route-scoped get_tasks came back empty).
    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return OperatingSystem.IsLinux() ? full : full.ToLowerInvariant();
    }

    /// <summary>
    /// Resolves the route name for a file or directory path within the vault.
    /// Longest routes.md path wins (a nested route is not swallowed by its
    /// parent); falls back to the projects|domains|clients naming convention.
    /// Returns null for paths that are not inside a route
    /// (vault root, type folders directly under logmark/, _system paths).
    /// </summary>
    public string? GetRouteForPath(string absolutePath) => LocatePath(absolutePath).Route;

    /// <summary>
    /// Route, route folder and scope for a file or directory path in the vault,
    /// via <see cref="RouteLocator"/>: scope is the first non-type folder below
    /// the route folder, never a search of the path for the route name.
    /// </summary>
    public RouteLocation LocatePath(string absolutePath) =>
        Routes.Locate(Path.GetRelativePath(_logmarkPath, absolutePath));

    /// <summary>
    /// Drop the routes.md debounce so the next lookup re-reads the file. Called
    /// by the route writer right after it replaces routes.md (bead lmk-7t1p).
    /// </summary>
    public void InvalidateRoutes()
    {
        lock (_routesLock)
        {
            _routesFileStamp = DateTime.MinValue;
            Interlocked.Exchange(ref _routesCheckedAt, long.MinValue / 2);
        }
    }

    /// <summary>The routes.md-derived locator, reloaded when routes.md changes.</summary>
    public RouteLocator Routes
    {
        get
        {
            RefreshRoutesIfChanged();
            return _routeLocator;
        }
    }

    /// <summary>
    /// Resolves a route name to its absolute path in the vault.
    /// Reverse of <see cref="GetRouteForPath"/>: scans the routes.md-derived
    /// map first, then falls back to convention-based paths (projects/X,
    /// domains/X, clients/X) by inspecting cached entries.
    /// Returns null if the route name has no matching path.
    /// </summary>
    public string? GetPathForRoute(string routeName)
    {
        if (string.IsNullOrEmpty(routeName)) return null;

        // Primary: the route's declared routes.md path.
        if (Routes.PathOf(routeName) is { } declared)
            return Path.Combine(_logmarkPath, declared.Replace('/', Path.DirectorySeparatorChar));

        // Fallback: scan cached entries for one whose GetRouteForPath matches.
        foreach (var ie in GetAllEntries(includeThinkers: false))
        {
            var entryRoute = GetRouteForPath(ie.FilePath);
            if (string.Equals(entryRoute, routeName, StringComparison.OrdinalIgnoreCase))
            {
                // Strip back from file path to the route directory.
                // logmark/<category>/<route>/_<type>/YYYY.md -> logmark/<category>/<route>
                var rel = Path.GetRelativePath(_logmarkPath, ie.FilePath).Replace('\\', '/');
                var parts = rel.Split('/');
                if (parts.Length >= 2 && parts[0] is "projects" or "domains" or "clients")
                    return Path.Combine(_logmarkPath, parts[0], parts[1]);
            }
        }

        // Last resort: treat the route name as a literal folder path under
        // logmark/. Routes outside routes.md AND outside the
        // projects/domains/clients convention - "agents/claude-code", "_inbox" -
        // are reported by digests and rendered as +agents/claude-code, so a
        // caller that cannot resolve them turns a real route into a dead click.
        //
        // Grounded in cached entries rather than Directory.Exists: it must not
        // invent a folder nothing was captured under, and the cache is the
        // sanctioned way to read vault contents.
        var routeRelative = routeName.Replace('\\', '/').Trim('/');
        if (routeRelative.Length > 0)
        {
            var prefix = routeRelative + "/";
            foreach (var ie in GetAllEntries(includeThinkers: false))
            {
                var rel = Path.GetRelativePath(_logmarkPath, ie.FilePath).Replace('\\', '/');
                if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(
                        _logmarkPath, routeRelative.Replace('/', Path.DirectorySeparatorChar));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a route name to its logmark-relative path with forward slashes
    /// (e.g. "quant-trading-platform" -> "projects/quant-trading-platform").
    /// Returns null if the name doesn't match a known route. Use this when
    /// callers need to feed a routePath into PathConstants.GetRoutedFilePath
    /// or EntryOrganizer.AppendEntry, which expect a path under logmark/, not
    /// a bare route name.
    /// </summary>
    public string? GetRelativePathForRoute(string routeName)
    {
        var absolute = GetPathForRoute(routeName);
        if (absolute == null) return null;
        return Path.GetRelativePath(_logmarkPath, absolute).Replace('\\', '/');
    }

    private string? ExtractRoute(string filePath) => GetRouteForPath(filePath);

    private void BuildRoutePathMap()
    {
        try
        {
            _routeLocator = new RouteLocator(new RouteParser(_vaultPath).Parse().Routes);
        }
        catch
        {
            // Routes file missing or unparseable - not fatal
            _routeLocator = RouteLocator.Empty;
        }
    }

    /// <summary>
    /// Rebuild the locator when routes.md's write time moves. Stat-debounced so
    /// the per-file route lookups in GetAllEntries do not each hit the disk.
    /// </summary>
    private void RefreshRoutesIfChanged()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _routesCheckedAt) < RoutesRecheckMs)
            return;

        lock (_routesLock)
        {
            if (now - _routesCheckedAt < RoutesRecheckMs)
                return;

            DateTime stamp;
            try
            {
                var routesFile = Path.Combine(_vaultPath, PathConstants.GetRoutesPath());
                stamp = File.Exists(routesFile) ? File.GetLastWriteTimeUtc(routesFile) : DateTime.MaxValue;
            }
            catch
            {
                return;
            }

            if (stamp != _routesFileStamp)
            {
                _routesFileStamp = stamp;
                BuildRoutePathMap();
            }
            Interlocked.Exchange(ref _routesCheckedAt, now);
        }
    }

    // The route argument is unused: scope comes from the folder below the
    // route's folder, never from searching the path for the route name, which
    // failed whenever a route's name differs from its folder.
    private string? ExtractScope(string filePath, string? route) => LocatePath(filePath).Scope;

    private static EntryType? InferTypeFromPath(string filePath)
    {
        return EntryTypeMapper.InferTypeFromPath(filePath);
    }

    #endregion

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
    }
}

public enum TaskFilter
{
    All,
    Today,
    Overdue
}
