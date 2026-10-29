using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using LogMark.Services.Lint.Fixers;
using LogMark.Services.Lint.Persistence;
using LogMark.Services.Lint.Rules;

namespace LogMark.Services.Lint;

/// <summary>
/// Single source of truth for the lint rule + fixer set. Both the desktop app
/// (App.xaml.cs) and the CLI (lint commands) build their LintRunner here so the
/// two surfaces never drift apart.
/// </summary>
public static class LintBootstrap
{
    /// <summary>Register the shipped rule set on a runner (tags, routes, entries).</summary>
    public static void RegisterRules(LintRunner runner)
    {
        // Tags
        runner.RegisterRule(new TagCaseVariantLint());
        runner.RegisterRule(new TagPunctVariantLint());
        runner.RegisterRule(new TagPluralSLint());
        runner.RegisterRule(new TagPluralEsLint());
        runner.RegisterRule(new TagPluralIesLint());
        runner.RegisterRule(new DegenerateClusterLint());
        // Routes
        runner.RegisterRule(new OrphanedEntryLint());
        runner.RegisterRule(new ScopeRouteOverlapLint());
        // Entries
        runner.RegisterRule(new EntryTypePrefixLint());
        runner.RegisterRule(new MissingEntryAnchorLint());
        // Alignment
        runner.RegisterRule(new LearnedTraitCapLint());
    }

    /// <summary>
    /// Build a fully-wired LintRunner: all rules + all fixers.
    /// </summary>
    public static LintRunner Build(
        ILintStateStore store,
        Func<IVaultContext> contextFactory,
        EntryCache.EntryCache entryCache,
        RouteService routeService,
        EntryOrganizer entryOrganizer)
    {
        var runner = new LintRunner(store, contextFactory);
        RegisterRules(runner);
        runner.RegisterFixer(new TagRewriteFixer(new FileAccessor(), entryCache));
        runner.RegisterFixer(new AddRouteFixer(routeService));
        runner.RegisterFixer(new StripEntryPrefixFixer(entryOrganizer));
        runner.RegisterFixer(new ConvertEntryTypeFixer(entryOrganizer));
        runner.RegisterFixer(new RemoveClusterFixer(new FileAccessor()));
        runner.RegisterFixer(new MintAnchorsFixer());
        return runner;
    }
}
