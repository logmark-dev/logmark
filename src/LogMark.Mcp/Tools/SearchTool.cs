using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to search entries in the vault by text, tags, or semantic meaning.
/// </summary>
public class SearchTool : ITool
{
    private readonly VaultServices _services;

    /// <summary>
    /// Semantic search needs the closed build's embedding model. The open
    /// build never has one, so it advertises no semantic parameter rather
    /// than one that always answers "unavailable" (bead lmk-9hkn).
    /// </summary>
    private readonly bool _semantic;

    public SearchTool(VaultServices services, bool semanticSearch = true)
    {
        _services = services;
        _semantic = semanticSearch;
    }

    public string Name => "search";

    public ToolDefinition Definition => new()
    {
        Name = "search",
        Description = _semantic
            ? "Search the vault for entries by text, tags, or route; set semantic=true to also include meaning-based (embedding) matches. " +
              "Results are concise (id, type, timestamp, truncated content); fetch full detail with read_entry(id). " +
              "Find this tool when: find notes by keyword, look up text, locate captures, semantic recall, related by meaning."
            : "Search the vault for entries by text, tags, or route. " +
              "Results are concise (id, type, timestamp, truncated content); fetch full detail with read_entry(id). " +
              "Find this tool when: find notes by keyword, look up text, locate captures.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = SchemaProperties(),
            Required = new List<string> { "query" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    private Dictionary<string, JsonSchemaProperty> SchemaProperties()
    {
        var properties = new Dictionary<string, JsonSchemaProperty>
        {
                ["query"] = new()
                {
                    Type = "string",
                    Description = "Search query text. Searches entry content."
                },
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Filter by route (e.g., 'logmark')."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Filter by scope within route."
                },
                ["entry_types"] = new()
                {
                    Type = "array",
                    Description = "Filter by entry types.",
                    Items = new JsonSchemaProperty
                    {
                        Type = "string",
                        Enum = new List<string> { "task", "idea", "block", "decision", "log", "workflow", "question", "gap", "analogy" }
                    }
                },
                ["tags"] = new()
                {
                    Type = "array",
                    Description = "Required tags (entries must have ALL specified tags).",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["exclude_tags"] = new()
                {
                    Type = "array",
                    Description = "Exclude entries with these tags.",
                    Items = new JsonSchemaProperty { Type = "string" }
                },
                ["limit"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of results.",
                    Default = 20
                },
                ["after"] = ResultShaping.AfterProperty,
                ["before"] = ResultShaping.BeforeProperty,
        };
        if (_semantic)
            properties["semantic"] = new()
            {
                Type = "boolean",
                Description = "Include semantic (embedding) search results alongside text search. " +
                    "Requires T1 embeddings model. Results are merged and deduplicated.",
                Default = false
            };
        return properties;
    }

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments == null || !arguments.TryGetValue("query", out var queryObj) || queryObj is not JsonElement queryElement)
        {
            return McpToolHelpers.ErrorResult("Missing required parameter: query");
        }

        var query = queryElement.GetString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpToolHelpers.ErrorResult("Query cannot be empty");
        }

        var route = McpToolHelpers.GetStringArg(arguments, "route")?.TrimStart('+');
        var scope = McpToolHelpers.GetStringArg(arguments, "scope");
        var entryTypes = McpToolHelpers.GetStringArrayArg(arguments, "entry_types");
        var tags = McpToolHelpers.GetStringArrayArg(arguments, "tags");
        var excludeTags = McpToolHelpers.GetStringArrayArg(arguments, "exclude_tags");
        var limit = McpToolHelpers.GetIntArg(arguments, "limit") ?? 20;
        var semantic = McpToolHelpers.GetBoolArg(arguments, "semantic") ?? false;
        var boundsError = ResultShaping.TryParseBounds(arguments, out var after, out var before);
        if (boundsError != null)
        {
            return McpToolHelpers.ErrorResult(boundsError);
        }

        try
        {
            var searchQuery = new SearchQuery
            {
                Text = query,
                Route = route,
                Scope = scope,
                Types = ParseEntryTypes(entryTypes),
                RequiredTags = tags,
                ExcludedTags = excludeTags,
                After = after,
                Before = before,
                Limit = limit
            };

            var result = _services.SearchService!.Search(searchQuery);
            // List(200) truncates Content in place; Preview(200) added a
            // ContentPreview field ALONGSIDE the still-full Content, making
            // every result strictly larger. Concise shape drops per-entry
            // metadata (session GUID, path, line numbers); read_entry(id)
            // returns the full record (bead lmk-1zld).
            var entries = ConciseEntry.FromMany(
                _services.EntryResolver!.ResolveMany(result.Entries, ResolveOptions.List(200)));

            // Supplement with long-form subpage/page content: subpages carry no
            // timestamp header, parse to zero entries, and are absent from the entry
            // index. Skip when an entry_types filter is set (subpages are untyped).
            // Additive - existing consumers ignore the new field (bead lmk-6asy).
            object? pageMatches = null;
            if (searchQuery.Types == null || searchQuery.Types.Count == 0)
            {
                var pages = LogMark.Services.Search.SubpageSearch.Find(_services.VaultPath, query, route, limit);
                if (pages.Count > 0)
                {
                    pageMatches = pages.Select(p => new
                    {
                        title = p.Title,
                        path = p.FilePath,
                        route = p.Route,
                        preview = TruncateContent(p.SearchText, 200)
                    }).ToList();
                }
            }

            // Optionally append semantic results
            object? semanticResults = null;
            if (semantic && _services.SemanticSearchService != null)
            {
                var similar = await _services.SemanticSearchService.SearchAsync(query, limit, ct);

                // Deduplicate against text results
                var textIds = new HashSet<string>(
                    result.Entries
                        .Where(e => e.Entry.ShortId != null)
                        .Select(e => e.Entry.ShortId!),
                    StringComparer.OrdinalIgnoreCase);

                var novel = similar
                    .Where(r => !textIds.Contains(r.EntryId))
                    .Select(r => new
                    {
                        entryId = r.EntryId,
                        content = TruncateContent(r.Content, 200),
                        route = r.Route,
                        score = Math.Round(r.Score, 4)
                    })
                    .ToList();

                if (novel.Count > 0)
                {
                    semanticResults = novel;
                }
            }

            var response = new Dictionary<string, object?>
            {
                ["Entries"] = entries,
                ["TotalCount"] = result.TotalCount,
                ["QueryParams"] = new
                {
                    query, route, scope, entryTypes, tags, excludeTags, limit,
                    after = McpToolHelpers.GetStringArg(arguments, "after"),
                    before = McpToolHelpers.GetStringArg(arguments, "before"),
                }
            };

            // Dictionary entries are not dropped when null (only properties
            // are), so the Breakdown is added only when the page was truncated.
            if (ResultShaping.Breakdown(result) is { } breakdown)
            {
                response["Breakdown"] = breakdown;
            }

            if (pageMatches != null)
            {
                response["PageMatches"] = pageMatches;
            }

            if (semanticResults != null)
            {
                response["SemanticMatches"] = semanticResults;
            }
            else if (semantic && _services.SemanticSearchService == null)
            {
                response["SemanticNote"] = "Semantic search unavailable (T1 embeddings model not found).";
            }

            return McpToolHelpers.SuccessResult(response);
        }
        catch (Exception ex)
        {
            return McpToolHelpers.ErrorResult($"Search failed: {ex.Message}");
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var firstLine = content.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= maxLength)
            return firstLine;
        return firstLine[..(maxLength - 3)] + "...";
    }

    private static List<EntryType>? ParseEntryTypes(List<string>? typeStrings)
    {
        if (typeStrings == null || typeStrings.Count == 0) return null;

        var types = new List<EntryType>();
        foreach (var t in typeStrings)
        {
            if (Enum.TryParse<EntryType>(t, ignoreCase: true, out var et))
                types.Add(et);
        }
        return types.Count > 0 ? types : null;
    }
}
