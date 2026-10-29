using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Events;
using LogMark.Models;
using LogMark.Models.Sensitivity;
using LogMark.Services.EntryCache;
using LogMark.Services.Sensitivity;

namespace LogMark.Services;

/// <summary>
/// Handles reading and writing entries to the Obsidian vault.
/// </summary>
public partial class VaultService
{
    private readonly string _vaultPath;
    private readonly NotationParser _parser;
    private readonly RouteService? _routeService;
    private readonly SystemLogger? _logger;
    private readonly EntityPoolService? _entityPoolService;
    private readonly EventLogger? _eventLogger;
    private readonly EntryCache.EntryCache? _entryCache;
    private readonly EventBus? _eventBus;
    private readonly IVaultFileWriter? _vaultWriter;
    private readonly IVaultWriteGate? _writeGate;

    [GeneratedRegex(@"^## (\d{4}-\d{2}-\d{2} \d{2}:\d{2})", RegexOptions.Multiline)]
    private static partial Regex EntryHeaderPattern();

    public VaultService(string vaultPath, NotationParser? parser = null, RouteService? routeService = null, SystemLogger? logger = null, EntityPoolService? entityPoolService = null, EventLogger? eventLogger = null, EntryCache.EntryCache? entryCache = null, EventBus? eventBus = null, IVaultFileWriter? vaultWriter = null, IVaultWriteGate? writeGate = null)
    {
        _vaultPath = vaultPath;
        _parser = parser ?? new NotationParser();
        _routeService = routeService;
        _logger = logger;
        _entityPoolService = entityPoolService;
        _eventLogger = eventLogger;
        _entryCache = entryCache;
        _eventBus = eventBus;
        _vaultWriter = vaultWriter;
        _writeGate = writeGate;
    }

    public string VaultPath => _vaultPath;

    public async Task<string> SaveEntryAsync(string content, string? context = null)
    {
        var now = DateTime.Now;
        var entry = _parser.Parse(content, context);
        entry.Timestamp = now;  // Ensure timestamp is set

        // Use the Entry-based SaveEntryAsync
        return await SaveEntryAsync(entry);
    }

    /// <summary>
    /// Get yearly file path for unrouted entry (no project references)
    /// </summary>
    private static string GetYearlyFilePath(Entry entry)
    {
        var typeName = EntryTypeMapper.GetFolderName(entry.Type);
        var year = entry.Timestamp.Year;

        // Root level (no routing) - use PathConstants for logmark namespace
        return PathConstants.GetUnroutedFilePath(typeName, year);
    }

    public bool VaultExists()
    {
        return Directory.Exists(_vaultPath);
    }

    /// <summary>
    /// Gets recent entries from log files.
    /// Returns (timestamp, preview, relativePath) tuples.
    /// </summary>
    public async Task<List<(DateTime Timestamp, string Preview, string Path)>> GetRecentEntriesAsync(int count)
    {
        var entries = new List<(DateTime Timestamp, string Preview, string Path)>();
        var logmarkDir = Path.Combine(_vaultPath, "logmark");

        if (!Directory.Exists(logmarkDir))
            return entries;

        // Get recent log files (last 7 days worth)
        var today = DateTime.Today;
        var filesToCheck = new List<string>();

        for (int i = 0; i < 7; i++)
        {
            var date = today.AddDays(-i);
            var path = Path.Combine(_vaultPath, GetDailyLogPath(date));
            if (File.Exists(path))
                filesToCheck.Add(path);
        }

        foreach (var filePath in filesToCheck)
        {
            var fileEntries = await ParseEntriesFromFileAsync(filePath);
            entries.AddRange(fileEntries);

            if (entries.Count >= count)
                break;
        }

        return entries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    private async Task<List<(DateTime Timestamp, string Preview, string Path)>> ParseEntriesFromFileAsync(string filePath)
    {
        var entries = new List<(DateTime Timestamp, string Preview, string Path)>();
        var content = await File.ReadAllTextAsync(filePath);

        // Extract date from filename (YYYY-MM-DD.md)
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (!DateTime.TryParse(fileName, out var fileDate))
            return entries;

        // Split by entry headers (## HH:mm)
        var sections = content.Split(["---"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var section in sections)
        {
            var match = EntryHeaderPattern().Match(section);
            if (!match.Success) continue;

            var timestampStr = match.Groups[1].Value;
            if (!DateTime.TryParse(timestampStr, out var timestamp))
                continue;

            // Get preview (first non-empty line after header)
            var lines = section.Split('\n')
                .SkipWhile(l => !l.StartsWith("## "))
                .Skip(1)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("context:"))
                .FirstOrDefault();

            var preview = lines ?? "(empty)";
            if (preview.Length > 50)
                preview = preview[..47] + "...";

            var relativePath = Path.GetRelativePath(_vaultPath, filePath);
            entries.Add((timestamp, preview, relativePath));
        }

        return entries;
    }

    /// <summary>
    /// DEPRECATED: Will be removed in Task 6 when GetRecentEntriesAsync is rewritten
    /// to read from internal log instead of scanning daily files.
    /// </summary>
    private static string GetDailyLogPath(DateTime date)
    {
        return $"logmark/{date:yyyy}/{date:MM}/{date:yyyy-MM-dd}.md";
    }

    private async Task AppendEntry(string filePath, Entry entry)
    {
        var sb = new StringBuilder();

        // Check if file has content - if so, add blank line before next entry
        var fileContent = File.Exists(filePath) ? await File.ReadAllTextAsync(filePath) : "";
        var fileHasContent = !string.IsNullOrWhiteSpace(fileContent.Trim('\uFEFF'));
        if (fileHasContent)
        {
            sb.AppendLine();
        }

        // Header with inline context
        var header = $"## {entry.Timestamp:yyyy-MM-dd HH:mm}";
        if (!string.IsNullOrEmpty(entry.Context))
        {
            var truncatedContext = TruncateContext(entry.Context);
            header += $" | {truncatedContext}";
        }
        sb.AppendLine(header);

        // Content with notation stripped (temporal expressions preserved as-is)
        var content = FileRouter.StripNotation(entry.RawContent, entry.Type, entry.ProseRouteTokens);
        sb.AppendLine(content);

        // Generate ShortId for ALL entry types (enables consistent linking)
        var shortId = TaskTracker.GenerateShortId();
        entry.ShortId = shortId;

        // Block ID suffix enables Obsidian links like [[_tasks/2026#^b123bbe1|task name]]
        var blockIdSuffix = $" ^{shortId}";

        // For tasks, add task-specific metadata line
        if (entry.Type == EntryType.Task)
        {
            if (entry.Deadline != null)
            {
                var isoFormat = entry.Deadline.DateTime.ToString("yyyy-MM-dd, HH:mm");
                sb.AppendLine($"_task: {isoFormat} | postponed: 0 | {shortId}{blockIdSuffix}");
            }
            else
            {
                sb.AppendLine($"_task: none | postponed: 0 | {shortId}{blockIdSuffix}");
            }
        }
        else
        {
            // For non-tasks, add entry ID metadata line
            sb.AppendLine($"_id: {shortId}{blockIdSuffix}");
        }

        // Entry attribution metadata (for AI agent workflow)
        if (!string.IsNullOrEmpty(entry.Source))
        {
            sb.AppendLine($"source: {entry.Source}");
        }
        if (!string.IsNullOrEmpty(entry.Session))
        {
            sb.AppendLine($"session: {entry.Session}");
        }

        // Cross-entry references
        if (entry.Refs.Count > 0)
        {
            var refsStr = string.Join(", ", entry.Refs.Select(r => r.ToString()));
            sb.AppendLine($"refs: {refsStr}");
        }

        if (_vaultWriter != null)
            await _vaultWriter.AppendTextAsync(filePath, sb.ToString());
        else
            await File.AppendAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Truncate context to fit inline with timestamp header (max 75 chars)
    /// </summary>
    private static string TruncateContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return context;

        // Strip " on X: " and tweet content for Twitter
        if (context.Contains(" on X: "))
            context = context.Split(" on X: ")[0] + " on X";

        // Truncate at 75 chars, cut at word boundary
        if (context.Length > 75)
        {
            var truncated = context.Substring(0, 75);
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 50) // Keep at least 50 chars
                truncated = truncated.Substring(0, lastSpace);
            context = truncated.TrimEnd('.', ',', ' ') + "...";
        }

        return context;
    }

    /// <summary>
    /// Saves an entry and returns the relative vault path.
    /// Existing callers that only need the path use this overload.
    /// For access to the write-gate result (e.g. to detect free-tier unmasked writes),
    /// use SaveEntryWithResultAsync instead.
    /// </summary>
    public async Task<string> SaveEntryAsync(Entry entry)
    {
        var (path, _) = await SaveEntryWithResultAsync(entry);
        return path;
    }

    /// <summary>
    /// Saves an entry and returns both the relative vault path and the write-gate result.
    /// Use this overload when the caller needs to check whether the entry was written
    /// as plain text because Pro is inactive (VaultWriteResult.Unmasked == true).
    /// </summary>
    public async Task<(string Path, VaultWriteResult Write)> SaveEntryWithResultAsync(Entry entry)
    {
        string relativePath;
        var aggregateWrite = VaultWriteResult.Clean;

        // Handle subpage creation for complex pastes (before routing modifies entry)
        if (entry.PasteContext?.RequiresSubpage == true)
        {
            // Determine route path (first project, or null for unrouted)
            var routePath = entry.Projects.Count > 0
                ? _routeService?.ResolveRoutePath(entry.Projects[0])
                : null;

            // Resolve Route object for write-gate masking
            Route? route = null;
            if (entry.Projects.Count > 0 && _routeService != null)
            {
                try
                {
                    route = await _routeService.ResolveAsync(entry.Projects[0],
                        RouteCreationPolicy.RejectIfUnknown).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Unknown route name -- subpage writes unrouted, same as today.
                    route = null;
                }
            }

            // Create subpage and replace entry content with link; capture its write result.
            var subpageWrite = await CreateSubpageAndReplaceContentAsync(entry, routePath, route).ConfigureAwait(false);
            // Merge subpage write result: if subpage write was unmasked, include those spans.
            if (subpageWrite.Unmasked)
            {
                aggregateWrite = subpageWrite;
            }
        }

        // Check if entry should be routed (has projects and RouteService available)
        if (_routeService != null && _routeService.ShouldRoute(entry))
        {
            // Use RouteService for routed entries (respects routes.md configuration)
            var destinations = await _routeService.RouteEntryAsync(entry);

            if (destinations.Count > 0)
            {
                // Log all destinations - redact preview for private-route writes
                var isPrivate = _routeService?.EntryTargetsPrivateRoute(entry) ?? false;
                var preview = isPrivate ? "(private route)" : SystemLogger.GetPreview(entry.RawContent);
                var typeLabel = SystemLogger.GetTypeLabel(entry.Type);

                foreach (var dest in destinations)
                {
                    _logger?.LogEntryWithLink($"Routed {typeLabel}", dest.Path, preview);

                    // Aggregate write results: if any destination is Unmasked, surface it.
                    if (dest.Write.Unmasked)
                    {
                        var merged = new List<SensitiveSpan>(aggregateWrite.UnmaskedSpans);
                        merged.AddRange(dest.Write.UnmaskedSpans);
                        aggregateWrite = new VaultWriteResult(true, merged);
                    }

                    // Publish ScopeCreated event if a new scope was auto-registered
                    if (!string.IsNullOrEmpty(dest.NewScopeName) && !string.IsNullOrEmpty(dest.BaseRouteName))
                    {
                        _eventBus?.Publish(new ScopeCreatedEvent(dest.BaseRouteName, dest.NewScopeName));
                    }
                }

                // Return first destination for backward compatibility
                relativePath = destinations[0].Path;
            }
            else
            {
                // Routing failed (no valid routes), fall back to unrouted
                relativePath = await SaveUnroutedEntry(entry);

                // Redact preview if the original entry targeted a private route (routing failed but intent was private)
                var isPrivate = _routeService?.EntryTargetsPrivateRoute(entry) ?? false;
                var preview = isPrivate ? "(private route)" : SystemLogger.GetPreview(entry.RawContent);
                var typeLabel = SystemLogger.GetTypeLabel(entry.Type);

                _logger?.LogEntryWithLink($"Saved {typeLabel}", relativePath, preview);
            }
        }
        else
        {
            // Unrouted entry - save to root level using type folder
            relativePath = await SaveUnroutedEntry(entry);

            // Defensive: check entry projects against private routes even for unrouted saves
            var isPrivate = _routeService?.EntryTargetsPrivateRoute(entry) ?? false;
            var preview = isPrivate ? "(private route)" : SystemLogger.GetPreview(entry.RawContent);
            var typeLabel = SystemLogger.GetTypeLabel(entry.Type);

            _logger?.LogEntryWithLink($"Saved {typeLabel}", relativePath, preview);
        }

        // Auto-add unknown tags to tags.md
        if (entry.Tags.Count > 0)
        {
            _entityPoolService?.AddUnknownTags(entry.Tags);
        }

        // Log EntryCreated event
        if (_eventLogger != null && entry.Id != Guid.Empty)
        {
            try
            {
                // Determine paste type from PasteContext
                string? pasteType = null;
                if (entry.PasteContext != null)
                {
                    if (entry.PasteContext.ImageData != null)
                        pasteType = "image";
                    else if (entry.PasteContext.HasConvertedMarkdown)
                        pasteType = "html";
                    else if (entry.PasteContext.HasPlainTextContent)
                        pasteType = "text";
                }

                var eventData = new EventData
                {
                    EntryId = entry.Id.ToString(),
                    EntryType = entry.Type?.ToString(),
                    Route = entry.Projects.FirstOrDefault(),
                    TagsJson = entry.Tags.Any() ? System.Text.Json.JsonSerializer.Serialize(entry.Tags) : null,
                    ProjectsJson = entry.Projects.Any() ? System.Text.Json.JsonSerializer.Serialize(entry.Projects) : null,
                    HasPaste = entry.PasteContext != null,
                    PasteType = pasteType,
                    HasDeadline = entry.Deadline != null,
                    ContentLength = entry.RawContent.Length,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        context = entry.Context,
                        vault_path = relativePath,
                        deadline_text = entry.Deadline?.OriginalText
                    })
                };

                _eventLogger.LogEvent(EventType.EntryCreated, eventData);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Failed to log EntryCreated event: {ex.Message}");
                // Don't throw - event logging is best-effort
            }
        }

        return (relativePath, aggregateWrite);
    }

    private async Task<string> SaveUnroutedEntry(Entry entry)
    {
        var relativePath = GetYearlyFilePath(entry);
        var fullPath = Path.Combine(_vaultPath, relativePath);

        // Ensure file exists (creates directory + empty file if needed)
        if (_vaultWriter != null)
        {
            _vaultWriter.EnsureFile(fullPath);
        }
        else
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (directory != null)
                Directory.CreateDirectory(directory);

            if (!File.Exists(fullPath))
                await File.WriteAllTextAsync(fullPath, string.Empty, Encoding.UTF8);
        }

        // Append entry (VaultWriter notifies EntryCache automatically)
        await AppendEntry(fullPath, entry);

        return relativePath;
    }

    /// <summary>
    /// Creates a subpage for complex paste content and replaces entry content with a link.
    /// Returns the VaultWriteResult from the subpage gate write.
    /// </summary>
    private async Task<VaultWriteResult> CreateSubpageAndReplaceContentAsync(Entry entry, string? routePath, Route? route)
    {
        var content = entry.PasteContext!.SubpageContent!;

        // Extract title and preview from content
        var lines = content.Split('\n');
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        // First non-empty line is title (strip markdown heading markers)
        var firstLine = nonEmptyLines.FirstOrDefault()?.Trim() ?? "Clip";
        var title = firstLine.TrimStart('#', ' ').Trim();

        // Find first content paragraph for preview (skip title, URLs, links, images)
        var preview = nonEmptyLines
            .Skip(1)
            .FirstOrDefault(l =>
                !l.Trim().StartsWith("http") &&
                !l.Trim().StartsWith("[") &&
                !l.Trim().StartsWith("!") &&
                l.Trim().Length > 10)
            ?.Trim() ?? "";

        // Truncate preview to ~100 chars
        if (preview.Length > 100)
            preview = preview.Substring(0, 100).TrimEnd() + "...";

        // Sanitize title for filename
        var safeTitle = string.Join("", title.Take(50)
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-'))
            .Trim()
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (string.IsNullOrEmpty(safeTitle))
            safeTitle = "clip";

        // Get entry type folder name from entry (already parsed)
        var typeFolderName = EntryTypeMapper.GetFolderName(entry.Type);

        // Generate path: [route]/[type]/pages/ or [type]/pages/
        var fileName = $"{entry.Timestamp:yyyy-MM-dd}-{safeTitle}.md";

        string pagesFolder;
        string relativeFolderPath;

        if (!string.IsNullOrEmpty(routePath))
        {
            // Routed: logmark/[route]/_[type]/pages/
            relativeFolderPath = PathConstants.GetRoutedPagesFolder(routePath, typeFolderName);
            pagesFolder = Path.Combine(_vaultPath, relativeFolderPath);
        }
        else
        {
            // Unrouted: logmark/_[type]/pages/
            relativeFolderPath = PathConstants.GetUnroutedPagesFolder(typeFolderName);
            pagesFolder = Path.Combine(_vaultPath, relativeFolderPath);
        }

        var subpagePath = Path.Combine(pagesFolder, fileName);

        // Build subpage content with source attribution
        var sb = new StringBuilder();
        sb.AppendLine(content);

        if (entry.PasteContext?.HasSourceUrl == true)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"Source: {entry.PasteContext.SourceUrl}");
        }

        // Write subpage through the gate (masks content on private routes)
        VaultWriteResult subpageWrite;
        if (_writeGate != null)
        {
            subpageWrite = await _writeGate.WriteSubpageAsync(subpagePath, sb.ToString(), route).ConfigureAwait(false);
        }
        else if (_vaultWriter != null)
        {
            _vaultWriter.EnsureAndWrite(subpagePath, sb.ToString());
            subpageWrite = VaultWriteResult.Clean;
        }
        else
        {
            Directory.CreateDirectory(pagesFolder);
            File.WriteAllText(subpagePath, sb.ToString());
            subpageWrite = VaultWriteResult.Clean;
        }

        _logger?.Info($"[paste-diag] subpage created: {subpagePath} (subpageContentLen={content.Length})");

        // Build link with preview for diary entry
        var relativeFilePath = Path.Combine(relativeFolderPath, Path.GetFileNameWithoutExtension(fileName));
        // Strip ] from title to avoid breaking wiki link syntax ([[path|title]])
        var safeDisplayTitle = title.Replace("]", "");
        var linkLine = $"[[{relativeFilePath.Replace('\\', '/')}|{safeDisplayTitle}]]";

        // Replace entry content with link + preview (preserve user's typed text)
        // Strip notation from user prefix to get descriptive text (e.g., "t: read by friday " -> "read by friday ")
        var userPrefix = entry.PasteContext?.UserPrefix ?? "";
        var strippedPrefix = FileRouter.StripNotation(userPrefix, entry.Type);
        var userSuffix = entry.PasteContext?.UserSuffix ?? "";

        // Build content: [stripped prefix][link]\n[preview][suffix]
        var contentBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(strippedPrefix))
        {
            contentBuilder.Append(strippedPrefix);
            if (!strippedPrefix.EndsWith(" "))
                contentBuilder.Append(' ');
        }
        contentBuilder.Append(linkLine);
        if (!string.IsNullOrEmpty(preview))
        {
            contentBuilder.Append('\n');
            contentBuilder.Append(preview);
        }
        if (!string.IsNullOrEmpty(userSuffix))
        {
            contentBuilder.Append(userSuffix);
        }

        entry.RawContent = contentBuilder.ToString();
        entry.ExpandedContent = entry.RawContent;

        if (_routeService?.EntryTargetsPrivateRoute(entry) == true)
        {
            _logger?.Info($"[paste-diag] subpage entry rebuilt for private route: userPrefix present={!string.IsNullOrEmpty(userPrefix)} suffix present={!string.IsNullOrEmpty(userSuffix)} contentLen={entry.RawContent.Length}");
        }
        else
        {
            // Defense-in-depth: even on the "public-route" branch, route the user-content fields through
            // ScrubForLog so a misclassified entry (or one whose +token only becomes private later) cannot
            // leak plaintext into _system/log.md. See H4b in docs/plans/2026-05-18-v018-qa-findings-investigation.md.
            _logger?.Info($"[paste-diag] subpage entry rebuilt: userPrefix='{_routeService?.ScrubForLog(userPrefix, 160) ?? userPrefix}' strippedPrefix='{_routeService?.ScrubForLog(strippedPrefix, 160) ?? strippedPrefix}' preview='{_routeService?.ScrubForLog(preview, 40) ?? (preview.Length > 40 ? preview.Substring(0, 40) + "..." : preview)}' userSuffix='{_routeService?.ScrubForLog(userSuffix, 160) ?? userSuffix}' rawContent='{_routeService?.ScrubForLog(entry.RawContent, 160) ?? (entry.RawContent.Length > 160 ? entry.RawContent.Substring(0, 160) + "..." : entry.RawContent)}'");
        }

        return subpageWrite;
    }
}
