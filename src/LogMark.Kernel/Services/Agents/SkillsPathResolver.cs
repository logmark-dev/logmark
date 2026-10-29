namespace LogMark.Services.Agents;

/// <summary>
/// Resolves the on-disk skills directory for an agent. Skills live in a
/// per-agent directory (e.g. ~/.claude/skills, ~/.codex/skills) rather than the
/// MCP config file ConfigPathResolver handles. Claude Desktop shares Claude
/// Code's ~/.claude/skills directory, so unlike the MCP config there is no MSIX
/// special case here.
/// </summary>
public class SkillsPathResolver
{
    private readonly WslBridge? _wsl;

    public SkillsPathResolver(WslBridge? wsl = null) => _wsl = wsl;

    public bool Supports(AgentDefinition agent) =>
        agent.SkillsDirPaths is { Length: > 0 };

    public string Resolve(AgentDefinition agent, WireScope scope, string? projectFolderPath)
    {
        if (scope == WireScope.Project)
        {
            if (projectFolderPath == null
                || agent.ProjectSkillsDirPaths is not { Length: > 0 } projectPaths)
                throw new ArgumentException(
                    "Project scope requires projectFolderPath and ProjectSkillsDirPaths");
            return Path.Combine(projectFolderPath,
                projectPaths[0].Replace('/', Path.DirectorySeparatorChar));
        }

        if (agent.SkillsDirPaths is not { Length: > 0 } userPaths)
            throw new ArgumentException(
                $"{agent.DisplayName} does not support file-based skills");

        return PathTemplateExpander.Expand(userPaths[0], agent.Host, _wsl);
    }
}
