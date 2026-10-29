using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to write a markdown page to the vault via SubpageWriter.
/// Requires either an existing entry_id (to wiki-link the page from) or
/// an explicit unlinked: true opt-in for orphan pages.
/// </summary>
public class WritePageTool : ITool
{
    private readonly VaultServices _services;

    public WritePageTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "write_page";

    public ToolDefinition Definition => new()
    {
        Name = "write_page",
        Description = "Write a markdown page to the vault. Creates a file at the appropriate pages directory based on route and type. Requires either 'entry_id' (to wiki-link the page from an existing entry) or 'unlinked: true' (orphan page opt-in). For most rich-content captures, prefer 'create_entry_with_subpage' which atomically creates entry + page + link. Find this tool when: author a standalone markdown document in the knowledge base.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["route"] = new()
                {
                    Type = "string",
                    Description = "Optional vault route path (e.g., 'research/karpathy'). Omit for unrouted pages."
                },
                ["type"] = new()
                {
                    Type = "string",
                    Description = "Entry type name for the pages folder."
                },
                ["filename"] = new()
                {
                    Type = "string",
                    Description = "Page filename including .md extension (e.g., '2026-03-14-karpathy-llm-os.md')."
                },
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Markdown content for the page body. To cross-reference other vault pages or entries inline, write Obsidian-style wiki links: `[[page-name]]` or `[[page-name|display text]]`. Plain ShortId references like `09621bc9` are not clickable -- prefer wiki links to the target page's filename (without .md). For file references to non-vault siblings (e.g., a research note above the logmark/ folder), prefer standard markdown links `[label](relative/path.md)`. Bare relative `.md` paths are rendered as clickable links by a safety net but are less portable across markdown viewers."
                },
                ["entry_id"] = new()
                {
                    Type = "string",
                    Description = "ShortId (8-char hex) of an existing entry to wiki-link this page from. Required unless 'unlinked' is true."
                },
                ["display_text"] = new()
                {
                    Type = "string",
                    Description = "Optional display text for the wiki link. Defaults to the page filename without extension."
                },
                ["unlinked"] = new()
                {
                    Type = "boolean",
                    Description = "Set to true to write an orphan page (not linked from any entry). Use only for non-entry pages."
                }
            },
            Required = new List<string> { "type", "filename", "content" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = true, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        if (arguments == null)
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameters"));

        var typeName = McpToolHelpers.GetStringArg(arguments, "type");
        if (string.IsNullOrWhiteSpace(typeName))
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: type"));

        var filename = McpToolHelpers.GetStringArg(arguments, "filename");
        if (string.IsNullOrWhiteSpace(filename))
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: filename"));

        var content = McpToolHelpers.GetStringArg(arguments, "content");
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(McpToolHelpers.ErrorResult("Missing required parameter: content"));

        var route = McpToolHelpers.GetStringArg(arguments, "route");
        var entryId = McpToolHelpers.GetStringArg(arguments, "entry_id");
        var displayText = McpToolHelpers.GetStringArg(arguments, "display_text");
        var unlinked = McpToolHelpers.GetBoolArg(arguments, "unlinked") ?? false;

        if (string.IsNullOrEmpty(entryId) && !unlinked)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult(
                "write_page requires either 'entry_id' (to wiki-link the page from an existing entry) or 'unlinked: true' (to write an orphan page). " +
                "For most rich-content captures, prefer 'create_entry_with_subpage' which atomically creates entry + page + link."));
        }

        if (!string.IsNullOrEmpty(entryId))
        {
            try
            {
                var resolvedDisplay = string.IsNullOrEmpty(displayText)
                    ? Path.GetFileNameWithoutExtension(filename)
                    : displayText;
                var linkResult = _services.EntryPageLinker!.WritePageAndLink(
                    route: route, typeName: typeName, filename: filename,
                    content: content, entryShortId: entryId, displayText: resolvedDisplay);

                return Task.FromResult(McpToolHelpers.SuccessResult(new WritePageResult
                {
                    Path = linkResult.PagePath,
                    Filename = filename,
                    LinkInserted = linkResult.LinkInserted,
                    EntryFilePath = linkResult.EntryFilePath
                }));
            }
            catch (EntryNotFoundException ex)
            {
                return Task.FromResult(McpToolHelpers.ErrorResult(
                    $"{ex.Message}. Pass 'unlinked: true' if you intend to write an orphan page."));
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(McpToolHelpers.ErrorResult(ex.Message));
            }
        }

        // unlinked: true path -- write the orphan page via SubpageWriter.
        try
        {
            _services.SubpageWriter!.WritePage(route, typeName, filename, content);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to write page: {ex.Message}"));
        }

        // Build result paths
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        string wikiPath;
        try
        {
            // Resolving variant: `route` here is whatever the agent passed, so
            // it is usually an alias and must resolve exactly as WritePage did.
            wikiPath = _services.SubpageWriter!.GetPageWikiPathFor(route, typeName, fileNameWithoutExtension);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult(ex.Message));
        }

        var result = new WritePageResult
        {
            Path = wikiPath,
            Filename = filename
        };

        return Task.FromResult(McpToolHelpers.SuccessResult(result));
    }

    private class WritePageResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("filename")]
        public string Filename { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("link_inserted")]
        public bool? LinkInserted { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("entry_file_path")]
        public string? EntryFilePath { get; set; }
    }
}
