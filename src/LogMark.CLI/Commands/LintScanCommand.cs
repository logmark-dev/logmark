using LogMark.Services.Lint;

namespace LogMark.CLI.Commands;

public class LintScanCommand : ICommand
{
    public string Name => "scan";
    public string Description => "Scan the vault for hygiene issues (read-only)";
    public string Usage => "lint scan [--category tags|routes|entries] [--exit-code]";
    public bool RequiresVault => true;

    public string[]? Examples => new[]
    {
        "lm lint scan",
        "lm --json lint scan",
        "lm lint scan --category entries",
        "lm lint scan --exit-code        # exit 1 if any issue is found (CI gate)",
    };

    public int Execute(CliContext context, string[] args)
    {
        if (!LintCommandSupport.TryParseCategory(args, out var category, out var error))
        {
            context.WriteError(error!);
            return 1;
        }

        context.WaitForCacheWarmup();
        var runner = LintCommandSupport.BuildRunner(context);
        runner.RunScanAsync(CancellationToken.None).GetAwaiter().GetResult();
        var issues = runner.GetOpenIssuesAsync(CancellationToken.None).GetAwaiter().GetResult();

        if (category != null)
            issues = LintCommandSupport.FilterByCategories(issues, new[] { category });

        if (context.JsonOutput)
            LintCommandSupport.PrintIssuesJson(context, issues);
        else
            LintCommandSupport.PrintIssuesPlain(context, issues);

        // --exit-code: non-zero when the vault is dirty, for CI gating. Scan is
        // informational by default (exit 0 even with issues) unless this is set.
        if (LintCommandSupport.HasFlag(args, "--exit-code") && issues.Count > 0)
            return 1;
        return 0;
    }
}
