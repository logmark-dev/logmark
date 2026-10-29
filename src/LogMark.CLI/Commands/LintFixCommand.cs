using System.Text.Json;
using LogMark.Services.Lint;

namespace LogMark.CLI.Commands;

public class LintFixCommand : ICommand
{
    // Safe-by-default scope: in-place tag rewrites + entry-prefix strips.
    // Route fixes CREATE routes (headless), so they are opt-in via --category routes.
    private static readonly string[] DefaultCategories = { "tags", "entries" };

    public string Name => "fix";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--apply"] = false, ["--category"] = true,
    };

    public string Description => "Apply hygiene fixes (dry-run unless --apply)";
    public string Usage => "lint fix [--apply] [--category tags|routes|entries]";
    public bool RequiresVault => true;

    public string[]? Examples => new[]
    {
        "lm lint fix                      # preview tag+entry fixes",
        "lm lint fix --apply              # apply tag+entry fixes",
        "lm --json lint fix --apply       # apply, machine-readable summary",
        "lm lint fix --category routes            # preview route issues; orphan fix is interactive, not headless",
    };

    public int Execute(CliContext context, string[] args)
    {
        if (!LintCommandSupport.TryParseCategory(args, out var category, out var error))
        {
            context.WriteError(error!);
            return 1;
        }
        var apply = LintCommandSupport.HasFlag(args, "--apply");
        var categories = category != null ? new[] { category } : DefaultCategories;
        var scope = string.Join(",", categories);

        context.WaitForCacheWarmup();
        var runner = LintCommandSupport.BuildRunner(context);
        runner.RunScanAsync(CancellationToken.None).GetAwaiter().GetResult();
        var all = runner.GetOpenIssuesAsync(CancellationToken.None).GetAwaiter().GetResult();
        var scoped = LintCommandSupport.FilterByCategories(all, categories);

        var eligible = scoped.Where(LintCommandSupport.IsBatchEligible).ToList();
        var manual = scoped.Where(i => !LintCommandSupport.IsBatchEligible(i)).ToList();
        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        if (!apply)
        {
            if (context.JsonOutput)
            {
                context.WriteResult(JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    scope = categories,
                    wouldFix = eligible.Count,
                    manual = manual.Count,
                }, jsonOpts));
                return 0;
            }
            context.WriteResult($"DRY RUN (scope: {scope}). Pass --apply to write changes.");
            context.WriteResult($"Would fix {eligible.Count} issue(s) automatically; {manual.Count} need manual review.");
            if (eligible.Count > 0) LintCommandSupport.PrintIssuesPlain(context, eligible);
            if (manual.Count > 0)
            {
                context.WriteResult("\nManual (not auto-fixed):");
                LintCommandSupport.PrintIssuesPlain(context, manual);
            }
            return 0;
        }

        var batch = runner.ApplyBatchAsync(eligible, CancellationToken.None).GetAwaiter().GetResult();

        if (context.JsonOutput)
        {
            // @fixed escapes the C# keyword; the JSON key is "fixed".
            context.WriteResult(JsonSerializer.Serialize(new
            {
                dryRun = false,
                scope = categories,
                @fixed = batch.Fixed,
                failed = batch.Failed,
                skipped = batch.Skipped,
                manual = manual.Count,
            }, jsonOpts));
            return batch.Failed > 0 ? 2 : 0;
        }

        context.WriteResult(
            $"Fixed {batch.Fixed}, Failed {batch.Failed}, Skipped {batch.Skipped} (scope: {scope}).");
        if (manual.Count > 0)
            context.WriteResult($"{manual.Count} issue(s) need manual review (run '{CommandRunner.CliName} lint scan' to list).");
        return batch.Failed > 0 ? 2 : 0;
    }
}
