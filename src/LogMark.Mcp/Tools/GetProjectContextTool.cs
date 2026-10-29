using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool for aggregated context load for session bootstrap.
/// Returns frozen decisions, active tasks, open blocks, and recent entries for a project.
/// </summary>
public class GetProjectContextTool : ITool
{
    private readonly VaultServices _services;

    public GetProjectContextTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "get_project_context";

    public ToolDefinition Definition => new()
    {
        Name = "get_project_context",
        Description = "Load aggregated context for a route. Returns route description, vault notes, scope descriptions, frozen decisions, active tasks, open blocks, and recent entries in a single call. List entries are concise (id, type, timestamp, truncated content); Summary carries the full totals. Fetch any entry's full detail with read_entry(id). Find this tool when: project state briefing, decisions plus tasks plus blockers.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Required route to load context for (e.g., 'logmark' or '+logmark')."
                },
                ["scopes"] = new()
                {
                    Type = "array",
                    Description = "Optional list of scopes to include. Default: all scopes.",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["status_tags"] = new()
                {
                    Type = "array",
                    Description = "Status tags to filter decisions (e.g., ['#frozen', '#active']). Default: ['#frozen'].",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["include_recent"] = new()
                {
                    Type = "boolean",
                    Description = "Include recent entries. Default: true.",
                    Default = true
                },
                ["recent_days"] = new()
                {
                    Type = "integer",
                    Description = "How many days back for recent entries. Default: 7.",
                    Default = 7
                },
                ["recent_limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum recent entries to return. Default: 10.",
                    Default = 10
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum active tasks and open blocks returned per list. Default: 15. Summary always carries the full totals.",
                    Default = 15
                }
            },
            Required = new List<string> { "route" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
        Meta = new Dictionary<string, object?> { ["anthropic/alwaysLoad"] = true },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var route = McpToolHelpers.GetStringArg(arguments, "route")?.TrimStart('+');
        if (string.IsNullOrWhiteSpace(route))
        {
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: route"));
        }

        var scopes = McpToolHelpers.GetStringArrayArg(arguments, "scopes");
        var statusTags = McpToolHelpers.GetStringArrayArg(arguments, "status_tags") ?? new List<string> { "#frozen" };
        var includeRecent = McpToolHelpers.GetBoolArg(arguments, "include_recent") ?? true;
        var recentDays = McpToolHelpers.GetIntArg(arguments, "recent_days") ?? 7;
        var recentLimit = McpToolHelpers.GetIntArg(arguments, "recent_limit") ?? 10;
        var limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 15;

        try
        {
            // Parse routes.md once for route context and scope discovery
            var parser = new RouteParser(_services.VaultPath!);
            var routeConfig = parser.Parse();
            var routeObj = routeConfig.Routes.FirstOrDefault(r =>
                r.Name.Equals(route, StringComparison.OrdinalIgnoreCase));

            // Build route context from vault metadata
            var routeContext = BuildRouteContext(routeConfig, routeObj);

            // Gather decisions (with status tags)
            var decisionsResult = _services.SearchService!.Search(new SearchQuery
            {
                Types = new[] { EntryType.Decision },
                Route = route,
                RequiredTags = statusTags,
                Limit = 200,
                Sort = SearchSort.TimestampAsc
            });

            // Route filters include child routes (fin-inv inside fin), and the
            // task/block/recent lists below keep them, each row carrying its own
            // route. Scopes and decisions describe THIS route only: a child's
            // decisions belong to the child, which ChildRoutes points at.
            bool Own(IndexedEntry ie) => string.Equals(ie.Route, route, StringComparison.OrdinalIgnoreCase);
            var ownDecisions = decisionsResult.Entries.Where(Own).ToList();

            // Discover scopes from decisions and other entries
            var discoveredScopes = scopes ?? DiscoverScopes(route, routeConfig, routeObj);

            var scopeContexts = new List<ScopeContext>();
            foreach (var scope in discoveredScopes)
            {
                var scopeDecisions = ownDecisions
                    .Where(ie => string.Equals(ie.Scope, scope, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var resolvedDecisions = ConciseEntry.FromMany(
                    _services.EntryResolver!.ResolveMany(scopeDecisions, ResolveOptions.Query));

                var hasBlocks = _services.SearchService!.Search(new SearchQuery
                {
                    Types = new[] { EntryType.Block },
                    Route = route,
                    Scope = scope,
                    IsCompleted = false,
                    Lifecycle = LifecycleFilter.Open,
                    Limit = 1
                }).Entries.Count > 0;

                var hasActiveTasks = _services.SearchService!.Search(new SearchQuery
                {
                    Types = new[] { EntryType.Task },
                    Route = route,
                    Scope = scope,
                    IsCompleted = false,
                    Lifecycle = LifecycleFilter.Open,
                    Limit = 1
                }).Entries.Count > 0;

                scopeContexts.Add(new ScopeContext
                {
                    Scope = scope,
                    Decisions = resolvedDecisions,
                    DecisionCount = resolvedDecisions.Count,
                    HasBlocks = hasBlocks,
                    HasActiveTasks = hasActiveTasks
                });
            }

            // Root-level decisions (no scope)
            var rootDecisionEntries = ownDecisions
                .Where(ie => ie.Scope == null)
                .ToList();
            var rootDecisions = ConciseEntry.FromMany(
                _services.EntryResolver!.ResolveMany(rootDecisionEntries, ResolveOptions.Query));

            // Fallback. The tag-filtered read is correct but near-empty on any
            // vault whose decisions were written by session-distill, which does
            // not tag. Returning nothing there is worse than returning
            // untagged decisions clearly marked as unconfirmed. Threshold is
            // two: one row is not a section. See
            // lmk-distill-decisions-carry-no-status-tag-0bs6 for the underlying
            // reconciliation this mitigates rather than fixes.
            const int MinTaggedDecisions = 2;
            const int FallbackDecisionLimit = 10;
            var decisionsAreFallback = false;
            if (rootDecisions.Count < MinTaggedDecisions)
            {
                // Same source data as the tagged root set above (Scope == null)
                // and the same route-wide Limit as the tagged decisionsResult
                // query above - this must see the identical route-wide read the
                // tagged set saw, just without the RequiredTags filter, or a
                // decision that is correctly scoped elsewhere and already
                // frozen gets pulled into "(root)" and relabelled unconfirmed.
                //
                // The route-wide Limit stays at 200 and the newest-N cut is
                // applied AFTER the Scope == null filter: cutting in the query
                // would let scoped rows crowd root-scoped ones out of the
                // window before the filter ever runs.
                var untagged = _services.SearchService!.Search(new SearchQuery
                {
                    Types = new[] { EntryType.Decision },
                    Route = route,
                    Limit = 200,
                    Sort = SearchSort.TimestampDesc
                }).Entries.Where(ie => Own(ie) && ie.Scope == null).ToList();

                if (untagged.Count > rootDecisions.Count)
                {
                    // Select by recency, present chronologically. The spec said
                    // "falls back to recent decisions" and the code took all of
                    // them: on projects/LogMark that is 165 entries / 66KB of
                    // bodies plus per-entry metadata, in the tool lm-orient
                    // calls on every orient. FallbackDecisionLimit matches this
                    // tool's own recent_limit default - the fallback IS a
                    // "recent decisions" list, which is exactly what that
                    // parameter governs.
                    //
                    // Re-sorting ascending before emit matters: the tagged
                    // query sorts TimestampAsc, so emitting newest-first here
                    // would flip the same section's order depending on which
                    // path fired. Selection order and presentation order are
                    // different questions and only the first one needs Desc.
                    //
                    // List(), like the sibling activity lists, after the
                    // measured 36KB response in lmk-1zld. The TAGGED set stays
                    // on Query deliberately: frozen decisions are this tool's
                    // durable semantic payload, and there is at most a handful.
                    var selected = untagged
                        .Take(FallbackDecisionLimit)
                        .OrderBy(ie => ie.Entry.Timestamp)
                        .ToList();
                    rootDecisions = ConciseEntry.FromMany(
                        _services.EntryResolver!.ResolveMany(selected, ResolveOptions.List()));
                    decisionsAreFallback = true;
                }
            }

            if (rootDecisions.Count > 0)
            {
                var hasRootBlocks = _services.SearchService!.Search(new SearchQuery
                {
                    Types = new[] { EntryType.Block },
                    Route = route,
                    IsCompleted = false,
                    Lifecycle = LifecycleFilter.Open,
                    Limit = int.MaxValue
                }).Entries.Any(ie => Own(ie) && ie.Scope == null);

                var hasRootTasks = _services.SearchService!.Search(new SearchQuery
                {
                    Types = new[] { EntryType.Task },
                    Route = route,
                    IsCompleted = false,
                    Lifecycle = LifecycleFilter.Open,
                    Limit = int.MaxValue
                }).Entries.Any(ie => Own(ie) && ie.Scope == null);

                scopeContexts.Insert(0, new ScopeContext
                {
                    Scope = "(root)",
                    Decisions = rootDecisions,
                    DecisionCount = rootDecisions.Count,
                    HasBlocks = hasRootBlocks,
                    HasActiveTasks = hasRootTasks,
                    DecisionsUnconfirmed = decisionsAreFallback ? true : null
                });
            }

            // Gather open blocks
            var blocksResult = _services.SearchService!.Search(new SearchQuery
            {
                Types = new[] { EntryType.Block },
                Route = route,
                IsCompleted = false,
                Lifecycle = LifecycleFilter.Open,
                Limit = 50,
                // Newest first: this section answers "what is stuck now".
                Sort = SearchSort.TimestampDesc
            });
            var openBlockEntries = blocksResult.Entries.ToList();
            var totalOpenBlocks = blocksResult.TotalCount;
            // List + ConciseEntry, not Query: these activity lists carry many
            // entries at once and dominated a measured 36KB response after the
            // first lmk-1zld round - per-entry metadata alone (session GUID,
            // path, line numbers) cost ~525 bytes x 50 entries. Decisions stay
            // on Query (full content) deliberately - frozen decisions are this
            // tool's durable semantic payload - but share the concise metadata
            // shape. Ids are returned either way, so read_entry recovers the
            // full record. Defaults and shape follow the client-cap survey in
            // docs/notes/2026-07-31-mcp-tool-result-size-research.md.
            var openBlocks = ConciseEntry.FromMany(_services.EntryResolver!.ResolveMany(
                openBlockEntries.Take(limit).ToList(), ResolveOptions.List()));

            // Gather active tasks
            var tasksResult = _services.SearchService!.Search(new SearchQuery
            {
                Types = new[] { EntryType.Task },
                Route = route,
                IsCompleted = false,
                Lifecycle = LifecycleFilter.Open,
                Limit = limit,
                Sort = SearchSort.TimestampDesc
            });
            var totalActiveTasks = tasksResult.TotalCount;
            var activeTasks = ConciseEntry.FromMany(
                _services.EntryResolver!.ResolveMany(tasksResult.Entries, ResolveOptions.List()));

            // Gather recent entries
            List<ConciseEntry>? recentEntries = null;
            if (includeRecent)
            {
                var recentResult = _services.SearchService!.GetRecent(recentDays, new SearchQuery
                {
                    Route = route,
                    Limit = recentLimit
                });
                // Was Preview(150), which emitted a 150-char ContentPreview
                // ALONGSIDE the still-full Content - strictly more bytes, not
                // fewer. List truncates Content in place instead.
                recentEntries = ConciseEntry.FromMany(
                    _services.EntryResolver!.ResolveMany(recentResult.Entries, ResolveOptions.List(150)));
            }

            // Counts the decisions this response ACTUALLY returned, across every
            // scope. It used to count the tag-filtered set while
            // Scopes[(root)].DecisionCount reported the fallback set, so the
            // two numbers in one response described different sets - the same
            // defect shape lmk-nnd5 filed against this tool, and closing that
            // shape is why this plan exists. FrozenDecisions below stays a
            // statement about the tagged set, which remains true as a subset
            // claim whether or not the fallback fired.
            var totalDecisions = scopeContexts.Sum(s => s.DecisionCount);
            var frozenDecisions = ownDecisions
                .Count(ie => NotationParser.ExtractTags(ie.Entry.Content)
                    .Contains("frozen", StringComparer.OrdinalIgnoreCase));

            return Task.FromResult(McpToolHelpers.SuccessResult(new ProjectContextResult
            {
                Route = route,
                RouteContext = routeContext,
                Scopes = scopeContexts,
                ChildRoutes = BuildChildRoutes(route),
                RecentEntries = recentEntries,
                OpenBlocks = openBlocks,
                ActiveTasks = activeTasks,
                // ActiveTasks/OpenBlocks are totals, not returned-list lengths:
                // those lists are capped at `limit`, and the counts are how an
                // agent knows more exist. TotalDecisions is deliberately NOT of
                // that kind - it counts what Scopes[].Decisions actually holds,
                // because a decision count that described a different set than
                // the array beside it is the defect this field is fixing.
                Summary = new ContextSummary
                {
                    TotalDecisions = totalDecisions,
                    FrozenDecisions = frozenDecisions,
                    ActiveTasks = totalActiveTasks,
                    OpenBlocks = totalOpenBlocks
                }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to get project context: {ex.Message}"));
        }
    }

    private RouteContextInfo BuildRouteContext(RouteConfig routeConfig, Route? routeObj)
    {
        var context = new RouteContextInfo
        {
            VaultNotes = routeConfig.VaultNotes
        };

        if (routeObj != null)
        {
            context.Description = routeObj.Description;
            context.Section = routeObj.Section;
            context.SectionNotes = routeObj.SectionNotes;

            if (routeObj.Scopes.Count > 0)
            {
                context.Scopes = routeObj.Scopes;
            }
        }

        return context;
    }

    private List<string> DiscoverScopes(string route, RouteConfig config, Route? routeObj)
    {
        // Prefer configured scopes from routes.md
        if (routeObj?.Scopes.Count > 0)
        {
            return routeObj.Scopes.Keys.ToList();
        }

        // Try discovering from cached entries first
        var allForRoute = _services.SearchService!.Search(new SearchQuery
        {
            Route = route,
            Limit = 1000
        });

        var cachedScopes = allForRoute.Entries
            .Where(ie => ie.Scope != null
                && string.Equals(ie.Route, route, StringComparison.OrdinalIgnoreCase))
            .Select(ie => ie.Scope!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cachedScopes.Count > 0)
            return cachedScopes;

        // Fall back to filesystem discovery for custom route paths
        if (routeObj?.Path != null)
        {
            var logmarkRoot = Path.Combine(_services.VaultPath!, "logmark");
            var normalizedPath = routeObj.Path.Replace('/', Path.DirectorySeparatorChar);
            var routePath = Path.Combine(logmarkRoot, normalizedPath);
            if (Directory.Exists(routePath))
            {
                return Directory.GetDirectories(routePath)
                    .Select(Path.GetFileName)
                    .Where(name => name != null && !name.StartsWith('_'))
                    .Select(name => name!)
                    .ToList();
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// Routes declared inside this route's folder, one line each, so an agent
    /// can ask for a child directly. Null when there are none.
    /// </summary>
    private List<ChildRouteContext>? BuildChildRoutes(string route)
    {
        var children = _services.EntryCache?.Routes.ChildrenOf(route);
        if (children == null || children.Count == 0)
            return null;

        int Count(string child, EntryType type) => _services.SearchService!.Search(new SearchQuery
        {
            Types = new[] { type },
            Route = child,
            IsCompleted = false,
            Lifecycle = LifecycleFilter.Open,
            Limit = 1
        }).TotalCount;

        return children
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ChildRouteContext
            {
                Route = c,
                OpenTasks = Count(c, EntryType.Task),
                OpenBlocks = Count(c, EntryType.Block)
            })
            .ToList();
    }

    private class ChildRouteContext
    {
        public string Route { get; set; } = "";
        public int OpenTasks { get; set; }
        public int OpenBlocks { get; set; }
    }

    private class ProjectContextResult
    {
        public string Route { get; set; } = "";
        public RouteContextInfo? RouteContext { get; set; }
        public List<ScopeContext> Scopes { get; set; } = new();
        public List<ChildRouteContext>? ChildRoutes { get; set; }
        public List<ConciseEntry>? RecentEntries { get; set; }
        public List<ConciseEntry>? OpenBlocks { get; set; }
        public List<ConciseEntry>? ActiveTasks { get; set; }
        public ContextSummary Summary { get; set; } = new();
    }

    private class RouteContextInfo
    {
        public string? VaultNotes { get; set; }
        public string? Description { get; set; }
        public string? Section { get; set; }
        public string? SectionNotes { get; set; }
        public Dictionary<string, string>? Scopes { get; set; }
    }

    private class ScopeContext
    {
        public string Scope { get; set; } = "";
        public List<ConciseEntry> Decisions { get; set; } = new();
        public int DecisionCount { get; set; }
        public bool HasBlocks { get; set; }
        public bool HasActiveTasks { get; set; }

        // Set only when Decisions fell back to the untagged pool (see the
        // fallback block above ExecuteAsync). Nullable so a normal
        // tagged-and-unchanged scope omits the property entirely rather than
        // emitting a misleading "false" - the caller (the orientation skill
        // reading get_project_context) must be able to tell a frozen decision
        // from an unconfirmed one from this response alone.
        public bool? DecisionsUnconfirmed { get; set; }
    }

    private class ContextSummary
    {
        /// <summary>
        /// How many decisions this response returned, summed over
        /// Scopes[].Decisions. Not a route-wide total: the root scope's list is
        /// capped when the untagged fallback fires, and this number reports
        /// what is actually in the payload rather than a different set.
        /// </summary>
        public int TotalDecisions { get; set; }

        /// <summary>
        /// How many of the tag-filtered decisions carry #frozen. A statement
        /// about the tagged set regardless of whether the fallback fired.
        /// </summary>
        public int FrozenDecisions { get; set; }
        public int ActiveTasks { get; set; }
        public int OpenBlocks { get; set; }
    }
}
