using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Orchestrates agent memory import: discover sources, plan (dry-run diff),
/// apply. One flow for every surface (the agent-memory-sync behavior row now;
/// CLI/MCP later).
/// </summary>
public class AgentMemoryImportService
{
    private readonly string _vaultPath;
    private readonly RouteService _routeService;
    private readonly Dictionary<string, IAgentMemorySource> _sources;
    private readonly IVaultFileWriter? _vaultWriter;
    private readonly SystemLogger? _systemLogger;

    public event Action<ImportReport>? ImportCompleted;

    public AgentMemoryImportService(
        string vaultPath,
        RouteService routeService,
        IEnumerable<IAgentMemorySource> sources,
        IVaultFileWriter? vaultWriter = null,
        SystemLogger? systemLogger = null)
    {
        _vaultPath = vaultPath;
        _routeService = routeService;
        _sources = sources.ToDictionary(s => s.SourceId);
        _vaultWriter = vaultWriter;
        _systemLogger = systemLogger;
    }

    public IReadOnlyList<AgentSourceInfo> DiscoverSources()
        => _sources.Values.Select(s => s.Discover()).ToList();

    public ImportPlan PlanImport(string sourceId, IReadOnlyCollection<string>? selectedProjects = null)
    {
        var source = _sources[sourceId];
        var facts = source.Enumerate()
            .Where(f => selectedProjects == null || selectedProjects.Contains(f.Project))
            .ToList();
        var mirror = new MirrorScanner(_vaultPath).Scan(sourceId);
        // Project filtering must not misclassify unselected projects as removed:
        // restrict the mirror side to the same project set.
        if (selectedProjects != null)
            mirror = mirror.Where(r => selectedProjects.Contains(r.Frontmatter.SourceProject)).ToList();
        var plan = ImportPlanner.Plan(sourceId, facts, mirror, source.MalformedFiles);

        // Destination resolution (design 4b): advisory metadata attached at
        // plan time so the plan surface, the applier, and the report all see
        // the same resolution. RemovedAtSource rows (Fact == null) carry none.
        var resolver = new WorkdirRouteResolver(_routeService.GetRoutes());
        var resolved = plan.Facts.Select(pf => pf.Fact == null ? pf : pf with
        {
            Resolution = resolver.Resolve(new ResolutionEvidence(
                source.EncodedWorkdirFolder(pf.Fact), pf.Fact.SourceType))
        }).ToList();
        return plan with { Facts = resolved };
    }

    public ImportReport ApplyImport(ImportPlan plan)
    {
        // Crash-case heal: a promotion that died between write-destination and
        // delete-staging left a stale staging copy; the destination copy is the
        // ledger record, so the machine-authored staging duplicate is removable.
        var scanner = new MirrorScanner(_vaultPath);
        scanner.Scan(plan.SourceId);
        foreach (var stale in scanner.StaleStagingCopies)
        {
            try { File.Delete(stale); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var applier = new ImportApplier(_vaultPath, _routeService, _vaultWriter);
        var report = applier.Apply(plan, DateTime.Now);
        _systemLogger?.Log(LogLevel.Info,
            $"Agent memory import ({plan.SourceId}): {report.NewCount} new, {report.ChangedCount} changed, " +
            $"{report.ConflictCount} conflicts, {report.RemovedCount} removed at source");
        ImportCompleted?.Invoke(report);
        return report;
    }
}
