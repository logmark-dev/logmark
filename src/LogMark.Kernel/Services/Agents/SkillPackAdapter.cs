using System.Text;

namespace LogMark.Services.Agents;

/// <summary>Thrown when a pack cannot be adapted to a framework profile (e.g. a
/// description exceeds the framework's cap under Error behavior).</summary>
public class SkillAdaptationException : Exception
{
    public SkillAdaptationException(string message) : base(message) { }
}

/// <summary>
/// Transforms a canonical <see cref="SkillPack"/> for one framework, per a
/// <see cref="FrameworkProfile"/>. Pure: the input pack is never mutated; a new
/// pack is returned. The Identity profile is a no-op that returns the input
/// reference. Frontmatter parsing assumes flat "key: value" lines, which the
/// authored LogMark packs use.
/// </summary>
public static class SkillPackAdapter
{
    public static SkillPack Adapt(SkillPack pack, FrameworkProfile profile)
    {
        bool noop = profile.FrontmatterMode == FrontmatterMode.Identity
            && profile.Sidecar == SidecarFormat.None
            && profile.BoundaryInstruction is null
            && (profile.PathRewrites is null || profile.PathRewrites.Count == 0);
        if (noop) return pack;

        var adapted = new List<SkillAsset>(pack.Skills.Count);
        foreach (var skill in pack.Skills)
            adapted.Add(AdaptSkill(skill, profile));

        return pack with { Skills = adapted };
    }

    private static SkillAsset AdaptSkill(SkillAsset skill, FrameworkProfile profile)
    {
        var files = new Dictionary<string, string>(skill.Files, StringComparer.Ordinal);
        string? name = null, description = null;

        if (files.TryGetValue("SKILL.md", out var skillMd))
        {
            var (frontmatter, body) = SplitFrontmatter(skillMd);

            if (frontmatter is not null && profile.FrontmatterMode == FrontmatterMode.Allowlist)
            {
                frontmatter = ApplyAllowlist(frontmatter, profile, out name, out description);
            }
            else if (frontmatter is not null)
            {
                var fields = ParseFields(frontmatter);
                fields.TryGetValue("name", out name);
                fields.TryGetValue("description", out description);
            }

            if (profile.BoundaryInstruction is not null)
                body = body.TrimEnd() + $"\n\n## Boundary\n\n{profile.BoundaryInstruction}\n";

            files["SKILL.md"] = frontmatter is null
                ? body
                : $"---\n{frontmatter}\n---\n{body}";
        }

        if (profile.PathRewrites is { Count: > 0 } rewrites)
        {
            foreach (var key in files.Keys.ToList())
            {
                var content = files[key];
                foreach (var rw in rewrites)
                    content = content.Replace(rw.From, rw.To);
                files[key] = content;
            }
        }

        if (profile.Sidecar == SidecarFormat.OpenAiYaml)
            files["openai.yaml"] = BuildOpenAiYaml(name, description);

        return skill with { Files = files };
    }

    private static (string? Frontmatter, string Body) SplitFrontmatter(string md)
    {
        md = md.Replace("\r\n", "\n");
        if (!md.StartsWith("---\n")) return (null, md);

        var lines = md.Split('\n');
        int close = -1;
        for (int i = 1; i < lines.Length; i++)
            if (lines[i] == "---") { close = i; break; }
        if (close == -1) return (null, md);

        var fm = string.Join("\n", lines.Skip(1).Take(close - 1));
        var body = string.Join("\n", lines.Skip(close + 1));
        return (fm, body);
    }

    private static Dictionary<string, string> ParseFields(string fm)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in fm.Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue;
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            dict[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return dict;
    }

    private static string ApplyAllowlist(
        string fm, FrameworkProfile profile, out string? name, out string? description)
    {
        var fields = ParseFields(fm);
        fields.TryGetValue("name", out name);
        fields.TryGetValue("description", out description);

        var keep = profile.KeepFields ?? Array.Empty<string>();
        var sb = new StringBuilder();
        foreach (var key in keep)
        {
            if (!fields.TryGetValue(key, out var val)) continue;
            if (key == "description" && profile.DescriptionLimit is int lim && val.Length > lim)
            {
                if (profile.DescriptionLimitBehavior == DescriptionLimitBehavior.Error)
                    throw new SkillAdaptationException(
                        $"description ({val.Length} chars) exceeds limit of {lim}");
                val = val[..lim];
                description = val;
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(key).Append(": ").Append(val);
        }
        return sb.ToString();
    }

    private static string BuildOpenAiYaml(string? name, string? description)
    {
        var sb = new StringBuilder();
        sb.Append("name: ").Append(name ?? string.Empty).Append('\n');
        sb.Append("description: ").Append(description ?? string.Empty).Append('\n');
        return sb.ToString();
    }
}
