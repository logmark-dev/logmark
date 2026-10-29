using System.Text.Json;
using System.Text.RegularExpressions;
using LogMark.Services.EntryCache;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Destructive tag fixer: rewrites variant-spelled tags to the canonical spelling
/// directly in the entry files that contain them.
///
/// This eliminates the rescan-refire bug that the additive TagAliasFixer had:
/// once rewritten, no variant appears in any entry, so a follow-up scan finds
/// zero issues for the same group.
///
/// Write primitive: IFileAccessor.WriteAllText (raw read+modify+write, no gate).
/// Entry files are plain markdown; they are never masked by VaultWriteGate at
/// the point we are reading them (masking happens at write time for private routes,
/// and the on-disk form is already the masked text). Reading and writing via the
/// same raw accessor is idempotent with respect to masking.
///
/// _system/ files are skipped: these are metadata (log.md, tags.md, lint-state.json)
/// and must not be rewritten by this fixer.
/// </summary>
public class TagRewriteFixer : ILintFixer
{
    public string Id => "tag-rewrite";

    // System.Text.Json serializes anonymous types with camelCase keys by default.
    // The tag lint rules produce {"canonical":"...","variants":[...]}
    // so we need case-insensitive deserialization.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileAccessor _fileAccessor;
    private readonly EntryCache.EntryCache _entryCache;

    public TagRewriteFixer(IFileAccessor fileAccessor, EntryCache.EntryCache entryCache)
    {
        _fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
        _entryCache = entryCache ?? throw new ArgumentNullException(nameof(entryCache));
    }

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "FixPayloadJson is empty", null));

        RewritePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RewritePayload>(issue.FixPayloadJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new LintFixResult(false, $"Malformed FixPayloadJson: {ex.Message}", null));
        }

        if (payload == null || string.IsNullOrEmpty(payload.Canonical) || payload.Variants == null)
            return Task.FromResult(new LintFixResult(false, "FixPayloadJson missing canonical or variants", null));

        var canonical = payload.Canonical;

        // Non-canonical variants are those that differ from the canonical spelling.
        // We only need to rewrite those; the canonical spelling is already correct.
        var nonCanonicalVariants = payload.Variants
            .Where(v => !string.Equals(v, canonical, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (nonCanonicalVariants.Count == 0)
            return Task.FromResult(new LintFixResult(true, null, "no-op: no non-canonical variants"));

        // Collect all files containing any variant, grouping by file path.
        // We use EntryCache.GetAllEntries() to enumerate indexed files.
        var allEntries = ctx.Entries.GetAllEntries();
        var candidateFiles = allEntries
            .Select(ie => ie.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filesRewritten = new List<string>();

        foreach (var filePath in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Skip _system/ files - these are metadata, not user entry content.
            if (IsSystemFile(filePath))
                continue;

            if (!_fileAccessor.Exists(filePath))
                continue;

            var content = _fileAccessor.ReadAllText(filePath);
            var rewritten = content;

            foreach (var variant in nonCanonicalVariants)
            {
                // Whole-tag boundary: the lookahead (?![a-zA-Z0-9_-]) matches the
                // same character class as NotationParser's tag regex #([a-zA-Z0-9_-]+),
                // ensuring we never rewrite inside a longer tag like #MLCommons when
                // looking for #ML.
                var pattern = $@"#{Regex.Escape(variant)}(?![a-zA-Z0-9_\-])";
                rewritten = Regex.Replace(rewritten, pattern, $"#{canonical}");
            }

            if (!string.Equals(rewritten, content, StringComparison.Ordinal))
            {
                _fileAccessor.WriteAllText(filePath, rewritten);
                _entryCache.InvalidateFile(filePath);
                filesRewritten.Add(filePath);
            }
        }

        var summary = filesRewritten.Count == 0
            ? "no files contained non-canonical variants"
            : $"rewrote {filesRewritten.Count} file(s): {string.Join(", ", filesRewritten.Select(Path.GetFileName))}";

        return Task.FromResult(new LintFixResult(true, null, summary));
    }

    private static bool IsSystemFile(string filePath)
    {
        // Match files inside any _system/ folder. EntryCache returns only file paths,
        // so we don't need to handle the bare _system directory case.
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/_system/", StringComparison.OrdinalIgnoreCase);
    }

    private record RewritePayload(string Canonical, string[] Variants);
}
