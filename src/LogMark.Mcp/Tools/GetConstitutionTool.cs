using LogMark.Mcp.Protocol;
using LogMark.Services.Vault;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to read the vault constitution (logmark/vault.md). Returns every
/// section; reports Established=false (all fields null) when the vault has no
/// constitution yet, which is the orient skill's signal to bootstrap.
/// </summary>
public class GetConstitutionTool : ITool
{
    private readonly VaultServices _services;

    public GetConstitutionTool(VaultServices services) => _services = services;

    public string Name => "get_constitution";

    public ToolDefinition Definition => new()
    {
        Name = "get_constitution",
        Description = "Read the vault's constitution (logmark/vault.md): vault name, purpose, scope, how agents should help, conventions, frozen vault-level decisions, don't-surface notes, and the format version the vault declares (Schema). Returns Established=false with empty fields when the vault has no constitution yet. Find this tool when: orient to a vault, or learn its purpose, scope, and conventions before acting.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>()
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
        Meta = new Dictionary<string, object?> { ["anthropic/alwaysLoad"] = true },
    };

    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(_services.VaultPath!, "logmark", "vault.md");
            var md = File.Exists(path) ? File.ReadAllText(path) : null;
            var c = new VaultConstitutionParser().Parse(md);

            return Task.FromResult(McpToolHelpers.SuccessResult(new ConstitutionView
            {
                Established = !c.IsEmpty,
                VaultName = c.VaultName,
                Purpose = c.Purpose,
                ScopeInclude = c.Scope?.IncludeProse,
                ScopeExclude = c.Scope?.ExcludeProse,
                HowAgentsHelp = c.AgentsHelpBy,
                Conventions = c.Conventions,
                FrozenDecisions = c.FrozenDecisions,
                DontSurface = c.DontSurface,
                Schema = c.Schema,
                SchemaWarning = VaultManifestFrontmatter.UnsupportedMajorWarning(c.Schema)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolHelpers.ErrorResult($"Failed to read constitution: {ex.Message}"));
        }
    }

    private class ConstitutionView
    {
        public bool Established { get; set; }
        public string? VaultName { get; set; }
        public string? Purpose { get; set; }
        public string? ScopeInclude { get; set; }
        public string? ScopeExclude { get; set; }
        public string? HowAgentsHelp { get; set; }
        public string? Conventions { get; set; }
        public string? FrozenDecisions { get; set; }
        public string? DontSurface { get; set; }
        public string? Schema { get; set; }
        public string? SchemaWarning { get; set; }
    }
}
