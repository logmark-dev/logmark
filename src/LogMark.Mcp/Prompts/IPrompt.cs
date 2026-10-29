using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Prompts;

/// <summary>
/// Interface for MCP prompts.
/// </summary>
public interface IPrompt
{
    string Name { get; }

    PromptDefinition Definition { get; }

    Task<PromptGetResult> GetAsync(Dictionary<string, string>? arguments, CancellationToken ct);
}
