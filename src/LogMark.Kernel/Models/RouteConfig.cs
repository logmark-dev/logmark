namespace LogMark.Models;

public class EntryTypeMapping
{
    public required string Prefix { get; set; }
    public required string TypeName { get; set; }
}

public class RouteConfig
{
    public string? VaultNotes { get; set; }     // Prose between # Routes and first ## (vault purpose)
    public List<Route> Routes { get; set; } = new();
    public List<EntryTypeMapping> EntryTypeMappings { get; set; } = new();
    public bool RouteUntyped { get; set; } = false;
    public string UntypedFolder { get; set; } = "inbox";
    public string UntypedFile { get; set; } = "inbox.md";
    public List<string> VaultWorkdirs { get; set; } = new(); // Vault-level filesystem bindings (project-dedicated vault)
}
