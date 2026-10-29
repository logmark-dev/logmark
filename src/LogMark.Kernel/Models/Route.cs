namespace LogMark.Models;

public class Route
{
    public required string Name { get; set; }
    public string? Path { get; set; }           // Vault-relative path, null = reference only
    public string? Section { get; set; }        // Which ## header (for UI grouping)
    public string? SectionNotes { get; set; }   // Prose between ## header and first ### (archetype signal)
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Status { get; set; }         // active, building, paused, archived
    public Dictionary<string, string> Scopes { get; set; } = new(); // Scope name -> description

    /// <summary>
    /// Private-route preset. Null means non-private (no shrouding).
    /// Set via `- private: true|paranoid|financial|dev` in routes.md.
    /// </summary>
    public PrivatePreset? Private { get; set; }

    public List<string> Workdirs { get; set; } = new(); // Filesystem bindings (cwd -> route); distinct from Path
}
