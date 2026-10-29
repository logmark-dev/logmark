using System.Text;
using System.Text.Json;
using LogMark.Models;
using LogMark.Services.Agents;
using LogMark.Services.EntryCache;

namespace LogMark.CLI.Commands;

/// <summary>
/// SessionStart hook executor: emits SessionStart context in the stdout wrapper
/// shape selected by the --framework flag (Claude Code, Gemini, Cursor, or
/// Copilot), defaulting to the Claude Code shape when absent or unknown. MUST
/// never damage an agent session: any failure (no vault, cold cache, bad stdin)
/// exits 0 with empty stdout and NO stderr (hosts treat hook stderr as failure),
/// unless LOGMARK_HOOK_DEBUG is set. Size-capped; every
/// entry is referenced by ShortId so the agent can fetch detail via MCP read_entry.
/// </summary>
public class AgentContextCommand : ICommand
{
    public string Name => "agent-context";
    public string Description => "Emit vault context for an agent SessionStart hook";
    public string Usage => "agent-context [--route <route>] [--framework <slug>]";
    public bool RequiresVault => false; // degrades to silence instead of failing the hook

    internal const int MaxPerSection = 10;
    internal const int DecisionWindowDays = 7;

    // When the primary working-state sections (blocks, tasks, questions,
    // decisions, recent activity) yield fewer than this many entries, the scope
    // is thin (e.g. an early-stage project) and we enrich with recent ideas.
    internal const int EnrichmentThreshold = 3;

    // Emitted when the hook fires after a context compaction so the agent
    // knows to re-read its project rules before continuing.
    internal const string RegroundingLine =
        "Context was compacted. If this project has a CLAUDE.md or AGENTS.md, re-read it before continuing.";

    public static Func<bool> IsInputRedirected { get; set; } = () => Console.IsInputRedirected;

    public int Execute(CliContext context, string[] args)
    {
        try
        {
            // Read the hook payload (drains the pipe). cwd is used for route
            // scoping when no --route is baked.
            var rawStdin = IsInputRedirected() ? Console.In.ReadToEnd() : "";

            // Detect compaction: Claude Code sets source="compact" when the session
            // transcript was condensed. Non-hook JSON (or a parse failure) is treated
            // as non-compact so the hook never breaks.
            var isCompact = false;
            if (!string.IsNullOrWhiteSpace(rawStdin))
            {
                try
                {
                    using var hookDoc = JsonDocument.Parse(rawStdin);
                    if (hookDoc.RootElement.TryGetProperty("source", out var src)
                        && string.Equals(src.GetString(), "compact", StringComparison.OrdinalIgnoreCase))
                        isCompact = true;
                }
                catch (JsonException) { /* not hook JSON: treat as non-compact */ }
            }

            string? route = null;
            string? framework = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--route" && i + 1 < args.Length) route = args[++i];
                else if (args[i] == "--framework" && i + 1 < args.Length) framework = args[++i];
            }

            context.WaitForCacheWarmup();

            var shape = HookProfileRegistry.For(framework ?? "claude-code")
                        ?? HookProfileRegistry.For("claude-code")!;

            // Resolve the query scope. An explicit baked --route always pins;
            // otherwise resolve from the host-supplied cwd against route workdirs.
            string? queryRoute = route;
            if (route is null)
            {
                var scope = AgentHookScope.Resolve(context.VaultPath, rawStdin, shape.CwdField);
                if (scope.Result.Kind == HookScopeKind.Unbound && scope.FeatureActive)
                {
                    // The workdir feature is in use but this directory is not bound:
                    // hint instead of dumping unrelated context.
                    var hint = "This directory is not linked to a LogMark route. "
                             + $"Run `{CommandRunner.CliName} link <route>` in it to scope vault context to that route.";
                    if (isCompact) hint = RegroundingLine + "\n\n" + hint;
                    context.WriteResult(WrapContext(shape.InjectOutput, hint));
                    return 0;
                }
                // Route -> scope to it; WholeVault or (Unbound + feature unused) -> whole vault (null).
                queryRoute = scope.Result.Kind == HookScopeKind.Route ? scope.Result.Route : null;
            }

            // Primary working-state, highest-value first. Open blocks/tasks/
            // questions by openness; recent decisions (durable); recent activity
            // (logs/diary, incl. untyped quick notes) is the session handoff that
            // lm-checkpoint writes and the agent reads back here.
            var (blocks, blocksTotal) = QueryWithTotal(context, EntryType.Block, queryRoute, notCompleted: true);
            var (tasks, tasksTotal) = QueryWithTotal(context, EntryType.Task, queryRoute, notCompleted: true);
            var questions = Query(context, EntryType.Question, queryRoute, notCompleted: true);
            var decisions = Query(context, EntryType.Decision, queryRoute,
                notCompleted: false, after: DateTime.Now.AddDays(-DecisionWindowDays));
            var activity = Query(context, EntryType.Log, queryRoute, notCompleted: false);

            var primaryCount = blocks.Count + tasks.Count + questions.Count
                             + decisions.Count + activity.Count;

            // Thin scope (e.g. an early-stage project that is mostly ideas):
            // enrich with recent ideas so the agent still gets the backlog as
            // context. Skipped when primary state is substantive, so the idea
            // pile never crowds out active work.
            var ideas = primaryCount < EnrichmentThreshold
                ? Query(context, EntryType.Idea, queryRoute, notCompleted: false)
                : (IReadOnlyList<IndexedEntry>)Array.Empty<IndexedEntry>();

            if (primaryCount == 0 && ideas.Count == 0)
            {
                if (isCompact)
                    context.WriteResult(WrapContext(shape.InjectOutput, RegroundingLine));
                return 0; // empty stdout = no context injected, by design
            }

            var md = isCompact
                ? BuildContinuation(context, shape, queryRoute, blocks, blocksTotal, tasks, tasksTotal)
                : BuildFresh(shape, queryRoute, blocks, tasks, questions, decisions, activity, ideas);
            context.WriteResult(WrapContext(shape.InjectOutput, md));
            return 0;
        }
        catch (Exception ex)
        {
            // Stderr would make a host flag the hook as failed; stay silent
            // unless LOGMARK_HOOK_DEBUG is set. Always exit 0.
            HookDiagnostics.Warn($"agent-context: {ex.Message}");
            return 0; // never break the session
        }
    }

    private static string WrapContext(HookOutputKind kind, string md) => kind switch
    {
        HookOutputKind.GeminiStyle => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["hookSpecificOutput"] = new Dictionary<string, object?> { ["additionalContext"] = md },
        }),
        HookOutputKind.CursorStyle => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["additional_context"] = md,
        }),
        HookOutputKind.CopilotStyle => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["additionalContext"] = md,
        }),
        _ => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["hookSpecificOutput"] = new Dictionary<string, object?>
            {
                ["hookEventName"] = "SessionStart",
                ["additionalContext"] = md,
            },
        }),
    };

    private static IReadOnlyList<IndexedEntry> Query(
        CliContext context, EntryType type, string? route, bool notCompleted, DateTime? after = null)
        => QueryWithTotal(context, type, route, notCompleted, after).Entries;

    /// <summary>
    /// Same query as <see cref="Query"/>, but also returns the pre-Limit
    /// TotalCount. BuildContinuation's pointer line needs the true open count
    /// (not the MaxPerSection=10 page) to report how many open items it did
    /// NOT show inline - Entries.Count alone would undercount past 10.
    /// </summary>
    private static (IReadOnlyList<IndexedEntry> Entries, int Total) QueryWithTotal(
        CliContext context, EntryType type, string? route, bool notCompleted, DateTime? after = null)
    {
        var query = new SearchQuery
        {
            Types = new[] { type },
            Route = route,
            After = after,
            IsCompleted = notCompleted ? false : null,
            Lifecycle = LifecycleFilter.Open,
            Limit = MaxPerSection,
            Sort = SearchSort.TimestampDesc,
        };
        var result = context.SearchService.Search(query);
        return (result.Entries, result.TotalCount);
    }

    /// <summary>
    /// The pre-2026-08-19 fresh-start shape: every primary section rendered
    /// under its own budget share. The shares are WEIGHTS, not absolute caps -
    /// blocks/tasks (the highest-value working state) get large shares, recent
    /// activity (the handoff) the largest, and the lower-signal sections
    /// (questions, decisions, ideas) less - scaled by Render to whatever the
    /// host's ContextBudgetChars leaves. As literal caps they summed to exactly
    /// the old 3000 default, so the profile budget never bound upward for
    /// Claude Code and bound only downward for every host still on 3000.
    /// </summary>
    private static string BuildFresh(
        HookProfile shape,
        string? route,
        IReadOnlyList<IndexedEntry> blocks,
        IReadOnlyList<IndexedEntry> tasks,
        IReadOnlyList<IndexedEntry> questions,
        IReadOnlyList<IndexedEntry> decisions,
        IReadOnlyList<IndexedEntry> activity,
        IReadOnlyList<IndexedEntry> ideas)
    {
        var intro = route is null
            ? "LogMark vault context:"
            : $"LogMark vault context for +{route}:";

        var sections = new List<PayloadSection>
        {
            new("Open blocks", Section(blocks), PayloadBudget.BlocksWeight),
            new("Open tasks", Section(tasks), PayloadBudget.TasksWeight),
            new("Open questions", Section(questions), PayloadBudget.QuestionsWeight),
            new($"Decisions (last {DecisionWindowDays} days)", Section(decisions), PayloadBudget.DecisionsWeight),
            new("Recent activity", Section(activity), PayloadBudget.ActivityWeight),
            new("Recent ideas", Section(ideas), PayloadBudget.IdeasWeight),
        };

        var sectionsBudget = Math.Max(0, shape.ContextBudgetChars - PayloadBudget.FooterReserve - intro.Length);

        // AgentContextPayload.Render emits bare "\n" throughout (each section header
        // is "\n{title}:\n"), so the intro and footer joins below use "\n" too rather
        // than AppendLine/Environment.NewLine (CRLF on Windows) - one line ending for
        // the whole rendered payload. The blank line after the intro comes from
        // pairing this "\n" with the leading "\n" of the first section's header.
        var footer = "\n" + RetrievalLine + " Orient with vault_summary or get_project_context.";

        // Scaling on: the section shares are weights against this host's budget.
        return intro + "\n"
             + AgentContextPayload.Render(sections, sectionsBudget, scaleReservesToBudget: true, out _)
             + footer;
    }

    /// <summary>
    /// The post-compaction shape: lead with what the previous stretch of work
    /// left behind, show only what is genuinely in flight, and point at the
    /// rest by count rather than re-dumping it. Replaces the old
    /// one-line-RegroundingLine-prepend + full fresh-start dump: a resuming
    /// agent used to get the same backlog a cold-start agent gets (up to 10
    /// open blocks/tasks each, oldest first once truncated) with the handoff
    /// buried or dropped inside it.
    /// </summary>
    private static string BuildContinuation(
        CliContext context,
        HookProfile shape,
        string? route,
        IReadOnlyList<IndexedEntry> blocks,
        int blocksTotal,
        IReadOnlyList<IndexedEntry> tasks,
        int tasksTotal)
    {
        // Recency-anchored fallback window; see HandoffResolver's doc comment.
        // lmk-continuation-anchor-by-session-identity-nhwh replaces this with a
        // session-identity anchor, keeping 24h as the documented fallback.
        var since = DateTime.Now.AddHours(-24);

        var lead = route is null
            ? $"Continuing work. {RegroundingLine}"
            : $"Continuing work in +{route}. {RegroundingLine}";

        var sb = new StringBuilder(lead);

        var handoff = HandoffResolver.Resolve(context.SearchService, route, since);
        if (handoff is not null)
            sb.Append("\n\nWhere you left off:\n").Append(HandoffLine(handoff));

        // In flight: open blocks/tasks whose timestamp falls inside the same
        // window the handoff was resolved against, newest first across both
        // types together (a block from an hour ago outranks a task from
        // yesterday regardless of type).
        var inFlightBlocks = blocks.Where(ie => ie.Entry.Timestamp >= since).ToList();
        var inFlightTasks = tasks.Where(ie => ie.Entry.Timestamp >= since).ToList();
        var inFlight = inFlightBlocks.Concat(inFlightTasks)
            .OrderByDescending(ie => ie.Entry.Timestamp)
            .ToList();

        // Render can drop further items once the section's own reserve is
        // exceeded - a second, budget-driven cut on top of the time-window
        // cut above. The pointer line below must count BOTH cuts, not just
        // the first, or it undercounts exactly in the crowded case where it
        // matters most (15 open, 10 fetched, 7 rendered -> the pointer must
        // say "8 other", not "5"). shownCount comes from Render itself
        // (the actual render, not a pre-computed estimate), so this can never
        // drift from what the payload really shows.
        var shownCount = 0;
        if (inFlight.Count > 0)
        {
            // Routed through the same AgentContextPayload.Render machinery
            // BuildFresh uses (Task 5), not a raw string.Join: a pathologically
            // large in-flight count (many blocks/tasks all captured within the
            // last 24h) must stop at an item boundary and report how many it
            // dropped, not get hard-sliced mid-item by a whole-payload
            // character cap. The fixed reserve below caps this section even
            // when the overall profile budget is generous (8000 for
            // claude-code), so a resuming agent gets "here's what's active,
            // plus N more via get_blocks" rather than an ever-growing dump.
            // Rendered WITHOUT scaling for exactly that reason: normalising a
            // single section's weight would hand it the whole remaining budget.
            var remainingBudget = Math.Max(0,
                shape.ContextBudgetChars - sb.Length - PayloadBudget.PointerReserve);
            var inFlightSections = new List<PayloadSection>
            {
                new("In flight", Section(inFlight), PayloadBudget.InFlightReserve),
            };
            var renderedInFlight = AgentContextPayload.Render(inFlightSections, remainingBudget, out shownCount);
            sb.Append("\n").Append(renderedInFlight);
        }

        // Render always consumes a section's Lines in order and never
        // reorders or skips ahead, so the first shownCount items of the
        // already-sorted inFlight list ARE exactly the ones it rendered -
        // reconstructing that prefix is how the block/task split below stays
        // accurate without Render needing to know entry types at all.
        var shownInFlight = inFlight.Take(shownCount).ToList();
        var shownBlocks = shownInFlight.Count(ie => ie.Entry.Type == EntryType.Block);
        var shownTasks = shownInFlight.Count(ie => ie.Entry.Type == EntryType.Task);

        // otherTasks/otherBlocks are open items the payload did not show,
        // full stop: total open minus what actually landed in the rendered
        // text above - never the pre-Render candidate count, which would
        // silently omit whatever the budget cut dropped on top of the
        // time-window cut.
        var otherTasks = Math.Max(0, tasksTotal - shownTasks);
        var otherBlocks = Math.Max(0, blocksTotal - shownBlocks);
        var pendingReviews = context.PendingDistillReviewCount(route);
        sb.Append(PointerLine(otherTasks, otherBlocks, pendingReviews));

        // Defensive cap: lead is a short fixed sentence, the handoff is
        // clipped to PayloadBudget.HandoffClipChars, and In flight goes
        // through Render's own
        // item-boundary budget above, so this should never fire in practice -
        // kept as a last-resort guard so the hook can never emit an unbounded
        // payload regardless of vault content.
        var result = sb.ToString();
        return result.Length > shape.ContextBudgetChars ? result[..shape.ContextBudgetChars] : result;
    }

    private static string HandoffLine(Handoff handoff)
    {
        var firstLine = handoff.Content.Split('\n')[0].Trim();
        if (firstLine.Length > PayloadBudget.HandoffClipChars)
            firstLine = firstLine[..PayloadBudget.HandoffClipChars];
        return $"- {firstLine} (id: {handoff.ShortId})";
    }

    /// <summary>
    /// The continuation shape's closing line: what the payload deliberately did
    /// NOT show, and how to reach it.
    ///
    /// Always non-empty. When nothing is left over it still emits the retrieval
    /// instruction, because the continuation path has no read_entry footer of
    /// its own and used to end with no way to fetch detail at all.
    ///
    /// The review-count clause names where review happens. The count is
    /// surfaced deliberately (the staged entries are invisible to orient
    /// otherwise), but "await review" alone implied an action the agent can
    /// take, and no MCP tool exposes the session queue - review is a desktop
    /// feed gesture. Internal so the review clause is testable: the open
    /// CliContext returns null for the count by design, so no open-build
    /// end-to-end run can reach it.
    /// </summary>
    internal static string PointerLine(int otherTasks, int otherBlocks, int? pendingReviews)
    {
        var total = otherTasks + otherBlocks;

        var clauses = new List<string>
        {
            total > 0
                ? $"{total} other open items ({otherTasks} tasks, {otherBlocks} blocks) - "
                  + "get_project_context or get_blocks for the rest."
                : RetrievalLine,
        };

        if (pendingReviews is > 0)
        {
            var noun = pendingReviews == 1 ? "session awaits" : "sessions await";
            clauses.Add($"{pendingReviews} distilled {noun} review in the LogMark desktop feed.");
        }

        return "\n" + string.Join(" ", clauses);
    }

    internal const string RetrievalLine =
        "Fetch any entry's full detail via the LogMark MCP tool read_entry(id).";

    private static IReadOnlyList<string> Section(IReadOnlyList<IndexedEntry> entries) =>
        entries.Select(ie =>
        {
            var firstLine = ie.Entry.Content.Split('\n')[0].Trim();
            if (firstLine.Length > PayloadBudget.ItemClipChars)
                firstLine = firstLine[..PayloadBudget.ItemClipChars];
            var id = string.IsNullOrEmpty(ie.Entry.ShortId) ? "" : $" (id: {ie.Entry.ShortId})";
            return $"- {firstLine}{id}";
        }).ToList();
}
