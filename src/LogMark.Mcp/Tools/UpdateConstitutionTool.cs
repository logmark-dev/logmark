using LogMark.Mcp.Protocol;
using LogMark.Services.Vault;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to replace the vault constitution (logmark/vault.md). Writes the
/// file through VaultWriter so the desktop's file watcher reloads it, and
/// returns a parsed summary of the sections present.
/// </summary>
public class UpdateConstitutionTool : ITool
{
    private readonly VaultServices _services;

    public UpdateConstitutionTool(VaultServices services) => _services = services;

    public string Name => "update_constitution";

    public ToolDefinition Definition => new()
    {
        Name = "update_constitution",
        Description = "Replace the vault's constitution (logmark/vault.md) with new markdown. Recognized sections: a '# Title' heading, a purpose paragraph, '## Scope' (INCLUDE:/EXCLUDE: lines), '## How agents should help', '## Conventions', '## Frozen vault-level decisions', and '## Don't surface'. Frontmatter already in the file (such as the `schema:` format version) is kept unless the new markdown brings its own. Writes the file and lets the desktop reload it. Find this tool when: establish or update a vault's purpose, scope, conventions, or pinned vault-level decisions.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["content"] = new()
                {
                    Type = "string",
                    Description = "Full markdown content for logmark/vault.md."
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
            return McpToolHelpers.ErrorResult("'content' is required and must contain vault.md markdown");

        try
        {
            var path = Path.Combine(_services.VaultPath!, "logmark", "vault.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Agents see parsed sections only (get_constitution), never the
            // frontmatter, so keep the existing block unless they send their own.
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            content = VaultManifestFrontmatter.CarryForward(existing, content);
            await _services.VaultWriter!.WriteFileAsync(path, content);

            var c = new VaultConstitutionParser().Parse(content);
            _services.Logger!.Info("MCP update_constitution: wrote logmark/vault.md");

            return McpToolHelpers.SuccessResult(new ConstitutionSummary
            {
                VaultName = c.VaultName,
                Purpose = c.Purpose,
                HasScope = c.Scope != null,
                HasConventions = c.Conventions != null,
                HasFrozenDecisions = c.FrozenDecisions != null,
                HasDontSurface = c.DontSurface != null
            });
        }
        catch (Exception ex)
        {
            _services.Logger!.Error($"update_constitution failed: {ex.Message}");
            return McpToolHelpers.ErrorResult($"Failed to update constitution: {ex.Message}");
        }
    }

    private class ConstitutionSummary
    {
        public string? VaultName { get; set; }
        public string? Purpose { get; set; }
        public bool HasScope { get; set; }
        public bool HasConventions { get; set; }
        public bool HasFrozenDecisions { get; set; }
        public bool HasDontSurface { get; set; }
    }
}
