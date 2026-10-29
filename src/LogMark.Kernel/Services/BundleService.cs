using System.Reflection;
using LogMark.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LogMark.Services;

/// <summary>
/// Loads and exposes vault bundles from embedded bundle.yaml manifests.
/// </summary>
public class BundleService
{
    private const string ResourcePrefix = "LogMark.bundles.";
    private const string ManifestSuffix = ".bundle.yaml";
    private const string DefaultBundleId = "build";

    private static readonly string[] BundleOrder =
    [
        "open",
        "build",
        "tracker",
        "research",
        "code-context",
        "operator",
        "journal",
        "source"
    ];

    private readonly Dictionary<string, BundleManifest> _bundles;

    public BundleService()
    {
        _bundles = LoadBundleManifests();
    }

    public IReadOnlyList<BundleManifest> GetBundles()
    {
        return BundleOrder
            .Select(GetBundle)
            .Where(bundle => bundle != null)
            .Cast<BundleManifest>()
            .ToList();
    }

    public BundleManifest? GetBundle(string id)
    {
        return _bundles.TryGetValue(id, out var manifest) ? manifest : null;
    }

    public BundleManifest GetDefaultBundle()
    {
        return _bundles[DefaultBundleId];
    }

    public bool IsValidBundle(string id)
    {
        return _bundles.ContainsKey(id);
    }

    public string GetBundleResourcePrefix(string bundleId)
    {
        var resourceId = bundleId.Replace('-', '_');
        return $"{ResourcePrefix}{resourceId}.";
    }

    private static Dictionary<string, BundleManifest> LoadBundleManifests()
    {
        var assembly = typeof(BundleService).Assembly;
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var result = new Dictionary<string, BundleManifest>(StringComparer.OrdinalIgnoreCase);
        var manifestResources = assembly.GetManifestResourceNames()
            .Where(name => NormalizeResourceName(name).StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(name => NormalizeResourceName(name).EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => Array.IndexOf(BundleOrder, UnsanitizeBundleId(ExtractBundleFolder(name))))
            .ToList();

        foreach (var resourceName in manifestResources)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            BundleManifest? manifest;
            try
            {
                manifest = deserializer.Deserialize<BundleManifest>(yaml);
            }
            catch
            {
                continue;
            }

            if (manifest == null || !manifest.IsValid()) continue;

            var folderName = ExtractBundleFolder(resourceName);
            if (!string.Equals(folderName, manifest.Id.Replace('-', '_'), StringComparison.OrdinalIgnoreCase))
                continue;

            result[manifest.Id] = manifest;
        }

        return result;
    }

    private static string ExtractBundleFolder(string resourceName)
    {
        var normalized = NormalizeResourceName(resourceName);
        var withoutPrefix = normalized.Substring(ResourcePrefix.Length);
        var firstDot = withoutPrefix.IndexOf('.');
        return firstDot >= 0 ? withoutPrefix[..firstDot] : withoutPrefix;
    }

    private static string UnsanitizeBundleId(string resourceId)
    {
        return resourceId.Replace('_', '-');
    }

    internal static string NormalizeResourceName(string resourceName)
    {
        return resourceName
            .Replace('\\', '.')
            .Replace('/', '.')
            .Replace('-', '_');
    }
}
