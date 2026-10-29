using System.IO;
using System.Reflection;
using LogMark.Exceptions;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Initializes vaults with LogMark structure using a bundle.
/// </summary>
public class VaultInitializer
{
    private const string FallbackSubject = "your subject";

    private readonly string _vaultPath;
    private readonly string _logmarkPath;
    private readonly bool _force;
    private readonly string _seededBy;
    private readonly BundleService _bundleService;

    public string VaultPath => _vaultPath;
    public string LogmarkPath => _logmarkPath;
    public bool WasReinterpreted { get; }

    /// <param name="seededBy">
    /// Identifies which product/version is writing or backfilling this vault's
    /// format stamp (see <see cref="VaultStamp"/>) - e.g. "desktop/0.17.0" or
    /// "oss-cli/0.17.0". Defaults to "unknown/0.0.0" for callers that have no
    /// clean line to a composition-root version (e.g. internal import/crawl
    /// paths that construct a VaultInitializer incidentally).
    /// </param>
    public VaultInitializer(string vaultPath, bool force = false, string seededBy = "unknown/0.0.0")
    {
        if (LooksLikeLogmarkContentRoot(vaultPath))
        {
            _logmarkPath = vaultPath;
            _vaultPath = Path.GetDirectoryName(vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ?? vaultPath;
            WasReinterpreted = true;
        }
        else
        {
            _vaultPath = vaultPath;
            _logmarkPath = Path.Combine(vaultPath, "logmark");
            WasReinterpreted = false;
        }

        _force = force;
        _seededBy = seededBy;
        _bundleService = new BundleService();
    }

    /// <summary>
    /// The root of an existing vault that would contain a vault initialized at
    /// <paramref name="path"/>, or null. Walks the ancestors of the effective
    /// vault root (the path itself is the re-init case, handled by
    /// VaultExistsException) for a logmark/_system folder or logmark/vault.md.
    /// A nested vault is double-read by the outer one as a route (bead lmk-zj5c).
    /// </summary>
    public static string? FindEnclosingVault(string path)
    {
        var root = new VaultInitializer(Path.GetFullPath(path)).VaultPath;
        for (var dir = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
             dir != null; dir = dir.Parent)
        {
            var logmark = Path.Combine(dir.FullName, "logmark");
            if (Directory.Exists(Path.Combine(logmark, "_system")) || File.Exists(Path.Combine(logmark, "vault.md")))
                return dir.FullName;
        }
        return null;
    }

    private static bool LooksLikeLogmarkContentRoot(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            return false;

        var leaf = Path.GetFileName(vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(leaf, "logmark", StringComparison.OrdinalIgnoreCase))
            return false;

        return Directory.Exists(Path.Combine(vaultPath, "_inbox"))
            || File.Exists(Path.Combine(vaultPath, "_system", "config.md"))
            || Directory.Exists(Path.Combine(vaultPath, "_chats"));
    }

    public bool IsInitialized()
    {
        return File.Exists(Path.Combine(_logmarkPath, "_system", "config.md"));
    }

    public bool LogMarkFolderExists()
    {
        return Directory.Exists(_logmarkPath);
    }

    public void Initialize(string bundleId = "build", bool seedEntries = true, string? subject = null)
    {
        var manifest = _bundleService.GetBundle(bundleId)
            ?? throw new ArgumentException(
                $"Unknown bundle: {bundleId}. Valid bundles: {string.Join(", ", _bundleService.GetBundles().Select(b => b.Id))}");

        if (LogMarkFolderExists() && !_force)
            throw new VaultExistsException(_logmarkPath);

        CreateInboxStructure();
        Directory.CreateDirectory(Path.Combine(_logmarkPath, "_system"));
        // Best-effort: VaultInitializer has no logger, and a stamp-write failure
        // must not abort vault init (warn-and-continue posture, matched elsewhere
        // via the caller's own logger where one exists).
        try { VaultStamp.EnsureWritten(_logmarkPath, _seededBy); } catch { /* best-effort */ }
        CopyBundleFiles(manifest, subject);

        if (seedEntries)
            SeedEntries(manifest, subject);
    }

    /// <summary>
    /// Idempotent top-up of structural files. Existing files are preserved.
    /// </summary>
    public void EnsureInitialized(string bundleId = "build")
    {
        var manifest = _bundleService.GetBundle(bundleId)
            ?? throw new ArgumentException(
                $"Unknown bundle: {bundleId}. Valid bundles: {string.Join(", ", _bundleService.GetBundles().Select(b => b.Id))}");

        CreateInboxStructure();
        Directory.CreateDirectory(Path.Combine(_logmarkPath, "_system"));
        // Best-effort: see comment in Initialize above - a stamp-write failure
        // must not abort the structural top-up.
        try { VaultStamp.EnsureWritten(_logmarkPath, _seededBy); } catch { /* best-effort */ }
        CopyBundleFiles(manifest, subject: null);
    }

    private void CreateInboxStructure()
    {
        Directory.CreateDirectory(Path.Combine(_logmarkPath, "_inbox"));
    }

    private void CopyBundleFiles(BundleManifest manifest, string? subject)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = _bundleService.GetBundleResourcePrefix(manifest.Id);
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => BundleResourceMatches(name, resourcePrefix))
            .Where(name => !BundleService.NormalizeResourceName(name).EndsWith(".bundle.yaml", StringComparison.OrdinalIgnoreCase))
            .Where(name => !BundleService.NormalizeResourceName(name).EndsWith(".seed.md", StringComparison.OrdinalIgnoreCase))
            .Where(name => !BundleService.NormalizeResourceName(name).EndsWith("._inbox._gitkeep", StringComparison.OrdinalIgnoreCase))
            .Where(name => !BundleService.NormalizeResourceName(name).EndsWith("._inbox..gitkeep", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var resourceName in resourceNames)
        {
            var relativePath = ResourceNameToPath(resourceName, resourcePrefix);
            var targetPath = Path.Combine(_logmarkPath, relativePath);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(targetPath) && !_force)
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                continue;

            using var reader = new StreamReader(stream);
            var content = ApplySubject(reader.ReadToEnd(), manifest, subject);
            File.WriteAllText(targetPath, content);
        }
    }

    private static bool BundleResourceMatches(string resourceName, string resourcePrefix)
    {
        return BundleService.NormalizeResourceName(resourceName)
            .StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResourceNameToPath(string resourceName, string prefix)
    {
        var normalized = BundleService.NormalizeResourceName(resourceName);
        var path = normalized.Substring(prefix.Length);
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return path;

        var dirSegments = new List<string>();
        var i = 0;
        while (i < segments.Length - 2)
        {
            dirSegments.Add(segments[i]);
            i++;
        }

        var fileName = segments[^2];
        var extension = segments[^1];
        var directories = string.Join(Path.DirectorySeparatorChar.ToString(), dirSegments);
        var fullFileName = $"{fileName}.{extension}";

        return string.IsNullOrEmpty(directories)
            ? fullFileName
            : Path.Combine(directories, fullFileName);
    }

    public string GetConfigPath(string fileName)
    {
        return Path.Combine(_logmarkPath, "_system", fileName);
    }

    private void SeedEntries(BundleManifest manifest, string? subject)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = _bundleService.GetBundleResourcePrefix(manifest.Id);
        var seedResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name =>
                BundleResourceMatches(name, resourcePrefix)
                && BundleService.NormalizeResourceName(name).EndsWith(".seed.md", StringComparison.OrdinalIgnoreCase));

        if (seedResourceName == null)
            return;

        using var stream = assembly.GetManifestResourceStream(seedResourceName);
        if (stream == null)
            return;

        using var reader = new StreamReader(stream);
        var seedContent = ApplySubject(reader.ReadToEnd(), manifest, subject);
        if (string.IsNullOrWhiteSpace(seedContent))
            return;

        var backfillEngine = new BackfillEngine(_vaultPath);
        backfillEngine.AddEntriesFromSeed(seedContent);
    }

    private static string ApplySubject(string content, BundleManifest manifest, string? subject)
    {
        if (!manifest.SubjectRequired)
            return content;

        var effectiveSubject = string.IsNullOrWhiteSpace(subject)
            ? FallbackSubject
            : subject!;
        return content.Replace("{{subject}}", effectiveSubject);
    }
}
