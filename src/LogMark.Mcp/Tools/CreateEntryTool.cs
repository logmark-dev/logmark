using System.Text.Json;
using LogMark.Exceptions;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Models.Sensitivity;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to create a new entry in the vault.
/// </summary>
public class CreateEntryTool : ITool
{
    private readonly VaultServices _services;

    public CreateEntryTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "create_entry";

    public ToolDefinition Definition => new()
    {
        Name = "create_entry",
        Description = "Create a new entry in the LogMark vault. Supports notation prefixes (t:, i:, b:, d:) for entry types, advanced prefixes (q:, g:, a:) for questions/gaps/analogies, +route for routing, and #tags. Find this tool when: capturing a note, logging a thought, jotting a task or idea.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Entry content. May include notation prefixes (t: for task, i: for idea, b: for block, d: for decision) or advanced prefixes (q: for question, g: for gap, a: for analogy), +route for routing to an existing route (an undeclared +word is kept as text, never creates a route), and #tags. To reference other vault pages, embed Obsidian-style wiki links inline: `[[target-page-name]]` or `[[target-page-name|display text]]`. To reference other entries by id, prefer the 'refs' array (typed references) over inline mentions."
                },
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Optional route target (e.g., '+logmark'). When given, the entry goes to this route only; other +words in content are kept as text. Must already exist in a vault with several sections (declare new routes with update_routes)."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Optional scope within route (e.g., 'architecture'). Creates two-level routing."
                },
                ["entry_type"] = new()
                {
                    Type = "string",
                    Description = "Optional explicit entry type. Overridden by notation prefix in content.",
                    Enum = new List<string> { "task", "idea", "block", "decision", "log", "timetrack", "question", "gap", "analogy" }
                },
                ["tags"] = new()
                {
                    Type = "array",
                    Description = "Optional tags to add (without # prefix).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["context"] = new()
                {
                    Type = "string",
                    Description = "Optional context override (default: 'MCP')."
                },
                ["session"] = new()
                {
                    Type = "string",
                    Description = "Optional session ID to associate entry with (format: YYYY-MM-DD-hash)."
                },
                ["refs"] = new()
                {
                    Type = "array",
                    Description = "Optional cross-entry references (format: 'type:shortid', e.g., 'task:b123bbe1').",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["source_agent"] = new()
                {
                    Type = "string",
                    Description = "Optional agent identifier for provenance tracking (e.g., 'claude-code', 'custom-bot'). Stored in entry source field."
                }
            },
            Required = new List<string> { "content" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments == null || !arguments.TryGetValue("content", out var contentObj) || contentObj is not JsonElement contentElement)
        {
            return ErrorResult("Missing required parameter: content");
        }

        var content = contentElement.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return ErrorResult("Content cannot be empty");
        }

        // Normalize facet-tag separators (#key:value -> #key/value) so disk prefers
        // the Obsidian-portable '/' form. Wikilinks and prose colons are untouched.
        content = NotationParser.NormalizeFacetTagsInText(content!);

        // Parse optional parameters
        var route = GetStringArg(arguments, "route");
        var scope = GetStringArg(arguments, "scope");
        var entryTypeStr = GetStringArg(arguments, "entry_type");
        var context = GetStringArg(arguments, "context") ?? "MCP";
        var tags = GetStringArrayArg(arguments, "tags");
        var sessionId = GetStringArg(arguments, "session");
        var refsInput = GetStringArrayArg(arguments, "refs");
        var sourceAgent = GetStringArg(arguments, "source_agent");

        // Combine scope with route if provided
        if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(scope))
        {
            route = $"{route.TrimStart('+')}/{scope}";
        }

        // An explicit route that is not declared yet: let the headless prompt
        // place it (sole section / sectionless vault) or refuse before writing.
        if (!string.IsNullOrEmpty(route))
        {
            var routeBase = route.TrimStart('+').Split('/', 2)[0];
            if (!_services.RouteService!.IsDeclaredRouteToken(routeBase)
                && await _services.RouteService.ResolveAsync(routeBase, RouteCreationPolicy.Prompt) == null)
            {
                return ErrorResult(
                    $"[ROUTE_NOT_DECLARED] Route '+{routeBase}' does not exist and cannot be placed automatically " +
                    "in this vault. Declare it first with update_routes (give it a path under a section), " +
                    "or use an existing route from get_routes. Nothing was written.");
            }
        }

        content = BackfillEntryWriter.ApplyTags(content, tags);
        content = BackfillEntryWriter.ApplyRoute(content, route);

        // Parse the content using NotationParser
        var entry = _services.NotationParser!.Parse(content, context);
        entry.Timestamp = DateTime.Now;

        // Only the explicit route (or, without one, declared routes) routes. Any
        // other +word is prose: it stays in the text and creates nothing.
        var ignoredTokens = BackfillEntryWriter.ApplyHeadlessRouteTokens(entry, _services.RouteService!, route);

        // Override entry type if explicitly specified and not in content
        if (!string.IsNullOrEmpty(entryTypeStr) && entry.Type == EntryType.Log)
        {
            entry.Type = entryTypeStr.ToLowerInvariant() switch
            {
                "task" => EntryType.Task,
                "idea" => EntryType.Idea,
                "block" => EntryType.Block,
                "decision" => EntryType.Decision,
                "timetrack" => EntryType.Timetrack,
                "question" => EntryType.Question,
                "gap" => EntryType.Gap,
                "analogy" => EntryType.Analogy,
                _ => EntryType.Log
            };
        }

        // Parse and set refs if provided
        if (refsInput != null && refsInput.Count > 0)
        {
            foreach (var refStr in refsInput)
            {
                var parsedRef = EntryRef.Parse(refStr);
                if (parsedRef != null)
                    entry.Refs.Add(parsedRef);
            }
        }

        // Set source attribution for MCP entries. An explicit source_agent wins;
        // otherwise the identity the server was launched with (connect passes
        // --agent <framework>) names the writer, so the vault keeps which agent
        // wrote the entry instead of a bare agent:mcp (lmk-jwy9). A server
        // started with no --agent still records the entry as agent-written.
        var serverAgent = _services.SessionContext?.DefaultAgent;
        var writer = !string.IsNullOrEmpty(sourceAgent) ? sourceAgent
            : !string.IsNullOrWhiteSpace(serverAgent) ? serverAgent
            : "mcp";
        entry.Source = $"agent:{writer}";

        // Session attribution has to be stamped BEFORE the write. RouteEntry and
        // SaveEntryAsync below both serialize the entry as they save it, so a
        // Session assigned after them is a dead write that never reaches disk -
        // which is why no MCP-created entry in any vault carries a session line.
        //
        // Materialize rather than read GetActiveSession: an unmaterialized pending
        // session's id is discarded by EnsureMaterialized (StartSession mints a
        // fresh one), so stamping the pending id would persist a reference with no
        // session file behind it. Route passed the way RecordEntry passes it below,
        // so the session's own route field is unchanged - the explicit arg first,
        // then the entry's parsed +project, which is what routing will land on.
        entry.Session = !string.IsNullOrEmpty(sessionId)
            ? sessionId
            : _services.SessionContext!
                .EnsureMaterialized(route ?? entry.Projects.FirstOrDefault())
                .SessionId;

        // Route the entry (FileRouter writes to routed path if destinations exist)
        List<RouteDestination> destinations;
        try
        {
            destinations = _services.RouteService!.RouteEntry(entry);
        }
        catch (RoutePrivateDesktopOnlyException ex)
        {
            return ErrorResult(
                $"[ROUTE_PRIVATE_DESKTOP_ONLY] Route '{ex.RouteName}' is private. " +
                "This route can only be captured through the LogMark desktop app. " +
                "Other surfaces (MCP, CLI, Telegram) cannot write to private routes.");
        }
        catch (LicenseRequiredException)
        {
            // Free-tier MCP caller attempted a private-route write.
            // Same user-facing error: private routes require the desktop app regardless of tier.
            return ErrorResult(
                "[ROUTE_PRIVATE_DESKTOP_ONLY] Writes to private routes are not available through MCP. " +
                "Use the LogMark desktop app.");
        }

        var destinationRoute = destinations.FirstOrDefault()?.RouteName;

        // Determine saved path - use routed path if available, otherwise save to inbox
        string savedPath;
        VaultWriteResult writeResult;
        try
        {
            if (destinations.Count > 0)
            {
                // Entry was routed by FileRouter - use the routed path and write result.
                savedPath = destinations[0].Path;
                writeResult = destinations[0].Write;
            }
            else
            {
                // No routing - save to inbox via VaultService
                savedPath = await _services.VaultService!.SaveEntryAsync(entry);
                writeResult = VaultWriteResult.Clean;
            }
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"MCP create_entry failed: {ex.Message}");
            return ErrorResult($"Failed to save entry: {ex.Message}");
        }

        // Log the action
        _services.Logger!.Info($"MCP created {entry.Type}: {content.Substring(0, Math.Min(50, content.Length))}...");

        // Auto-record entry in session (materializes session on first mutation).
        // Skip if ShortId is missing -- the grouper requires it to resolve the entry,
        // and there is no recoverable way to identify the entry within a year-file
        // without it. NotationParser should always assign one on save; a null here
        // would indicate a deeper bug worth surfacing via the warning below.
        if (entry.ShortId != null)
        {
            _services.SessionContext!.RecordEntry(savedPath, entry.ShortId, destinationRoute);
        }
        else
        {
            _services.Logger!.Warn($"MCP created entry without ShortId; skipping session recording: {savedPath}");
        }

        // Build result - use the ShortId that was set during save
        var result = new CreateEntryResult
        {
            Success = true,
            Path = savedPath,
            EntryId = entry.ShortId ?? LogMark.Services.EntryCache.EntryResolver.GenerateHashId(entry.Timestamp, entry.RawContent),
            ParsedType = entry.Type?.ToString()?.ToLowerInvariant() ?? "log",
            Route = destinations.FirstOrDefault()?.RouteName,
            RoutePath = destinations.FirstOrDefault()?.RoutePath,
            Tags = entry.Tags,
            IgnoredRouteTokens = ignoredTokens.Count > 0 ? ignoredTokens : null,
            ActiveSession = _services.SessionContext!.GetActiveSessionInfo()
        };

        // If free-tier graceful degradation: entry was written as plain text because
        // Pro / PrivateRoutes feature is inactive. Include unmasked metadata so callers
        // can surface an appropriate warning or advisory to the user.
        if (writeResult.Unmasked && destinations.Count > 0)
        {
            result.Unmasked = true;
            result.UnmaskedSpanCount = writeResult.UnmaskedSpans.Count;
            result.UnmaskedRoute = destinations[0].RouteName;
        }

        return new ToolCallResult
        {
            Content = new List<ToolContent>
            {
                new() { Type = "text", Text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) }
            }
        };
    }

    private static string? GetStringArg(Dictionary<string, object?> args, string key) =>
        McpToolHelpers.GetStringArg(args, key);

    private static List<string>? GetStringArrayArg(Dictionary<string, object?> args, string key) =>
        McpToolHelpers.GetStringArrayArg(args, key);

    private static ToolCallResult ErrorResult(string message) => McpToolHelpers.ErrorResult(message);

    private class CreateEntryResult
    {
        public bool Success { get; set; }
        public string Path { get; set; } = "";
        public string EntryId { get; set; } = "";
        public string ParsedType { get; set; } = "";
        public string? Route { get; set; }

        // Resolved route directory (route.Path + scope), used by composite tools that
        // need to write a sibling page. Distinct from Route (the alias + scope name).
        public string? RoutePath { get; set; }

        public List<string>? Tags { get; set; }

        // +words in content that were not routing directives (kept as text).
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? IgnoredRouteTokens { get; set; }

        public string? Message { get; set; }
        public ActiveSessionInfo? ActiveSession { get; set; }

        // Free-tier graceful degradation: entry was written as plain text because
        // Pro / PrivateRoutes feature is inactive. Only present when true.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public bool? Unmasked { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? UnmaskedSpanCount { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? UnmaskedRoute { get; set; }
    }
}
