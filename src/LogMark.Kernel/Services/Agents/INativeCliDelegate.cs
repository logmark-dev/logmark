namespace LogMark.Services.Agents;

public interface INativeCliDelegate
{
    bool IsAvailable(NativeWireCli cli);
    WiringResult Add(NativeWireCli cli, AgentDefinition agent, WireScope scope,
        string mcpExePath, string[] mcpArgs, string? projectFolderPath);
    WiringResult Remove(NativeWireCli cli, AgentDefinition agent, WireScope scope,
        string? projectFolderPath);
}
