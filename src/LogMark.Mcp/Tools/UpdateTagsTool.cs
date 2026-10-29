using LogMark.Mcp.Protocol;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to replace the vault's tag registry (logmark/_system/tags.md).
/// Tags are free-form; this file documents the project's tag vocabulary so a
/// generic skill can learn it. Writes through VaultWriter.
/// </summary>
public class UpdateTagsTool : ITool
{
    private readonly VaultServices _services;

    public UpdateTagsTool(VaultServices services) => _services = services;

    public string Name => "update_tags";

    public ToolDefinition Definition => new()
    {
        Name = "update_tags",
        Description = "Replace the vault's tag registry (logmark/_system/tags.md) with new markdown. Use to record the project's tag vocabulary and what each tag means (for example #adr, release tags). Find this tool when: establish or revise the project's tag conventions.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Full markdown content for logmark/_system/tags.md."
                }
            },
            Required = new List<string> { "content" }
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false, DestructiveHint = true, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var content = McpToolHelpers.GetStringArg(arguments, "content");
        if (string.IsNullOrWhiteSpace(content))
            return McpToolHelpers.ErrorResult("'content' is required and must contain tags.md markdown");

        try
        {
            var path = Path.Combine(_services.VaultPath!, PathConstants.GetTagsPath());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await _services.VaultWriter!.WriteFileAsync(path, content);
            _services.Logger!.Info("MCP update_tags: wrote logmark/_system/tags.md");
            return McpToolHelpers.SuccessResult(new { written = true, path = "logmark/_system/tags.md" });
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"update_tags failed: {ex.Message}");
            return McpToolHelpers.ErrorResult($"Failed to update tags: {ex.Message}");
        }
    }
}
