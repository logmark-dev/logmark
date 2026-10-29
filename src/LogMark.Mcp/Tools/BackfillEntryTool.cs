using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogMark.Exceptions;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to create an entry with a historical timestamp.
/// Used for populating the vault with past decisions, tasks, etc.
/// </summary>
public class BackfillEntryTool : ITool
{
    private readonly VaultServices _services;
    private readonly BackfillEntryWriter _writer;

    public BackfillEntryTool(VaultServices services)
    {
        _services = services;
        _writer = new BackfillEntryWriter(services);
    }

    public string Name => "backfill_entry";

    public ToolDefinition Definition => new()
    {
        Name = "backfill_entry",
        Description = "Create an entry with a historical timestamp. Used for populating the vault with past decisions from documentation, CLAUDE.md, etc. Entries are inserted chronologically within the yearly file. Find this tool when: importing old notes, historical capture, backdated record.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Entry body text. Do NOT prepend notation prefixes (t:, i:, b:, d:, q:, g:, a:) -- those are typing signals, not body text. Set the type via entry_type instead. #tags are allowed inline."
                },
                ["timestamp"] = new()
                {
                    Type = "string",
                    Description = "ISO 8601 datetime for the entry (e.g., '2025-06-15T10:30:00Z'). Entry will be inserted chronologically."
                },
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Optional route target (e.g., '+logmark')."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Optional scope within route (e.g., 'architecture')."
                },
                ["entry_type"] = new()
                {
                    Type = "string",
                    Description = "Entry type. Set this explicitly to fix the entry's kind -- do not rely on in-body notation prefixes (those should not appear in content at all).",
                    Enum = new List<string> { "task", "idea", "block", "decision", "log", "question", "gap", "analogy" }
                },
                ["tags"] = new()
                {
                    Type = "array",
                    Description = "Optional tags to add (without # prefix).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["source"] = new()
                {
                    Type = "string",
                    Description = "Origin of the backfilled data (e.g., 'CLAUDE.md', 'philosophy.md')."
                },
                ["context"] = new()
                {
                    Type = "string",
                    Description = "Optional context override."
                },
                ["source_agent"] = new()
                {
                    Type = "string",
                    Description = "Optional agent identifier for provenance tracking (e.g., 'claude-code'). Appended to source field."
                }
            },
            Required = new List<string> { "content", "timestamp" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments == null)
        {
            return ErrorResult("Missing arguments");
        }

        // Validate required parameters
        if (!arguments.TryGetValue("content", out var contentObj) || contentObj is not JsonElement contentElement)
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
        content = NotationParser.NormalizeFacetTagsInText(content);

        if (!arguments.TryGetValue("timestamp", out var timestampObj) || timestampObj is not JsonElement timestampElement)
        {
            return ErrorResult("Missing required parameter: timestamp");
        }

        var timestampStr = timestampElement.GetString();
        if (string.IsNullOrWhiteSpace(timestampStr) || !DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return ErrorResult($"Invalid timestamp format: {timestampStr}. Expected ISO 8601 format (e.g., '2025-06-15T10:30:00Z').");
        }

        // Parse optional parameters
        var route = McpToolHelpers.GetStringArg(arguments, "route");
        var scope = McpToolHelpers.GetStringArg(arguments, "scope");
        var entryTypeStr = McpToolHelpers.GetStringArg(arguments, "entry_type");
        var source = McpToolHelpers.GetStringArg(arguments, "source");
        var context = McpToolHelpers.GetStringArg(arguments, "context") ?? (source != null ? $"Backfilled from {source}" : "Backfill");
        var tags = McpToolHelpers.GetStringArrayArg(arguments, "tags");
        var sourceAgent = McpToolHelpers.GetStringArg(arguments, "source_agent");

        // Combine scope with route if provided
        if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(scope))
        {
            route = $"{route.TrimStart('+')}/{scope}";
        }

        content = BackfillEntryWriter.ApplyTags(content, tags);
        content = BackfillEntryWriter.ApplyRoute(content, route);

        // Parse the content
        var entry = _services.NotationParser!.Parse(content, context);
        entry.Timestamp = timestamp;
        BackfillEntryWriter.ApplyHeadlessRouteTokens(entry, _services.RouteService!, route);

        BackfillEntryWriter.ApplyEntryTypeOverride(entry, entryTypeStr);

        // Set source attribution (agent provenance appended if provided)
        var baseSource = source != null ? $"backfill:{source}" : "backfill";
        entry.Source = !string.IsNullOrEmpty(sourceAgent) ? $"{baseSource}:agent:{sourceAgent}" : baseSource;

        // Determine destination path. GetDestinationPath throws RoutePrivateDesktopOnlyException
        // when the target route is marked private -- backfill bypasses FileRouter so the
        // masking gate is enforced here directly.
        (string destPath, string yearFile) destResult;
        try
        {
            destResult = _writer.GetDestinationPath(entry);
        }
        catch (RoutePrivateDesktopOnlyException ex)
        {
            return ErrorResult(
                $"[ROUTE_PRIVATE_DESKTOP_ONLY] Route '{ex.RouteName}' is private. " +
                "This route can only be captured through the LogMark desktop app. " +
                "Other surfaces (MCP, CLI, Telegram) cannot write to private routes.");
        }

        var (destPath, yearFile) = destResult;

        try
        {
            var insertPosition = await _writer.InsertEntryChronologicallyAsync(destPath, entry);

            _services.Logger!.Info($"MCP backfill {entry.Type} to {yearFile}: {content.Substring(0, Math.Min(40, content.Length))}...");

            // Use the persisted ShortId assigned by BuildEntryText. This is the id
            // written to disk as the _id:/_task: anchor, so it round-trips through
            // EntryResolver and resolves for write_page / link / read_entry. Returning
            // a content-hash here (the old behavior) produced an id that nothing on
            // disk reproduced, so freshly backfilled entries were unresolvable.
            var entryId = entry.ShortId ?? TaskTracker.GenerateShortId();
            var relativePath = Path.GetRelativePath(_services.VaultPath!, destPath).Replace('\\', '/');
            var routeName = entry.Projects.Count > 0 ? entry.Projects[0].Split('/')[0] : null;
            _services.SessionContext!.RecordEntry(relativePath, entryId, routeName);

            var result = new BackfillResult
            {
                Success = true,
                Path = relativePath,
                EntryId = entryId,
                YearFile = yearFile,
                InsertedPosition = insertPosition,
                ActiveSession = _services.SessionContext!.GetActiveSessionInfo()
            };

            return new ToolCallResult
            {
                Content = new List<ToolContent>
                {
                    new() { Type = "text", Text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) }
                }
            };
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"MCP backfill_entry failed: {ex.Message}");
            return ErrorResult($"Failed to backfill entry: {ex.Message}");
        }
    }

    private static ToolCallResult ErrorResult(string message) => McpToolHelpers.ErrorResult(message);

    private class BackfillResult
    {
        public bool Success { get; set; }
        public string Path { get; set; } = "";
        public string EntryId { get; set; } = "";
        public string YearFile { get; set; } = "";
        public int InsertedPosition { get; set; }
        public string? Message { get; set; }
        public ActiveSessionInfo? ActiveSession { get; set; }
    }
}
