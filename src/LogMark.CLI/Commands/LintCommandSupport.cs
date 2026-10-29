using System.Text.Json;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using LogMark.Services.Lint;
using LogMark.Services.Lint.Persistence;

using EntryCacheService = LogMark.Services.EntryCache.EntryCache;

namespace LogMark.CLI.Commands;

/// <summary>
/// Shared construction + output formatting for the lint CLI commands. Builds a
/// LintRunner from CliContext services via the same LintBootstrap the desktop
/// app uses, so CLI and GUI lint identically.
/// </summary>
internal static class LintCommandSupport
{
    public static readonly string[] KnownCategories = { "tags", "routes", "entries" };

    public static LintRunner BuildRunner(CliContext context)
    {
        var vaultPath = context.VaultPath!;
        var store = new JsonLintStateStore(Path.Combine(vaultPath, PathConstants.GetLintStatePath()));
        var tagsParser = new TagsFileParser(Path.Combine(vaultPath, PathConstants.GetTagsPath()));
        EntryCacheService cache = context.EntryCache;
        var routeService = context.RouteService;
        // Headless write path: writeGate is null (same as MCP/CLI capture). Lint fixers
        // rewrite already-on-disk (masked) content, not new plaintext, so masking is moot.
        var organizer = new EntryOrganizer(vaultPath, context.VaultWriter, null);
        return LintBootstrap.Build(
            store,
            () => new VaultContext(cache, tagsParser, routeService, vaultPath),
            cache, routeService, organizer);
    }

    /// <summary>Parse "--category &lt;c&gt;" if present; null = no filter. Validates against KnownCategories.</summary>
    public static bool TryParseCategory(string[] args, out string? category, out string? error)
    {
        category = null; error = null;
        var i = Array.IndexOf(args, "--category");
        if (i < 0) return true;
        if (i + 1 >= args.Length) { error = "--category requires a value (tags|routes|entries)"; return false; }
        var c = args[i + 1].ToLowerInvariant();
        if (!KnownCategories.Contains(c)) { error = $"Unknown category '{c}' (expected tags|routes|entries)"; return false; }
        category = c; return true;
    }

    public static bool HasFlag(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

    public static IReadOnlyList<LintIssue> FilterByCategories(
        IReadOnlyList<LintIssue> issues, IReadOnlyCollection<string> categories)
        => issues.Where(i => categories.Contains(i.Category)).ToList();

    /// <summary>An issue the batch path will auto-apply: exactly one Batchable action.</summary>
    public static bool IsBatchEligible(LintIssue issue)
        => issue.Actions is { Count: 1 } a && a[0].Batchable;

    public static void PrintIssuesPlain(CliContext context, IReadOnlyList<LintIssue> issues)
    {
        if (issues.Count == 0) { context.WriteResult("No lint issues found."); return; }
        foreach (var g in issues.GroupBy(i => i.Category).OrderBy(g => g.Key))
        {
            context.WriteResult($"\n[{(string.IsNullOrEmpty(g.Key) ? "uncategorized" : g.Key)}] {g.Count()} issue(s)");
            foreach (var i in g)
            {
                var actions = i.Actions is { Count: > 0 }
                    ? " {" + string.Join(" | ", i.Actions.Select(a => a.Label)) + "}"
                    : "";
                context.WriteResult($"  {i.Severity,-7} {i.RuleId,-22} {i.SubjectRef}  {i.Description}{actions}");
            }
        }
        context.WriteResult($"\nTotal: {issues.Count} issue(s).");
    }

    public static void PrintIssuesJson(CliContext context, IReadOnlyList<LintIssue> issues)
    {
        var dto = issues.Select(i => new
        {
            i.Id, i.RuleId, severity = i.Severity.ToString(), i.Category,
            i.SubjectRef, i.Description,
            batchEligible = IsBatchEligible(i),
            actions = (i.Actions ?? Array.Empty<LintAction>())
                .Select(a => new { a.Label, a.VerbId, a.Batchable }),
        });
        // camelCase to match the CLI's established JSON convention (see Formatting.cs).
        context.WriteResult(JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }
}
