using System.Text;
using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Services.EntryParser;

/// <summary>
/// Serializes changes back to markdown format.
/// </summary>
public static partial class EntrySerializer
{
    /// <summary>
    /// Replace entry content, return new markdown.
    /// Preserves header format and metadata lines.
    /// </summary>
    public static string ReplaceEntry(string markdown, DateTime timestamp, string newContent, string? shortId = null)
    {
        var lines = markdown.Split('\n');
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return markdown;

        var result = new StringBuilder();

        // Lines before entry
        for (int i = 0; i < entry.StartLine - 1; i++)
        {
            result.AppendLine(lines[i].TrimEnd('\r'));
        }

        // Entry header (preserve as-is)
        result.AppendLine(lines[entry.StartLine - 1].TrimEnd('\r'));

        // Blank line after header
        result.AppendLine();

        // New content
        result.AppendLine(newContent.TrimEnd());

        // Preserve metadata lines (task metadata, context in legacy format)
        for (int i = entry.StartLine; i < entry.EndLine && i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            if (IsMetadataLine(trimmed))
            {
                result.AppendLine(line);
            }
        }

        // Lines after entry
        for (int i = entry.EndLine; i < lines.Length; i++)
        {
            if (i < lines.Length - 1 || !string.IsNullOrEmpty(lines[i].TrimEnd('\r')))
            {
                result.AppendLine(lines[i].TrimEnd('\r'));
            }
        }

        return result.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Update task metadata (deadline, postpone count).
    /// </summary>
    public static string UpdateTaskMetadata(string markdown, DateTime timestamp,
        DateTime? newDeadline = null, bool? completed = null, int? postponeIncrement = null,
        string? shortId = null)
    {
        var lines = markdown.Split('\n').ToList();
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null || entry.TaskMeta == null)
            return markdown;

        var taskMeta = entry.TaskMeta;
        var deadline = newDeadline ?? taskMeta.Deadline;
        var postponed = taskMeta.PostponedCount + (postponeIncrement ?? 0);

        // Find and update the _task: metadata line
        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Match task metadata line (with or without strikethrough)
            if (TaskMetadataLinePattern().IsMatch(trimmed) && trimmed.Contains(taskMeta.ShortId))
            {
                var leadingWhitespace = line.Substring(0, line.Length - line.TrimStart().Length);
                var deadlineStr = deadline != null ? FormatDeadline(deadline.Value) : "none";
                var newLine = $"_task: {deadlineStr} | postponed: {postponed} | {taskMeta.ShortId}";

                // Apply completion styling
                if (completed == true)
                {
                    newLine = $"~~{newLine}~~";
                }

                lines[i] = leadingWhitespace + newLine;
                break;
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Set task completion to the given state (add/remove strikethrough from content and metadata).
    /// Idempotent: passing <paramref name="completed"/>=true on an already-completed task is a no-op
    /// in intent, regardless of current file state.
    ///
    /// The caller passes the desired final state; the serializer does NOT toggle based on the file's
    /// current state. This prevents cache/file divergence from inverting user intent: if the cache
    /// shows pending and the user clicks "complete", the file ends up completed even if an external
    /// writer (Obsidian, MCP, CLI, sync) had already marked it completed in the file.
    /// </summary>
    public static string SetTaskCompletion(string markdown, DateTime timestamp, string shortId, bool completed)
    {
        var lines = markdown.Split('\n').ToList();
        // Disambiguate by shortId: minute-resolution headers mean several entries
        // can share one "## yyyy-MM-dd HH:mm" line (agent bursts). Passing the id
        // through targets the clicked task rather than the first timestamp match,
        // which otherwise strikes the wrong same-minute sibling and leaves the
        // clicked checkbox reverting to unchecked. Empty shortId (backfilled tasks
        // without a `_task:` trailer) falls back to the timestamp match.
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return markdown;

        // Process lines within entry range
        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            // Skip context lines
            if (trimmed.StartsWith("context:", StringComparison.OrdinalIgnoreCase))
                continue;

            var leadingWhitespace = line.Substring(0, line.Length - line.TrimStart().Length);

            if (completed)
            {
                // Add strikethrough if not already present
                if (!trimmed.StartsWith("~~"))
                {
                    lines[i] = leadingWhitespace + "~~" + trimmed + "~~";
                }
            }
            else
            {
                // Remove strikethrough
                if (trimmed.StartsWith("~~") && trimmed.EndsWith("~~"))
                {
                    var content = trimmed.Substring(2, trimmed.Length - 4);
                    lines[i] = leadingWhitespace + content;
                }
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Set an extended metadata field on an entry.
    /// If the field already exists, updates its value. If not, inserts after the last metadata line.
    /// If no metadata lines exist, appends after the last content line.
    /// </summary>
    public static string SetMetadataField(string markdown, DateTime timestamp, string key, string value, string? shortId = null)
    {
        if (!KeyPattern().IsMatch(key))
            throw new ArgumentException($"Key must match ^[a-z_]+$, got: '{key}'", nameof(key));

        var lines = markdown.Split('\n').ToList();
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            throw new ArgumentException($"No entry found at timestamp {timestamp}");

        var fieldPrefix = $"{key}: ";

        // Scan entry lines for existing field and track last metadata line
        int existingFieldIndex = -1;
        int lastMetadataLineIndex = -1;
        int lastContentLineIndex = -1;

        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (trimmed.StartsWith(fieldPrefix, StringComparison.Ordinal))
            {
                existingFieldIndex = i;
                lastMetadataLineIndex = i;
            }
            else if (IsMetadataLine(trimmed))
            {
                lastMetadataLineIndex = i;
            }
            else
            {
                lastContentLineIndex = i;
            }
        }

        if (existingFieldIndex >= 0)
        {
            // Update existing field in place
            var existingLine = lines[existingFieldIndex].TrimEnd('\r');
            var leadingWhitespace = existingLine.Substring(0, existingLine.Length - existingLine.TrimStart().Length);
            lines[existingFieldIndex] = $"{leadingWhitespace}{key}: {value}";
        }
        else if (lastMetadataLineIndex >= 0)
        {
            // Insert after last metadata line
            lines.Insert(lastMetadataLineIndex + 1, $"{key}: {value}");
        }
        else if (lastContentLineIndex >= 0)
        {
            // No metadata -- insert after last content line
            lines.Insert(lastContentLineIndex + 1, $"{key}: {value}");
        }
        else
        {
            // Only header and blank lines -- insert before entry boundary
            int insertIndex = entry.EndLine - 1;
            if (insertIndex >= lines.Count)
                insertIndex = lines.Count - 1;
            lines.Insert(insertIndex + 1, $"{key}: {value}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Remove an extended metadata field from an entry.
    /// Returns unchanged markdown if entry or field not found.
    /// </summary>
    public static string RemoveMetadataField(string markdown, DateTime timestamp, string key, string? shortId = null)
    {
        var lines = markdown.Split('\n').ToList();
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return markdown;

        var fieldPrefix = $"{key}: ";

        // Scan entry lines for the field
        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            if (trimmed.StartsWith(fieldPrefix, StringComparison.Ordinal))
            {
                lines.RemoveAt(i);
                return string.Join("\n", lines);
            }
        }

        // Field not found -- no-op
        return markdown;
    }

    /// <summary>
    /// Add a ref to an existing entry's refs: metadata line.
    /// If no refs: line exists, inserts one after the last metadata line.
    /// Returns unchanged markdown if entry not found or ref already exists.
    /// </summary>
    public static string AddRef(string markdown, DateTime timestamp, EntryRef newRef, string? shortId = null)
    {
        var lines = markdown.Split('\n').ToList();
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return markdown;

        var refString = newRef.ToString();

        // Scan entry lines for existing refs: line and track last metadata line
        int refsLineIndex = -1;
        int lastMetadataLineIndex = -1;

        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (trimmed.StartsWith("refs:", StringComparison.OrdinalIgnoreCase))
            {
                refsLineIndex = i;
                lastMetadataLineIndex = i;
            }
            else if (IsMetadataLine(trimmed))
            {
                lastMetadataLineIndex = i;
            }
        }

        if (refsLineIndex >= 0)
        {
            // Existing refs: line -- check for duplicate and append
            var existingLine = lines[refsLineIndex].TrimEnd('\r');
            var refsValue = existingLine.Trim().Substring(5).Trim(); // After "refs:"

            // Check for duplicate
            var existingRefs = refsValue.Split(',')
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrEmpty(r));

            if (existingRefs.Any(r => r.Equals(refString, StringComparison.OrdinalIgnoreCase)))
                return markdown; // Already exists

            var leadingWhitespace = existingLine.Substring(0, existingLine.Length - existingLine.TrimStart().Length);
            lines[refsLineIndex] = $"{leadingWhitespace}refs: {refsValue}, {refString}";
        }
        else if (lastMetadataLineIndex >= 0)
        {
            // No refs: line, insert after last metadata line
            lines.Insert(lastMetadataLineIndex + 1, $"refs: {refString}");
        }
        else
        {
            // No metadata lines at all -- insert before entry boundary
            // Find the last content line within the entry
            int insertIndex = entry.EndLine - 1;
            // Ensure we don't go past the array
            if (insertIndex >= lines.Count)
                insertIndex = lines.Count - 1;

            lines.Insert(insertIndex + 1, $"refs: {refString}");
        }

        return string.Join("\n", lines);
    }

    // Wikilink span used to exclude [[...]] fragments from facet-tag rewriting.
    // Mirrors NotationParser.WikiLinkPattern: non-greedy, single-line, so it never
    // swallows across a line break.
    [GeneratedRegex(@"\[\[[^\]\r\n]*\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkSpanPattern();

    /// <summary>
    /// Within the entry section matched by <paramref name="timestamp"/>, replace facet
    /// tag #key/oldValue (or #key:oldValue) with #key/newValue. Only that entry's
    /// section is touched; wikilink spans ([[...]]) are left untouched; matching is
    /// case-insensitive on key and value. Returns the new text and whether anything
    /// changed (false when the entry or the facet was not found).
    /// </summary>
    public static (string Text, bool Changed) SwapFacetTag(
        string fileContent, DateTime timestamp, string facetKey, string oldValue, string newValue)
    {
        var lines = fileContent.Split('\n');
        var entry = EntryParser.FindEntry(fileContent, timestamp);

        if (entry == null)
            return (fileContent, false);

        var pattern = new Regex(
            $@"#{Regex.Escape(facetKey)}[/:]{Regex.Escape(oldValue)}(?![a-zA-Z0-9_-])",
            RegexOptions.IgnoreCase);
        var replacement = $"#{facetKey}/{newValue}";

        var changed = false;

        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Length; i++)
        {
            var line = lines[i];
            var lineChanged = false;

            // Split the line on wikilink spans and rewrite only the segments outside
            // them, so a fragment like [[x#status/todo]] is never touched.
            var lastIndex = 0;
            var sb = new StringBuilder();
            foreach (Match span in WikiLinkSpanPattern().Matches(line))
            {
                var outside = line.Substring(lastIndex, span.Index - lastIndex);
                var rewrittenOutside = pattern.Replace(outside, m =>
                {
                    lineChanged = true;
                    return replacement;
                });
                sb.Append(rewrittenOutside);
                sb.Append(span.Value);
                lastIndex = span.Index + span.Length;
            }
            var tail = line.Substring(lastIndex);
            var rewrittenTail = pattern.Replace(tail, m =>
            {
                lineChanged = true;
                return replacement;
            });
            sb.Append(rewrittenTail);

            if (lineChanged)
            {
                lines[i] = sb.ToString();
                changed = true;
            }
        }

        if (!changed)
            return (fileContent, false);

        return (string.Join("\n", lines), true);
    }

    /// <summary>
    /// Append " #key/value" to the entry's first content line (the first line
    /// within the entry that is not the "## " header, blank, a "---" separator,
    /// or a metadata line). Used by the view-op "move" dispatch when
    /// <see cref="SwapFacetTag"/> finds nothing to replace - i.e. moving a row
    /// out of a board's ungrouped column, where the entry does not yet carry
    /// the facet tag at all. Returns the new text and whether anything changed
    /// (false when the entry was not found or has no content line to append to).
    /// </summary>
    public static (string Text, bool Changed) AppendFacetTag(
        string fileContent, DateTime timestamp, string facetKey, string value)
    {
        var lines = fileContent.Split('\n');
        var entry = EntryParser.FindEntry(fileContent, timestamp);

        if (entry == null)
            return (fileContent, false);

        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;
            if (IsMetadataLine(trimmed))
                continue;

            lines[i] = line.TrimEnd() + $" #{facetKey}/{value}";
            return (string.Join("\n", lines), true);
        }

        return (fileContent, false);
    }

    /// <summary>
    /// Insert a wiki link into an entry as the last content line before metadata.
    /// Scoped idempotency: only checks the target entry for existing link to the same wikiPath.
    /// Returns markdown unchanged if entry not found or link already exists within the entry.
    /// Pass <paramref name="shortId"/> whenever the caller knows it: timestamps are
    /// minute-precise, so without an id the link lands on the first same-minute sibling.
    /// </summary>
    public static string InsertWikiLink(
        string markdown, DateTime timestamp, string wikiPath, string displayText, string? shortId = null)
    {
        var lines = markdown.Split('\n').ToList();
        var range = EntryParser.GetEntryLineRange(markdown, timestamp, shortId);

        if (range == null)
            return markdown;

        var (start, end) = range.Value;

        // Check for existing link within this entry's lines (scoped idempotency).
        // Match [[wikiPath| or [[wikiPath]] exactly -- not as a prefix of a longer path.
        var linkPipePattern = $"[[{wikiPath}|";
        var linkClosePattern = $"[[{wikiPath}]]";

        for (int i = start - 1; i < end && i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Contains(linkPipePattern) || line.Contains(linkClosePattern))
                return markdown;
        }

        // Find insertion point: before first metadata line within the entry.
        // Track the last content line as fallback.
        int firstMetadataIndex = -1;
        int lastContentIndex = -1;

        for (int i = start - 1; i < end && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (IsMetadataLine(trimmed))
            {
                if (firstMetadataIndex < 0)
                    firstMetadataIndex = i;
            }
            else
            {
                lastContentIndex = i;
            }
        }

        var linkLine = $"See [[{wikiPath}|{displayText}]]";

        if (firstMetadataIndex >= 0)
        {
            // Insert before the first metadata line
            lines.Insert(firstMetadataIndex, linkLine);
        }
        else if (lastContentIndex >= 0)
        {
            // No metadata -- insert after last content line
            lines.Insert(lastContentIndex + 1, linkLine);
        }
        else
        {
            // Only header and blank lines -- insert before entry boundary
            int insertIndex = end - 1;
            if (insertIndex >= lines.Count)
                insertIndex = lines.Count - 1;
            lines.Insert(insertIndex + 1, linkLine);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Insert ONE accumulated link line of the form
    /// "<paramref name="label"/>: [[path1|text1]], [[path2|text2]]" into the entry
    /// identified by <paramref name="timestamp"/>. Sibling to
    /// <see cref="InsertWikiLink"/> (which writes one "See [[..]]" line per call);
    /// this composes every link onto a single readable line - used for the
    /// "Distilled:" traceability block a session stub gains at accept time.
    /// Scoped idempotency: if the entry already carries a line beginning with
    /// "<paramref name="label"/>:", the markdown is returned unchanged (the block
    /// is written once). Returns markdown unchanged when the entry is not found or
    /// <paramref name="links"/> is empty. The line is inserted before the first
    /// metadata line within the entry, matching InsertWikiLink's anchor. The label
    /// is expected to be capitalized (e.g. "Distilled") so it is not mistaken for a
    /// lowercase "key: value" metadata line. Pass <paramref name="shortId"/> whenever
    /// the caller knows it: two session stubs can share one minute header, and
    /// without an id the block lands on - and is suppressed by - the wrong sibling.
    /// </summary>
    public static string InsertLinkLine(
        string markdown, DateTime timestamp, string label,
        IReadOnlyList<(string WikiPath, string DisplayText)> links,
        string? shortId = null)
    {
        if (links == null || links.Count == 0)
            return markdown;

        var lines = markdown.Split('\n').ToList();
        var range = EntryParser.GetEntryLineRange(markdown, timestamp, shortId);

        if (range == null)
            return markdown;

        var (start, end) = range.Value;
        var labelPrefix = label + ":";

        // Find insertion point (before first metadata line) while enforcing scoped
        // idempotency: a line already starting with "<label>:" means the block was
        // written; no-op rather than appending a duplicate.
        int firstMetadataIndex = -1;
        int lastContentIndex = -1;

        for (int i = start - 1; i < end && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            if (trimmed.StartsWith(labelPrefix, StringComparison.Ordinal))
                return markdown;

            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (IsMetadataLine(trimmed))
            {
                if (firstMetadataIndex < 0)
                    firstMetadataIndex = i;
            }
            else
            {
                lastContentIndex = i;
            }
        }

        var linkText = string.Join(", ", links.Select(l => $"[[{l.WikiPath}|{l.DisplayText}]]"));
        var linkLine = $"{labelPrefix} {linkText}";

        if (firstMetadataIndex >= 0)
        {
            lines.Insert(firstMetadataIndex, linkLine);
        }
        else if (lastContentIndex >= 0)
        {
            lines.Insert(lastContentIndex + 1, linkLine);
        }
        else
        {
            int insertIndex = end - 1;
            if (insertIndex >= lines.Count)
                insertIndex = lines.Count - 1;
            lines.Insert(insertIndex + 1, linkLine);
        }

        return string.Join("\n", lines);
    }

    // Tag token used by RetagEntry for in-place add/remove. Mirrors
    // NotationParser.TagPattern, including the leading (?=...) "at least one letter"
    // lookahead, so a pure-numeric "#2" reference is never treated as a removable tag.
    [GeneratedRegex(@"#(?=[a-zA-Z0-9_/:-]*[a-zA-Z])([a-zA-Z0-9_-]+(?:[/:][a-zA-Z0-9_-]+)*)", RegexOptions.Compiled)]
    private static partial Regex TagTokenPattern();

    /// <summary>
    /// Add and/or remove inline #tags on the entry at <paramref name="timestamp"/>
    /// (disambiguated by <paramref name="shortId"/> when several entries share the
    /// minute). Removal strips matching "#tag" tokens from the entry's content lines
    /// (leaf-compared, case-insensitive), leaving header, metadata, and amend-trail
    /// (">") lines untouched; a content line left empty by removal is dropped.
    /// Addition appends "#tag" for each requested tag not already present (leaf-
    /// compared) onto a trailing tag-only content line, or as a new line before the
    /// first metadata line when none exists. Pure-numeric "#2"-style references are
    /// not tags and are neither added nor removed. Returns the markdown unchanged when
    /// the entry is not found or nothing changed.
    /// </summary>
    public static string RetagEntry(string markdown, DateTime timestamp, string? shortId,
        IReadOnlyList<string> add, IReadOnlyList<string> remove)
    {
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);
        if (entry == null)
            return markdown;

        var removeLeaves = (remove ?? Array.Empty<string>())
            .Select(NormalizeTagLeaf)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changed = false;

        // --- Removal pass: rebuild the file, dropping matched tokens (and any content
        // line left empty as a result). ---
        var lines = markdown.Split('\n');
        var rebuilt = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var inEntry = i >= entry.StartLine - 1 && i < entry.EndLine;
            if (!inEntry || removeLeaves.Count == 0)
            {
                rebuilt.Add(lines[i]);
                continue;
            }

            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.Trim();
            var isContent = !(trimmed.StartsWith("##") || trimmed.Length == 0
                || trimmed == "---" || trimmed.StartsWith(">") || IsMetadataLine(trimmed));
            if (!isContent)
            {
                rebuilt.Add(lines[i]);
                continue;
            }

            var updated = RemoveTagTokens(raw, removeLeaves, out var removedAny);
            if (!removedAny)
            {
                rebuilt.Add(lines[i]);
                continue;
            }

            changed = true;
            if (updated.Trim().Length == 0)
                continue; // line consisted only of removed tags -- drop it
            rebuilt.Add(updated);
        }

        var afterRemoval = string.Join("\n", rebuilt);

        // --- Add pass. ---
        var addTags = (add ?? Array.Empty<string>())
            .Select(t => (t ?? "").Trim().TrimStart('#').Trim())
            .Where(t => t.Length > 0 && TagTokenPattern().IsMatch("#" + t))
            .ToList();

        if (addTags.Count == 0)
            return changed ? afterRemoval : markdown;

        var entry2 = EntryParser.FindEntry(afterRemoval, timestamp, shortId);
        if (entry2 == null)
            return changed ? afterRemoval : markdown;

        var lines2 = afterRemoval.Split('\n').ToList();

        var existingLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int tagOnlyLineIndex = -1, firstMetadataIndex = -1, lastContentIndex = -1;
        for (int i = entry2.StartLine - 1; i < entry2.EndLine && i < lines2.Count; i++)
        {
            var trimmed = lines2[i].TrimEnd('\r').Trim();
            if (trimmed.StartsWith("##") || trimmed.Length == 0 || trimmed == "---")
                continue;
            if (IsMetadataLine(trimmed))
            {
                if (firstMetadataIndex < 0) firstMetadataIndex = i;
                continue;
            }

            // Content or amend-trail line.
            lastContentIndex = i;
            foreach (Match m in TagTokenPattern().Matches(trimmed))
                existingLeaves.Add(NotationParser.LeafTag(m.Groups[1].Value));
            if (!trimmed.StartsWith(">") && IsTagOnlyLine(trimmed))
                tagOnlyLineIndex = i;
        }

        var newTokens = new List<string>();
        var seen = new HashSet<string>(existingLeaves, StringComparer.OrdinalIgnoreCase);
        foreach (var t in addTags)
        {
            var leaf = NotationParser.LeafTag(t);
            if (!seen.Add(leaf)) continue;
            newTokens.Add("#" + t);
        }

        if (newTokens.Count == 0)
            return changed ? afterRemoval : markdown;

        var addition = string.Join(" ", newTokens);
        if (tagOnlyLineIndex >= 0)
        {
            lines2[tagOnlyLineIndex] = lines2[tagOnlyLineIndex].TrimEnd('\r').TrimEnd() + " " + addition;
        }
        else
        {
            var insertAt = firstMetadataIndex >= 0
                ? firstMetadataIndex
                : (lastContentIndex >= 0 ? lastContentIndex + 1 : entry2.EndLine);
            if (insertAt > lines2.Count) insertAt = lines2.Count;
            lines2.Insert(insertAt, addition);
        }

        return string.Join("\n", lines2);
    }

    /// <summary>Normalize a caller-supplied tag ("#surface/mcp", "mcp") to its leaf.</summary>
    private static string NormalizeTagLeaf(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Trim().TrimStart('#').Trim();
        return t.Length == 0 ? "" : NotationParser.LeafTag(t);
    }

    /// <summary>
    /// Remove every #tag token whose leaf is in <paramref name="removeLeaves"/> from a
    /// single line, preserving the line's leading indent and collapsing the whitespace
    /// hole a removed token leaves behind. Returns the line unchanged (and removedAny
    /// false) when nothing matched.
    /// </summary>
    private static string RemoveTagTokens(string line, IReadOnlyList<string> removeLeaves, out bool removedAny)
    {
        var indentLen = line.Length - line.TrimStart(' ', '\t').Length;
        var indent = line.Substring(0, indentLen);
        var body = line.Substring(indentLen);

        var any = false;
        var replaced = TagTokenPattern().Replace(body, m =>
        {
            var leaf = NotationParser.LeafTag(m.Groups[1].Value);
            if (removeLeaves.Any(r => r.Equals(leaf, StringComparison.OrdinalIgnoreCase)))
            {
                any = true;
                return "";
            }
            return m.Value;
        });

        removedAny = any;
        if (!any) return line;

        replaced = Regex.Replace(replaced, @"[ \t]{2,}", " ").Trim();
        return replaced.Length == 0 ? indent : indent + replaced;
    }

    /// <summary>True when a trimmed line is composed solely of #tag tokens and whitespace.</summary>
    private static bool IsTagOnlyLine(string trimmed)
    {
        if (!TagTokenPattern().IsMatch(trimmed)) return false;
        return TagTokenPattern().Replace(trimmed, "").Trim().Length == 0;
    }

    // Entry type prefixes that should NOT be treated as metadata
    private static readonly HashSet<string> EntryTypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "t", "i", "b", "d", "q", "g", "a",
        "task", "idea", "block", "decision", "question", "gap", "analogy"
    };

    /// <summary>
    /// Check if a trimmed line is a metadata line (public accessor).
    /// </summary>
    public static bool IsMetadataLinePublic(string trimmedLine) => IsMetadataLine(trimmedLine);

    private static bool IsMetadataLine(string trimmed)
    {
        if (trimmed.StartsWith("_task:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("~~_task:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("_id:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("source:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("session:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("context:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("resolved:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("refs:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Extended metadata catch-all: lowercase key: value, excluding entry type prefixes
        var match = Regex.Match(trimmed, @"^([a-z_]+): .+");
        if (match.Success && !EntryTypePrefixes.Contains(match.Groups[1].Value))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Format deadline for metadata line.
    /// </summary>
    private static string FormatDeadline(DateTime deadline)
    {
        return deadline.ToString("yyyy-MM-dd, HH:mm");
    }

    // Matches _task: line with or without strikethrough (date or "none")
    [GeneratedRegex(@"^(?:~~)?_task:\s*(?:none|\d{4}-\d{2}-\d{2})")]
    private static partial Regex TaskMetadataLinePattern();

    /// <summary>
    /// Append an amend line to an entry without modifying existing content.
    /// Format: > [amend YYYY-MM-DD HH:mm, source] content
    /// Inserted after content/existing amends, before metadata lines.
    /// </summary>
    public static string AmendEntry(string markdown, DateTime timestamp, string amendContent,
        string source, DateTime? amendTimestamp = null, string? shortId = null)
    {
        var lines = markdown.Split('\n').ToList();
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return markdown;

        var effectiveTimestamp = amendTimestamp ?? DateTime.Now;
        var amendLine = $"> [amend {effectiveTimestamp:yyyy-MM-dd HH:mm}, {source}] {amendContent}";

        // Scan entry lines to find insertion point:
        // After the last content line or existing amend line, before the first metadata line.
        int insertIndex = -1;
        int firstMetadataIndex = -1;
        int lastContentOrAmendIndex = -1;

        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (IsMetadataLine(trimmed))
            {
                if (firstMetadataIndex < 0)
                    firstMetadataIndex = i;
            }
            else
            {
                lastContentOrAmendIndex = i;
            }
        }

        if (firstMetadataIndex >= 0)
        {
            // Insert before the first metadata line
            insertIndex = firstMetadataIndex;
        }
        else if (lastContentOrAmendIndex >= 0)
        {
            // No metadata -- insert after last content/amend line
            insertIndex = lastContentOrAmendIndex + 1;
        }
        else
        {
            // Only header and blank lines -- insert before entry boundary
            insertIndex = entry.EndLine;
            if (insertIndex > lines.Count)
                insertIndex = lines.Count;
        }

        lines.Insert(insertIndex, amendLine);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Extract (remove) an entry from a markdown file.
    /// Returns the extracted entry text and the remaining markdown.
    /// If entry not found, returns (null, original markdown).
    /// If remainder is all whitespace, returns empty string.
    /// </summary>
    public static (string? Extracted, string Remainder) ExtractEntry(string markdown, DateTime timestamp)
        => ExtractEntry(markdown, timestamp, shortId: null);

    /// <summary>
    /// Extract overload that disambiguates by ShortId when supplied -- required when a
    /// file holds multiple entries sharing one minute-precise timestamp (see
    /// <see cref="EntryParser.FindEntry(string, DateTime, string?)"/>).
    /// </summary>
    public static (string? Extracted, string Remainder) ExtractEntry(string markdown, DateTime timestamp, string? shortId)
    {
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);

        if (entry == null)
            return (null, markdown);

        var lines = markdown.Split('\n');

        // Entry range: [startIdx, endIdx) as 0-based indices
        var startIdx = entry.StartLine - 1;
        var endIdx = entry.EndLine; // Already exclusive as 0-based index

        // Expand to include leading separator and blank lines.
        // Walk backwards from startIdx to consume blank lines and a --- separator
        // that visually belong to this entry.
        var extractStart = startIdx;
        var scanBack = startIdx - 1;
        while (scanBack >= 0 && string.IsNullOrWhiteSpace(lines[scanBack].TrimEnd('\r')))
            scanBack--;
        if (scanBack >= 0 && lines[scanBack].TrimEnd('\r').Trim() == "---")
        {
            extractStart = scanBack;
        }
        else
        {
            // No separator found, but consume leading blank lines to keep remainder clean
            extractStart = scanBack + 1;
        }

        // Also consume trailing blank lines after the entry range to avoid double blanks
        var extractEnd = endIdx;
        while (extractEnd < lines.Length && string.IsNullOrWhiteSpace(lines[extractEnd].TrimEnd('\r')))
            extractEnd++;

        // Build extracted text (just the entry itself: header, content, metadata)
        var extractedBuilder = new StringBuilder();
        for (int i = startIdx; i < endIdx; i++)
        {
            var line = lines[i].TrimEnd('\r');
            extractedBuilder.AppendLine(line);
        }
        var extracted = extractedBuilder.ToString().TrimEnd() + "\n";

        // Build remainder: lines before the extraction zone + lines after
        var remainderBuilder = new StringBuilder();
        for (int i = 0; i < extractStart; i++)
        {
            remainderBuilder.AppendLine(lines[i].TrimEnd('\r'));
        }
        for (int i = extractEnd; i < lines.Length; i++)
        {
            remainderBuilder.AppendLine(lines[i].TrimEnd('\r'));
        }

        var remainder = remainderBuilder.ToString();

        // Clean up double blank lines in remainder
        while (remainder.Contains("\n\n\n"))
        {
            remainder = remainder.Replace("\n\n\n", "\n\n");
        }

        // If remainder is all whitespace, return empty string
        if (string.IsNullOrWhiteSpace(remainder))
            return (extracted, "");

        return (extracted, remainder);
    }

    // Validates extended metadata keys: lowercase letters and underscores only
    [GeneratedRegex(@"^[a-z_]+$")]
    private static partial Regex KeyPattern();
}
