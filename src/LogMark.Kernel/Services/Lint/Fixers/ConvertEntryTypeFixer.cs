using System.Globalization;
using System.Text.Json;
using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Reclassify an entry to the type named by its leading prefix: strip the prefix,
/// then relocate the block to the target type's pool. Fix verb "convert-entry-type".
/// Payload: {"path","timestamp","targetType"} (timestamp = "yyyy-MM-dd HH:mm";
/// targetType = EntryType enum name).
/// </summary>
public class ConvertEntryTypeFixer : ILintFixer
{
    public string Id => "convert-entry-type";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly EntryOrganizer _organizer;

    public ConvertEntryTypeFixer(EntryOrganizer organizer) =>
        _organizer = organizer ?? throw new ArgumentNullException(nameof(organizer));

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "Empty payload", null));

        Payload? p;
        try { p = JsonSerializer.Deserialize<Payload>(issue.FixPayloadJson, _json); }
        catch (JsonException ex) { return Task.FromResult(new LintFixResult(false, $"Malformed payload: {ex.Message}", null)); }

        if (p == null || string.IsNullOrWhiteSpace(p.Path) || string.IsNullOrWhiteSpace(p.Timestamp) || string.IsNullOrWhiteSpace(p.TargetType))
            return Task.FromResult(new LintFixResult(false, "Payload missing path, timestamp, or targetType", null));

        if (!DateTime.TryParseExact(p.Timestamp, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return Task.FromResult(new LintFixResult(false, $"Bad timestamp '{p.Timestamp}'", null));

        if (!Enum.TryParse<EntryType>(p.TargetType, out var target))
            return Task.FromResult(new LintFixResult(false, $"Unknown target type '{p.TargetType}'", null));

        // Strip first (in place), then relocate the now-clean block. ShortId
        // disambiguates entries that share a minute-precise timestamp in one file.
        var strip = _organizer.StripLeadingPrefix(p.Path, ts, p.ShortId);
        if (!strip.Success)
            return Task.FromResult(new LintFixResult(false, strip.Error, null));

        var convert = _organizer.ConvertEntryType(p.Path, ts, target, p.ShortId);
        return Task.FromResult(convert.Success
            ? new LintFixResult(true, null, $"Converted to {target} -> {convert.TargetFilePath}")
            : new LintFixResult(false, convert.Error, null));
    }

    private record Payload(string Path, string Timestamp, string TargetType, string? ShortId = null);
}
