using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Prompts;

/// <summary>
/// Prompt that guides agents to enrich a vault with advanced entry types
/// (question, gap, analogy) for deeper knowledge work.
/// Runs after a vault is bootstrapped to add depth (q:, g:, a:) entries.
/// </summary>
public class LeonardoDepthPrompt : IPrompt
{
    public string Name => "leonardo-depth";

    public PromptDefinition Definition => new()
    {
        Name = "leonardo-depth",
        Description = "Enrich an existing vault with questions, gaps, and analogies. Scans decisions and ideas to surface open inquiries, knowledge debt, and cross-domain mappings using advanced entry types (q:, g:, a:).",
        Arguments = new List<PromptArgument>
        {
            new()
            {
                Name = "route",
                Description = "Route to analyze (e.g., '+logmark'). If omitted, analyzes unrouted entries.",
                Required = false
            },
            new()
            {
                Name = "scope",
                Description = "Optional scope within route (e.g., 'architecture').",
                Required = false
            }
        }
    };

    public Task<PromptGetResult> GetAsync(Dictionary<string, string>? arguments, CancellationToken ct)
    {
        var route = arguments != null && arguments.TryGetValue("route", out var r) ? r : null;
        var scope = arguments != null && arguments.TryGetValue("scope", out var s) ? s : null;

        var promptText = BuildPrompt(route, scope);

        var result = new PromptGetResult
        {
            Description = "Enrich vault with Leonardo depth entries",
            Messages = new List<PromptMessage>
            {
                new()
                {
                    Role = "user",
                    Content = new ToolContent { Type = "text", Text = promptText }
                }
            }
        };

        return Task.FromResult(result);
    }

    private static string BuildPrompt(string? route, string? scope)
    {
        var routeFilter = route != null ? $" for route `+{route.TrimStart('+')}`" : "";
        var scopeFilter = scope != null ? $" (scope: `{scope}`)" : "";
        var routePrefix = route != null ? $"+{route.TrimStart('+')}" : "";
        if (scope != null && routePrefix.Length > 0)
            routePrefix += $"/{scope}";

        return $@"# Leonardo Depth: Enrich Vault with Questions, Gaps, and Analogies

You are performing a depth pass on a LogMark vault{routeFilter}{scopeFilter}. Your goal is to surface implicit questions, knowledge gaps, and cross-domain analogies from existing entries.

## Background

Leonardo da Vinci's notebooks mixed observations with open questions, acknowledged ignorance, and structural mappings between domains. LogMark's advanced entry types make this practice explicit:

| Type | Prefix | Purpose | Example |
|------|--------|---------|---------|
| Question | `q:` | Open inquiry worth tracking | `q: why does latency spike after deploy?` |
| Gap | `g:` | Knowledge debt -- something you know you don't know | `g: no profiling data for cold starts` |
| Analogy | `a:` | Structural mapping across domains | `a: rate limiting is like traffic lights -- flow control through constraint` |

## Step 1: Read existing entries

Use `search` and `list_recent`{routeFilter} to understand what exists in the vault.
Look at decisions, blocks, ideas, and tasks for implicit depth signals.

## Step 2: Identify depth opportunities

Scan entries for these patterns:

### Questions (q:)
- Decisions that reference untested assumptions
- Ideas that end with ""?"" or ""wondering if""
- Blocks where the root cause is unclear
- Tasks that depend on answers nobody has

### Gaps (g:)
- Decisions made without data (""we think"", ""probably"", ""should be"")
- Missing benchmarks, profiles, or measurements
- Undocumented dependencies or tribal knowledge
- Areas where the team lacks expertise

### Analogies (a:)
- Problems that resemble solved problems in other domains
- Patterns that repeat across projects
- Architecture decisions that mirror well-known systems
- Concepts from one field that illuminate another

## Step 3: Start a session

Call `start_session` with route `{(routePrefix.Length > 0 ? routePrefix.Split('/')[0] : "logmark")}` to group depth entries.

## Step 4: Create depth entries

Use `create_entry` for each identified opportunity. Guidelines:

**Questions** (`q:`):
- Frame as specific, answerable inquiries
- Reference the entry that raised the question using `refs:`
- Tag with `#open` (unanswered) or `#investigating` (being explored)
- Route to the same project as the source entry
{(routePrefix.Length > 0 ? $"- Prefix with `{routePrefix}`\n" : "")}
**Gaps** (`g:`):
- State what is unknown, not what should be done (that's a task)
- Include what would change if the gap were filled
- Tag with the domain where knowledge is missing
{(routePrefix.Length > 0 ? $"- Prefix with `{routePrefix}`\n" : "")}
**Analogies** (`a:`):
- Use the pattern: ""X is like Y because Z""
- Both sides of the mapping should be concrete
- Tag both domains involved
{(routePrefix.Length > 0 ? $"- Prefix with `{routePrefix}`\n" : "")}
## Step 5: End session

Call `end_session` to close the depth pass session.

## Step 6: Report

Summarize what you found:
- Count of questions, gaps, and analogies created
- Most significant findings
- Suggested follow-up actions (tasks that could fill gaps, experiments that could answer questions)

## Principles

- **Quality over quantity**: 3 sharp questions beat 10 vague ones
- **Reference sources**: Use `refs:` to link depth entries to their origins
- **Don't duplicate**: If a question already exists as an entry, skip it
- **Respect the vault**: These are observations, not prescriptions. The user decides what to act on
- **Augmentary, not primary**: These types enrich the vault alongside core entries. Don't force every observation into q/g/a";
    }
}
