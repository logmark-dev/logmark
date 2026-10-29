namespace LogMark.Services.Agents;

/// <summary>
/// Discovers installable skill packs from a root directory (each immediate
/// subdirectory is a pack the loader can read). The on-disk pack tree is
/// copied to build output by LogMark.Core.csproj, so the App passes
/// AppContext.BaseDirectory/Resources/SkillPacks. A malformed pack directory
/// is skipped so one bad pack cannot hide the rest. Results are cached.
/// </summary>
public sealed class SkillPackCatalog
{
    private readonly string _packsRootDir;
    private List<SkillPack>? _cache;

    public SkillPackCatalog(string packsRootDir) => _packsRootDir = packsRootDir;

    public IReadOnlyList<SkillPack> Packs => _cache ??= Discover();

    public SkillPack? Get(string packSlug) =>
        Packs.FirstOrDefault(p => p.Slug == packSlug);

    private List<SkillPack> Discover()
    {
        var packs = new List<SkillPack>();
        if (!Directory.Exists(_packsRootDir)) return packs;

        foreach (var dir in Directory.GetDirectories(_packsRootDir)
                     .OrderBy(d => d, StringComparer.Ordinal))
        {
            try { packs.Add(SkillPackLoader.Load(dir)); }
            catch (SkillPackFormatException) { /* skip malformed pack */ }
        }
        return packs;
    }
}
