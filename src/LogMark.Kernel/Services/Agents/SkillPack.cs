namespace LogMark.Services.Agents;

/// <summary>
/// One installable skill: a folder name plus its files. Files maps a
/// forward-slash relative path (always including "SKILL.md") to file content.
/// </summary>
public record SkillAsset(string SkillName, IReadOnlyDictionary<string, string> Files);

/// <summary>
/// A pack is the unit a user installs: a process skill plus its operation
/// skills (and a shared reference file), identified by Slug for ownership.
/// RecommendsHooks signals that the pack pairs with the lifecycle hooks, so the
/// install UI can offer to wire them; it does not wire anything itself.
/// Version is the pack's content revision, compared against the on-disk
/// ".logmark-pack" marker's packVersion by SkillsInstaller to gate reinstalls
/// (an older-versioned install must not overwrite newer installed content).
/// Routine content changes do NOT need a bump: staleness is detected by the
/// marker's per-file content hashes. Bump this only for a deliberate
/// downgrade barrier.
/// </summary>
public record SkillPack(
    string Slug,
    string DisplayName,
    string Description,
    IReadOnlyList<SkillAsset> Skills,
    bool RecommendsHooks = false,
    int Version = 1);
