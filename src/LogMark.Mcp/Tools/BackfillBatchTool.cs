using System.Globalization;
using System.Text.Json;
using LogMark.Exceptions;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to batch create entries with historical timestamps.
/// Used for bulk population of the vault from documentation.
/// </summary>
public class BackfillBatchTool : ITool
{
    private readonly VaultServices _services;
    private readonly BackfillEntryWriter _writer;

    public BackfillBatchTool(VaultServices services)
    {
        _services = services;
        _writer = new BackfillEntryWriter(services);
    }

    public string Name => "backfill_batch";

    public ToolDefinition Definition => new()
    {
        Name = "backfill_batch",
        Description = "Batch create entries with historical timestamps. Use for bulk population of the vault from CLAUDE.md, philosophy docs, etc. Supports dry_run mode for validation. Find this tool when: bulk import, migrating many historical records at once.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["entries"] = new()
                {
                    Type = "array",
                    Description = "Array of entries to backfill.",
                    Items = new JsonSchemaProperty
                    {
                        Type = "object",
                        Description = "Entry object with content, timestamp, and optional routing/metadata."
                    }
                },
                ["dry_run"] = new()
                {
                    Type = "boolean",
                    Description = "If true, validate entries without writing to vault. Default: false."
                },
                ["stop_on_error"] = new()
                {
                    Type = "boolean",
                    Description = "If true, stop processing on first error. Default: true."
                }
            },
            Required = new List<string> { "entries" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments == null || !arguments.TryGetValue("entries", out var entriesObj) || entriesObj is not JsonElement entriesElement)
        {
            return ErrorResult("Missing required parameter: entries");
        }

        if (entriesElement.ValueKind != JsonValueKind.Array)
        {
            return ErrorResult("Parameter 'entries' must be an array");
        }

        var dryRun = GetBoolArg(arguments, "dry_run") ?? false;
        var stopOnError = GetBoolArg(arguments, "stop_on_error") ?? true;

        var results = new List<EntryResult>();
        var errors = new List<BackfillError>();
        var index = 0;
        var created = 0;
        var failed = 0;

        foreach (var entryElement in entriesElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ProcessEntry(entryElement, index, dryRun);
                results.Add(result);

                if (result.Success)
                {
                    created++;
                }
                else
                {
                    failed++;
                    errors.Add(new BackfillError
                    {
                        Index = index,
                        ContentPreview = result.ContentPreview ?? "",
                        Error = result.Error ?? "Unknown error"
                    });

                    if (stopOnError)
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(new BackfillError
                {
                    Index = index,
                    ContentPreview = GetContentPreview(entryElement),
                    Error = ex.Message
                });

                if (stopOnError)
                    break;
            }

            index++;
        }

        _services.Logger!.Info($"MCP backfill_batch: {created} created, {failed} failed" + (dryRun ? " (dry run)" : ""));

        // Record all successfully created entries in session (skip dry run)
        if (!dryRun && created > 0)
        {
            var routeName = results
                .Where(r => r.Success && r.Path != null)
                .Select(r => McpToolHelpers.RouteOf(_services, r.Path!))
                .FirstOrDefault();

            foreach (var entry in results.Where(r => r.Success && r.Path != null && r.EntryId != null))
            {
                _services.SessionContext!.RecordEntry(entry.Path!, entry.EntryId!, routeName);
            }
        }

        var batchResult = new BatchResult
        {
            Success = failed == 0,
            Total = created + failed,  // Total processed (not total in input array)
            Created = created,
            Failed = failed,
            DryRun = dryRun,
            Errors = errors,
            Entries = results,
            ActiveSession = dryRun ? null : _services.SessionContext!.GetActiveSessionInfo()
        };

        return new ToolCallResult
        {
            Content = new List<ToolContent>
            {
                new() { Type = "text", Text = JsonSerializer.Serialize(batchResult, new JsonSerializerOptions { WriteIndented = true }) }
            }
        };
    }

    private async Task<EntryResult> ProcessEntry(JsonElement entryElement, int index, bool dryRun)
    {
        // Extract content
        if (!entryElement.TryGetProperty("content", out var contentProp) ||
            contentProp.ValueKind != JsonValueKind.String)
        {
            return new EntryResult
            {
                Success = false,
                Index = index,
                Error = "Missing or invalid 'content' field"
            };
        }

        var content = contentProp.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return new EntryResult
            {
                Success = false,
                Index = index,
                Error = "Content cannot be empty"
            };
        }

        // Normalize facet-tag separators (#key:value -> #key/value) so disk prefers
        // the Obsidian-portable '/' form. Wikilinks and prose colons are untouched.
        content = NotationParser.NormalizeFacetTagsInText(content);

        // Extract timestamp
        if (!entryElement.TryGetProperty("timestamp", out var timestampProp) ||
            timestampProp.ValueKind != JsonValueKind.String)
        {
            return new EntryResult
            {
                Success = false,
                Index = index,
                ContentPreview = GetPreview(content),
                Error = "Missing or invalid 'timestamp' field"
            };
        }

        var timestampStr = timestampProp.GetString();
        if (string.IsNullOrWhiteSpace(timestampStr) ||
            !DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return new EntryResult
            {
                Success = false,
                Index = index,
                ContentPreview = GetPreview(content),
                Error = $"Invalid timestamp: {timestampStr}"
            };
        }

        // Extract optional fields
        var route = GetStringProperty(entryElement, "route");
        var scope = GetStringProperty(entryElement, "scope");
        var entryTypeStr = GetStringProperty(entryElement, "entry_type");
        var source = GetStringProperty(entryElement, "source");
        var context = GetStringProperty(entryElement, "context") ?? (source != null ? $"Backfilled from {source}" : "Backfill");
        var tags = GetStringArrayProperty(entryElement, "tags");

        // Combine scope with route if provided
        if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(scope))
        {
            route = $"{route.TrimStart('+')}/{scope}";
        }

        content = BackfillEntryWriter.ApplyTags(content, tags);
        content = BackfillEntryWriter.ApplyRoute(content, route);

        // Parse content
        var entry = _services.NotationParser!.Parse(content, context);
        entry.Timestamp = timestamp;
        BackfillEntryWriter.ApplyHeadlessRouteTokens(entry, _services.RouteService!, route);

        BackfillEntryWriter.ApplyEntryTypeOverride(entry, entryTypeStr);

        // Set source attribution
        entry.Source = source != null ? $"backfill:{source}" : "backfill";

        // Determine destination. The writer enforces the private-route guard
        // (backfill bypasses FileRouter, so masking cannot happen; private
        // routes are desktop-only).
        (string destPath, string yearFile) dest;
        try
        {
            dest = _writer.GetDestinationPath(entry);
        }
        catch (RoutePrivateDesktopOnlyException ex)
        {
            return new EntryResult
            {
                Success = false,
                Index = index,
                ContentPreview = GetPreview(content),
                Error = $"[ROUTE_PRIVATE_DESKTOP_ONLY] Route '{ex.RouteName}' is private. " +
                        "This route can only be captured through the LogMark desktop app. " +
                        "Other surfaces (MCP, CLI, Telegram) cannot write to private routes."
            };
        }
        var (destPath, yearFile) = dest;

        if (dryRun)
        {
            // No write happens, so no id is persisted; report a representative token
            // (a real run mints a fresh one) rather than an unresolvable content hash.
            return new EntryResult
            {
                Success = true,
                Index = index,
                Path = Path.GetRelativePath(_services.VaultPath!, destPath).Replace('\\', '/'),
                EntryId = TaskTracker.GenerateShortId(),
                YearFile = yearFile,
                ContentPreview = GetPreview(content),
                DryRun = true
            };
        }

        // Actually write the entry. InsertEntryChronologicallyAsync -> BuildEntryText
        // assigns entry.ShortId (the persisted _id:/_task: anchor) as a side effect.
        var insertPosition = await _writer.InsertEntryChronologicallyAsync(destPath, entry);

        return new EntryResult
        {
            Success = true,
            Index = index,
            Path = Path.GetRelativePath(_services.VaultPath!, destPath).Replace('\\', '/'),
            EntryId = entry.ShortId ?? TaskTracker.GenerateShortId(),
            YearFile = yearFile,
            InsertedPosition = insertPosition,
            ContentPreview = GetPreview(content)
        };
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static List<string>? GetStringArrayProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var str = item.GetString();
                if (!string.IsNullOrEmpty(str))
                    result.Add(str);
            }
        }
        return result;
    }

    private static bool? GetBoolArg(Dictionary<string, object?> args, string key)
    {
        if (args.TryGetValue(key, out var value) && value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;
            if (element.ValueKind == JsonValueKind.False)
                return false;
        }
        return null;
    }

    private static string GetPreview(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";
        return content.Length > 50 ? content.Substring(0, 47) + "..." : content;
    }

    private static string GetContentPreview(JsonElement element)
    {
        if (element.TryGetProperty("content", out var contentProp) &&
            contentProp.ValueKind == JsonValueKind.String)
        {
            return GetPreview(contentProp.GetString() ?? "");
        }
        return "(no content)";
    }

    private static ToolCallResult ErrorResult(string message) => McpToolHelpers.ErrorResult(message);

    private class EntryResult
    {
        public bool Success { get; set; }
        public int Index { get; set; }
        public string? Path { get; set; }
        public string? EntryId { get; set; }
        public string? YearFile { get; set; }
        public int InsertedPosition { get; set; }
        public string? ContentPreview { get; set; }
        public bool DryRun { get; set; }
        public string? Error { get; set; }
    }

    private class BackfillError
    {
        public int Index { get; set; }
        public string ContentPreview { get; set; } = "";
        public string Error { get; set; } = "";
    }

    private class BatchResult
    {
        public bool Success { get; set; }
        public int Total { get; set; }
        public int Created { get; set; }
        public int Failed { get; set; }
        public bool DryRun { get; set; }
        public List<BackfillError> Errors { get; set; } = new();
        public List<EntryResult> Entries { get; set; } = new();
        public ActiveSessionInfo? ActiveSession { get; set; }
    }
}
