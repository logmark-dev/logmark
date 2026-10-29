using System.IO;
using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using LogMark.Services.Sensitivity;
using EntryParserStatic = LogMark.Services.EntryParser.EntryParser;

namespace LogMark.Services;

/// <summary>
/// Result of an AnchorMintSweep Plan() or Apply() pass. Candidates counts
/// anchor-less entry blocks found (Plan) or fixed (Apply); SamplePaths holds
/// up to 10 distinct vault-relative file paths that contain a candidate.
/// </summary>
public record SweepResult(int Candidates, IReadOnlyList<string> SamplePaths, int Adopted = 0);

/// <summary>
/// One file's worth of anchor-less entries. Count is how many entry blocks in
/// that file lack a block anchor. Emitted by AnchorMintSweep.PlanByFile so a
/// caller that fixes one subject at a time (the lint rule, layer 2 of the
/// capture-loop machinery) can address a single file rather than the vault.
/// </summary>
public record AnchorGap(string AbsolutePath, string VaultRelativePath, int Count);

/// <summary>
/// Finds entry blocks the parser recognizes ("## timestamp" header plus
/// content) that are untranscludable because their block anchor is missing,
/// and mints one so Obsidian-style [[file#^id]] transclusion works.
///
/// Two shapes count as "lacks a block anchor":
///   1. The entry already carries a "_id:"/"_task:" metadata line with a
///      ShortId, but the line has no trailing " ^&lt;id&gt;" suffix (the
///      parser's EntryIdPattern/TaskMetadataPattern both treat that suffix
///      as optional, for exactly this backwards-compatibility case). Apply()
///      reuses the id already on the line -- it does not mint a new one.
///   2. The entry has no "_id:"/"_task:" line at all. Apply() anchors the id
///      the reader already serves for it (EntryResolver.GetEntryId: its
///      published short_id, else its content hash; a fresh
///      TaskTracker.GenerateShortId() only on collision) and inserts a new metadata line
///      in the grammar FileRouter.AppendEntry/VaultWriteGate.AppendEntryBlockAsync
///      write for freshly captured entries: "_id: {id} ^{id}" for non-task
///      entries, "_task: none | postponed: 0 | {id} ^{id}" for tasks.
///
/// Discovery mirrors EntryCache.WarmupAsync: every *.md file under
/// logmark/, excluding anything under a "_system" segment.
///
/// Writes go through IVaultWriteGate.RewriteFileAsync when a gate is
/// supplied (the same gate-aware, in-place rewrite path EntryOrganizer uses
/// for AmendEntry/RetagEntry/LinkEntryToPage), falling back to
/// IVaultFileWriter.WriteFile otherwise -- mirroring LintCommandSupport's
/// headless lint-fixer wiring, since minting an anchor never introduces new
/// plaintext content that would need private-route masking.
/// </summary>
public class AnchorMintSweep
{
    private const int SampleCap = 10;

    private static readonly Regex IdLine = new(
        @"^_id:\s*(?<id>[a-f0-9]{8})(?<anchor>\s*\^[a-f0-9]{8})?$",
        RegexOptions.Compiled);

    private static readonly Regex TaskLine = new(
        @"^(?:~~)?_task:\s*(?:none|\d{4}-\d{2}-\d{2}(?:,\s*\d{2}:\d{2})?)\s*\|\s*postponed:\s*\d+\s*\|\s*(?<id>[a-f0-9]{8})(?<anchor>\s*\^[a-f0-9]{8})?(?:~~)?$",
        RegexOptions.Compiled);

    // The logmark.ink publish format: a trailing "short_id: <8 hex>" metadata line.
    private static readonly Regex ShortIdLine = new(
        @"^short_id:\s*(?<id>[a-f0-9]{8})$",
        RegexOptions.Compiled);

    private HashSet<string>? _usedIds;

    private readonly string _vaultPath;
    private readonly string _logmarkPath;
    private readonly IFileAccessor _fileAccessor;
    private readonly IVaultFileWriter _writer;
    private readonly IVaultWriteGate? _writeGate;

    public AnchorMintSweep(string vaultPath, IFileAccessor? fileAccessor = null,
        IVaultFileWriter? writer = null, IVaultWriteGate? writeGate = null)
    {
        _vaultPath = vaultPath;
        _logmarkPath = Path.Combine(vaultPath, "logmark");
        _fileAccessor = fileAccessor ?? new FileAccessor();
        _writer = writer ?? new VaultWriter();
        _writeGate = writeGate;
    }

    /// <summary>Counts and samples anchor-less entries. Writes nothing.</summary>
    public SweepResult Plan() => Run(apply: false);

    /// <summary>Mints missing anchors and writes the affected files.</summary>
    public SweepResult Apply() => Run(apply: true);

    /// <summary>
    /// Per-file breakdown of anchor-less entries. Writes nothing. Unlike Plan,
    /// which caps its samples at 10 paths, this lists every affected file, so
    /// each can become its own separately fixable lint subject.
    /// </summary>
    public IReadOnlyList<AnchorGap> PlanByFile()
    {
        var gaps = new List<AnchorGap>();

        foreach (var file in EnumerateEntryFiles())
        {
            var count = ProcessFile(file, apply: false).Candidates;
            if (count > 0)
                gaps.Add(new AnchorGap(file, ToVaultRelative(file), count));
        }

        return gaps;
    }

    /// <summary>
    /// Mints missing anchors in a single file and writes it. Returns the number
    /// minted (0 when the file is absent, empty, or already fully anchored).
    /// Scoped rather than vault-wide because the lint fixer applies one issue at
    /// a time: a whole-vault Apply behind one issue would report every sibling
    /// issue fixed without having been asked.
    /// </summary>
    public int ApplyToFile(string absolutePath)
    {
        if (!_fileAccessor.FileExists(absolutePath))
            return 0;

        var (candidates, _, updated) = ProcessFile(absolutePath, apply: true);
        if (candidates == 0 || updated is null)
            return 0;

        WriteFile(absolutePath, updated);
        return candidates;
    }

    /// <summary>
    /// Every id already used by an "_id:"/"_task:" line anywhere in the vault,
    /// built lazily on first use and extended as this sweep adopts or mints ids,
    /// so two entries can never end up with the same id in one run.
    /// </summary>
    private HashSet<string> UsedIds()
    {
        if (_usedIds is not null)
            return _usedIds;

        _usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerateEntryFiles())
        {
            foreach (var raw in _fileAccessor.ReadAllText(file).Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                var m = IdLine.Match(line);
                if (!m.Success)
                    m = TaskLine.Match(line);
                if (m.Success)
                    _usedIds.Add(m.Groups["id"].Value);
            }
        }

        return _usedIds;
    }

    private IEnumerable<string> EnumerateEntryFiles()
    {
        if (!_fileAccessor.DirectoryExists(_logmarkPath))
            return Array.Empty<string>();

        return _fileAccessor.EnumerateFiles(_logmarkPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/_system/", StringComparison.OrdinalIgnoreCase));
    }

    private SweepResult Run(bool apply)
    {
        var candidates = 0;
        var adopted = 0;
        var samples = new List<string>();

        foreach (var file in EnumerateEntryFiles())
        {
            var (fileCandidates, fileAdopted, updated) = ProcessFile(file, apply);
            if (fileCandidates == 0)
                continue;

            candidates += fileCandidates;
            adopted += fileAdopted;
            if (samples.Count < SampleCap)
                samples.Add(ToVaultRelative(file));

            if (apply && updated is not null)
                WriteFile(file, updated);
        }

        return new SweepResult(candidates, samples, adopted);
    }

    /// <summary>
    /// Counts (and, when apply is set, mints) the anchor-less entries in one
    /// file. Returns the count and, for an apply pass that changed something,
    /// the rewritten content. Writing is the caller's job so PlanByFile can
    /// share this walk without any chance of touching disk.
    /// </summary>
    private (int Candidates, int Adopted, string? Updated) ProcessFile(string file, bool apply)
    {
        var content = _fileAccessor.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(content))
            return (0, 0, null);

        var typeHint = EntryTypeMapper.InferTypeFromPath(file);
        var entries = EntryParserStatic.Parse(content, typeHint)
            .Where(e => !e.HasParseError)
            .OrderByDescending(e => e.StartLine) // bottom-up so line inserts don't shift earlier entries
            .ToList();

        var lines = content.Split('\n').ToList();
        var candidates = 0;
        var adopted = 0;

        foreach (var entry in entries)
        {
            var anchorInfo = FindAnchorLine(lines, entry.StartLine, entry.EndLine);
            if (anchorInfo.HasAnchor)
                continue;

            candidates++;

            if (!apply)
                continue;

            if (anchorInfo.LineIndex is int lineIndex && anchorInfo.ExistingId is string existingId)
                AppendAnchorToLine(lines, lineIndex, existingId);
            else if (anchorInfo.ShortIdLineIndex is int sIndex && anchorInfo.ShortId is string sid
                     && UsedIds().Add(sid))
            {
                var hasCr = lines[sIndex].EndsWith("\r", StringComparison.Ordinal);
                lines[sIndex] = (entry.Type == EntryType.Task
                    ? $"_task: none | postponed: 0 | {sid} ^{sid}"
                    : $"_id: {sid} ^{sid}") + (hasCr ? "\r" : "");
                adopted++;
            }
            else
                InsertNewAnchoredLine(lines, entry);
        }

        if (candidates == 0 || !apply)
            return (candidates, 0, null);

        return (candidates, adopted, string.Join("\n", lines));
    }

    private void WriteFile(string file, string content)
    {
        if (_writeGate != null)
            _writeGate.RewriteFileAsync(file, content).GetAwaiter().GetResult();
        else
            _writer.WriteFile(file, content);
    }

    private readonly record struct AnchorInfo(
        bool HasAnchor, int? LineIndex, string? ExistingId,
        int? ShortIdLineIndex = null, string? ShortId = null);

    /// <summary>
    /// Scans an entry's raw lines for its "_id:"/"_task:" metadata line.
    /// Returns HasAnchor=true when that line already carries " ^&lt;id&gt;".
    /// Returns the (0-based) line index and captured id when a metadata line
    /// exists but has no anchor yet, so the caller can append in place
    /// instead of minting a new id. Returns LineIndex=null when the entry
    /// has no "_id:"/"_task:" line at all.
    /// </summary>
    private static AnchorInfo FindAnchorLine(List<string> lines, int startLine, int endLine)
    {
        int? shortIndex = null;
        string? shortId = null;

        for (var i = startLine - 1; i < endLine && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            if (shortIndex is null)
            {
                var shortMatch = ShortIdLine.Match(trimmed);
                if (shortMatch.Success)
                {
                    shortIndex = i;
                    shortId = shortMatch.Groups["id"].Value;
                    continue;
                }
            }

            var idMatch = IdLine.Match(trimmed);
            if (idMatch.Success)
                return new AnchorInfo(idMatch.Groups["anchor"].Success, i, idMatch.Groups["id"].Value);

            var taskMatch = TaskLine.Match(trimmed);
            if (taskMatch.Success)
                return new AnchorInfo(taskMatch.Groups["anchor"].Success, i, taskMatch.Groups["id"].Value);
        }

        return new AnchorInfo(HasAnchor: false, LineIndex: null, ExistingId: null,
            ShortIdLineIndex: shortIndex, ShortId: shortId);
    }

    /// <summary>
    /// Appends " ^&lt;id&gt;" to an existing anchor-less "_id:"/"_task:" line,
    /// preserving a trailing strikethrough "~~" (completed tasks) if present.
    /// </summary>
    private static void AppendAnchorToLine(List<string> lines, int index, string id)
    {
        var raw = lines[index];
        var hasCr = raw.EndsWith("\r", StringComparison.Ordinal);
        var body = hasCr ? raw.Substring(0, raw.Length - 1) : raw;
        var trimmedBody = body.TrimEnd();

        if (trimmedBody.EndsWith("~~", StringComparison.Ordinal)
            && trimmedBody.TrimStart().StartsWith("~~_task:", StringComparison.OrdinalIgnoreCase))
        {
            var head = trimmedBody.Substring(0, trimmedBody.Length - 2);
            trimmedBody = head + $" ^{id}" + "~~";
        }
        else
        {
            trimmedBody += $" ^{id}";
        }

        lines[index] = hasCr ? trimmedBody + "\r" : trimmedBody;
    }

    /// <summary>
    /// Anchors the served id and inserts a new "_id:"/"_task:" metadata line for an
    /// entry that has none, at the same insertion point EntrySerializer's
    /// AmendEntry/AddRef/SetMetadataField use: before the first existing
    /// metadata line, else after the last content line, else at the entry
    /// boundary for an empty entry. Detects and preserves any trailing CRLF
    /// terminator from adjacent lines.
    /// </summary>
    private void InsertNewAnchoredLine(List<string> lines, ParsedEntry entry)
    {
        // Anchor the id the reader has been serving (EntryResolver.GetEntryId),
        // so an id an agent learned before minting still resolves after it.
        // Only a collision with an id already in the vault gets a fresh one.
        var id = EntryResolver.GetEntryId(entry);
        if (!UsedIds().Add(id))
            do { id = TaskTracker.GenerateShortId(); } while (!UsedIds().Add(id));
        var newLine = entry.Type == EntryType.Task
            ? $"_task: none | postponed: 0 | {id} ^{id}"
            : $"_id: {id} ^{id}";

        var insertAt = FindMetadataInsertIndex(lines, entry.StartLine, entry.EndLine);

        var hasCr = DetectLineTerminator(lines, insertAt);
        if (hasCr)
            newLine += "\r";

        lines.Insert(insertAt, newLine);
    }

    /// <summary>
    /// Detects whether lines in the file use CRLF termination by checking
    /// the line at or near the insertion point, falling back to any line
    /// in the file that has a trailing carriage return.
    /// </summary>
    private static bool DetectLineTerminator(List<string> lines, int insertAt)
    {
        if (insertAt < lines.Count && lines[insertAt].EndsWith("\r", StringComparison.Ordinal))
            return true;

        if (insertAt > 0 && lines[insertAt - 1].EndsWith("\r", StringComparison.Ordinal))
            return true;

        foreach (var line in lines)
        {
            if (line.EndsWith("\r", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int FindMetadataInsertIndex(List<string> lines, int startLine, int endLine)
    {
        var firstMetadataIndex = -1;
        var lastContentIndex = -1;

        for (var i = startLine - 1; i < endLine && i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();
            if (trimmed.StartsWith("##", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(trimmed) || trimmed == "---")
                continue;

            if (EntrySerializer.IsMetadataLinePublic(trimmed))
            {
                if (firstMetadataIndex < 0)
                    firstMetadataIndex = i;
            }
            else
            {
                lastContentIndex = i;
            }
        }

        if (firstMetadataIndex >= 0)
            return firstMetadataIndex;
        if (lastContentIndex >= 0)
            return lastContentIndex + 1;

        var insertIndex = endLine;
        return insertIndex > lines.Count ? lines.Count : insertIndex;
    }

    private string ToVaultRelative(string path)
    {
        if (!Path.IsPathRooted(path))
            return path;

        var normalized = path.Replace('\\', '/');
        var vaultNormalized = _vaultPath.Replace('\\', '/').TrimEnd('/');

        return normalized.StartsWith(vaultNormalized + "/", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(vaultNormalized.Length + 1)
            : normalized;
    }
}
