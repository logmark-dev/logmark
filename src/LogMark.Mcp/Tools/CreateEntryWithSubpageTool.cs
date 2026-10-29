using System.Text.Json;
using System.Text.Json.Serialization;
using LogMark.Mcp.Protocol;
using LogMark.Services;
using JsonIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Composite tool: creates an entry, writes a subpage, and inserts a wiki link
/// from the entry to the page in a single call. This is the canonical path for
/// "rich entry with attached page" -- never produces orphan pages, never
/// requires the agent to thread an entry_id manually.
/// </summary>
public class CreateEntryWithSubpageTool : ITool
{
    private readonly VaultServices _services;
    private readonly CreateEntryTool _createEntry;

    public CreateEntryWithSubpageTool(VaultServices services)
    {
        _services = services;
        _createEntry = new CreateEntryTool(services);
    }

    public string Name => "create_entry_with_subpage";

    public ToolDefinition Definition => new()
    {
        Name = "create_entry_with_subpage",
        Description = "Atomically: create an entry, write a subpage, and insert a wiki link from the entry to the page. Use this for any rich capture (long-form brief, results page, deep dive) that should remain discoverable from a rolling-log entry. The 'subpage' object must include 'type' (e.g., decisions, ideas, diary), 'filename' (with .md), and 'content'; optional 'display_text' overrides the wiki link label. Find this tool when: capture with a long attached document body.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["entry_content"] = new()
                {
                    Type = "string",
                    Description = "Entry content. Supports notation prefixes (t:/i:/b:/d:/q:/g:/a:), +route routing, and #tags. Just like create_entry."
                },
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Optional route for the entry (e.g., 'logmark'). When given, the entry goes to this route only; other +words in entry_content are kept as text."
                },
                ["scope"] = new()
                {
                    Type = "string",
                    Description = "Optional scope within route."
                },
                ["entry_type"] = new()
                {
                    Type = "string",
                    Description = "Optional explicit entry type. Overridden by notation prefix."
                },
                ["session"] = new()
                {
                    Type = "string",
                    Description = "Optional session id."
                },
                ["source_agent"] = new()
                {
                    Type = "string",
                    Description = "Optional agent identifier for provenance."
                },
                ["subpage"] = new()
                {
                    Type = "object",
                    Description = "Subpage spec. Required keys: 'type' (canonical names: tasks, ideas, blocks, decisions, diary, questions, gaps, analogies; use 'diary' for log subpages), 'filename' (page filename including .md, e.g., '2026-04-30-brief.md'), and 'content' (markdown body for the page; no frontmatter, embed metadata as inline notation if needed). To cross-reference other vault pages inside 'content', use Obsidian-style wiki links: `[[target-page-name]]` or `[[target-page-name|display text]]` (target is the other page's filename without .md). Plain ShortId mentions like `09621bc9` won't be clickable. For file references to non-vault siblings (e.g., a research note above the logmark/ folder), prefer standard markdown links `[label](relative/path.md)`. Bare relative `.md` paths are rendered as clickable links by a safety net but are less portable across markdown viewers. Optional 'display_text' overrides the wiki-link label inserted into the parent entry (defaults to filename without extension)."
                }
            },
            Required = new List<string> { "entry_content", "subpage" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments is null) return McpToolHelpers.ErrorResult("Missing arguments");

        // Validate subpage spec FIRST so we never write an entry just to discover the subpage is malformed.
        if (!arguments.TryGetValue("subpage", out var subRaw) || subRaw is not JsonElement sub || sub.ValueKind != JsonValueKind.Object)
            return McpToolHelpers.ErrorResult("'subpage' object is required");

        var subType = sub.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var subFile = sub.TryGetProperty("filename", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        var subContent = sub.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var subDisplay = sub.TryGetProperty("display_text", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

        if (string.IsNullOrEmpty(subType) || string.IsNullOrEmpty(subFile) || string.IsNullOrEmpty(subContent))
            return McpToolHelpers.ErrorResult("subpage.type, subpage.filename, and subpage.content are all required");

        // Pre-validate type so we fail before creating an entry.
        if (EntryTypeMapper.ResolveTypeName(subType) is null)
            return McpToolHelpers.ErrorResult($"Unknown subpage type '{subType}'. Use one of: tasks, ideas, blocks, decisions, diary, questions, gaps, analogies, workflow, timetrack.");

        // Step 1: create entry (delegate to CreateEntryTool).
        // Adapter: this tool's outer parameter name is "entry_content"; CreateEntryTool expects "content".
        var createArgs = new Dictionary<string, object?>(arguments);
        createArgs.Remove("subpage");
        if (createArgs.TryGetValue("entry_content", out var ec))
        {
            createArgs["content"] = ec;
            createArgs.Remove("entry_content");
        }
        var createResult = await _createEntry.ExecuteAsync(createArgs, ct);
        if (createResult.IsError) return createResult;

        // Extract entry id, route alias, and resolved route path from CreateEntry's JSON result.
        // CreateEntryResult uses PascalCase property names via default JsonSerializer behavior.
        // RoutePath is the route's resolved directory (e.g., "domains/finance/investing/trade-ideas")
        // -- the subpage MUST land under this path so it co-locates with the entry, not under the
        // alias name ("fin-inv/trade-ideas") which would invent a parallel folder tree.
        string? entryId;
        string? entryRoute;
        string? entryRoutePath;
        bool? innerUnmasked = null;
        int? innerUnmaskedSpanCount = null;
        string? innerUnmaskedRoute = null;
        try
        {
            using var json = JsonDocument.Parse(createResult.Content[0].Text!);
            entryId = json.RootElement.GetProperty("EntryId").GetString();
            entryRoute = json.RootElement.TryGetProperty("Route", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;
            entryRoutePath = json.RootElement.TryGetProperty("RoutePath", out var rp) && rp.ValueKind == JsonValueKind.String
                ? rp.GetString()
                : null;

            // Propagate free-tier graceful degradation signal if present in inner result.
            if (json.RootElement.TryGetProperty("Unmasked", out var u) && u.ValueKind == JsonValueKind.True)
            {
                innerUnmasked = true;
                innerUnmaskedSpanCount = json.RootElement.TryGetProperty("UnmaskedSpanCount", out var sc)
                    && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0;
                innerUnmaskedRoute = json.RootElement.TryGetProperty("UnmaskedRoute", out var ur)
                    && ur.ValueKind == JsonValueKind.String ? ur.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            return McpToolHelpers.ErrorResult($"Entry was created but its result could not be parsed: {ex.Message}");
        }

        if (string.IsNullOrEmpty(entryId))
            return McpToolHelpers.ErrorResult("Entry was created but no EntryId was returned; cannot link subpage.");

        // Step 2 + 3: page + link via EntryPageLinker.
        try
        {
            var resolvedDisplay = string.IsNullOrEmpty(subDisplay)
                ? Path.GetFileNameWithoutExtension(subFile)
                : subDisplay;

            // Prefer the resolved route path so the page sits under the same vault
            // subtree as the entry. Fall back to the alias name only when the entry
            // wasn't routed (no destination in routes.md).
            var subpageRoute = !string.IsNullOrEmpty(entryRoutePath) ? entryRoutePath : entryRoute;

            var link = _services.EntryPageLinker!.WritePageAndLink(
                route: subpageRoute, typeName: subType!, filename: subFile!,
                content: subContent!, entryShortId: entryId!, displayText: resolvedDisplay);

            var result = new CreateEntryWithSubpageResult
            {
                EntryId = entryId,
                EntryRoute = entryRoute,
                PagePath = link.PagePath,
                EntryFilePath = link.EntryFilePath,
                LinkInserted = link.LinkInserted
            };

            // If free-tier graceful degradation: entry was written as plain text because
            // Pro / PrivateRoutes feature is inactive. Propagate the signal so callers
            // can surface an appropriate advisory to the user.
            if (innerUnmasked == true)
            {
                result.Unmasked = true;
                result.UnmaskedSpanCount = innerUnmaskedSpanCount;
                result.UnmaskedRoute = innerUnmaskedRoute;
            }

            return McpToolHelpers.SuccessResult(result);
        }
        catch (Exception ex)
        {
            return McpToolHelpers.ErrorResult($"Entry was created (id={entryId}) but subpage linking failed: {ex.Message}");
        }
    }

    private class CreateEntryWithSubpageResult
    {
        [JsonPropertyName("entry_id")]
        public string? EntryId { get; set; }

        [JsonPropertyName("entry_route")]
        public string? EntryRoute { get; set; }

        [JsonPropertyName("page_path")]
        public string? PagePath { get; set; }

        [JsonPropertyName("entry_file_path")]
        public string? EntryFilePath { get; set; }

        [JsonPropertyName("link_inserted")]
        public bool LinkInserted { get; set; }

        // Free-tier graceful degradation: entry was written as plain text because
        // Pro / PrivateRoutes feature is inactive. Only present when true.
        [JsonPropertyName("unmasked")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Unmasked { get; set; }

        [JsonPropertyName("unmasked_span_count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? UnmaskedSpanCount { get; set; }

        [JsonPropertyName("unmasked_route")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UnmaskedRoute { get; set; }
    }
}
