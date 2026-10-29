using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Prompts;

/// <summary>
/// Registry for MCP prompts.
/// </summary>
public class PromptRegistry
{
    private readonly Dictionary<string, IPrompt> _prompts = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IPrompt prompt)
    {
        _prompts[prompt.Name] = prompt;
    }

    public IPrompt? GetPrompt(string name)
    {
        return _prompts.TryGetValue(name, out var prompt) ? prompt : null;
    }

    public PromptsListResult GetPromptList()
    {
        return new PromptsListResult
        {
            Prompts = _prompts.Values.Select(p => p.Definition).ToList()
        };
    }
}
