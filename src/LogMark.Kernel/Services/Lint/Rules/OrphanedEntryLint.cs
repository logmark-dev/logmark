using System.Text.Json;

namespace LogMark.Services.Lint.Rules;

public class OrphanedEntryLint : ILintRule
{
    public string Id => "orphan-folder";
    public string Category => "routes";
    public LintCostClass Cost => LintCostClass.Medium;

    public Task<IReadOnlyList<LintIssue>> ScanAsync(IVaultContext ctx, CancellationToken ct)
    {
        var issues = new List<LintIssue>();
        var logmarkRoot = Path.Combine(ctx.VaultPath, "logmark");
        if (!Directory.Exists(logmarkRoot))
            return Task.FromResult<IReadOnlyList<LintIssue>>(issues);

        var coverage = new RouteCoverageIndex(ctx.Routes.GetRoutes());
        var flagged = new List<string>();  // sorted vault-relative paths flagged this scan

        var allDirs = Directory.EnumerateDirectories(logmarkRoot, "*", SearchOption.AllDirectories)
            .Select(abs => ToVaultRelative(abs, logmarkRoot))
            .OrderBy(p => p, StringComparer.Ordinal)  // ancestors before descendants
            .ToList();

        foreach (var rel in allDirs)
        {
            ct.ThrowIfCancellationRequested();

            // Skip reserved folders: any path segment starting with '_' (system /
            // entry-type pools per notation spec) or '.' (hidden / tooling folders
            // such as .knowledge, .obsidian, .trash -- never user content awaiting a route).
            if (HasReservedSegment(rel)) continue;

            // Skip LogMark-managed non-underscore top-level namespaces (e.g. feed/, thinkers/).
            if (PathConstants.ManagedNonUnderscoreFolders.Contains(FirstSegment(rel))) continue;

            // Skip if an ancestor was already flagged (de-dup: one issue per orphan subtree).
            if (flagged.Any(f => rel.StartsWith(f + "/", StringComparison.Ordinal)))
                continue;

            if (coverage.IsCovered(rel)) continue;

            // Only flag folders that actually hold entries. With lazy folder creation
            // and larval routes, an empty folder is a cold/structural shell, not content
            // stranded without a route -- flagging it would be pure noise. A folder is an
            // orphan worth surfacing only when it contains captured entries the user
            // can't browse because nothing routes to it (e.g. domains/art with a _diary).
            if (!HasEntries(Path.Combine(logmarkRoot, rel))) continue;

            flagged.Add(rel);

            var hash = LintHashing.Compute(rel);
            var leaf = rel.Split('/').Last();
            var payload = JsonSerializer.Serialize(new { folderPath = rel, defaultName = leaf });

            issues.Add(new LintIssue(
                Id: Guid.NewGuid().ToString(),
                RuleId: Id,
                Severity: LintSeverity.Suggest,
                SubjectRef: $"folder:{rel}",
                Description: $"Folder '{rel}/' is not covered by any route",
                // Surfaced as a labeled action (not the generic legacy "Fix" button).
                // Not batchable: each orphan is a distinct route-naming decision.
                SuggestedFixVerbId: null,
                FixPayloadJson: null,
                DismissalStateHash: hash,
                FirstSeenAt: DateTime.UtcNow,
                LastSeenAt: DateTime.UtcNow,
                Actions: new List<LintAction>
                {
                    new("Add route", "add-route", payload, Batchable: false),
                }));
        }
        return Task.FromResult<IReadOnlyList<LintIssue>>(issues);
    }

    private static string ToVaultRelative(string absolutePath, string logmarkRoot)
    {
        var rel = Path.GetRelativePath(logmarkRoot, absolutePath);
        return rel.Replace('\\', '/');
    }

    private static bool HasReservedSegment(string vaultRelative) =>
        vaultRelative.Split('/').Any(seg => seg.StartsWith('_') || seg.StartsWith('.'));

    /// <summary>
    /// True if the folder contains at least one entry file -- a markdown file inside
    /// an entry-type pool ("_tasks", "_ideas", ...) anywhere beneath it. This is the
    /// "has captured content" signal: empty/cold folders are excluded so the rule
    /// only surfaces folders whose entries are stranded without a route.
    /// </summary>
    private static bool HasEntries(string absoluteFolder)
    {
        if (!Directory.Exists(absoluteFolder)) return false;
        foreach (var md in Directory.EnumerateFiles(absoluteFolder, "*.md", SearchOption.AllDirectories))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(md));
            if (parent != null && parent.StartsWith('_'))
                return true;
        }
        return false;
    }

    private static string FirstSegment(string vaultRelative)
    {
        var slash = vaultRelative.IndexOf('/');
        return slash < 0 ? vaultRelative : vaultRelative[..slash];
    }
}
