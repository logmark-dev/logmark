using System.Text;
using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;

namespace LogMark.Mcp.Prompts;

/// <summary>
/// Prompt that auto-generates a tool reference from the ToolRegistry at runtime.
/// Includes a static preamble with composition patterns and a dynamic section
/// listing all registered tools with their input schemas.
/// </summary>
public class VaultToolsGuidePrompt : IPrompt
{
    private readonly ToolRegistry _toolRegistry;

    public VaultToolsGuidePrompt(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public string Name => "vault-tools-guide";

    public PromptDefinition Definition => new()
    {
        Name = "vault-tools-guide",
        Description = "Auto-generated reference of all vault tools with composition patterns and input schemas."
    };

    public Task<PromptGetResult> GetAsync(Dictionary<string, string>? arguments, CancellationToken ct)
    {
        var text = BuildPrompt();

        var result = new PromptGetResult
        {
            Description = "Vault tools guide with composition patterns and tool reference",
            Messages = new List<PromptMessage>
            {
                new()
                {
                    Role = "user",
                    Content = new ToolContent { Type = "text", Text = text }
                }
            }
        };

        return Task.FromResult(result);
    }

    private string BuildPrompt()
    {
        var sb = new StringBuilder();

        // Static preamble: composition patterns
        sb.AppendLine("# Vault Tools Guide");
        sb.AppendLine();
        sb.AppendLine("## Composition Patterns");
        sb.AppendLine();
        sb.AppendLine("### Rich entry with linked page (default)");
        sb.AppendLine("Use `create_entry_with_subpage`. One call -- atomically creates the rolling-log entry, writes the subpage, and inserts the wiki link.");
        sb.AppendLine();
        sb.AppendLine("### Rich entry with linked page (advanced, three-step)");
        sb.AppendLine("Only when you need separate transactions (e.g., subpage written from an external pipeline, then linked later):");
        sb.AppendLine("1. `create_entry` -- capture the rolling-log entry; record the returned `entry_id`.");
        sb.AppendLine("2. `write_page` -- write the subpage with `entry_id` set so the link is inserted on success.");
        sb.AppendLine("   (Or pass `unlinked: true` for a true orphan page; this is rare.)");
        sb.AppendLine("3. `organize_entry` (action: link) -- only needed if step 2 was deferred / cross-process.");
        sb.AppendLine();
        // save_asset is a closed tool; the open build does not register it (lmk-068p).
        if (_toolRegistry.GetTool("save_asset") is not null)
        {
            sb.AppendLine("### Entry with images");
            sb.AppendLine("1. `save_asset` -- download or save the image to the vault assets folder");
            sb.AppendLine("2. `create_entry_with_subpage` -- atomically create the entry, write a subpage referencing the image, and link them");
            sb.AppendLine();
        }
        sb.AppendLine("### Cross-referencing other pages or entries");
        sb.AppendLine("Vault pages are markdown -- use Obsidian-style wiki links inline in any `content` field to cross-reference another page:");
        sb.AppendLine("- `[[target-page-name]]` -- link by filename (without .md)");
        sb.AppendLine("- `[[target-page-name|display text]]` -- link with custom display text");
        sb.AppendLine();
        sb.AppendLine("These render as clickable navigation in Obsidian and in LogMark's own viewers. Plain ShortId mentions in prose (`` `09621bc9` ``) are NOT clickable -- always prefer wiki links when referencing existing pages.");
        sb.AppendLine();
        sb.AppendLine("To reference another *entry* by ShortId, prefer the typed `refs` array on `create_entry` (`[\"task:b123bbe1\"]`) over inline mentions. Use `organize_entry` (action: link) only when you need to insert an entry->page link after the fact (e.g., the page was written by a separate process).");
        sb.AppendLine();
        sb.AppendLine("### Session workflow");
        sb.AppendLine("1. `start_session` -- begin a tracked session for a route");
        sb.AppendLine("2. Perform any operations (create entries, write pages, etc.)");
        sb.AppendLine("3. `end_session` -- close the session and generate a summary");
        sb.AppendLine();

        // Dynamic tool reference
        sb.AppendLine("## Tool Reference");
        sb.AppendLine();

        var tools = _toolRegistry.GetToolList().Tools;

        if (tools.Count == 0)
        {
            sb.AppendLine("No tools registered.");
        }
        else
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            foreach (var tool in tools)
            {
                sb.AppendLine($"### {tool.Name}");
                sb.AppendLine();
                sb.AppendLine(tool.Description);
                sb.AppendLine();
                sb.AppendLine("**Input Schema:**");
                sb.AppendLine("```json");
                sb.AppendLine(JsonSerializer.Serialize(tool.InputSchema, jsonOptions));
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
