namespace LogMark.Services.Lint;

public interface IVaultContext
{
    EntryCache.EntryCache Entries { get; }
    TagsFileParser TagsFile { get; }
    RouteService Routes { get; }
    string VaultPath { get; }
}
