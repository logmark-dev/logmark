using System.Text.Json;
using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Lint.Rules;

public class EntryTypePrefixLint : ILintRule
{
    public string Id => "entry-type-prefix";
    public string Category => "entries";
    public LintCostClass Cost => LintCostClass.Medium;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var issues = new List<LintIssue>();

        foreach (var indexed in ctx.Entries.GetAllEntries())
        {
            ct.ThrowIfCancellationRequested();

            // Skip completed (struck-through) entries. Their first content line is
            // wrapped in ~~...~~; the parser exposes the inner "g:"/"t:" prefix on
            // Content (so this rule would flag it), but StripEntryPrefixFixer operates
            // on the raw wrapped line and can't act on it -- the fix would be a silent
            // no-op and the issue would reappear on the next scan. A struck entry is
            // archival anyway, so prefix hygiene on a done item is pure noise.
            if (indexed.Entry.IsCompleted) continue;

            var prefix = EntryPrefix.TryParseLeading(indexed.Entry.Content);
            if (prefix == null) continue;

            var poolType = PoolTypeFromPath(ctx.VaultPath, indexed.FilePath);
            if (poolType == null) continue; // not a recognized entry pool

            var prefixType = prefix.Value.Type;
            var token = prefix.Value.Token;       // e.g. "g:"
            var timestamp = indexed.Entry.Timestamp;
            var shortId = indexed.Entry.ShortId;  // disambiguates same-minute entries; may be null
            var relPath = ToVaultRelative(ctx.VaultPath, indexed.FilePath);
            // Identity keys include ShortId: a single file can hold several entries with
            // one minute-precise timestamp, so timestamp alone is not unique (it would
            // collide both here and in the fixer's FindEntry lookup).
            var idKey = string.IsNullOrEmpty(shortId) ? $"{timestamp:yyyy-MM-ddTHH:mm}" : shortId;
            var subjectRef = $"entry:{relPath}#{idKey}";
            var hash = LintHashing.Compute($"{relPath}|{idKey}|{token}");

            // timestamp as the markdown-header string so fixers parse it unambiguously (no DateTimeKind drift)
            var tsString = timestamp.ToString("yyyy-MM-dd HH:mm");
            var stripPayload = JsonSerializer.Serialize(new { path = relPath, timestamp = tsString, shortId });
            var convertPayload = JsonSerializer.Serialize(
                new { path = relPath, timestamp = tsString, targetType = prefixType.ToString(), shortId }); // targetType = EntryType enum name, e.g. "Gap"

            // A short excerpt of the entry body so the reviewer can judge the entry
            // without opening it (the prefix itself is already named in the message).
            var snippet = Snippet(indexed.Entry.Content);

            LintSeverity severity;
            string description;
            List<LintAction> actions;

            if (prefixType == poolType.Value)
            {
                // Case A: redundant prefix.
                severity = LintSeverity.Suggest;
                description = $"Entry in {EntryPrefix.TypeLabel(poolType.Value)} repeats its type prefix \"{token}\"";
                actions = new List<LintAction>
                {
                    new($"Remove \"{token}\"", "strip-entry-prefix", stripPayload, Batchable: true),
                };
            }
            else if (poolType.Value == EntryType.Log)
            {
                // Case B: log entry that names a real type.
                severity = LintSeverity.Suggest;
                description = $"Log entry tagged \"{token}\" - looks like a {EntryPrefix.TypeLabel(prefixType)}";
                actions = new List<LintAction>
                {
                    new($"Convert to {EntryPrefix.TypeLabel(prefixType)}", "convert-entry-type", convertPayload, Batchable: true),
                };
            }
            else
            {
                // Case C: cross-type conflict. Offer both; never batch.
                severity = LintSeverity.Warn;
                description = $"Entry in {EntryPrefix.TypeLabel(poolType.Value)} but tagged \"{token}\" ({EntryPrefix.TypeLabel(prefixType)}) - type conflict";
                actions = new List<LintAction>
                {
                    new($"→ {EntryPrefix.TypeLabel(prefixType)}", "convert-entry-type", convertPayload, Batchable: false),
                    new($"Keep as {EntryPrefix.TypeLabel(poolType.Value)}", "strip-entry-prefix", stripPayload, Batchable: false),
                };
            }

            if (snippet.Length > 0)
                description = $"{description}: \"{snippet}\"";

            issues.Add(new LintIssue(
                Id: Guid.NewGuid().ToString(),
                RuleId: Id,
                Severity: severity,
                SubjectRef: subjectRef,
                Description: description,
                // multi-action rule: fixers read Actions[n].PayloadJson, not the single-fix fields
                SuggestedFixVerbId: null,
                FixPayloadJson: null,
                DismissalStateHash: hash,
                FirstSeenAt: DateTime.UtcNow,
                LastSeenAt: DateTime.UtcNow,
                Category: Category,
                Actions: actions));
        }

        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }

    private static EntryType? PoolTypeFromPath(string vaultPath, string filePath)
    {
        var rel = ToVaultRelative(vaultPath, filePath);
        var parsed = PathConstants.ParsePath(rel);
        if (parsed == null) return null;
        return EntryTypeMapper.GetEntryTypeFromFolder(parsed.TypeName);
    }

    private static string ToVaultRelative(string vaultPath, string filePath)
    {
        var rel = Path.GetRelativePath(vaultPath, filePath);
        return rel.Replace('\\', '/');
    }

    private const int SnippetMaxLength = 80;

    /// <summary>
    /// A single-line excerpt of the entry's body for the issue message: the first
    /// non-empty content line with its leading type prefix stripped (the prefix is
    /// already named in the message), collapsed to one line and truncated.
    /// </summary>
    private static string Snippet(string content)
    {
        string firstLine = "";
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line)) { firstLine = line; break; }
        }

        var body = (EntryPrefix.StripLeading(firstLine) ?? firstLine).Trim();
        if (body.Length <= SnippetMaxLength) return body;
        return body.Substring(0, SnippetMaxLength).TrimEnd() + "...";
    }
}
