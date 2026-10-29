using System.Globalization;
using System.Text.Json;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Strip a redundant leading type prefix from an entry, in place. Fix verb
/// "strip-entry-prefix". Payload: {"path","timestamp"} where timestamp is the
/// "yyyy-MM-dd HH:mm" markdown-header string.
/// </summary>
public class StripEntryPrefixFixer : ILintFixer
{
    public string Id => "strip-entry-prefix";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly EntryOrganizer _organizer;

    public StripEntryPrefixFixer(EntryOrganizer organizer) =>
        _organizer = organizer ?? throw new ArgumentNullException(nameof(organizer));

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "Empty payload", null));

        Payload? p;
        try { p = JsonSerializer.Deserialize<Payload>(issue.FixPayloadJson, _json); }
        catch (JsonException ex) { return Task.FromResult(new LintFixResult(false, $"Malformed payload: {ex.Message}", null)); }

        if (p == null || string.IsNullOrWhiteSpace(p.Path) || string.IsNullOrWhiteSpace(p.Timestamp))
            return Task.FromResult(new LintFixResult(false, "Payload missing path or timestamp", null));

        if (!DateTime.TryParseExact(p.Timestamp, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
            return Task.FromResult(new LintFixResult(false, $"Bad timestamp '{p.Timestamp}'", null));

        var r = _organizer.StripLeadingPrefix(p.Path, ts, p.ShortId);
        return Task.FromResult(r.Success
            ? new LintFixResult(true, null, $"Stripped prefix in {p.Path}")
            : new LintFixResult(false, r.Error, null));
    }

    // ShortId disambiguates entries that share a minute-precise timestamp in one file.
    private record Payload(string Path, string Timestamp, string? ShortId = null);
}
