namespace LogMark.Services.Lint;

public class VaultContext : IVaultContext
{
    public VaultContext(
        EntryCache.EntryCache entries,
        TagsFileParser tagsFile,
        RouteService routes,
        string vaultPath)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        TagsFile = tagsFile ?? throw new ArgumentNullException(nameof(tagsFile));
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        VaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
    }

    public EntryCache.EntryCache Entries { get; }
    public TagsFileParser TagsFile { get; }
    public RouteService Routes { get; }
    public string VaultPath { get; }
}
