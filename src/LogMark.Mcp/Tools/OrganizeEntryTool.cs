using LogMark.Mcp.Protocol;
using LogMark.Services.Verbs;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP dispatcher for the ORGANIZE single-entry verbs. Maps an action to a Core
/// VerbRegistry verb and dispatches through it, so reroute/convert/amend/retag/link
/// share one tool instead of one class each. Replaces the standalone retag_entry
/// and link_entry_to_page tools. Design:
/// docs/plans/2026-07-02-mcp-organize-consolidation-design.md
/// </summary>
public class OrganizeEntryTool : ITool
{
    private readonly VaultServices _services;
    private readonly VerbRegistry _registry;

    private static readonly Dictionary<string, string> ActionToVerb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reroute"] = "reroute_entry",
        ["convert"] = "convert_type",
        ["amend"] = "amend_entry",
        ["retag"] = "retag_entry",
        ["link"] = "link_entry_to_page",
    };

    // Per-action fields forwarded to the verb handlers, which own required-arg validation.
    private static readonly string[] PassthroughArgs =
        { "target_route", "new_type", "amend_text", "add", "remove", "wiki_path", "display_text" };

    public OrganizeEntryTool(VaultServices services, VerbRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    public string Name => "organize_entry";

    public ToolDefinition Definition => new()
    {
        Name = "organize_entry",
        Description = "Organize a single entry by its ShortId: move it to another route (reroute), change its type (convert), append an amend note (amend), add/remove #tags (retag), or link it to a page (link). Find this tool when: move a note to a project, change an entry's type, amend an entry, add or remove tags on an entry, link an entry to a page.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["action"] = new() { Type = "string", Description = "One of: reroute, convert, amend, retag, link." },
                ["entry_id"] = new() { Type = "string", Description = "The 8-char hex entry ID (ShortId)." },
                ["target_route"] = new() { Type = "string", Description = "reroute: destination route name (leading + optional)." },
                ["new_type"] = new() { Type = "string", Description = "convert: task, idea, block, decision, question, gap, analogy, log." },
                ["amend_text"] = new() { Type = "string", Description = "amend: the note to append to the entry." },
                ["add"] = new() { Type = "string", Description = "retag: tags to add, comma/space-separated (leading # optional)." },
                ["remove"] = new() { Type = "string", Description = "retag: tags to remove, comma/space-separated." },
                ["wiki_path"] = new() { Type = "string", Description = "link: page path to link to (as returned by write_page)." },
                ["display_text"] = new() { Type = "string", Description = "link: optional display text for the wiki link." },
            },
            Required = new List<string> { "action", "entry_id" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var action = McpToolHelpers.GetStringArg(arguments, "action")?.Trim();
        if (string.IsNullOrWhiteSpace(action) || !ActionToVerb.TryGetValue(action, out var verbName))
            return McpToolHelpers.ErrorResult($"'action' must be one of: {string.Join(", ", ActionToVerb.Keys)}.");

        var entryId = McpToolHelpers.GetStringArg(arguments, "entry_id");
        if (string.IsNullOrWhiteSpace(entryId))
            return McpToolHelpers.ErrorResult("Missing required parameter: entry_id");

        // The registry handlers resolve the entry via EntryCache.ResolveShortId, whose
        // index is empty until warmup completes. Await it for deterministic resolution.
        if (_services.EntryCache?.WarmupTask is { } warmup)
            await warmup;

        var args = new Dictionary<string, string?> { ["entry_id"] = entryId };
        foreach (var key in PassthroughArgs)
        {
            var value = McpToolHelpers.GetStringArg(arguments, key);
            if (!string.IsNullOrWhiteSpace(value))
                args[key] = value;
        }

        try
        {
            var result = await _registry.Execute(verbName, args, "mcp", VerbTier.Free);
            return result.Success
                ? McpToolHelpers.SuccessResult(new { success = true, action, entry_id = entryId, message = result.Message })
                : McpToolHelpers.ErrorResult(result.Error ?? "Organize action failed");
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"MCP organize_entry ({action}) failed: {ex.Message}");
            return McpToolHelpers.ErrorResult($"Failed to organize entry: {ex.Message}");
        }
    }
}
