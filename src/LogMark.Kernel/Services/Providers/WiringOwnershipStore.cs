using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogMark.Services.Providers;

/// <summary>
/// One wired entry a LogMark product can write and remove: a framework slug,
/// a capability ("hooks" or "mcp"), a scope ("user", "project", "local"), and
/// the project directory for project/local scope. Skills are not here - their
/// owners live in the skill folder's .logmark-pack marker.
/// </summary>
public sealed record WiringTarget(string Framework, string Capability, string Scope, string? ProjectDir)
{
    public const string EntryName = "logmark";
}

/// <summary>
/// The owners of one wired entry (root design 2026-10-21 section 3.1).
/// Cross-product protocol from v0.1.0: evolve additively only, guarded by
/// OwnershipVersion. Fields this version does not know land in Extra and are
/// written back unchanged, so an older LogMark never erases what a newer one
/// recorded.
/// </summary>
public sealed record WiringOwnershipRecord(
    int OwnershipVersion, string Framework, string Capability, string Scope,
    string? Project, string Entry, IReadOnlyList<string> Owners, DateTimeOffset UpdatedAt)
{
    public const int CurrentVersion = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>
/// File store for ownership records, one JSON file per wired entry. Writes go
/// through a temp file and an atomic replace. There is deliberately no
/// cross-process lock: a lost update can only drop an owner, and a provider
/// missing from Owners gets the non-destructive default on disconnect, so the
/// race fails safe. An unparseable record loads as null (the caller falls back
/// to legacy attribution).
/// </summary>
public sealed class WiringOwnershipStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions ReadOptions = new()
    { PropertyNameCaseInsensitive = true };

    private readonly string _rootDir;

    public WiringOwnershipStore(string rootDir) => _rootDir = rootDir;

    /// <summary>Same tiering as ProviderManifestStore.DefaultRoot, sibling folder "wiring".</summary>
    public static string DefaultRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("LOGMARK_APPDATA_PATH");
        if (!string.IsNullOrEmpty(overrideRoot))
            return Path.Combine(overrideRoot, "wiring");
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogMark", "wiring")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".logmark", "wiring");
    }

    /// <summary>Full path, no trailing separator, forward slashes, lowercase on Windows.</summary>
    public static string CanonicalProject(string projectDir)
    {
        var full = Path.GetFullPath(projectDir)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .TrimEnd('/');
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    public static string FileNameFor(WiringTarget target)
    {
        var name = $"{target.Framework}.{target.Capability}.{target.Scope}";
        if (target.Scope != "user" && !string.IsNullOrEmpty(target.ProjectDir))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalProject(target.ProjectDir)));
            name += "." + Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
        return name + ".json";
    }

    public WiringOwnershipRecord? Load(WiringTarget target)
    {
        var path = Path.Combine(_rootDir, FileNameFor(target));
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<WiringOwnershipRecord>(File.ReadAllText(path), ReadOptions); }
        catch { return null; }
    }

    /// <summary>
    /// Read-modify-write: replaces the owners and updatedAt of the existing
    /// record, keeping its unknown fields and a higher ownershipVersion
    /// (forward safety, final review Important 3). The write goes through a
    /// temp file and an atomic replace; the temp file is removed if the replace
    /// fails.
    /// </summary>
    public void Save(WiringTarget target, IEnumerable<string> owners)
    {
        Directory.CreateDirectory(_rootDir);
        var existing = Load(target);
        var record = new WiringOwnershipRecord(
            Math.Max(existing?.OwnershipVersion ?? 0, WiringOwnershipRecord.CurrentVersion),
            target.Framework, target.Capability, target.Scope,
            target.Scope == "user" || target.ProjectDir is null ? null : CanonicalProject(target.ProjectDir),
            WiringTarget.EntryName,
            owners.Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList(),
            DateTimeOffset.UtcNow)
        { Extra = existing?.Extra };

        var path = Path.Combine(_rootDir, FileNameFor(target));
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, WriteOptions));
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    public void Delete(WiringTarget target)
    {
        var path = Path.Combine(_rootDir, FileNameFor(target));
        if (File.Exists(path)) File.Delete(path);
    }
}
