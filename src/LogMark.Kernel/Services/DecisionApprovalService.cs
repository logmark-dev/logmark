using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LogMark.Services.EntryCache;

using EntryCacheService = LogMark.Services.EntryCache.EntryCache;

namespace LogMark.Services;

/// <summary>
/// Mutates Decision and Block entries by swapping mutually-exclusive lifecycle tags.
/// Decisions: active / frozen / rejected (exclusive set).
/// Blocks: resolved / dismissed (exclusive set).
/// Mark-not-delete: entries stay in the vault; tags swap.
///
/// Pattern matches <see cref="TaskTracker"/>: locate the entry by shortId in the file,
/// rewrite via <see cref="IVaultFileWriter"/>, invalidate the cache.
///
/// Resolution note serialization (blocks only): mirrors <see cref="TaskTracker"/>'s
/// task-completion convention exactly - a standalone "resolved: &lt;note&gt;" line,
/// placed immediately after the entry's identifying metadata line (here, the
/// "_id: shortid" anchor; for tasks it is the "_task: ..." line). Reusing the same
/// "resolved:" trailer keyword keeps vault markdown uniform between the two entry
/// types and lets <c>EntryParser</c> parse both with the one line-recognizer
/// (see the unconditional "resolved:" check in EntryParser.Parse), rather than
/// inventing a second keyword. The note is optional at this service's API surface:
/// existing no-note callers (ResolveDecisionAsync/RejectDecisionAsync, and
/// ResolveBlockAsync/DismissBlockAsync when called without a resolution argument)
/// are unaffected - the resolution line is only touched when a non-blank note is
/// supplied.
/// </summary>
public sealed class DecisionApprovalService
{
    private static readonly string[] DecisionLifecycleTags = { "active", "frozen", "rejected" };
    private static readonly string[] BlockLifecycleTags = { "resolved", "dismissed" };

    /// <summary>
    /// Tasks have one tag-based exit, not an exclusive pair. Completion is
    /// strikethrough via TaskTracker; dismissal is this tag. The two are
    /// deliberately different notations because they mean different things - a
    /// dismissed task is not done, and rendering it as done was the falsehood
    /// that kept overtaken tasks open instead.
    /// </summary>
    private static readonly string[] TaskLifecycleTags = { "dismissed" };

    private static readonly ConcurrentDictionary<string, Regex> StripPatterns = new();
    private static readonly Regex CollapseWhitespacePattern =
        new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex CollapseNewlinesPattern =
        new(@"(?:\r\n|\r|\n)+", RegexOptions.Compiled);

    private readonly EntryCacheService _cache;
    private readonly IVaultFileWriter _writer;
    private readonly IFileAccessor _fileAccessor;
    private readonly SystemLogger? _logger;

    public DecisionApprovalService(
        EntryCacheService cache,
        IVaultFileWriter writer,
        IFileAccessor fileAccessor,
        SystemLogger? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        _logger = logger;
    }

    public Task<bool> PromoteDecisionAsync(string filePath, string shortId) =>
        SetExclusiveTagAsync(filePath, shortId, DecisionLifecycleTags, target: "frozen");

    public Task<bool> RejectDecisionAsync(string filePath, string shortId) =>
        SetExclusiveTagAsync(filePath, shortId, DecisionLifecycleTags, target: "rejected");

    /// <param name="resolution">
    /// Optional resolution note. When supplied (non-blank), stored as a
    /// "resolved: &lt;note&gt;" trailer line right after the block's "_id:" line -
    /// see the class doc comment for the serialization rationale. Omit (or pass
    /// null) to resolve without a note, unchanged from prior behavior.
    /// </param>
    public Task<bool> ResolveBlockAsync(string filePath, string shortId, string? resolution = null) =>
        SetExclusiveTagAsync(filePath, shortId, BlockLifecycleTags, target: "resolved", resolution);

    /// <param name="resolution">
    /// Optional resolution note, same serialization as <see cref="ResolveBlockAsync"/>.
    /// </param>
    public Task<bool> DismissBlockAsync(string filePath, string shortId, string? resolution = null) =>
        SetExclusiveTagAsync(filePath, shortId, BlockLifecycleTags, target: "dismissed", resolution);

    /// <summary>
    /// Dismiss a task that no longer applies: overtaken by other work, dead, or
    /// a duplicate. This is the second exit tasks lacked until 2026-07-27, when
    /// the only way to close one was to mark it complete - writing into the
    /// vault that work happened when it did not. Most of the 61 open +logmark
    /// tasks were that shape.
    ///
    /// Follows the block pattern (tag plus resolution trailer), not the task
    /// pattern (strikethrough), because a dismissed task must not render as
    /// done. The reason is mandatory at the verb layer: a dismissal without one
    /// is indistinguishable from losing the task.
    /// </summary>
    public Task<bool> DismissTaskAsync(string filePath, string shortId, string? resolution = null) =>
        SetExclusiveTagAsync(filePath, shortId, TaskLifecycleTags, target: "dismissed", resolution);

    private async Task<bool> SetExclusiveTagAsync(string filePath, string shortId, string[] lifecycleSet, string target, string? resolution = null)
    {
        if (!_fileAccessor.Exists(filePath))
        {
            _logger?.Warn($"DecisionApprovalService: file not found: {filePath}");
            return false;
        }

        var text = _fileAccessor.ReadAllText(filePath);
        var result = RewriteEntryTags(text, shortId, lifecycleSet, target, resolution);
        if (!result.found)
        {
            _logger?.Warn($"DecisionApprovalService: shortId {shortId} not found in {filePath}");
            return false;
        }
        if (!result.changed)
        {
            return true;
        }

        await _writer.WriteFileAsync(filePath, result.text);
        _cache.InvalidateFile(filePath);
        return true;
    }

    /// <summary>
    /// Locate the entry block containing <paramref name="shortId"/>, strip any of the
    /// <paramref name="lifecycleSet"/> tokens that appear within it, then ensure
    /// <paramref name="target"/> is present (idempotent: if already the only lifecycle
    /// tag present, returns no-change).
    /// </summary>
    /// <remarks>
    /// Entry block boundaries: the previous "## " header line (inclusive) up to the
    /// next "## " header (exclusive) or end of file. ShortId is matched against the
    /// "_id: SHORTID" metadata line first, falling back to "[SHORTID]" for legacy
    /// task-style entries.
    /// </remarks>
    private static (string text, bool changed, bool found) RewriteEntryTags(
        string text, string shortId, string[] lifecycleSet, string target, string? resolution = null)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        var anchorIndex = FindEntryAnchor(lines, shortId);
        if (anchorIndex < 0) return (text, false, false);

        var (start, end) = FindEntryBlock(lines, anchorIndex);

        var blockChanged = false;
        var targetTagToken = "#" + target;
        var targetPresent = false;

        for (int i = start; i < end; i++)
        {
            // The resolution trailer line ("resolved: <note>") is free text, not a
            // tag line - a note that happens to mention "#resolved" or "#dismissed"
            // as prose (e.g. "reverted the #resolved flag by mistake") must not be
            // mangled by the lifecycle-tag strip sweep below. Skip it entirely.
            if (IsResolutionTrailerLine(lines[i]))
                continue;

            var original = lines[i];
            var rewritten = original;
            foreach (var tag in lifecycleSet)
            {
                if (string.Equals(tag, target, StringComparison.OrdinalIgnoreCase))
                    continue;
                rewritten = StripTagToken(rewritten, tag);
            }
            if (rewritten != original)
            {
                lines[i] = rewritten;
                blockChanged = true;
            }
            if (ContainsTagToken(lines[i], target))
                targetPresent = true;
        }

        if (!targetPresent)
        {
            var insertAt = ChooseTagInsertionPoint(lines, start, end);
            if (insertAt < end)
            {
                lines[insertAt] = lines[insertAt].TrimEnd() + " " + targetTagToken;
            }
            else
            {
                lines.Insert(end, targetTagToken);
            }
            blockChanged = true;
        }

        // Resolution note (blocks only): inserted/updated right after the anchor
        // line, mirroring TaskTracker's placement of its "resolved: ..." trailer
        // right after "_task: ...". Runs after the tag rewrite above so a freshly
        // inserted lifecycle tag line (which may land immediately after the
        // anchor when no other tag-only line exists) ends up AFTER the resolution
        // line, not before it - see UpsertResolutionLine for the index reasoning.
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            if (UpsertResolutionLine(lines, anchorIndex, resolution))
                blockChanged = true;
        }

        if (!blockChanged) return (text, false, true);

        var preserveTrailingNewline = text.EndsWith("\n");
        var joined = string.Join("\n", lines);
        if (preserveTrailingNewline && !joined.EndsWith("\n"))
            joined += "\n";
        return (joined, true, true);
    }

    /// <summary>
    /// Insert or update the "resolved: &lt;note&gt;" trailer line immediately
    /// after <paramref name="anchorIndex"/> (the entry's "_id:" line). If that
    /// line already holds a resolution (e.g. a second resolve/dismiss call with
    /// a different note), it is replaced in place rather than duplicated.
    /// Returns false (no-op) when the note is unchanged, so idempotent calls do
    /// not mark the file as modified.
    /// </summary>
    private static bool UpsertResolutionLine(List<string> lines, int anchorIndex, string resolution)
    {
        var newLine = $"resolved: {NormalizeResolutionNote(resolution)}";
        var nextIndex = anchorIndex + 1;

        if (nextIndex < lines.Count &&
            lines[nextIndex].Trim().StartsWith("resolved:", StringComparison.OrdinalIgnoreCase))
        {
            if (lines[nextIndex].Trim() == newLine) return false;
            lines[nextIndex] = newLine;
            return true;
        }

        lines.Insert(nextIndex, newLine);
        return true;
    }

    /// <summary>
    /// The resolution trailer is serialized as a single markdown line - embedded
    /// newlines in the raw note would otherwise be written verbatim and read back
    /// as a spurious "## " entry header (or other structural line) by EntryParser
    /// on the next load. Collapse any run of \r\n / \n / \r into one space and
    /// trim, degrading gracefully rather than rejecting the note.
    /// </summary>
    private static string NormalizeResolutionNote(string note) =>
        CollapseNewlinesPattern.Replace(note, " ").Trim();

    /// <summary>
    /// True for the "resolved: &lt;note&gt;" trailer line written by
    /// <see cref="UpsertResolutionLine"/>. Used to exempt that line from the
    /// lifecycle-tag strip sweep in <see cref="RewriteEntryTags"/>, since it is
    /// free text and may legitimately contain a lifecycle tag token as prose.
    /// </summary>
    private static bool IsResolutionTrailerLine(string line) =>
        line.TrimStart().StartsWith("resolved:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the metadata line carrying <paramref name="shortId"/>, trying the
    /// three grammars an entry can be written in: the "_id:" anchor, the
    /// "_task:" line the desktop capture path writes for tasks, and the legacy
    /// "[shortid]" bracket form.
    ///
    /// The "_task:" pass was added 2026-07-27 with the task dismissal exit.
    /// Without it this service reached tasks written by session distill (which
    /// emit "_id:") but not tasks written by the app, and a dismissal on one of
    /// those would have failed with "shortId not found" - the exact mirror of
    /// the TaskTracker defect fixed the same day, where completion matched only
    /// "_task:" and silently missed 61 "_id:" tasks.
    /// </summary>
    private static int FindEntryAnchor(List<string> lines, string shortId)
    {
        var idPattern = IdPatternFor(shortId);
        var taskPattern = TaskPatternFor(shortId);
        var bracketPattern = BracketPatternFor(shortId);

        for (int i = 0; i < lines.Count; i++)
        {
            if (idPattern.IsMatch(lines[i].Trim())) return i;
        }
        for (int i = 0; i < lines.Count; i++)
        {
            if (taskPattern.IsMatch(lines[i].Trim())) return i;
        }
        for (int i = 0; i < lines.Count; i++)
        {
            if (bracketPattern.IsMatch(lines[i])) return i;
        }
        return -1;
    }

    private static (int start, int end) FindEntryBlock(List<string> lines, int anchorIndex)
    {
        int start = anchorIndex;
        for (int i = anchorIndex; i >= 0; i--)
        {
            if (lines[i].StartsWith("## "))
            {
                start = i;
                break;
            }
            start = i;
        }

        int end = lines.Count;
        for (int i = anchorIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ") || lines[i].TrimEnd() == "---")
            {
                end = i;
                break;
            }
        }
        return (start, end);
    }

    private static string StripTagToken(string line, string tag)
    {
        var pattern = StripRegexFor(tag);
        if (!pattern.IsMatch(line)) return line;
        var stripped = pattern.Replace(line, " ");
        return CollapseWhitespacePattern.Replace(stripped, " ").Trim();
    }

    private static bool ContainsTagToken(string line, string tag) =>
        ContainsRegexFor(tag).IsMatch(line);

    private static Regex StripRegexFor(string tag) =>
        StripPatterns.GetOrAdd("strip:" + tag.ToLowerInvariant(), _ =>
            new Regex($@"\s*#{Regex.Escape(tag)}\b\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));

    private static Regex ContainsRegexFor(string tag) =>
        StripPatterns.GetOrAdd("contains:" + tag.ToLowerInvariant(), _ =>
            new Regex($@"(^|\s)#{Regex.Escape(tag)}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));

    private static Regex IdPatternFor(string shortId) =>
        StripPatterns.GetOrAdd("id:" + shortId.ToLowerInvariant(), _ =>
            new Regex($@"^_id:\s*{Regex.Escape(shortId)}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));

    /// <summary>
    /// The task metadata grammar: "_task: &lt;deadline&gt; | postponed: N | id"
    /// with an optional " ^id" anchor suffix, optionally wrapped in "~~" when
    /// the task is completed. The id is the last field, so anchor on it rather
    /// than trying to re-parse the whole line here.
    /// </summary>
    private static Regex TaskPatternFor(string shortId) =>
        StripPatterns.GetOrAdd("task:" + shortId.ToLowerInvariant(), _ =>
            new Regex($@"^(?:~~)?_task:.*\|\s*{Regex.Escape(shortId)}\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));

    private static Regex BracketPatternFor(string shortId) =>
        StripPatterns.GetOrAdd("bracket:" + shortId.ToLowerInvariant(), _ =>
            new Regex($@"\[{Regex.Escape(shortId)}\]",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));

    private static bool IsTagLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return false;
        return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(t => t.StartsWith("#"));
    }

    /// <summary>
    /// Pick the line within [start, end) where the target tag should be appended.
    /// Prefer an existing tag-only line; otherwise return end (signaling "insert
    /// new line at end").
    /// </summary>
    private static int ChooseTagInsertionPoint(List<string> lines, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            if (IsTagLine(lines[i]))
                return i;
        }
        return end;
    }
}
