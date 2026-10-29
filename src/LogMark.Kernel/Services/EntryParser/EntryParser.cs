using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services.EntryParser;

/// <summary>
/// Parses markdown files containing LogMark entries.
/// Handles diary, tasks, ideas, blocks, decisions, workflow, questions, gaps, and analogy entries.
/// </summary>
public static partial class EntryParser
{
    /// <summary>
    /// Parse all entries from markdown content.
    /// </summary>
    /// <param name="markdown">The markdown content to parse</param>
    /// <param name="typeHint">Optional type hint from file path (e.g., _tasks folder implies Task type)</param>
    /// <returns>List of parsed entries</returns>
    public static List<ParsedEntry> Parse(string markdown, EntryType? typeHint = null)
    {
        var entries = new List<ParsedEntry>();

        if (string.IsNullOrWhiteSpace(markdown))
            return entries;

        var lines = markdown.Split('\n');
        var currentEntry = new EntryBuilder();
        var inEntry = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var lineNumber = i + 1; // 1-based
            var trimmed = line.Trim();

            // Check for entry header: ## YYYY-MM-DD HH:mm or ## YYYY-MM-DD HH:mm | context
            var headerMatch = HeaderPattern().Match(line);
            if (headerMatch.Success)
            {
                // Finalize previous entry if any
                if (inEntry)
                {
                    var entry = currentEntry.Build(typeHint);
                    if (entry != null)
                        entries.Add(entry);
                }

                // Start new entry
                currentEntry = new EntryBuilder
                {
                    StartLine = lineNumber
                };

                // Parse timestamp
                var dateStr = headerMatch.Groups[1].Value;
                var timeStr = headerMatch.Groups[2].Value;
                if (DateTime.TryParse($"{dateStr} {timeStr}", out var timestamp))
                {
                    currentEntry.Timestamp = timestamp;
                }

                // Parse context if present
                if (headerMatch.Groups[3].Success && !string.IsNullOrEmpty(headerMatch.Groups[3].Value))
                {
                    currentEntry.Context = headerMatch.Groups[3].Value.Trim();
                }

                currentEntry.EndLine = lineNumber;
                inEntry = true;
                continue;
            }

            if (!inEntry)
                continue;

            // Update end line
            currentEntry.EndLine = lineNumber;

            // Skip separators
            if (trimmed == "---")
                continue;

            // Check for context line (legacy format)
            if (trimmed.StartsWith("context:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentEntry.Context == null)
                {
                    currentEntry.Context = trimmed.Substring(8).Trim();
                }
                continue;
            }

            // Check for source line (entry attribution)
            if (trimmed.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            {
                currentEntry.Source = trimmed.Substring(7).Trim();
                continue;
            }

            // Check for session line (entry attribution)
            if (trimmed.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
            {
                currentEntry.Session = trimmed.Substring(8).Trim();
                continue;
            }

            // Check for entry ID line (non-task entries): _id: shortid [^blockid]
            var idMatch = EntryIdPattern().Match(trimmed);
            if (idMatch.Success)
            {
                currentEntry.ShortId = idMatch.Groups[1].Value;
                continue;
            }

            // Check for refs line (cross-entry references)
            if (trimmed.StartsWith("refs:", StringComparison.OrdinalIgnoreCase))
            {
                var refsStr = trimmed.Substring(5).Trim();
                var refParts = refsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var refPart in refParts)
                {
                    var token = RefToken.Parse(refPart);
                    if (token == null) continue;
                    currentEntry.RefTokens.Add(token);
                    if (token is RefToken.Entry e)
                        currentEntry.Refs.Add(e.Ref);
                }
                continue;
            }

            // Check for task metadata: _task: YYYY-MM-DD, HH:mm | postponed: N | shortid
            // Also matches: _task: none | postponed: N | shortid
            var taskMetaMatch = TaskMetadataPattern().Match(trimmed);
            if (taskMetaMatch.Success)
            {
                var deadlineStr = taskMetaMatch.Groups[1].Value;
                var postponedStr = taskMetaMatch.Groups[2].Value;
                var shortId = taskMetaMatch.Groups[3].Value;

                int.TryParse(postponedStr, out var postponed);

                if (deadlineStr.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    currentEntry.TaskDeadline = null;
                    currentEntry.TaskPostponed = postponed;
                    currentEntry.TaskShortId = shortId;
                }
                else if (DateTime.TryParse(deadlineStr.Replace(",", ""), out var deadline))
                {
                    currentEntry.TaskDeadline = deadline;
                    currentEntry.TaskPostponed = postponed;
                    currentEntry.TaskShortId = shortId;
                }

                // Check if the metadata line itself is struck through
                if (trimmed.StartsWith("~~") && trimmed.EndsWith("~~"))
                {
                    currentEntry.IsCompleted = true;
                }
                continue;
            }

            // Check for resolution line (after task completion)
            if (trimmed.StartsWith("resolved:", StringComparison.OrdinalIgnoreCase))
            {
                currentEntry.TaskResolution = trimmed.Substring(9).Trim();
                continue;
            }

            // Check for strikethrough (completed) content
            if (trimmed.StartsWith("~~") && trimmed.EndsWith("~~"))
            {
                currentEntry.IsCompleted = true;
                // Store content without strikethrough markers
                var content = trimmed.Substring(2, trimmed.Length - 4);
                currentEntry.ContentLines.Add(content);
                continue;
            }

            // Check for workflow line: description #tags (duration or running)
            var workflowMatch = WorkflowLinePattern().Match(trimmed);
            if (workflowMatch.Success && currentEntry.WorkflowMeta == null)
            {
                var descPart = workflowMatch.Groups[1].Value.Trim();
                var durationPart = workflowMatch.Groups[2].Value;

                // Validate this is actually a workflow duration format (Nh Nm, Nm, or "running")
                var isRunning = durationPart == "running";
                var duration = isRunning ? TimeSpan.Zero : ParseDuration(durationPart);

                // Only treat as workflow if duration is valid or "running"
                if (!isRunning && duration == TimeSpan.Zero && !DurationPattern().IsMatch(durationPart))
                {
                    // Not a valid workflow duration, treat as regular content
                    currentEntry.ContentLines.Add(line.TrimEnd('\r'));
                    continue;
                }

                // Extract tags from description
                var tags = NotationParser.ExtractTags(descPart);
                descPart = NotationParser.StripTags(descPart);

                currentEntry.WorkflowMeta = new WorkflowMetadata
                {
                    Description = descPart,
                    Tags = tags,
                    IsRunning = isRunning,
                    TotalDuration = duration,
                    Timeline = new List<TimeSegment>()
                };
                continue;
            }

            // Check for timeline segment: - HH:mm-HH:mm or - HH:mm-???
            if (currentEntry.WorkflowMeta != null)
            {
                var segmentMatch = TimelineSegmentPattern().Match(trimmed);
                if (segmentMatch.Success)
                {
                    var segment = ParseTimelineSegment(segmentMatch, currentEntry.Timestamp?.Date ?? DateTime.Today);
                    if (segment != null)
                    {
                        currentEntry.WorkflowMeta.Timeline.Add(segment);
                    }
                    continue;
                }

                // Skip timeline: label
                if (trimmed.StartsWith("timeline:", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Extended metadata: unrecognized key: value lines AFTER the first content line
            if (!string.IsNullOrWhiteSpace(trimmed) && currentEntry.HasContentLine)
            {
                var extMetaMatch = ExtendedMetadataPattern().Match(trimmed);
                if (extMetaMatch.Success)
                {
                    var key = extMetaMatch.Groups[1].Value;
                    var value = extMetaMatch.Groups[2].Value;
                    currentEntry.ExtendedMetadata[key] = value;
                    continue;
                }
            }

            // Regular content line
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                currentEntry.ContentLines.Add(line.TrimEnd('\r'));
                currentEntry.HasContentLine = true;
            }
        }

        // Finalize last entry
        if (inEntry)
        {
            var entry = currentEntry.Build(typeHint);
            if (entry != null)
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Find specific entry by timestamp.
    /// </summary>
    public static ParsedEntry? FindEntry(string markdown, DateTime timestamp)
        => FindEntry(markdown, timestamp, shortId: null);

    /// <summary>
    /// Find a specific entry, disambiguating by ShortId when supplied. Timestamps are
    /// only minute-precise, so a single file can hold several entries with the same
    /// "## yyyy-MM-dd HH:mm" header (e.g. an MCP agent writing a burst). When a
    /// non-empty <paramref name="shortId"/> is given it wins; otherwise (or if no
    /// entry carries that id) we fall back to the first timestamp match for
    /// backward compatibility.
    /// </summary>
    public static ParsedEntry? FindEntry(string markdown, DateTime timestamp, string? shortId)
        => FindEntry(markdown, timestamp, shortId, typeHint: null);

    /// <summary>
    /// Same as the three-argument overload, but parses with the source folder's
    /// type hint. Callers that know which file the markdown came from MUST pass
    /// it: type is carried by the folder, not by the text (prefixes are stripped
    /// on write), so parsing raw markdown alone reports the default type for
    /// every saved entry. That is how 63 of 64 "converted from" markers in the
    /// dogfooding vault came to read "diary" regardless of the real source type.
    /// </summary>
    public static ParsedEntry? FindEntry(
        string markdown, DateTime timestamp, string? shortId, EntryType? typeHint)
    {
        var entries = Parse(markdown, typeHint);
        if (!string.IsNullOrEmpty(shortId))
        {
            var byId = entries.FirstOrDefault(e =>
                !string.IsNullOrEmpty(e.ShortId) &&
                string.Equals(e.ShortId, shortId, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }
        return entries.FirstOrDefault(e => Math.Abs((e.Timestamp - timestamp).TotalSeconds) < 1);
    }

    /// <summary>
    /// Get line range for an entry (for editing).
    /// Returns 1-based line numbers (start, end) inclusive.
    /// </summary>
    public static (int start, int end)? GetEntryLineRange(string markdown, DateTime timestamp)
        => GetEntryLineRange(markdown, timestamp, shortId: null);

    /// <summary>
    /// Get line range for an entry, disambiguating by ShortId when supplied.
    /// Same minute-collision semantics as <see cref="FindEntry(string, DateTime, string?)"/>:
    /// a non-empty <paramref name="shortId"/> wins, otherwise the first timestamp
    /// match is returned. Callers that mutate an entry in place must pass the id -
    /// several entries can share one "## yyyy-MM-dd HH:mm" header.
    /// </summary>
    public static (int start, int end)? GetEntryLineRange(string markdown, DateTime timestamp, string? shortId)
    {
        var entry = FindEntry(markdown, timestamp, shortId);
        if (entry == null)
            return null;

        return (entry.StartLine, entry.EndLine);
    }

    private static TimeSegment? ParseTimelineSegment(Match match, DateTime date)
    {
        var startHour = int.Parse(match.Groups[1].Value);
        var startMin = int.Parse(match.Groups[2].Value);
        var start = date.AddHours(startHour).AddMinutes(startMin);

        var endPart = match.Groups[3].Value;
        if (endPart == "???")
        {
            return new TimeSegment { Start = start, IsOpen = true };
        }

        var endMatch = TimePattern().Match(endPart);
        if (!endMatch.Success)
            return null;

        var endHour = int.Parse(endMatch.Groups[1].Value);
        var endMin = int.Parse(endMatch.Groups[2].Value);
        var end = date.AddHours(endHour).AddMinutes(endMin);

        // Handle overnight segments
        if (end < start)
            end = end.AddDays(1);

        return new TimeSegment { Start = start, End = end, IsOpen = false };
    }

    private static TimeSpan ParseDuration(string durationStr)
    {
        var match = DurationPattern().Match(durationStr);
        if (!match.Success)
            return TimeSpan.Zero;

        var hours = 0;
        var minutes = 0;

        if (match.Groups[1].Success && !string.IsNullOrEmpty(match.Groups[1].Value))
            hours = int.Parse(match.Groups[1].Value);
        if (match.Groups[2].Success)
            minutes = int.Parse(match.Groups[2].Value);

        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
    }

    // ## 2025-12-04 14:30 or ## 2025-12-04 14:30 | context (allows leading whitespace)
    [GeneratedRegex(@"^\s*## (\d{4}-\d{2}-\d{2}) (\d{2}:\d{2})(?:\s*\|\s*(.+))?$")]
    private static partial Regex HeaderPattern();

    // _task: YYYY-MM-DD, HH:mm | postponed: N | shortid [^blockid]
    // _task: none | postponed: N | shortid [^blockid]
    // Also handles strikethrough: ~~_task: ... | shortid [^blockid]~~
    // Block ID suffix (^shortid) is optional for backwards compatibility
    [GeneratedRegex(@"^(?:~~)?_task:\s*(none|\d{4}-\d{2}-\d{2}(?:,\s*\d{2}:\d{2})?)\s*\|\s*postponed:\s*(\d+)\s*\|\s*([a-f0-9]{8})(?:\s*\^[a-f0-9]{8})?(?:~~)?$")]
    private static partial Regex TaskMetadataPattern();

    // _id: shortid [^blockid] - entry ID for non-task entries
    // Block ID suffix (^shortid) is optional for backwards compatibility
    [GeneratedRegex(@"^_id:\s*([a-f0-9]{8})(?:\s*\^[a-f0-9]{8})?$")]
    private static partial Regex EntryIdPattern();

    // Workflow line: description #tags (duration) or (running)
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)$")]
    private static partial Regex WorkflowLinePattern();

    // Timeline segment: - HH:mm-HH:mm or - HH:mm-???
    [GeneratedRegex(@"^-\s*(\d{2}):(\d{2})-(\S+)")]
    private static partial Regex TimelineSegmentPattern();

    // Time pattern HH:mm
    [GeneratedRegex(@"(\d{2}):(\d{2})")]
    private static partial Regex TimePattern();

    // Duration: 1h 30m, 1h, 30m
    [GeneratedRegex(@"(?:(\d+)h\s*)?(\d+)m")]
    private static partial Regex DurationPattern();

    // Extended metadata: lowercase_key: value (catch-all for unrecognized metadata)
    [GeneratedRegex(@"^([a-z_]+): (.+)$")]
    private static partial Regex ExtendedMetadataPattern();

    /// <summary>
    /// Internal builder for accumulating entry data during parsing.
    /// </summary>
    private class EntryBuilder
    {
        public DateTime? Timestamp { get; set; }
        public string? Context { get; set; }
        public List<string> ContentLines { get; } = new();
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public bool IsCompleted { get; set; }
        public WorkflowMetadata? WorkflowMeta { get; set; }
        public string? Source { get; set; }
        public string? Session { get; set; }
        public List<EntryRef> Refs { get; } = new();
        public List<RefToken> RefTokens { get; } = new();
        public string? ShortId { get; set; }
        public Dictionary<string, string> ExtendedMetadata { get; } = new();
        public bool HasContentLine { get; set; }

        // Task metadata fields (combined into TaskMetadata in Build)
        public DateTime? TaskDeadline { get; set; }
        public int TaskPostponed { get; set; }
        public string? TaskShortId { get; set; }
        public string? TaskResolution { get; set; }

        public bool HasTaskMeta => TaskShortId != null;

        public ParsedEntry? Build(EntryType? typeHint)
        {
            if (!Timestamp.HasValue)
                return null;

            // Build TaskMetadata if we have task fields
            TaskMetadata? taskMeta = null;
            if (HasTaskMeta)
            {
                taskMeta = new TaskMetadata
                {
                    Deadline = TaskDeadline,
                    PostponedCount = TaskPostponed,
                    ShortId = TaskShortId!,
                    Resolution = TaskResolution
                };
            }

            // Determine entry type
            var type = DetermineType(typeHint, taskMeta);

            // Build content string
            // For workflow entries, use description if ContentLines is empty
            var content = string.Join("\n", ContentLines).Trim();
            if (string.IsNullOrEmpty(content) && WorkflowMeta != null)
                content = WorkflowMeta.Description;

            // ShortId comes from TaskMeta for tasks, or from _id: line for non-tasks
            var shortId = taskMeta?.ShortId ?? ShortId;

            return new ParsedEntry
            {
                Timestamp = Timestamp.Value,
                Context = Context,
                Content = content,
                StartLine = StartLine,
                EndLine = EndLine,
                Type = type,
                IsCompleted = IsCompleted,
                HasParseError = false,
                TaskMeta = taskMeta,
                WorkflowMeta = WorkflowMeta,
                Source = Source,
                Session = Session,
                Refs = Refs,
                RefTokens = RefTokens,
                ShortId = shortId,
                ExtendedMetadata = ExtendedMetadata,
                Resolution = TaskResolution
            };
        }

        private EntryType DetermineType(EntryType? typeHint, TaskMetadata? taskMeta)
        {
            // Workflow takes precedence if we detected workflow metadata
            if (WorkflowMeta != null)
                return EntryType.Timetrack;

            // Task if we have task metadata
            if (taskMeta != null)
                return EntryType.Task;

            // Type hint from file path (e.g., _tasks, _ideas, etc.)
            if (typeHint.HasValue)
                return typeHint.Value;

            // Check content for type prefix (shared vocabulary)
            var content = string.Join("\n", ContentLines);
            var prefix = EntryPrefix.TryParseLeading(content);
            if (prefix.HasValue)
                return prefix.Value.Type;

            // Default to Log
            return EntryType.Log;
        }
    }
}
