using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogMark.Services.Agents;

/// <summary>
/// Installs, detects, and removes LogMark skill packs in an agent's on-disk
/// skills directory. The twin of InboundWirer: same registry and path
/// resolution, but writes skill folders instead of MCP config entries. Each
/// installed skill folder carries a ".logmark-pack" marker file so reinstalls
/// are idempotent and removal never deletes a user's own skill of the same
/// name. The marker is refcounted across products sharing the same on-disk
/// skills dir (desktop + OSS CLI both install the same pack to the same
/// path): it stores an "owners" list, and a folder is only deleted once the
/// last owner uninstalls it. Pre-coexistence markers (a bare slug string, no
/// JSON) are treated as owned-with-unknown-owner and migrated to the JSON
/// shape on the next write that touches them.
/// </summary>
public class SkillsInstaller
{
    private const string MarkerFile = ".logmark-pack";
    private readonly SkillsPathResolver _resolver;

    public SkillsInstaller(SkillsPathResolver resolver) => _resolver = resolver;

    /// <summary>
    /// Parsed shape of a ".logmark-pack" marker file. Files maps each
    /// pack-owned file's forward-slash relative path to the hash of the
    /// content this installer wrote, so a later run can tell "the shipped
    /// pack moved" apart from "the user edited their copy". Null for markers
    /// written before hashing existed - see GetState's migration handling.
    /// </summary>
    private sealed record PackMarker(
        string Pack, int PackVersion, List<string> Owners,
        Dictionary<string, string>? Files = null)
    {
        /// <summary>
        /// True for a pre-coexistence bare-slug marker, which proves LogMark
        /// wrote this folder before hashing or owners existed. Used by GetState
        /// to adopt drifted content instead of treating it as a user edit.
        ///
        /// Never serialized: this describes the marker's on-disk FORMAT, not
        /// its data, and ReadMarker sets it only on the plain-slug branch.
        /// Round-tripping it would let a hand-written JSON marker claim legacy
        /// status and win the adoption path it is not entitled to.
        /// </summary>
        [JsonIgnore]
        public bool IsLegacy { get; init; }
    }

    /// <summary>
    /// Lowercase hex SHA-256 over the UTF-8 bytes of a file's text. Content is
    /// hashed rather than file bytes so a BOM or encoding difference on read
    /// cannot masquerade as a user edit: both the write path and the read path
    /// go through a string.
    /// </summary>
    internal static string HashContent(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>
    /// Reads a marker file, returning null when the folder is unowned by
    /// LogMark (no marker, or a marker that doesn't parse as either format).
    /// Legacy plain-slug markers (pre-coexistence: the file's entire content
    /// is just the pack slug, no JSON) are owned iff the slug matches -
    /// there is no owners list to read, so Owners comes back empty; the
    /// caller migrates to JSON on the next write.
    /// </summary>
    private static PackMarker? ReadMarker(string markerPath, string expectedSlug)
    {
        if (!File.Exists(markerPath)) return null;
        var text = File.ReadAllText(markerPath).Trim();
        if (!text.StartsWith('{'))
            return text == expectedSlug
                ? new PackMarker(text, 0, new List<string>()) { IsLegacy = true }
                : null;
        try
        {
            return JsonSerializer.Deserialize<PackMarker>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
    }

    private static void WriteMarker(
        string markerPath, string slug, IEnumerable<string> owners, int packVersion,
        Dictionary<string, string>? files)
        => File.WriteAllText(markerPath, JsonSerializer.Serialize(
            new PackMarker(slug, packVersion, owners.Distinct().ToList(), files),
            new JsonSerializerOptions
            { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    public WiringResult Install(AgentDefinition agent, SkillPack pack,
        WireScope scope, string? projectFolderPath, string ownerId,
        IEnumerable<string>? carryOwners = null)
    {
        if (!_resolver.Supports(agent))
            return new WiringResult(false, $"{agent.DisplayName} does not support file-based skills");

        string skillsDir;
        try { skillsDir = _resolver.Resolve(agent, scope, projectFolderPath); }
        catch (ArgumentException ex) { return new WiringResult(false, ex.Message); }

        // Conflict guard: a folder that exists WITHOUT a marker we own (no
        // marker at all, or a marker for a different pack) is a user's own
        // skill. Refuse the whole install rather than clobber it.
        foreach (var skill in pack.Skills)
        {
            var folder = Path.Combine(skillsDir, skill.SkillName);
            if (!Directory.Exists(folder)) continue;
            var marker = ReadMarker(Path.Combine(folder, MarkerFile), pack.Slug);
            if (marker is null || marker.Pack != pack.Slug)
                return new WiringResult(false,
                    $"A non-LogMark skill named '{skill.SkillName}' already exists at {folder}; refusing to overwrite.");
        }

        try
        {
            foreach (var skill in pack.Skills)
            {
                var folder = Path.Combine(skillsDir, skill.SkillName);
                var markerPath = Path.Combine(folder, MarkerFile);
                var existing = ReadMarker(markerPath, pack.Slug);

                // Downgrade gate: an installer carrying an OLDER pack must not
                // stomp newer installed content. Legacy plain-slug markers
                // carry no packVersion and read back as PackVersion 0 (see
                // ReadMarker), so a JSON marker always wins over a legacy one.
                // Equal versions still overwrite so reinstall repairs
                // hand-edited files; only strict downgrades are blocked.
                var storedVersion = existing?.PackVersion ?? 0;
                var skipContent = storedVersion > pack.Version;

                Directory.CreateDirectory(folder);
                if (!skipContent)
                {
                    foreach (var (relPath, content) in skill.Files)
                    {
                        var dest = Path.Combine(folder,
                            relPath.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.WriteAllText(dest, content);
                    }
                }

                // Hashes describe what is on disk. When content was written,
                // that is this pack's content; when the downgrade gate skipped
                // the write, the existing hashes still describe the newer
                // installed content and must be preserved.
                var hashes = skipContent
                    ? existing?.Files
                    : skill.Files.ToDictionary(f => f.Key, f => HashContent(f.Value));

                var owners = existing?.Owners ?? new List<string>();
                // carryOwners keeps a legacy (owner-less) marker's real owner from
                // being replaced by the installing caller alone.
                WriteMarker(markerPath, pack.Slug, owners.Concat(carryOwners ?? Array.Empty<string>()).Append(ownerId),
                    Math.Max(storedVersion, pack.Version), hashes);
            }
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to install skills: {ex.Message}");
        }
    }

    public SkillInstallState GetState(AgentDefinition agent, SkillPack pack,
        WireScope scope, string? projectFolderPath, string ownerId)
    {
        if (!_resolver.Supports(agent))
            return new SkillInstallState(SkillInstallStatus.Unsupported, null);

        string skillsDir;
        try { skillsDir = _resolver.Resolve(agent, scope, projectFolderPath); }
        catch (ArgumentException) { return new SkillInstallState(SkillInstallStatus.Unsupported, null); }

        int present = 0, conflict = 0;
        var shippedMoved = false;
        var modifiedFiles = new List<string>();
        var hashesMissing = false;

        foreach (var skill in pack.Skills)
        {
            var folder = Path.Combine(skillsDir, skill.SkillName);
            if (!Directory.Exists(folder)) continue;
            var marker = ReadMarker(Path.Combine(folder, MarkerFile), pack.Slug);
            if (marker is null || marker.Pack != pack.Slug)
            {
                conflict++;
                continue;
            }
            present++;

            foreach (var (relPath, shippedContent) in skill.Files)
            {
                var dest = Path.Combine(folder, relPath.Replace('/', Path.DirectorySeparatorChar));
                var shippedHash = HashContent(shippedContent);
                var onDiskHash = File.Exists(dest) ? HashContent(File.ReadAllText(dest)) : null;

                if (marker.Files is null || !marker.Files.TryGetValue(relPath, out var recorded))
                {
                    // No record of what was installed, so a user edit and a
                    // stale copy are indistinguishable from content alone. A
                    // legacy marker proves LogMark wrote this folder, and
                    // LogMark only ever wrote pack content there, so drift is
                    // adopted. A JSON marker merely missing its files map came
                    // from a build that could have recorded hashes, so its
                    // silence says less and the cautious path applies.
                    hashesMissing = true;
                    if (onDiskHash != shippedHash)
                    {
                        shippedMoved = true;
                        if (!marker.IsLegacy) modifiedFiles.Add($"{skill.SkillName}/{relPath}");
                    }
                    continue;
                }

                if (onDiskHash != recorded) modifiedFiles.Add($"{skill.SkillName}/{relPath}");
                if (shippedHash != recorded) shippedMoved = true;
            }
        }

        if (conflict > 0)
            return new SkillInstallState(SkillInstallStatus.Conflict, skillsDir,
                $"{conflict} skill folder(s) not owned by LogMark");
        if (present == 0)
            return new SkillInstallState(SkillInstallStatus.NotInstalled, skillsDir);
        if (present != pack.Skills.Count)
            return new SkillInstallState(SkillInstallStatus.Partial, skillsDir,
                $"{present}/{pack.Skills.Count} installed");

        var locallyModified = modifiedFiles.Count > 0;
        if (shippedMoved)
            return new SkillInstallState(
                SkillInstallStatus.Outdated, skillsDir,
                locallyModified
                    ? $"Update available; locally modified: {string.Join(", ", modifiedFiles)}"
                    : "Update available",
                NeedsRepair: !locallyModified,
                LocallyModified: locallyModified);

        // Up to date. A hash-less marker with matching content still needs one
        // repair pass to record hashes and join the self-heal path.
        return new SkillInstallState(
            SkillInstallStatus.Installed, skillsDir,
            locallyModified ? $"Locally modified: {string.Join(", ", modifiedFiles)}" : null,
            NeedsRepair: hashesMissing && !locallyModified,
            LocallyModified: locallyModified);
    }

    /// <summary>
    /// Union of the owners recorded in this pack's markers. Null when no
    /// folder of this pack is installed; empty for legacy (pre-owners) markers.
    /// </summary>
    public IReadOnlyList<string>? GetOwners(AgentDefinition agent, SkillPack pack,
        WireScope scope, string? projectFolderPath)
    {
        if (!_resolver.Supports(agent)) return null;
        string skillsDir;
        try { skillsDir = _resolver.Resolve(agent, scope, projectFolderPath); }
        catch (ArgumentException) { return null; }

        List<string>? owners = null;
        foreach (var skill in pack.Skills)
        {
            var marker = ReadMarker(Path.Combine(skillsDir, skill.SkillName, MarkerFile), pack.Slug);
            if (marker is null || marker.Pack != pack.Slug) continue;
            owners ??= new List<string>();
            foreach (var owner in marker.Owners ?? new List<string>())
                if (!owners.Contains(owner)) owners.Add(owner);
        }
        return owners;
    }

    public WiringResult Uninstall(AgentDefinition agent, SkillPack pack,
        WireScope scope, string? projectFolderPath, string ownerId, bool allOwners = false)
    {
        if (!_resolver.Supports(agent))
            return new WiringResult(true); // nothing could have been installed

        string skillsDir;
        try { skillsDir = _resolver.Resolve(agent, scope, projectFolderPath); }
        catch (ArgumentException) { return new WiringResult(true); }

        try
        {
            foreach (var skill in pack.Skills)
            {
                var folder = Path.Combine(skillsDir, skill.SkillName);
                if (!Directory.Exists(folder)) continue;

                var markerPath = Path.Combine(folder, MarkerFile);
                var marker = ReadMarker(markerPath, pack.Slug);
                if (marker is null || marker.Pack != pack.Slug) continue; // foreign folder, untouched

                // Legacy markers (pre-coexistence) carry no owners list
                // (Owners == []), because there was only ever one product
                // able to have installed it. A JSON marker missing the
                // "owners" key entirely deserializes Owners as null rather
                // than []. Both guard to an empty list here: removing
                // ownerId from an empty list is still empty, so this still
                // deletes - preserving the pre-refcount uninstall behavior
                // instead of stranding the folder forever waiting for an
                // owner that was never recorded.
                var remaining = allOwners
                    ? new List<string>()
                    : (marker.Owners ?? new List<string>())
                        .Where(o => o != ownerId).ToList();
                if (remaining.Count == 0)
                    Directory.Delete(folder, recursive: true);
                else
                    WriteMarker(markerPath, pack.Slug, remaining, marker.PackVersion, marker.Files);
            }
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to remove skills: {ex.Message}");
        }
    }
}

public enum SkillInstallStatus
{
    Unsupported,
    NotInstalled,
    Partial,
    Installed,
    Outdated,
    Conflict
}

/// <summary>
/// NeedsRepair is the single flag the startup self-heal acts on: it is true
/// only when reinstalling is both useful and safe. LocallyModified is
/// reported for the UI and is always mutually exclusive with NeedsRepair -
/// a file the user edited is never silently overwritten.
/// </summary>
public record SkillInstallState(
    SkillInstallStatus Status,
    string? SkillsDir,
    string? Detail = null,
    bool NeedsRepair = false,
    bool LocallyModified = false);
