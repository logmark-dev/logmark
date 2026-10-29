using LogMark.Interfaces;
using LogMark.Models;

namespace LogMark.Services;

using File = System.IO.File;
using Path = System.IO.Path;

/// <summary>
/// Handles task mutations in markdown files (complete, postpone).
/// All query operations are handled by EntryCache.
/// </summary>
public class TaskTracker
{
    private readonly string _vaultPath;
    private readonly SystemLogger? _logger;
    private readonly EventLogger? _eventLogger;
    private readonly IVaultFileWriter? _vaultWriter;
    private readonly ITimeProvider? _timeProvider;

    public TaskTracker(string vaultPath, SystemLogger? logger = null, EventLogger? eventLogger = null, IVaultFileWriter? vaultWriter = null, ITimeProvider? timeProvider = null)
    {
        _vaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
        _logger = logger;
        _eventLogger = eventLogger;
        _vaultWriter = vaultWriter;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Generate short unique ID for tasks (8 hex chars = 32 bits)
    /// Format: a3f2c8d1
    /// </summary>
    public static string GenerateShortId()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    /// <summary>
    /// Append short ID to task content
    /// Format: "content [shortid]"
    /// </summary>
    public static string AppendShortId(string content, string shortId)
    {
        return $"{content} [{shortId}]";
    }

    /// <summary>
    /// Extract short ID from task content
    /// Returns (content-without-id, short-id) or (original-content, null) if no ID found
    /// </summary>
    public static (string content, string? shortId) ExtractShortId(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(content, @"^(.+?)\s+\[([a-f0-9]{8})\]$");
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value);
        }
        return (content, null);
    }

    private static readonly System.Text.RegularExpressions.Regex CollapseNewlinesPattern =
        new(@"(?:\r\n|\r|\n)+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The "resolved: &lt;note&gt;" trailer is serialized as a single markdown
    /// line - a raw note containing embedded newlines would otherwise be written
    /// verbatim and read back as a spurious "## " entry header (or other
    /// structural line) by EntryParser on the next load. Collapse any run of
    /// \r\n / \n / \r into one space and trim, degrading gracefully rather than
    /// rejecting the note. Shared by both task-resolution call sites below
    /// (completion and post-hoc update).
    /// </summary>
    private static string NormalizeResolutionNote(string note) =>
        CollapseNewlinesPattern.Replace(note, " ").Trim();

    /// <summary>
    /// Complete a task by adding strikethrough to its markdown content and metadata.
    /// </summary>
    /// <summary>
    /// Strikes the task through in markdown. Returns true only when the file
    /// was actually rewritten - a miss (no metadata line carries the id, or it
    /// is already struck through) returns false rather than reporting a silent
    /// success. Callers MUST surface a false: reporting completion that did not
    /// happen is what let ~91% of the dogfooding backlog refuse to close for
    /// months while every layer above claimed it had.
    /// </summary>
    public bool CompleteTask(string filePath, string shortId, DateTime completedAt, string? resolution = null)
    {
        var absolutePath = ResolveFilePath(filePath);
        if (absolutePath == null || !File.Exists(absolutePath))
        {
            _logger?.Warn($"CompleteTask: File not found: {filePath}");
            return false;
        }

        if (!UpdateMarkdownOnComplete(absolutePath, shortId, resolution))
        {
            _logger?.Warn($"CompleteTask: no entry matching {shortId} in {filePath}");
            return false;
        }

        // Log TaskCompleted event
        if (_eventLogger != null)
        {
            try
            {
                var eventData = new EventData
                {
                    EntryId = shortId,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        short_id = shortId,
                        completed_via = "command"
                    })
                };

                _eventLogger.LogEvent(EventType.TaskCompleted, eventData);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Failed to log TaskCompleted event: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// Update or remove resolution for an already-completed task.
    /// </summary>
    /// <param name="filePath">Path to the markdown file (absolute or vault-relative)</param>
    /// <param name="shortId">Task short ID (8 hex chars)</param>
    /// <param name="resolution">New resolution text, or null to remove existing resolution</param>
    /// <returns>True if the resolution was updated, false if task not found</returns>
    public bool UpdateTaskResolution(string filePath, string shortId, string? resolution)
    {
        var absolutePath = ResolveFilePath(filePath);
        if (absolutePath == null || !File.Exists(absolutePath))
        {
            _logger?.Warn($"UpdateTaskResolution: File not found: {filePath}");
            return false;
        }

        return UpdateResolutionInMarkdown(absolutePath, shortId, resolution);
    }

    private bool UpdateResolutionInMarkdown(string filePath, string shortId, string? resolution)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(resolution))
                resolution = NormalizeResolutionNote(resolution);

            var content = File.ReadAllText(filePath);
            var lines = content.Split('\n').ToList();

            // Find the task metadata line with the shortId
            int metadataLineIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                // Match both regular and struck-through metadata lines
                if (trimmed.Contains(shortId) &&
                    (trimmed.Contains("_task:") || trimmed.Contains("~~_task:")))
                {
                    metadataLineIndex = i;
                    break;
                }
            }

            if (metadataLineIndex < 0)
            {
                _logger?.Warn($"UpdateResolutionInMarkdown: Task {shortId} not found in file");
                return false;
            }

            // Check if next line is a resolution line
            var hasExistingResolution = metadataLineIndex + 1 < lines.Count &&
                lines[metadataLineIndex + 1].Trim().StartsWith("resolved:", StringComparison.OrdinalIgnoreCase);

            if (hasExistingResolution)
            {
                if (!string.IsNullOrWhiteSpace(resolution))
                {
                    // Update existing resolution
                    lines[metadataLineIndex + 1] = $"resolved: {resolution}";
                }
                else
                {
                    // Remove resolution line
                    lines.RemoveAt(metadataLineIndex + 1);
                }
            }
            else if (!string.IsNullOrWhiteSpace(resolution))
            {
                // Add new resolution line after metadata
                lines.Insert(metadataLineIndex + 1, $"resolved: {resolution}");
            }
            else
            {
                // No existing resolution and no new resolution - nothing to do
                return true;
            }

            if (_vaultWriter != null)
                _vaultWriter.WriteFile(filePath, string.Join('\n', lines));
            else
                File.WriteAllText(filePath, string.Join('\n', lines));
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error($"UpdateResolutionInMarkdown: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Postpone a task by updating its deadline in the markdown file.
    /// </summary>
    public void PostponeTask(string filePath, string shortId, DateTime currentDeadline, int postponeCount, int days)
    {
        var absolutePath = ResolveFilePath(filePath);
        if (absolutePath == null || !File.Exists(absolutePath))
        {
            _logger?.Warn($"PostponeTask: File not found: {filePath}");
            return;
        }

        // If the current deadline is already in the past, anchor to today (preserving the
        // original time-of-day) before adding days. Otherwise postponing a stale task by 1 day
        // leaves it in the past, so it re-surfaces as overdue on every restart.
        var now = _timeProvider?.Now ?? DateTime.Now;
        var baseTime = currentDeadline < now
            ? now.Date.Add(currentDeadline.TimeOfDay)
            : currentDeadline;
        var newDeadline = baseTime.AddDays(days);
        var newCount = postponeCount + 1;

        _logger?.Info($"PostponeTask: {shortId} from {currentDeadline:yyyy-MM-dd HH:mm} to {newDeadline:yyyy-MM-dd HH:mm}");

        var markdownUpdated = UpdateDeadlineInMarkdown(absolutePath, shortId, newDeadline, newCount);
        _logger?.Info($"PostponeTask: Markdown update result: {(markdownUpdated ? "success" : "FAILED")}");

        // Log TaskPostponed event
        if (_eventLogger != null)
        {
            try
            {
                var eventData = new EventData
                {
                    EntryId = shortId,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        short_id = shortId,
                        old_deadline = currentDeadline.ToString("O"),
                        new_deadline = newDeadline.ToString("O"),
                        days_added = days,
                        postpone_count = newCount
                    })
                };

                _eventLogger.LogEvent(EventType.TaskPostponed, eventData);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Failed to log TaskPostponed event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Returns true when the file was rewritten. Matches either metadata
    /// grammar: "_task: ... | {id}" written by FileRouter for interactively
    /// captured tasks, and "_id: {id}" written by session distill and the
    /// anchor mint sweep. Matching only the former was the 2026-07-27 P0.
    /// </summary>
    private bool UpdateMarkdownOnComplete(string filePath, string shortId, string? resolution = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(resolution))
                resolution = NormalizeResolutionNote(resolution);

            var content = File.ReadAllText(filePath);
            var lines = content.Split('\n').ToList();
            bool modified = false;
            int insertResolutionAfter = -1;

            // Find metadata line with shortId, then strikethrough both content and metadata lines
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();

                // Look for a metadata line carrying the shortId, in either grammar.
                if ((trimmed.StartsWith("_task:") || trimmed.StartsWith("_id:"))
                    && trimmed.EndsWith(shortId) && !trimmed.StartsWith("~~"))
                {
                    // Found metadata line - strikethrough it and the content line before it
                    var leadingWhitespace = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                    lines[i] = $"{leadingWhitespace}~~{trimmed}~~";
                    insertResolutionAfter = i;

                    // Find and strikethrough the content line (should be just before metadata)
                    if (i > 0)
                    {
                        var contentLine = lines[i - 1];
                        var contentTrimmed = contentLine.Trim();
                        if (!string.IsNullOrWhiteSpace(contentTrimmed) && !contentTrimmed.StartsWith("~~") && !contentTrimmed.StartsWith("##"))
                        {
                            var contentLeading = contentLine.Substring(0, contentLine.Length - contentLine.TrimStart().Length);
                            lines[i - 1] = $"{contentLeading}~~{contentTrimmed}~~";
                        }
                    }

                    modified = true;
                    break;
                }
            }

            // Fallback: try old format with inline [shortId]
            if (!modified)
            {
                var shortIdPattern = $"[{shortId}]";

                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var trimmed = line.Trim();

                    // Look for line containing the short ID that isn't already struck through
                    if (trimmed.Contains(shortIdPattern, StringComparison.Ordinal) &&
                        !trimmed.StartsWith("~~"))
                    {
                        var leadingWhitespace = line.Substring(0, line.Length - line.TrimStart().Length);
                        lines[i] = $"{leadingWhitespace}~~{trimmed}~~";
                        insertResolutionAfter = i;
                        modified = true;
                        break;
                    }
                }
            }

            // Insert resolution line after metadata if provided
            if (modified && !string.IsNullOrWhiteSpace(resolution) && insertResolutionAfter >= 0)
            {
                lines.Insert(insertResolutionAfter + 1, $"resolved: {resolution}");
            }

            if (modified)
            {
                if (_vaultWriter != null)
                    _vaultWriter.WriteFile(filePath, string.Join('\n', lines));
                else
                    File.WriteAllText(filePath, string.Join('\n', lines));
            }

            return modified;
        }
        catch (Exception ex)
        {
            _logger?.Error($"UpdateMarkdownOnComplete: {ex.Message}");
            return false;
        }
    }

    private bool UpdateDeadlineInMarkdown(string filePath, string shortId, DateTime newDeadline, int newPostponeCount)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            var lines = content.Split('\n');
            bool modified = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmedLine = lines[i].Trim();

                if (trimmedLine.StartsWith("_task:") && trimmedLine.EndsWith(shortId))
                {
                    var newDeadlineText = FormatDeadline(newDeadline);
                    // The block anchor is not decoration: it is what gives the
                    // entry a persisted, addressable id, and an entry without one
                    // silently refuses every later mutation. The guard above is
                    // EndsWith(shortId), which an ALREADY-ANCHORED line satisfies
                    // (it ends with the anchor's own id), so omitting the anchor
                    // here did not merely fail to add one - it stripped the one
                    // that was there, on the first postpone. Measured on the
                    // dogfooding vault: 13 of the 14 anchorless entries had been
                    // postponed at least once. Written unconditionally, which also
                    // makes postpone self-healing for the entries already damaged;
                    // the id is reused, so nothing referring to them breaks.
                    var newMetadataLine = $"_task: {newDeadlineText} | postponed: {newPostponeCount} | {shortId} ^{shortId}";

                    var leadingWhitespace = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                    lines[i] = leadingWhitespace + newMetadataLine;

                    _logger?.Debug($"UpdateDeadlineInMarkdown: Updated to: {newMetadataLine}");
                    modified = true;
                    break;
                }
            }

            // Fallback: try old format with inline [shortId]
            if (!modified)
            {
                var shortIdPattern = $"[{shortId}]";

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    if (line.Contains(shortIdPattern, StringComparison.Ordinal))
                    {
                        var newDeadlineText = FormatDeadline(newDeadline);
                        var originalLine = lines[i];
                        lines[i] = ReplaceTemporalPattern(lines[i], newDeadlineText);

                        if (originalLine != lines[i])
                        {
                            modified = true;
                        }
                        break;
                    }
                }
            }

            if (!modified)
            {
                _logger?.Warn($"UpdateDeadlineInMarkdown: ShortId {shortId} not found in file");
                return false;
            }

            if (_vaultWriter != null)
                _vaultWriter.WriteFile(filePath, string.Join('\n', lines));
            else
                File.WriteAllText(filePath, string.Join('\n', lines));
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error($"UpdateDeadlineInMarkdown: Exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Patches a resolved deadline into a task whose _task: line was saved with
    /// "none". The non-blocking capture path (lmk-e4yi) writes the entry
    /// immediately, then runs the LLM deadline fallback off the capture thread;
    /// when that finds a deadline this fills it into the already-saved entry.
    /// Preserves the postpone count and the trailing "^shortId" Obsidian block
    /// anchor, and only rewrites a line whose deadline is still "none" so it never
    /// clobbers a deadline set in the interim. Returns true if a line was patched.
    /// </summary>
    public bool SetDeadline(string filePath, string shortId, DateTime deadline)
    {
        try
        {
            var absolutePath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(_vaultPath, filePath);
            var lines = File.ReadAllText(absolutePath).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("_task:") || !trimmed.Contains(shortId))
                    continue;

                // Current deadline token sits between "_task:" and the first "|".
                var afterPrefix = trimmed.Substring("_task:".Length);
                var firstPipe = afterPrefix.IndexOf('|');
                var current = (firstPipe >= 0 ? afterPrefix[..firstPipe] : afterPrefix).Trim();

                // Only fill a "none" line; never overwrite an existing deadline.
                if (!current.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return false;

                var postponeMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"postponed:\s*(\d+)");
                var postponeCount = postponeMatch.Success ? postponeMatch.Groups[1].Value : "0";

                var isoFormat = deadline.ToString("yyyy-MM-dd, HH:mm");
                var leadingWhitespace = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                lines[i] = $"{leadingWhitespace}_task: {isoFormat} | postponed: {postponeCount} | {shortId} ^{shortId}";

                if (_vaultWriter != null)
                    _vaultWriter.WriteFile(absolutePath, string.Join('\n', lines));
                else
                    File.WriteAllText(absolutePath, string.Join('\n', lines));

                _logger?.Debug($"SetDeadline: {shortId} -> {isoFormat}");
                return true;
            }

            _logger?.Warn($"SetDeadline: ShortId {shortId} not found in {filePath}");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error($"SetDeadline: Exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parse task metadata line format: _task: YYYY-MM-DD, HH:mm | postponed: N | shortid
    /// Also handles: _task: none | postponed: N | shortid
    /// </summary>
    public static (Deadline? deadline, int postponeCount, string shortId)? ParseTaskMetadataLine(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line,
            @"^_task:\s*(none|\d{4}-\d{2}-\d{2}(?:,\s*\d{2}:\d{2})?)\s*\|\s*postponed:\s*(\d+)\s*\|\s*([a-f0-9]{8})$");

        if (!match.Success)
            return null;

        var deadlineStr = match.Groups[1].Value;
        var postponeStr = match.Groups[2].Value;
        var shortId = match.Groups[3].Value;

        int.TryParse(postponeStr, out var postponeCount);

        if (deadlineStr.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return (null, postponeCount, shortId);
        }

        if (!DateTime.TryParse(deadlineStr.Replace(",", ""), out var dateTime))
            return null;

        var deadline = new Deadline
        {
            DateTime = dateTime,
            OriginalText = deadlineStr
        };

        return (deadline, postponeCount, shortId);
    }

    /// <summary>
    /// Set, change, or CLEAR a task's deadline, overwriting whatever is there.
    /// Pass null to clear it (the "_task:" grammar spells that "none").
    ///
    /// The gap this fills (lmk-0hp3): every other deadline path is one-way.
    /// <see cref="SetDeadline"/> deliberately patches only a line still reading
    /// "none" so the async capture fallback cannot clobber a deadline set in the
    /// interim, and <see cref="PostponeTask"/> can only push a deadline further
    /// out. So a task that acquired a WRONG deadline could not be corrected at
    /// all - only dismissed and recreated, losing its id and its history. That
    /// is not hypothetical: the temporal recognizer read "leave the cap at 20"
    /// as eight in the evening (lmk-ylg7), and three tasks were churned working
    /// around the missing correction before the parser was fixed.
    ///
    /// Preserves the postpone count (it records history, not scheduling) and
    /// writes the "^shortId" block anchor unconditionally, which also mints one
    /// back for entries damaged before the lmk-si5j fix.
    ///
    /// Only rewrites the "_task:" grammar. Tasks written by session distill carry
    /// an "_id:" line with no deadline field, and this returns false for them
    /// rather than reporting a success that changed nothing.
    /// </summary>
    /// <returns>True only if a line was actually rewritten.</returns>
    public bool ChangeDeadline(string filePath, string shortId, DateTime? deadline)
    {
        try
        {
            var absolutePath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(_vaultPath, filePath);
            if (!File.Exists(absolutePath))
            {
                _logger?.Warn($"ChangeDeadline: file not found: {filePath}");
                return false;
            }

            var lines = File.ReadAllText(absolutePath).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("_task:") || !trimmed.Contains(shortId))
                    continue;

                var postponeMatch = System.Text.RegularExpressions.Regex.Match(
                    trimmed, @"postponed:\s*(\d+)");
                var postponeCount = postponeMatch.Success ? postponeMatch.Groups[1].Value : "0";

                var deadlineText = deadline is DateTime d ? FormatDeadline(d) : "none";
                var leading = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                lines[i] = $"{leading}_task: {deadlineText} | postponed: {postponeCount} | {shortId} ^{shortId}";

                if (_vaultWriter != null)
                    _vaultWriter.WriteFile(absolutePath, string.Join('\n', lines));
                else
                    File.WriteAllText(absolutePath, string.Join('\n', lines));

                _logger?.Info($"ChangeDeadline: {shortId} -> {deadlineText}");
                return true;
            }

            _logger?.Warn($"ChangeDeadline: no _task: line for {shortId} in {filePath}");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error($"ChangeDeadline: {ex.Message}");
            return false;
        }
    }

    private string FormatDeadline(DateTime deadline)
    {
        return deadline.ToString("yyyy-MM-dd, HH:mm");
    }

    private string ReplaceTemporalPattern(string line, string newDeadline)
    {
        // First, try to match augmented format: "original (YYYY-MM-DD)" or "original (YYYY-MM-DD, HH:mm)"
        var augmentedPattern = @"\((\d{4}-\d{2}-\d{2}(?:,\s*\d{2}:\d{2})?)\)";
        var augmentedRegex = new System.Text.RegularExpressions.Regex(augmentedPattern);

        if (augmentedRegex.IsMatch(line))
        {
            return augmentedRegex.Replace(line, $"({newDeadline})", 1);
        }

        // Fallback: Legacy patterns for old format
        var patterns = new[]
        {
            @"\d{4}-\d{2}-\d{2},?\s*\d{2}:\d{2}",
            @"\d{4}-\d{2}-\d{2}",
            @"\d{1,2}:\d{2}\s*(?:am|pm)",
            @"\d{1,2}\s*(?:am|pm)",
            @"\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
            @"\b(?:today|tomorrow|yesterday)\b",
            @"\b(?:eod|cob)\b"
        };

        foreach (var pattern in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (regex.IsMatch(line))
            {
                line = regex.Replace(line, newDeadline, 1);
                break;
            }
        }

        return line;
    }

    /// <summary>
    /// Convert Windows paths to WSL paths when running on Linux
    /// </summary>
    private static string NormalizePathForPlatform(string path)
    {
        if (Environment.OSVersion.Platform == PlatformID.Unix && path.Length >= 3 && path[1] == ':')
        {
            var drive = char.ToLower(path[0]);
            var restOfPath = path.Substring(2).Replace('\\', '/');
            return $"/mnt/{drive}{restOfPath}";
        }

        return path;
    }

    private string? ResolveFilePath(string filePath)
    {
        // If already absolute, normalize and return
        if (Path.IsPathRooted(filePath))
            return NormalizePathForPlatform(filePath);

        // Vault-relative path - resolve to absolute
        var absolutePath = Path.Combine(_vaultPath, filePath);
        return NormalizePathForPlatform(absolutePath);
    }
}
