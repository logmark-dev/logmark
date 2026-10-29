using System.Text.Json;

namespace LogMark.Services.Providers;

/// <summary>
/// File store for provider manifests. One JSON file per provider id under the
/// shared providers dir. Reads are lenient (skip unparseable); Prune deletes
/// manifests whose binaries are all dead, so any product's startup self-heals
/// the registry after an uninstall-without-cleanup.
/// </summary>
public sealed class ProviderManifestStore
{
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions ReadOptions = new()
    { PropertyNameCaseInsensitive = true };

    private readonly string _rootDir;
    public ProviderManifestStore(string rootDir) => _rootDir = rootDir;

    /// <summary>
    /// Live machine registry root. Honors <c>LOGMARK_APPDATA_PATH</c> (the same
    /// override SurfaceBootstrap honors for bootstrap/usage/license state) so
    /// tests and sandboxed agent runs redirect ALL machine-local coordination
    /// state with one variable instead of writing the developer's real registry.
    /// </summary>
    public static string DefaultRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("LOGMARK_APPDATA_PATH");
        if (!string.IsNullOrEmpty(overrideRoot))
            return Path.Combine(overrideRoot, "providers");
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogMark", "providers")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".logmark", "providers");
    }

    public void Save(ProviderManifest manifest)
    {
        Directory.CreateDirectory(_rootDir);
        var path = Path.Combine(_rootDir, manifest.ProviderId + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }

    public IReadOnlyList<ProviderManifest> LoadAll()
    {
        if (!Directory.Exists(_rootDir)) return Array.Empty<ProviderManifest>();
        var result = new List<ProviderManifest>();
        foreach (var file in Directory.GetFiles(_rootDir, "*.json"))
        {
            try
            {
                var m = JsonSerializer.Deserialize<ProviderManifest>(File.ReadAllText(file), ReadOptions);
                if (m is not null) result.Add(m);
            }
            catch { /* lenient: a broken file never blocks coordination */ }
        }
        return result;
    }

    public void Prune(Func<string, bool> fileExists)
    {
        foreach (var m in LoadAll())
        {
            if (m.Binaries.Values.Any(b => fileExists(b.Path))) continue;
            try { File.Delete(Path.Combine(_rootDir, m.ProviderId + ".json")); } catch { }
        }
    }
}
