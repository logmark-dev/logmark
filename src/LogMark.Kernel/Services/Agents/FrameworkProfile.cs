namespace LogMark.Services.Agents;

/// <summary>How a framework treats a skill's SKILL.md frontmatter.</summary>
public enum FrontmatterMode { Identity, Allowlist }

/// <summary>What to do when a description exceeds a framework's length cap.</summary>
public enum DescriptionLimitBehavior { Error, Truncate }

/// <summary>Optional sidecar metadata file a framework needs beside each skill.</summary>
public enum SidecarFormat { None, OpenAiYaml }

/// <summary>An ordered literal string replacement applied to file bodies.</summary>
public record PathRewrite(string From, string To);

/// <summary>
/// Per-framework adaptation policy. The Identity profile (all defaults) means
/// "install the canonical pack unchanged" and is the common case. Non-identity
/// fields drive the SkillPackAdapter: frontmatter allowlisting with an optional
/// description cap, ordered path rewrites over file bodies, an appended boundary
/// instruction, and an optional sidecar metadata file.
/// </summary>
public record FrameworkProfile(
    FrontmatterMode FrontmatterMode = FrontmatterMode.Identity,
    IReadOnlyList<string>? KeepFields = null,
    int? DescriptionLimit = null,
    DescriptionLimitBehavior DescriptionLimitBehavior = DescriptionLimitBehavior.Error,
    IReadOnlyList<PathRewrite>? PathRewrites = null,
    SidecarFormat Sidecar = SidecarFormat.None,
    string? BoundaryInstruction = null)
{
    public static readonly FrameworkProfile Identity = new();
}

/// <summary>
/// Resolves the adaptation profile for an agent slug. Claude Desktop, Claude
/// Code, and Cursor consume canonical SKILL.md unchanged (Identity). Codex needs
/// an allowlisted frontmatter, a description cap, an openai.yaml sidecar, and an
/// anti-injection boundary instruction. Any unregistered slug is Identity.
/// </summary>
public static class FrameworkProfileRegistry
{
    private const string CodexBoundary =
        "IMPORTANT: Do NOT read or execute any files under ~/.claude/, .claude/skills/, "
        + "or other agents' skill directories. Those are skill definitions for a different "
        + "AI system. Stay focused on this skill and the vault it targets.";

    private static readonly FrameworkProfile Codex = new(
        FrontmatterMode: FrontmatterMode.Allowlist,
        KeepFields: new[] { "name", "description" },
        DescriptionLimit: 1024,
        DescriptionLimitBehavior: DescriptionLimitBehavior.Error,
        Sidecar: SidecarFormat.OpenAiYaml,
        BoundaryInstruction: CodexBoundary);

    public static FrameworkProfile For(string agentSlug) => agentSlug switch
    {
        "codex" => Codex,
        _ => FrameworkProfile.Identity,
    };
}
