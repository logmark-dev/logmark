using System.IO;
using System.Linq;
using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.Sensitivity;
using LogMark.Services.Timetrack;

namespace LogMark.Services.EntryParser;

/// <summary>
/// Result of an organize operation (reroute, convert).
/// </summary>
public class OrganizeResult
{
    public bool Success { get; init; }
    public string? TargetFilePath { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Vault-relative paths of referrer files whose inbound wikilinks were
    /// rewritten to the entry's new location (lmk-jugm). Empty for operations
    /// that do not move the entry, when the entry has no ShortId anchor, or
    /// when nothing links to it. Callers holding an EntryCache should
    /// invalidate these files.
    /// </summary>
    public IReadOnlyList<string> RewrittenLinkFiles { get; init; } = Array.Empty<string>();

    public static OrganizeResult Ok(string targetFilePath, IReadOnlyList<string>? rewrittenLinkFiles = null) =>
        new() { Success = true, TargetFilePath = targetFilePath, RewrittenLinkFiles = rewrittenLinkFiles ?? Array.Empty<string>() };

    public static OrganizeResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Composes EntrySerializer operations into higher-level organize verbs.
/// Operates on file paths and vault structure.
/// </summary>
public class EntryOrganizer
{
    private readonly string _vaultPath;
    private readonly IVaultFileWriter _writer;
    private readonly IVaultWriteGate? _writeGate;

    public EntryOrganizer(string vaultPath, IVaultFileWriter writer, IVaultWriteGate? writeGate = null)
    {
        _vaultPath = vaultPath;
        _writer = writer;
        _writeGate = writeGate;
    }

    /// <summary>
    /// Move an entry from one file to a different route.
    /// Appends an amend trail noting the reroute origin.
    /// <paramref name="contentTransform"/>, when supplied, is applied to the
    /// extracted raw entry before the amend trail is appended - e.g. to rewrite
    /// a wikilink mid-move. Null (default) moves the entry verbatim.
    /// </summary>
    public OrganizeResult RerouteEntry(string sourceFilePath, DateTime timestamp,
        string targetRoutePath, EntryType entryType, string? shortId = null,
        Func<string, string>? contentTransform = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);

        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        var markdown = File.ReadAllText(absoluteSource);
        // ShortId disambiguates when several entries share one minute-precise timestamp.
        var (extracted, remainder) = EntrySerializer.ExtractEntry(markdown, timestamp, shortId);

        if (extracted == null)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in {sourceFilePath}");

        if (contentTransform != null)
            extracted = contentTransform(extracted);

        // Derive source route label from the source file path
        var sourceLabel = DeriveRouteLabel(sourceFilePath);

        // Append amend trail to the extracted entry
        var amendedEntry = AppendAmendToRawEntry(
            extracted,
            $"rerouted from {sourceLabel}, {DateTime.Now:yyyy-MM-dd HH:mm}, system");

        // Build target file path
        var typeFolderName = EntryTypeMapper.GetFolderName(entryType);
        var year = timestamp.Year;
        var targetRelative = PathConstants.GetRoutedFilePath(targetRoutePath, typeFolderName, year);
        var absoluteTarget = ToAbsolute(targetRelative);

        // No-op guard: rerouting to the route the entry already lives in. The two-write
        // move below (write target = entry added, then write source = entry removed)
        // would clobber the target with the entry-removed remainder when the paths are
        // the same file, deleting the entry. Leave it untouched. See 2026-06-08 trace.
        if (SameFile(absoluteSource, absoluteTarget))
            return OrganizeResult.Ok(targetRelative);

        if (_writeGate != null)
        {
            _writeGate.RewriteFileAsync(absoluteTarget, BuildCombinedFileContent(absoluteTarget, amendedEntry)).GetAwaiter().GetResult();
            _writeGate.RewriteFileAsync(absoluteSource, remainder).GetAwaiter().GetResult();
        }
        else
        {
            // Fallback path: gate not wired (unit tests, legacy callers).
            // WriteFile is on IVaultFileWriter; the [Obsolete] attribute is on VaultWriter's
            // concrete surface but the interface method is not marked obsolete.
            // Ensure target directory exists and append entry
            AppendEntryToFile(absoluteTarget, amendedEntry);

            // Write updated source file (entry removed)
            _writer.WriteFile(absoluteSource, remainder);
        }

        PruneEmptyAfterExtract(absoluteSource);

        var rewritten = RewriteInboundAnchorLinks(
            ToVaultRelative(absoluteSource), targetRelative, shortId);

        return OrganizeResult.Ok(targetRelative, rewritten);
    }

    /// <summary>
    /// Convert an entry from one type to another, moving it to the appropriate type folder.
    /// Appends an amend trail noting the conversion.
    /// </summary>
    public OrganizeResult ConvertEntryType(string sourceFilePath, DateTime timestamp, EntryType newType, string? shortId = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);

        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        var markdown = File.ReadAllText(absoluteSource);

        // Find entry to get its current type for the amend trail. ShortId disambiguates
        // when several entries share one minute-precise timestamp in this file.
        // The source folder's type hint is required: without it every saved entry
        // parses as the default type, which both mislabels the amend trail and
        // makes StripSourceTypeSignal below miss the metadata it must strip.
        var entry = EntryParser.FindEntry(
            markdown, timestamp, shortId, EntryTypeMapper.InferTypeFromPath(absoluteSource));
        if (entry == null)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in {sourceFilePath}");

        var oldTypeDisplay = EntryTypeMapper.GetDisplayName(entry.Type).ToLowerInvariant();

        // Extract the entry (same ShortId disambiguation as the find above)
        var (extracted, remainder) = EntrySerializer.ExtractEntry(markdown, timestamp, shortId);
        if (extracted == null)
            return OrganizeResult.Fail($"Failed to extract entry at {timestamp:yyyy-MM-dd HH:mm}");

        // Strip the SOURCE type's type-determining metadata before the move.
        // EntryParser.DetermineType checks workflow metadata, then task metadata,
        // and only THEN the destination folder's type hint -- so without this,
        // converting a Task or Timetrack entry moves the file but the entry keeps
        // re-parsing as its old type: a silent no-op that still reports Success.
        extracted = StripSourceTypeSignal(extracted, entry);

        // Append convert amend trail
        var amendedEntry = AppendAmendToRawEntry(
            extracted,
            $"converted from {oldTypeDisplay}, {DateTime.Now:yyyy-MM-dd HH:mm}, system");

        // Determine target path based on whether source is routed or unrouted.
        // ParsePath expects a vault-relative path -- callers (NativeViewer's
        // ResolveEntryFilePathForToggle) may pass an absolute path, which would
        // make ParsePath return null and drop the entry into _inbox instead of
        // the source route. Normalize to vault-relative first.
        var parsed = PathConstants.ParsePath(ToVaultRelative(absoluteSource));
        var newTypeFolderName = EntryTypeMapper.GetFolderName(newType);
        var year = timestamp.Year;

        string targetRelative;
        if (parsed?.Route != null)
        {
            // Routed: same route, different type folder
            targetRelative = PathConstants.GetRoutedFilePath(parsed.Route, newTypeFolderName, year);
        }
        else
        {
            // Unrouted: logmark/_inbox/_{newTypeFolderName}/{year}.md
            targetRelative = PathConstants.GetUnroutedFilePath(newTypeFolderName, year);
        }

        var absoluteTarget = ToAbsolute(targetRelative);

        // No-op guard: converting to the same type the entry already has resolves to
        // the same file. The two-write move below would delete the entry (see the
        // matching guard in RerouteEntry). Leave it untouched.
        if (SameFile(absoluteSource, absoluteTarget))
            return OrganizeResult.Ok(targetRelative);

        if (_writeGate != null)
        {
            _writeGate.RewriteFileAsync(absoluteTarget, BuildCombinedFileContent(absoluteTarget, amendedEntry)).GetAwaiter().GetResult();
            _writeGate.RewriteFileAsync(absoluteSource, remainder).GetAwaiter().GetResult();
        }
        else
        {
            // Append entry to target
            AppendEntryToFile(absoluteTarget, amendedEntry);

            // Write updated source file (entry removed)
            _writer.WriteFile(absoluteSource, remainder);
        }

        PruneEmptyAfterExtract(absoluteSource);

        var rewritten = RewriteInboundAnchorLinks(
            ToVaultRelative(absoluteSource), targetRelative, shortId);

        return OrganizeResult.Ok(targetRelative, rewritten);
    }

    /// <summary>
    /// Strip the type-determining metadata that would otherwise make a moved
    /// entry keep re-parsing as its old type (see ConvertEntryType). Task carries
    /// a discrete "_task: ... | shortId" metadata line -- that line is the
    /// entry's ONLY identity anchor (Task entries get no separate "_id:" line;
    /// see TestVaultBuilder.WithEntry), so it is rewritten to a plain
    /// "_id: shortId ^shortId" line rather than deleted outright, which would
    /// silently strand the entry with no ShortId anchor and break
    /// EntryCache.ResolveShortId on it after the move. Timetrack has no
    /// discrete metadata line -- the type signal is the trailing " (Nh Nm)"/
    /// " (running)" suffix on the entry's own content line -- so that suffix is
    /// stripped directly via StripWorkflowSuffix. Every other source type
    /// (Idea, Block, Decision, Question, Gap, Analogy, Log) has no
    /// type-determining metadata; no-op.
    /// </summary>
    private static string StripSourceTypeSignal(string extractedEntry, ParsedEntry sourceEntry)
    {
        return sourceEntry.Type switch
        {
            EntryType.Task => ReplaceTaskMetaWithIdLine(extractedEntry, sourceEntry.ShortId),
            EntryType.Timetrack when sourceEntry.WorkflowMeta != null =>
                StripWorkflowSuffix(extractedEntry, sourceEntry.WorkflowMeta),
            _ => extractedEntry
        };
    }

    /// <summary>
    /// Replace the "_task: deadline | postponed: N | shortId ^shortId" line
    /// with "_id: shortId ^shortId", dropping the task-specific fields
    /// (deadline, postponed count) that no longer apply once the entry is a
    /// different type, while preserving the ShortId anchor so the entry stays
    /// resolvable by ID after the move. Also matches the strikethrough form
    /// EntryParser.TaskMetadataPattern recognizes for a completed task
    /// ("~~_task: ... | shortId ^shortId~~", written by
    /// TaskTracker.UpdateMarkdownOnComplete) -- otherwise a completed task's
    /// struck-through metadata line still satisfies TaskMetadataPattern after
    /// conversion, so the entry keeps re-parsing as EntryType.Task. The
    /// replacement line is always the clean, un-struck "_id: shortId
    /// ^shortId" form: a converted entry is no longer a completed task, so
    /// neither the strikethrough nor the task fields should carry over. No-op
    /// (returns entryText unchanged) if the entry has no ShortId or no
    /// "_task:" line (plain or struck-through) is found.
    /// </summary>
    private static string ReplaceTaskMetaWithIdLine(string entryText, string? shortId)
    {
        if (string.IsNullOrEmpty(shortId))
            return entryText;

        var lines = entryText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedEnd = line.TrimEnd('\r');
            if (TaskMetaLinePattern.IsMatch(trimmedEnd.TrimStart()))
            {
                var hadCr = line.Length != trimmedEnd.Length;
                lines[i] = $"_id: {shortId} ^{shortId}" + (hadCr ? "\r" : "");
                break;
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Recognizes a task metadata line in either its plain form
    /// ("_task: ...") or the strikethrough form a completed task is written
    /// in ("~~_task: ...~~"), mirroring the optional leading "~~" that
    /// EntryParser.TaskMetadataPattern also tolerates. Only the leading
    /// marker is checked (not the trailing "~~" or field structure) -- the
    /// passed-in ShortId, not anything parsed from the line, drives the
    /// replacement text.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex TaskMetaLinePattern =
        new(@"^(?:~~)?_task:\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Remove the trailing " (Nh Nm)" / " (running)" suffix that marks an
    /// entry's content line as workflow-shaped (EntryParser.WorkflowLinePattern),
    /// then drop the "timeline:" block entirely (the label line plus its
    /// "- HH:mm-HH:mm (...)" / "- HH:mm-???" segment lines). Both steps are
    /// required: EntryParser's workflow check runs on EVERY line, not just the
    /// first, so leaving a timeline segment line in place (itself shaped like
    /// "text (duration)") would re-trigger the workflow match and silently
    /// re-classify the converted entry as Timetrack all over again. The
    /// timeline block is always machine-generated by TimetrackFormatter, never
    /// user-authored freeform text, so dropping it on conversion away from
    /// Timetrack is safe.
    /// Reconstructs the exact suffix from the already-parsed WorkflowMetadata
    /// (the same TimetrackFormatter the timetrack writer uses) rather than
    /// re-deriving it with a fresh regex, so this stays in lockstep with
    /// whatever EntryParser/TimetrackFormatter consider a valid duration.
    /// </summary>
    private static string StripWorkflowSuffix(string entryText, WorkflowMetadata meta)
    {
        var suffix = meta.IsRunning
            ? " (running)"
            : $" ({TimetrackFormatter.FormatDuration(meta.TotalDuration)})";

        var lines = entryText.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmedEnd = line.TrimEnd('\r');
            if (trimmedEnd.EndsWith(suffix, StringComparison.Ordinal))
            {
                var hadCr = line.Length != trimmedEnd.Length;
                lines[i] = trimmedEnd[..^suffix.Length] + (hadCr ? "\r" : "");
                break;
            }
        }

        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            if (trimmed.Equals("timeline:", StringComparison.OrdinalIgnoreCase)
                || TimelineSegmentLinePattern.IsMatch(trimmed))
            {
                lines.RemoveAt(i);
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Mirrors EntryParser's private TimelineSegmentPattern
    /// (^-\s*(\d{2}):(\d{2})-(\S+)) -- matches "- HH:mm-HH:mm" and
    /// "- HH:mm-???" timeline segment lines so StripWorkflowSuffix can drop
    /// them alongside the "timeline:" label.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex TimelineSegmentLinePattern =
        new(@"^-\s*(\d{2}):(\d{2})-(\S+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>True when both paths resolve to the same file (case-insensitive, normalized).</summary>
    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strip a redundant leading type prefix ("g:", "gap:", ...) from a single
    /// entry's first content line, in place. Type and route are unchanged. Idempotent:
    /// an entry with no leading prefix is a successful no-op.
    /// </summary>
    public OrganizeResult StripLeadingPrefix(string sourceFilePath, DateTime timestamp, string? shortId = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);
        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        var markdown = File.ReadAllText(absoluteSource);
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);
        if (entry == null)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in {sourceFilePath}");

        var lines = markdown.Split('\n');
        var headerIdx = entry.StartLine - 1;        // 0-based "## ..." line
        var firstContent = -1;
        for (int i = headerIdx + 1; i < entry.EndLine && i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i].TrimEnd('\r'))) { firstContent = i; break; }
        }
        if (firstContent < 0)
            return OrganizeResult.Ok(ToVaultRelative(absoluteSource)); // empty entry: nothing to strip

        var stripped = EntryPrefix.StripLeading(lines[firstContent].TrimEnd('\r'));
        if (stripped == null)
            return OrganizeResult.Ok(ToVaultRelative(absoluteSource)); // no prefix: idempotent no-op

        lines[firstContent] = stripped;
        var updated = string.Join("\n", lines);

        if (_writeGate != null)
            _writeGate.RewriteFileAsync(absoluteSource, updated).GetAwaiter().GetResult();
        else
            _writer.WriteFile(absoluteSource, updated);

        return OrganizeResult.Ok(ToVaultRelative(absoluteSource));
    }

    /// <summary>
    /// Append an amend trail line to the entry at the given timestamp, in place.
    /// Writes through the gate so private-route masking is preserved.
    /// The shortId parameter disambiguates when several entries share one
    /// minute-precise timestamp, matching RerouteEntry/ConvertEntryType/
    /// StripLeadingPrefix; it is threaded to EntrySerializer.AmendEntry.
    /// </summary>
    public OrganizeResult AmendEntry(string sourceFilePath, DateTime timestamp,
        string amendText, string author, string? shortId = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);
        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        if (string.IsNullOrWhiteSpace(amendText))
            return OrganizeResult.Fail("Amend text is empty");

        var markdown = File.ReadAllText(absoluteSource);
        var updated = EntrySerializer.AmendEntry(markdown, timestamp, amendText, author, shortId: shortId);

        if (updated == markdown)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in {sourceFilePath}");

        if (_writeGate != null)
            _writeGate.RewriteFileAsync(absoluteSource, updated).GetAwaiter().GetResult();
        else
            _writer.WriteFile(absoluteSource, updated);

        return OrganizeResult.Ok(ToVaultRelative(absoluteSource));
    }

    /// <summary>
    /// Add and/or remove inline #tags on the entry at <paramref name="timestamp"/>,
    /// in place. Wraps the pure <see cref="EntrySerializer.RetagEntry"/>. Idempotent:
    /// adding a tag already present or removing a tag not present returns Ok with no
    /// write. Same gate-aware write path as AmendEntry so private-route masking holds.
    /// </summary>
    public OrganizeResult RetagEntry(string sourceFilePath, DateTime timestamp,
        IReadOnlyList<string> addTags, IReadOnlyList<string> removeTags, string? shortId = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);
        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        if ((addTags == null || addTags.Count == 0) && (removeTags == null || removeTags.Count == 0))
            return OrganizeResult.Fail("Provide at least one tag to add or remove");

        var markdown = File.ReadAllText(absoluteSource);
        var updated = EntrySerializer.RetagEntry(
            markdown, timestamp, shortId,
            addTags ?? Array.Empty<string>(), removeTags ?? Array.Empty<string>());

        if (updated != markdown)
        {
            if (_writeGate != null)
                _writeGate.RewriteFileAsync(absoluteSource, updated).GetAwaiter().GetResult();
            else
                _writer.WriteFile(absoluteSource, updated);
        }

        return OrganizeResult.Ok(ToVaultRelative(absoluteSource));
    }

    /// <summary>
    /// Insert a wiki link from the entry at <paramref name="timestamp"/> to a page.
    /// Wraps the pure <see cref="EntrySerializer.InsertWikiLink"/>, which is
    /// scoped-idempotent: it returns the input unchanged when a link to the same
    /// page is already present. The caller resolves the entry by ShortId before
    /// calling, so an unchanged result means "already linked", not "entry
    /// missing" - so this returns Ok (no write) rather than Fail in that case.
    /// <paramref name="shortId"/> disambiguates same-minute siblings; when null the
    /// first timestamp match wins. Same in-place, gate-aware write path as AmendEntry.
    /// </summary>
    public OrganizeResult LinkEntryToPage(string sourceFilePath, DateTime timestamp,
        string wikiPath, string displayText, string? shortId = null)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);
        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        if (string.IsNullOrWhiteSpace(wikiPath))
            return OrganizeResult.Fail("Wiki path is empty");

        var markdown = File.ReadAllText(absoluteSource);
        var updated = EntrySerializer.InsertWikiLink(markdown, timestamp, wikiPath, displayText, shortId);

        if (updated != markdown)
        {
            if (_writeGate != null)
                _writeGate.RewriteFileAsync(absoluteSource, updated).GetAwaiter().GetResult();
            else
                _writer.WriteFile(absoluteSource, updated);
        }

        return OrganizeResult.Ok(ToVaultRelative(absoluteSource));
    }

    /// <summary>
    /// Soft-delete an entry: extract it from its source file and move it to
    /// logmark/_system/_trash/{year}.md, stamping a _trashed: machine field
    /// (origin path + timestamp) and a human deletion trail. Recoverable via
    /// RestoreEntry. The source file gets the entry removed.
    /// <paramref name="movePages"/> gates the linked-subpage move below: false
    /// skips it entirely (the page file stays where it is, no _trashed_page:
    /// stamp) - needed when the page IS the record of truth (e.g. a staged
    /// memory's import ledger) and moving it would let a re-import resurrect it.
    /// </summary>
    public OrganizeResult TrashEntry(string sourceFilePath, DateTime timestamp,
        string? shortId = null, bool movePages = true)
    {
        var absoluteSource = ToAbsolute(sourceFilePath);

        if (!File.Exists(absoluteSource))
            return OrganizeResult.Fail($"Source file not found: {sourceFilePath}");

        var markdown = File.ReadAllText(absoluteSource);
        // ShortId disambiguates when several entries share one minute-precise timestamp.
        var (extracted, remainder) = EntrySerializer.ExtractEntry(markdown, timestamp, shortId);

        if (extracted == null)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in {sourceFilePath}");

        var originRelative = ToVaultRelative(absoluteSource).Replace('\\', '/');
        var routeLabel = DeriveRouteLabel(sourceFilePath);

        // Detect and move a linked subpage, if any.
        string? pageOrigin = null;
        if (movePages)
        {
            var pageLink = ContentParser.GetPageLinks(extracted).FirstOrDefault();
            if (pageLink != null)
            {
                var resolvedPage = ResolveSubpageOrigin(pageLink.Path, absoluteSource);
                if (resolvedPage != null && File.Exists(resolvedPage))
                {
                    pageOrigin = ToVaultRelative(resolvedPage).Replace('\\', '/');
                    var trashPageAbsolute = Path.Combine(
                        ToAbsolute(PathConstants.GetTrashPagesFolder()),
                        TrashedPageFileName(pageOrigin));
                    Directory.CreateDirectory(Path.GetDirectoryName(trashPageAbsolute)!);
                    File.Move(resolvedPage, trashPageAbsolute, overwrite: false);
                }
            }
        }

        var stamped = StampTrashTrail(extracted, originRelative, routeLabel, DateTime.Now, pageOrigin);

        var trashRelative = PathConstants.GetTrashFilePath(timestamp.Year);
        var absoluteTrash = ToAbsolute(trashRelative);

        if (_writeGate != null)
        {
            _writeGate.RewriteFileAsync(absoluteTrash, BuildCombinedFileContent(absoluteTrash, stamped)).GetAwaiter().GetResult();
            _writeGate.RewriteFileAsync(absoluteSource, remainder).GetAwaiter().GetResult();
        }
        else
        {
            AppendEntryToFile(absoluteTrash, stamped);
            _writer.WriteFile(absoluteSource, remainder);
        }

        PruneEmptyAfterExtract(absoluteSource);

        return OrganizeResult.Ok(trashRelative);
    }

    /// <summary>
    /// Restore a trashed entry to its origin file (read from the _trashed:
    /// field), stripping the _trashed:/_trashed_page: machine fields. The human
    /// "> [deleted ...]" trail is left in place as history. Recreates the origin
    /// file/folders if the route was removed while the entry was in trash.
    /// </summary>
    public OrganizeResult RestoreEntry(string trashFilePath, DateTime timestamp, string? shortId = null)
    {
        var absoluteTrash = ToAbsolute(trashFilePath);

        if (!File.Exists(absoluteTrash))
            return OrganizeResult.Fail($"Trash file not found: {trashFilePath}");

        var markdown = File.ReadAllText(absoluteTrash);

        // ShortId disambiguates when several trashed entries share one minute-precise timestamp.
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);
        if (entry == null)
            return OrganizeResult.Fail($"Entry not found at {timestamp:yyyy-MM-dd HH:mm} in trash");

        if (!entry.ExtendedMetadata.TryGetValue("_trashed", out var trashedValue))
        {
            trashedValue = ScanRawField(markdown, timestamp, "_trashed", shortId);
            if (trashedValue == null)
                return OrganizeResult.Fail("Entry has no _trashed: origin metadata");
        }

        var originRelative = trashedValue.Split('|', 2)[0].Trim();

        var (extracted, remainder) = EntrySerializer.ExtractEntry(markdown, timestamp, shortId);
        if (extracted == null)
            return OrganizeResult.Fail($"Failed to extract entry at {timestamp:yyyy-MM-dd HH:mm} from trash");

        // Restore a moved subpage, if recorded.
        var pageOrigin = entry.ExtendedMetadata.TryGetValue("_trashed_page", out var pageVal)
            ? pageVal
            : ScanRawField(markdown, timestamp, "_trashed_page", shortId);
        if (!string.IsNullOrWhiteSpace(pageOrigin))
        {
            var trashPageAbsolute = Path.Combine(
                ToAbsolute(PathConstants.GetTrashPagesFolder()),
                TrashedPageFileName(pageOrigin));
            if (File.Exists(trashPageAbsolute))
            {
                var originPageAbsolute = ToAbsolute(pageOrigin);
                Directory.CreateDirectory(Path.GetDirectoryName(originPageAbsolute)!);
                File.Move(trashPageAbsolute, originPageAbsolute, overwrite: true);
            }
        }

        var cleaned = EntrySerializer.RemoveMetadataField(extracted, timestamp, "_trashed", shortId);
        cleaned = EntrySerializer.RemoveMetadataField(cleaned, timestamp, "_trashed_page", shortId);

        var absoluteOrigin = ToAbsolute(originRelative);

        if (_writeGate != null)
        {
            _writeGate.RewriteFileAsync(absoluteOrigin, BuildCombinedFileContent(absoluteOrigin, cleaned)).GetAwaiter().GetResult();
            _writeGate.RewriteFileAsync(absoluteTrash, remainder).GetAwaiter().GetResult();
        }
        else
        {
            AppendEntryToFile(absoluteOrigin, cleaned);
            _writer.WriteFile(absoluteTrash, remainder);
        }

        PruneEmptyAfterExtract(absoluteTrash);

        return OrganizeResult.Ok(originRelative);
    }

    /// <summary>
    /// Enumerate all trashed entries by reading logmark/_system/_trash/*.md
    /// directly (the trash store is intentionally excluded from EntryCache).
    /// Returns empty when the trash folder does not exist.
    /// </summary>
    public IReadOnlyList<TrashedEntry> ListTrash()
    {
        var trashFolderAbsolute = ToAbsolute(PathConstants.GetTrashFolder());
        if (!Directory.Exists(trashFolderAbsolute))
            return Array.Empty<TrashedEntry>();

        var results = new List<TrashedEntry>();

        foreach (var file in Directory.EnumerateFiles(trashFolderAbsolute, "*.md", SearchOption.TopDirectoryOnly))
        {
            var markdown = File.ReadAllText(file);
            var trashRelative = ToVaultRelative(file).Replace('\\', '/');

            foreach (var entry in EntryParser.Parse(markdown))
            {
                var originPath = entry.ExtendedMetadata.TryGetValue("_trashed", out var v)
                    ? v.Split('|', 2)[0].Trim()
                    : "";
                var originLabel = DeriveRouteLabel(originPath);
                results.Add(new TrashedEntry(entry, trashRelative, originLabel, originPath));
            }
        }

        return results
            .OrderByDescending(t => t.Entry.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Permanently delete everything under logmark/_system/_trash/.
    /// </summary>
    public OrganizeResult EmptyTrash()
    {
        var trashFolderAbsolute = ToAbsolute(PathConstants.GetTrashFolder());
        if (!Directory.Exists(trashFolderAbsolute))
            return OrganizeResult.Ok(PathConstants.GetTrashFolder());

        Directory.Delete(trashFolderAbsolute, recursive: true);
        return OrganizeResult.Ok(PathConstants.GetTrashFolder());
    }

    /// <summary>
    /// Insert the _trashed: machine field (and optional _trashed_page:) plus a
    /// human "> [deleted from ...]" trail into a raw extracted entry, before the
    /// first existing metadata line. The _trashed: line is itself metadata
    /// (matches ^[a-z_]+: .+) so it round-trips through extract/parse.
    /// </summary>
    private static string StampTrashTrail(string entryText, string originRelative,
        string routeLabel, DateTime now, string? pageOrigin)
    {
        var lines = entryText.Split('\n').ToList();

        int firstMetadataIndex = -1;
        int lastContentOrAmendIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;
            if (EntrySerializer.IsMetadataLinePublic(trimmed))
            {
                if (firstMetadataIndex < 0) firstMetadataIndex = i;
            }
            else
            {
                lastContentOrAmendIndex = i;
            }
        }

        int insertIndex = firstMetadataIndex >= 0
            ? firstMetadataIndex
            : (lastContentOrAmendIndex >= 0 ? lastContentOrAmendIndex + 1 : lines.Count);

        var trail = new List<string> { $"_trashed: {originRelative} | {now:yyyy-MM-dd HH:mm}" };
        if (pageOrigin != null)
            trail.Add($"_trashed_page: {pageOrigin}");
        trail.Add($"> [deleted from {routeLabel}, {now:yyyy-MM-dd HH:mm}, user]");

        lines.InsertRange(insertIndex, trail);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Append an amend trail line to a raw extracted entry (single entry, not full file).
    /// Inserts after content/existing amends, before metadata lines.
    /// </summary>
    private static string AppendAmendToRawEntry(string entryText, string trailContent)
    {
        var lines = entryText.Split('\n').ToList();
        var amendLine = $"> [{trailContent}]";

        // Find insertion point: after last content/amend line, before first metadata line
        int insertIndex = lines.Count;
        int firstMetadataIndex = -1;
        int lastContentOrAmendIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            // Skip header and empty lines
            if (trimmed.StartsWith("##") || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (EntrySerializer.IsMetadataLinePublic(trimmed))
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
            insertIndex = firstMetadataIndex;
        }
        else if (lastContentOrAmendIndex >= 0)
        {
            insertIndex = lastContentOrAmendIndex + 1;
        }

        lines.Insert(insertIndex, amendLine);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// After an extract rewrites a file with the leftover remainder, tidy up if
    /// that file is now empty: delete it, then remove empty ancestor folders up
    /// to (but not including) the logmark root. Prevents phantom type chips and
    /// empty route rows after the last entry in a route/type is moved or deleted.
    /// Stops at the first ancestor that still contains any file (recursively).
    /// </summary>
    private void PruneEmptyAfterExtract(string absoluteFilePath)
    {
        var full = Path.GetFullPath(absoluteFilePath);

        if (File.Exists(full))
        {
            // Only prune when the file has no remaining entry content.
            if (!string.IsNullOrWhiteSpace(File.ReadAllText(full)))
                return;
            File.Delete(full);
        }

        var logmarkRoot = Path.GetFullPath(Path.Combine(_vaultPath, "logmark"));
        var dir = Path.GetDirectoryName(full);

        while (dir != null
               && Directory.Exists(dir)
               && IsStrictlyUnder(dir, logmarkRoot)
               && !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
        {
            Directory.Delete(dir, recursive: true); // removes empty scaffolding (e.g. an empty pages/)
            dir = Path.GetDirectoryName(dir);
        }
    }

    /// <summary>
    /// Rewrite inbound path-based wikilinks after a move (lmk-jugm): any
    /// "[[&lt;oldFile&gt;#&lt;shortId&gt;...]]" in the vault now points at a dead anchor,
    /// because the anchor travelled with the entry to the new file. Id-based
    /// refs survive a move untouched; path#anchor links are the only casualty,
    /// so only they are rewritten - a file-level link "[[&lt;oldFile&gt;]]" without
    /// this entry's anchor still points at a real file and is left alone.
    ///
    /// Matching is a literal "&lt;path&gt;#&lt;shortId&gt;" replace over four needle
    /// shapes: the vault-relative path with and without its ".md" extension,
    /// each with and without the "logmark/" prefix (both forms occur in the
    /// wild; DistillPromotion writes prefixed, extensionless). Prefixed
    /// needles are replaced first so the unprefixed needle - a substring of
    /// the prefixed one - cannot corrupt a prefixed match. The replacement
    /// always uses the extensionless form of the new path, and preserves the
    /// prefix style of the match. ShortIds are 8-char unique, so a false
    /// positive requires the full old path AND the id - not constructible by
    /// accident.
    ///
    /// Best-effort by design: a scan or write failure on one referrer never
    /// fails the move that already happened - the entry is safe, a stale link
    /// is the pre-existing behaviour. Returns the vault-relative paths of the
    /// files rewritten so callers can invalidate their cache.
    /// </summary>
    private IReadOnlyList<string> RewriteInboundAnchorLinks(
        string oldRelative, string newRelative, string? shortId)
    {
        if (string.IsNullOrEmpty(shortId))
            return Array.Empty<string>();

        var oldNorm = oldRelative.Replace('\\', '/');
        var newNorm = newRelative.Replace('\\', '/');
        var oldNoExt = TrimMd(oldNorm);
        var newNoExt = TrimMd(newNorm);
        if (string.Equals(oldNoExt, newNoExt, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        const string prefix = "logmark/";
        var newUnprefixed = newNoExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? newNoExt[prefix.Length..] : newNoExt;

        // (needle, replacement) in replacement-safe order: prefixed first.
        var replacements = new List<(string Needle, string Replacement)>
        {
            ($"{oldNoExt}.md#{shortId}", $"{newNoExt}#{shortId}"),
            ($"{oldNoExt}#{shortId}", $"{newNoExt}#{shortId}"),
        };
        if (oldNoExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var oldUnprefixed = oldNoExt[prefix.Length..];
            replacements.Add(($"{oldUnprefixed}.md#{shortId}", $"{newUnprefixed}#{shortId}"));
            replacements.Add(($"{oldUnprefixed}#{shortId}", $"{newUnprefixed}#{shortId}"));
        }

        var rewritten = new List<string>();
        try
        {
            var logmarkRoot = Path.Combine(_vaultPath, "logmark");
            if (!Directory.Exists(logmarkRoot))
                return rewritten;

            foreach (var file in Directory.EnumerateFiles(logmarkRoot, "*.md", SearchOption.AllDirectories))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    // Cheap prefilter: no shortId, no possible needle.
                    if (!content.Contains(shortId, StringComparison.Ordinal))
                        continue;

                    var updated = content;
                    foreach (var (needle, replacement) in replacements)
                        updated = updated.Replace(needle, replacement, StringComparison.OrdinalIgnoreCase);
                    if (ReferenceEquals(updated, content) || updated == content)
                        continue;

                    if (_writeGate != null)
                        _writeGate.RewriteFileAsync(file, updated).GetAwaiter().GetResult();
                    else
                        _writer.WriteFile(file, updated);

                    rewritten.Add(ToVaultRelative(file).Replace('\\', '/'));
                }
                catch
                {
                    // One unreadable/unwritable referrer must not fail the move.
                }
            }
        }
        catch
        {
            // Enumeration failure: the move already succeeded; report what was done.
        }
        return rewritten;
    }

    private static string TrimMd(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;

    /// <summary>
    /// True if <paramref name="candidate"/> is a strict descendant of
    /// <paramref name="root"/> (not equal to it). Case-insensitive (Windows).
    /// </summary>
    private static bool IsStrictlyUnder(string candidate, string root)
    {
        var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return false;
        return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Append an entry to a file, with --- separator if the file already has content.
    /// Creates the file and directory if they don't exist.
    /// </summary>
    private void AppendEntryToFile(string absoluteTarget, string entryText)
    {
        _writer.WriteFile(absoluteTarget, BuildCombinedFileContent(absoluteTarget, entryText));
    }

    /// <summary>
    /// Produces the combined file content that AppendEntryToFile would write.
    /// Used by both the legacy fallback path and the gate path so they stay in sync.
    /// If the target directory doesn't exist yet, creates it (gate path needs this
    /// because RewriteFileAsync does not create parent directories).
    /// </summary>
    private static string BuildCombinedFileContent(string absoluteTarget, string entryText)
    {
        if (File.Exists(absoluteTarget))
        {
            var existing = File.ReadAllText(absoluteTarget);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing.TrimEnd() + "\n\n---\n\n" + entryText.TrimEnd() + "\n";
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteTarget)!);
        }
        return entryText.TrimEnd() + "\n";
    }

    /// <summary>
    /// Resolve a page-link path (as embedded in entry content, e.g.
    /// "projects/x/_ideas/pages/2026-03-15-note") to an absolute .md path.
    /// Tries vault-root-relative first (under logmark/), then source-directory-relative,
    /// then vault-root-relative without the logmark/ prefix.
    /// </summary>
    private string? ResolveSubpageOrigin(string linkPath, string sourceFilePath)
    {
        var withExt = linkPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? linkPath : linkPath + ".md";

        // Vault-relative under logmark/
        var underLogmark = Path.Combine(_vaultPath, "logmark", withExt);
        if (File.Exists(underLogmark)) return Path.GetFullPath(underLogmark);

        // Relative to the source entry's directory
        var sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (sourceDir != null)
        {
            var rel = Path.Combine(sourceDir, Path.GetFileName(withExt));
            if (File.Exists(rel)) return Path.GetFullPath(rel);
        }

        // Vault-relative (no logmark prefix)
        var underVault = Path.Combine(_vaultPath, withExt);
        if (File.Exists(underVault)) return Path.GetFullPath(underVault);

        return null;
    }

    /// <summary>
    /// Derive a human-readable route label from a file path.
    /// e.g., "logmark/_inbox/_ideas/2026.md" -> "_inbox/_ideas"
    /// e.g., "logmark/projects/shotqueue/_tasks/2026.md" -> "projects/shotqueue/_tasks"
    /// </summary>
    private static string DeriveRouteLabel(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');

        // Strip "logmark/" prefix if present
        var logmarkIdx = normalized.IndexOf("logmark/", StringComparison.OrdinalIgnoreCase);
        if (logmarkIdx >= 0)
            normalized = normalized.Substring(logmarkIdx + "logmark/".Length);

        // Strip the filename (year.md)
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash >= 0)
            normalized = normalized.Substring(0, lastSlash);

        return normalized;
    }

    /// <summary>
    /// Deterministic, collision-safe filename for a trashed subpage: an 8-char
    /// hash of the vault-relative origin path, prefixed to the original filename.
    /// Two subpages with the same base name but different origins no longer
    /// collide in the flat _trash/_pages/ folder. Computed identically on store
    /// and restore so the round-trip resolves the same file.
    /// </summary>
    private static string TrashedPageFileName(string originVaultRelativePath)
    {
        var normalized = originVaultRelativePath.Replace('\\', '/');
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes)[..8].ToLowerInvariant();
        return $"{hash}-{Path.GetFileName(normalized)}";
    }

    /// <summary>
    /// Raw fallback: scan the markdown lines of the entry at the given timestamp
    /// for a "key: value" line and return the value, or null. Covers entries
    /// where the field would not surface via ParsedEntry.ExtendedMetadata
    /// (e.g. an entry with no preceding content line).
    /// </summary>
    private static string? ScanRawField(string markdown, DateTime timestamp, string key, string? shortId = null)
    {
        var entry = EntryParser.FindEntry(markdown, timestamp, shortId);
        if (entry == null) return null;
        var lines = markdown.Split('\n');
        var prefix = key + ": ";
        for (int i = entry.StartLine - 1; i < entry.EndLine && i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed.Substring(prefix.Length).Trim();
        }
        return null;
    }

    private string ToAbsolute(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(_vaultPath, path);
    }

    /// <summary>
    /// Inverse of ToAbsolute: produces a vault-relative path. If the input is
    /// already relative, it is returned as-is. If absolute but not under the
    /// vault, it is returned as-is (downstream parsers will reject it).
    /// </summary>
    private string ToVaultRelative(string path)
    {
        if (!Path.IsPathRooted(path))
            return path;

        var normalized = path.Replace('\\', '/');
        var vaultNormalized = _vaultPath.Replace('\\', '/').TrimEnd('/');

        if (normalized.StartsWith(vaultNormalized + "/", StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(vaultNormalized.Length + 1);

        return path;
    }

    private static readonly System.Text.RegularExpressions.Regex BlockIdPattern =
        new(@"^[a-zA-Z0-9-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Append an entry to the correct route + type file, honoring Entry.Timestamp
    /// and an optional caller-provided block ID. When routePath is null, uses the
    /// unrouted inbox. When blockId is null, a ShortId is generated.
    ///
    /// Idempotent on blockId: when a non-null blockId is supplied AND the target
    /// file already contains an anchor "^&lt;blockId&gt;", the second write is
    /// skipped. This lets multiple synthesis passes (topical + temporal) merge
    /// into one vault without duplicating atoms — same text-hash blockId means
    /// the second run is a no-op for that entry. The first writer's tags win;
    /// if richer tag merging becomes worth its weight later, it goes here.
    /// </summary>
    public OrganizeResult AppendEntry(Entry entry, string? routePath, string? blockId)
    {
        if (entry.Type == null)
            return OrganizeResult.Fail("Entry.Type is required");

        if (blockId != null && !BlockIdPattern.IsMatch(blockId))
            throw new ArgumentException(
                $"Invalid blockId '{blockId}': must match ^[a-zA-Z0-9-]+$", nameof(blockId));

        var typeFolder = EntryTypeMapper.GetFolderName(entry.Type.Value);
        var year = entry.Timestamp.Year;
        var relative = string.IsNullOrWhiteSpace(routePath)
            ? PathConstants.GetUnroutedFilePath(typeFolder, year)
            : PathConstants.GetRoutedFilePath(routePath, typeFolder, year);
        var absolute = ToAbsolute(relative);

        if (blockId != null && BlockIdAlreadyPresent(absolute, blockId))
            return OrganizeResult.Ok(relative);

        var text = BuildEntryText(entry, blockId);
        AppendEntryToFile(absolute, text);

        return OrganizeResult.Ok(relative);
    }

    /// <summary>
    /// Cheap check: does the target file already carry an anchor for this
    /// blockId? Reads the file once, scans for "^&lt;blockId&gt;" as a token.
    /// Word boundary on the right so "^abc12345" doesn't match "^abc123456".
    /// </summary>
    private bool BlockIdAlreadyPresent(string absoluteTarget, string blockId)
    {
        if (!File.Exists(absoluteTarget)) return false;
        var content = File.ReadAllText(absoluteTarget);
        var marker = "^" + blockId;
        var idx = 0;
        while ((idx = content.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            var end = idx + marker.Length;
            // Right boundary: end-of-string or non-blockId char (the regex
            // BlockIdPattern is [a-zA-Z0-9-]+).
            if (end >= content.Length
                || !(char.IsLetterOrDigit(content[end]) || content[end] == '-'))
                return true;
            idx = end;
        }
        return false;
    }

    private static string BuildEntryText(Entry entry, string? blockId)
    {
        var sb = new System.Text.StringBuilder();
        var header = $"## {entry.Timestamp:yyyy-MM-dd HH:mm}";
        if (!string.IsNullOrWhiteSpace(entry.Context))
            header += $" | {entry.Context}";
        sb.AppendLine(header);

        var body = FileRouter.StripNotation(entry.RawContent, entry.Type);
        sb.AppendLine(body);

        var resolvedId = blockId;
        if (string.IsNullOrEmpty(resolvedId))
        {
            resolvedId = TaskTracker.GenerateShortId();
            entry.ShortId = resolvedId;
        }

        sb.AppendLine($"_id: {resolvedId} ^{resolvedId}");

        if (!string.IsNullOrEmpty(entry.Source))
            sb.AppendLine($"source: {entry.Source}");
        if (!string.IsNullOrEmpty(entry.Session))
            sb.AppendLine($"session: {entry.Session}");
        if (entry.Refs.Count > 0)
            sb.AppendLine($"refs: {string.Join(", ", entry.Refs.Select(r => r.ToString()))}");
        // Read back by EntryParser's extended-metadata catch-all into
        // ParsedEntry.Evidence. Emitted after refs: so it sits with the other
        // metadata rather than anywhere near the content body - a metadata line
        // that lands in the body reaches distill prompts as candidate text.
        if (!string.IsNullOrEmpty(entry.Evidence))
            sb.AppendLine($"evidence: {entry.Evidence}");
        if (entry.Tags.Count > 0)
        {
            var tagLine = string.Join(" ", entry.Tags.Select(t => t.StartsWith("#") ? t : "#" + t));
            if (!body.Contains(tagLine, StringComparison.Ordinal))
                sb.AppendLine(tagLine);
        }

        return sb.ToString();
    }
}
