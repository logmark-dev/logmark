using System.Text.Json;

namespace LogMark.Services.Agents;

/// <summary>
/// Thrown when an authored skill pack on disk does not match the canonical
/// layout: a missing or malformed pack.json, a pack with no skills, or a skill
/// folder lacking a SKILL.md.
/// </summary>
public class SkillPackFormatException : Exception
{
    public SkillPackFormatException(string message) : base(message) { }
}

/// <summary>
/// Loads a canonical skill pack from disk into a <see cref="SkillPack"/>.
/// Layout: &lt;packDir&gt;/pack.json plus one folder per skill, each containing
/// a required SKILL.md and any number of optional nested files. File keys are
/// normalized to forward-slash relative paths. Skills are returned ordered by
/// name (ordinal) for deterministic install. pack.json's "version" field is
/// optional and defaults to 1 -- no authored pack sets it yet.
/// </summary>
public static class SkillPackLoader
{
    private sealed record PackManifest(
        string? Slug, string? DisplayName, string? Description, bool? RecommendsHooks,
        int? Version);

    public static SkillPack Load(string packDir)
    {
        var manifestPath = Path.Combine(packDir, "pack.json");
        if (!File.Exists(manifestPath))
            throw new SkillPackFormatException($"pack.json not found in {packDir}");

        PackManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PackManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new SkillPackFormatException($"pack.json is not valid JSON: {ex.Message}");
        }

        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Slug)
            || string.IsNullOrWhiteSpace(manifest.DisplayName)
            || string.IsNullOrWhiteSpace(manifest.Description))
            throw new SkillPackFormatException(
                "pack.json must define non-empty slug, displayName, and description");

        var skills = new List<SkillAsset>();
        foreach (var skillDir in Directory.GetDirectories(packDir))
        {
            var skillName = Path.GetFileName(skillDir);
            if (!File.Exists(Path.Combine(skillDir, "SKILL.md")))
                throw new SkillPackFormatException(
                    $"skill folder '{skillName}' is missing SKILL.md");

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(skillDir, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                files[rel] = File.ReadAllText(file);
            }
            skills.Add(new SkillAsset(skillName, files));
        }

        if (skills.Count == 0)
            throw new SkillPackFormatException($"pack '{manifest.Slug}' contains no skills");

        skills.Sort((a, b) => string.CompareOrdinal(a.SkillName, b.SkillName));
        return new SkillPack(
            manifest.Slug!, manifest.DisplayName!, manifest.Description!, skills,
            manifest.RecommendsHooks ?? false, manifest.Version ?? 1);
    }
}
