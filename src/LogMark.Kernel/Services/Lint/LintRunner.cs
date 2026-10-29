using System.Diagnostics;
using LogMark.Services.Lint.Persistence;

namespace LogMark.Services.Lint;

public class LintRunner
{
    private readonly ILintStateStore _store;
    private readonly Func<IVaultContext> _contextFactory;
    private readonly List<ILintRule> _rules = new();
    private readonly Dictionary<string, ILintFixer> _fixers = new();
    private readonly object _scanLock = new();
    private bool _isScanning;
    private int _lastIssueCount;
    // Cached open-issue list (sans dismissals). Populated by every site that
    // already recomputes _lastIssueCount (scan, fix, dismiss, GetOpenIssues,
    // GetLastScanAt). Read by GetIssueCountsSince so the sidebar Lint badge
    // can compute "added since" without re-loading state from disk.
    private IReadOnlyList<LintIssue> _lastOpenIssues = Array.Empty<LintIssue>();

    public LintRunner(ILintStateStore store, Func<IVaultContext> contextFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public bool IsScanning
    {
        get { lock (_scanLock) return _isScanning; }
    }

    public DateTime? LastScanAt { get; private set; }

    /// <summary>
    /// Returns the cached count of currently-open lint issues (open minus
    /// dismissed) from the most recent scan, fix, dismiss, or
    /// <see cref="GetOpenIssuesAsync"/> call. Returns 0 before any of those
    /// have populated the cache.
    ///
    /// Synchronous and cheap by design: the sidebar Lint lens badge calls
    /// this on every sidebar refresh, so it must not touch disk or run a
    /// scan. The view-side flow (LintInboxView calls GetOpenIssuesAsync /
    /// RunScanAsync) keeps the cache fresh; the sidebar reads it.
    /// </summary>
    public int GetIssueCount()
    {
        lock (_scanLock) return _lastIssueCount;
    }

    /// <summary>
    /// Returns (Total, AddedSince) for the sidebar Lint-unread badge.
    ///   Total = cached open-issue count (same as GetIssueCount).
    ///   AddedSince = cached open issues with FirstSeenAt > lastSeenUtc.
    /// Synchronous, cache-only -- mirrors GetIssueCount for the sidebar
    /// hot path. AddedSince is 0 before any scan / GetOpenIssues /
    /// GetLastScanAt populates the cache; the count badge has the same
    /// warmup behavior. Returns (0, 0) on empty cache.
    /// </summary>
    public (int Total, int AddedSince) GetIssueCountsSince(DateTime lastSeenUtc)
    {
        IReadOnlyList<LintIssue> snapshot;
        int total;
        lock (_scanLock)
        {
            snapshot = _lastOpenIssues;
            total = _lastIssueCount;
        }
        if (snapshot.Count == 0) return (total, 0);

        var addedSince = 0;
        foreach (var issue in snapshot)
        {
            if (issue.FirstSeenAt.ToUniversalTime() > lastSeenUtc.ToUniversalTime())
                addedSince++;
        }
        return (total, addedSince);
    }

    public async Task<DateTime?> GetLastScanAtAsync(CancellationToken ct)
    {
        if (LastScanAt.HasValue) return LastScanAt;
        var state = await _store.LoadAsync(ct);
        // Cache the persisted value in-memory so subsequent reads don't hit disk.
        // While we hold the state, also seed the open-issue count cache so the
        // sidebar Lint badge becomes accurate after the first LastScanAt read
        // (which the LintInbox view performs on open).
        var openIssues = ComputeOpenIssues(state);
        lock (_scanLock)
        {
            if (!LastScanAt.HasValue)
                LastScanAt = state.LastScannedAt;
            _lastOpenIssues = openIssues;
            _lastIssueCount = openIssues.Count;
        }
        return LastScanAt;
    }

    private static int ComputeOpenCount(LintState state) => ComputeOpenIssues(state).Count;

    private static IReadOnlyList<LintIssue> ComputeOpenIssues(LintState state)
    {
        var dismissedKeys = state.Dismissals
            .Select(d => (d.RuleId, d.SubjectRef, d.DismissalStateHash))
            .ToHashSet();
        return state.OpenIssues
            .Where(i => !dismissedKeys.Contains((i.RuleId, i.SubjectRef, i.DismissalStateHash)))
            .ToList();
    }

    public void RegisterRule(ILintRule rule) => _rules.Add(rule);
    public void RegisterFixer(ILintFixer fixer) => _fixers[fixer.Id] = fixer;

    public async Task<ScanResult> RunScanAsync(CancellationToken ct)
    {
        lock (_scanLock)
        {
            if (_isScanning)
                throw new InvalidOperationException("Scan already in progress");
            _isScanning = true;
        }

        DateTime? completed = null;
        try
        {
            var sw = Stopwatch.StartNew();
            var ctx = _contextFactory();
            var prior = await _store.LoadAsync(ct);
            var allIssues = new List<LintIssue>();

            foreach (var rule in _rules)
            {
                ct.ThrowIfCancellationRequested();
                var issues = await rule.ScanAsync(ctx, ct);
                // Project Category from rule onto each emitted issue. Rules don't need
                // to set Category themselves; the runner is the single source of truth
                // for category-per-rule.
                foreach (var issue in issues)
                {
                    allIssues.Add(issue with { Category = rule.Category });
                }
            }

            completed = DateTime.UtcNow;
            var newState = prior with
            {
                LastScannedAt = completed,
                OpenIssues = allIssues,
            };
            await _store.SaveAsync(newState, ct);
            sw.Stop();

            // Refresh the cached open-issue list + count so the sidebar lens
            // badge reflects the scan result (count AND delta-since-LastSeen)
            // without waiting for a view refresh.
            var openIssues = ComputeOpenIssues(newState);
            lock (_scanLock)
            {
                _lastOpenIssues = openIssues;
                _lastIssueCount = openIssues.Count;
            }

            return new ScanResult(
                IssuesFound: allIssues.Count,
                IssuesRemoved: prior.OpenIssues.Count - allIssues.Count,
                CompletedAt: completed.Value,
                Duration: sw.Elapsed);
        }
        finally
        {
            lock (_scanLock)
            {
                if (completed.HasValue)
                    LastScanAt = completed.Value;
                _isScanning = false;
            }
        }
    }

    public async Task<IReadOnlyList<LintIssue>> GetOpenIssuesAsync(CancellationToken ct)
    {
        var state = await _store.LoadAsync(ct);
        var dismissedKeys = state.Dismissals
            .Select(d => (d.RuleId, d.SubjectRef, d.DismissalStateHash))
            .ToHashSet();
        var open = state.OpenIssues
            .Where(i => !dismissedKeys.Contains((i.RuleId, i.SubjectRef, i.DismissalStateHash)))
            .ToList();
        // Refresh the cached list + count for the sidebar lens badge on every read.
        lock (_scanLock)
        {
            _lastOpenIssues = open;
            _lastIssueCount = open.Count;
        }
        return open;
    }

    public async Task<LintFixResult> ApplyFixAsync(LintIssue issue, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.SuggestedFixVerbId))
            return new LintFixResult(false, "Issue has no SuggestedFixVerbId", null);
        if (!_fixers.TryGetValue(issue.SuggestedFixVerbId, out var fixer))
            return new LintFixResult(false,
                $"No fixer registered for verb '{issue.SuggestedFixVerbId}'", null);

        var ctx = _contextFactory();
        var result = await fixer.ApplyAsync(issue, ctx, ct);

        if (result.Success)
        {
            var state = await _store.LoadAsync(ct);
            var newState = state with
            {
                OpenIssues = state.OpenIssues.Where(i => i.Id != issue.Id).ToList()
            };
            await _store.SaveAsync(newState, ct);
            var openIssues = ComputeOpenIssues(newState);
            lock (_scanLock)
            {
                _lastOpenIssues = openIssues;
                _lastIssueCount = openIssues.Count;
            }
        }

        return result;
    }

    /// <summary>
    /// Apply a single chosen action of an issue. Dispatches to the fixer named by
    /// action.VerbId with action.PayloadJson; on success removes the issue.
    /// </summary>
    public async Task<LintFixResult> ApplyActionAsync(LintIssue issue, LintAction action, CancellationToken ct)
    {
        if (!_fixers.TryGetValue(action.VerbId, out var fixer))
            return new LintFixResult(false, $"No fixer registered for verb '{action.VerbId}'", null);

        var ctx = _contextFactory();
        var shim = issue with { SuggestedFixVerbId = action.VerbId, FixPayloadJson = action.PayloadJson };
        var result = await fixer.ApplyAsync(shim, ctx, ct);

        if (result.Success)
        {
            var state = await _store.LoadAsync(ct);
            var newState = state with
            {
                OpenIssues = state.OpenIssues.Where(i => i.Id != issue.Id).ToList()
            };
            await _store.SaveAsync(newState, ct);
            var openIssues = ComputeOpenIssues(newState);
            lock (_scanLock)
            {
                _lastOpenIssues = openIssues;
                _lastIssueCount = openIssues.Count;
            }
        }

        return result;
    }

    /// <summary>
    /// Apply the recommended action to every batch-eligible issue (exactly one
    /// action, Batchable == true). Coalesces the lint-state save into one write.
    /// Issues that are not batch-eligible are counted as Skipped.
    /// </summary>
    public async Task<BatchResult> ApplyBatchAsync(IEnumerable<LintIssue> issues, CancellationToken ct)
    {
        var ctx = _contextFactory();
        int fixedCount = 0, failed = 0, skipped = 0;
        var fixedIds = new List<string>();

        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();
            var actions = issue.Actions ?? Array.Empty<LintAction>();
            var action = actions.Count == 1 && actions[0].Batchable ? actions[0] : null;
            if (action == null) { skipped++; continue; }
            if (!_fixers.TryGetValue(action.VerbId, out var fixer)) { failed++; continue; }

            var shim = issue with { SuggestedFixVerbId = action.VerbId, FixPayloadJson = action.PayloadJson };
            var result = await fixer.ApplyAsync(shim, ctx, ct);
            if (result.Success) { fixedCount++; fixedIds.Add(issue.Id); }
            else { failed++; }
        }

        if (fixedIds.Count > 0)
        {
            var state = await _store.LoadAsync(ct);
            var newState = state with
            {
                OpenIssues = state.OpenIssues.Where(i => !fixedIds.Contains(i.Id)).ToList()
            };
            await _store.SaveAsync(newState, ct);
            var openIssues = ComputeOpenIssues(newState);
            lock (_scanLock)
            {
                _lastOpenIssues = openIssues;
                _lastIssueCount = openIssues.Count;
            }
        }

        return new BatchResult(fixedCount, failed, skipped);
    }

    public async Task DismissAsync(LintIssue issue, CancellationToken ct)
    {
        var state = await _store.LoadAsync(ct);
        var dismissal = new LintDismissal(
            RuleId: issue.RuleId,
            SubjectRef: issue.SubjectRef,
            DismissalStateHash: issue.DismissalStateHash,
            DismissedAt: DateTime.UtcNow);

        if (state.Dismissals.Any(d =>
            d.RuleId == dismissal.RuleId &&
            d.SubjectRef == dismissal.SubjectRef &&
            d.DismissalStateHash == dismissal.DismissalStateHash))
            return;

        var newState = state with
        {
            Dismissals = state.Dismissals.Append(dismissal).ToList()
        };
        await _store.SaveAsync(newState, ct);
        var openIssues = ComputeOpenIssues(newState);
        lock (_scanLock)
        {
            _lastOpenIssues = openIssues;
            _lastIssueCount = openIssues.Count;
        }
    }
}
