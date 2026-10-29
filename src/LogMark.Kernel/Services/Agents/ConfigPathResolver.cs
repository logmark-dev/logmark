namespace LogMark.Services.Agents;

public class ConfigPathResolver
{
    private const string MsixPackagesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private readonly IRegistryProbe _probe;
    private readonly WslBridge? _wsl;

    public ConfigPathResolver(IRegistryProbe probe, WslBridge? wsl = null)
    {
        _probe = probe;
        _wsl = wsl;
    }

    public string Resolve(AgentDefinition agent, WireScope scope, string? projectFolderPath)
    {
        if (scope == WireScope.Project)
        {
            if (projectFolderPath == null || agent.ProjectConfigPaths == null)
                throw new ArgumentException("Project scope requires projectFolderPath and ProjectConfigPaths");
            var relative = agent.ProjectConfigPaths[0];
            return Path.Combine(projectFolderPath, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        // Local scope lives in the user file (Claude Code: ~/.claude.json,
        // under projects["<dir>"]), so it resolves to the same path as User;
        // the project dir selects the entry inside it, not the file.
        if (scope == WireScope.Local && projectFolderPath == null)
            throw new ArgumentException("Local scope requires projectFolderPath");

        var userPath = PathTemplateExpander.Expand(agent.ConfigPaths[0], agent.Host, _wsl);

        if (agent.Slug == "claude-desktop")
        {
            var msixPath = DetectClaudeMsixPath();
            if (msixPath != null) return msixPath;
        }

        return userPath;
    }

    private string? DetectClaudeMsixPath()
    {
        var packages = _probe.EnumerateSubKeys(MsixPackagesKey)
            .Where(n => n.StartsWith("Claude_"))
            .OrderByDescending(n => n)
            .ToList();
        if (packages.Count == 0) return null;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", packages[0],
            "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");
    }
}
