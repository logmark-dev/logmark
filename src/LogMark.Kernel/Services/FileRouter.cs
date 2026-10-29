using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Models.Sensitivity;
using LogMark.Services.Sensitivity;

namespace LogMark.Services;

/// <summary>
/// Handles physical routing of entries to destination files
/// Creates directories, files, and appends entries
/// </summary>
public class FileRouter
{
    private readonly string _vaultPath;
    private readonly SystemLogger _logger;
    private readonly EntryTypeMapper _mapper;
    private readonly IVaultFileWriter _vaultWriter;
    private readonly ISensitivityService? _sensitivity;
    private IVaultWriteGate? _writeGate;

    public FileRouter(string vaultPath, SystemLogger logger, IVaultFileWriter vaultWriter,
                      ISensitivityService? sensitivity = null,
                      IVaultWriteGate? writeGate = null)
    {
        _vaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vaultWriter = vaultWriter ?? throw new ArgumentNullException(nameof(vaultWriter));
        _mapper = new EntryTypeMapper();
        _sensitivity = sensitivity;
        _writeGate = writeGate;
    }

    /// <summary>
    /// Late-inject the write gate after construction.
    /// Called by RouteService.SetWriteGate to eliminate two-phase construction.
    /// </summary>
    public void SetWriteGate(IVaultWriteGate gate)
    {
        _writeGate = gate;
    }

    /// <summary>
    /// Route entry to destination file (async; handles private-route masking).
    /// Creates file if needed, appends entry.
    /// When route.Private is set and a SensitivityService is injected, the content
    /// written to THIS destination is the masked version only -- entry.RawContent is
    /// never mutated, so other destinations in the same routing pass get plaintext.
    /// Returns the relative destination path and the VaultWriteResult from the gate.
    /// </summary>
    public async Task<(string Path, VaultWriteResult Write)> RouteEntryAsync(Entry entry, Route route)
    {
        if (string.IsNullOrWhiteSpace(route.Path))
            throw new InvalidOperationException($"Cannot route to reference-only route: {route.Name}");

        if (entry.Type == null)
            throw new InvalidOperationException("Cannot route entry without type");

        var mapping = _mapper.GetMapping(entry.Type);
        if (mapping == null)
            throw new InvalidOperationException($"No mapping for entry type: {entry.Type}");

        var year = entry.Timestamp.Year;
        var typeName = mapping.TypeName;

        // Build yearly path: logmark/[route]/_[type]/YYYY.md
        var typeFolder = PathConstants.GetRoutedTypeFolder(route.Path, typeName);
        var destFolder = Path.Combine(_vaultPath, typeFolder);
        var destFile = Path.Combine(destFolder, $"{year}.md");

        // Create folder on-demand
        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
            _logger.Debug($"Created: {typeFolder}");
        }

        // Create file if doesn't exist (no frontmatter - YAGNI)
        if (!File.Exists(destFile))
        {
            _vaultWriter.EnsureFile(destFile);
            _logger.Debug($"Created: {PathConstants.GetRoutedFilePath(route.Path, typeName, year)}");
        }

        // Determine per-destination content: mask if private, plaintext otherwise.
        // entry.RawContent is NOT mutated -- override is scoped to this write only.
        VaultWriteResult writeResult;
        if (_writeGate is not null)
        {
            writeResult = await _writeGate.WriteEntryAsync(destFile, entry, route).ConfigureAwait(false);
        }
        else
        {
            // Fallback path for contexts where no gate is wired (unit tests,
            // legacy callers). Retains the previous behavior exactly.
            string? overrideContent = null;
            if (route.Private is not null && _sensitivity is not null)
            {
                var masked = await _sensitivity.MaskAndRegisterAsync(entry.RawContent, route).ConfigureAwait(false);
                overrideContent = masked.MaskedText;
            }
            AppendEntry(destFile, entry, overrideContent);
            writeResult = VaultWriteResult.Clean;
        }

        var relativePath = PathConstants.GetRoutedFilePath(route.Path, typeName, year);
        _logger.Info($"Route: +{route.Name} {entry.Type?.ToString().ToLowerInvariant()}: -> {relativePath}");

        return (relativePath, writeResult);
    }

    /// <summary>
    /// Synchronous wrapper for RouteEntryAsync. Used by tests and sync callers.
    /// Returns only the path; discards the VaultWriteResult for backward compatibility.
    /// </summary>
    public string RouteEntry(Entry entry, Route route)
        => RouteEntryAsync(entry, route).GetAwaiter().GetResult().Path;

    private void AppendEntry(string filePath, Entry entry, string? overrideContent = null)
    {
        var sb = new StringBuilder();

        // Check if file has content - if so, add blank line before next entry
        var fileContent = File.Exists(filePath) ? File.ReadAllText(filePath).Trim('\uFEFF') : "";
        var fileHasContent = !string.IsNullOrWhiteSpace(fileContent);
        if (fileHasContent)
        {
            sb.AppendLine();
        }

        // Header with inline context
        var header = $"## {entry.Timestamp:yyyy-MM-dd HH:mm}";
        if (!string.IsNullOrWhiteSpace(entry.Context))
        {
            var truncatedContext = TruncateContext(entry.Context);
            header += $" | {truncatedContext}";
        }
        sb.AppendLine(header);

        // Strip all notation from content (temporal expressions preserved as-is).
        // When overrideContent is set (private route), strip from the masked text instead.
        var rawForStrip = overrideContent ?? entry.RawContent;
        var content = StripNotation(rawForStrip, entry.Type, entry.ProseRouteTokens);
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

        // Add source attribution (for agent entries)
        if (!string.IsNullOrEmpty(entry.Source))
        {
            sb.AppendLine($"source: {entry.Source}");
        }

        // Add session reference (for session tracking)
        if (!string.IsNullOrEmpty(entry.Session))
        {
            sb.AppendLine($"session: {entry.Session}");
        }

        _vaultWriter.AppendText(filePath, sb.ToString());
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
    /// Strip notation from content (entry types, projects)
    /// Preserves tags (#tag) for Obsidian search/linking
    /// </summary>
    public static string StripNotation(string content, EntryType? entryType)
        => StripNotation(content, entryType, Array.Empty<string>());

    /// <summary>
    /// Strip notation, keeping the +words in <paramref name="proseTokens"/>
    /// verbatim. Writers pass Entry.ProseRouteTokens.
    /// </summary>
    public static string StripNotation(string content, EntryType? entryType, IReadOnlyCollection<string> proseTokens)
    {
        // Strip +project references first so a route token before the type
        // prefix (e.g. "+travel i:foo") doesn't shadow the start-of-body
        // anchor we use for the type-prefix strip below.
        content = NotationParser.StripProjects(content, proseTokens);

        // Strip the entry-type prefix at the start of the body. Only consider
        // prefixes that match the entry's type — otherwise an entry whose body
        // references another type (e.g. a Question whose body cites "idea:abc"
        // ref-tokens) would have those refs stripped while the leading "q:"
        // survives, which is the exact shape of the q:/g:/a: artefacts seen
        // in the dev vault.
        if (entryType != null)
        {
            var prefixesForType = GetTypePrefixes(entryType.Value);
            // Allow horizontal leading whitespace (preserve newlines so we
            // don't accidentally consume a leading blank line).
            var leading = 0;
            while (leading < content.Length && content[leading] != '\n' && char.IsWhiteSpace(content[leading]))
                leading++;

            foreach (var prefix in prefixesForType)
            {
                if (content.Length - leading >= prefix.Length &&
                    string.Compare(content, leading, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var afterPrefix = content[(leading + prefix.Length)..];
                    // Trim a single trailing space after the prefix (the canonical "q: foo" -> "foo" form).
                    if (afterPrefix.Length > 0 && afterPrefix[0] == ' ')
                        afterPrefix = afterPrefix[1..];
                    content = afterPrefix;
                    break;
                }
            }
        }

        // Preserve tags: #tag (keep for Obsidian search/linking)
        // Tags are NOT stripped - they remain visible in markdown

        // Clean up multiple spaces (preserve newlines)
        content = Regex.Replace(content, @"[^\S\n]+", " "); // Collapse horizontal whitespace only
        content = Regex.Replace(content, @"\n{3,}", "\n\n"); // Max 2 consecutive newlines
        content = content.Trim();

        return content;
    }

    // Canonical form first so "idea: foo" matches "idea:" before any shorthand check.
    // Returned prefixes are checked at the start of the body only.
    private static string[] GetTypePrefixes(EntryType type) => type switch
    {
        EntryType.Idea      => new[] { "idea:", "i:" },
        EntryType.Task      => new[] { "task:", "t:" },
        EntryType.Block     => new[] { "block:", "b:" },
        EntryType.Decision  => new[] { "decision:", "d:" },
        EntryType.Timetrack => new[] { "timetrack:", "tt:" },
        EntryType.Question  => new[] { "question:", "q:" },
        EntryType.Gap       => new[] { "gap:", "g:" },
        EntryType.Analogy   => new[] { "analogy:", "a:" },
        _                   => Array.Empty<string>()
    };
}
