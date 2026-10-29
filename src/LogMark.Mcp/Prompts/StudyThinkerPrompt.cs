using LogMark.Mcp.Protocol;

namespace LogMark.Mcp.Prompts;

/// <summary>
/// Prompt that guides agents through studying a thinker's work using
/// two-pass Leonardo synthesis methodology. Captures explicit knowledge
/// (ideas, decisions) in Pass 1, then implicit knowledge (questions, gaps,
/// analogies) in Pass 2. Outputs entries and pages with thinktank provenance.
/// </summary>
public class StudyThinkerPrompt : IPrompt
{
    public string Name => "study-thinker";

    public PromptDefinition Definition => new()
    {
        Name = "study-thinker",
        Description = "Study a thinker's work using two-pass Leonardo synthesis. Captures explicit and implicit knowledge as vault entries with thinktank provenance metadata.",
        Arguments = new List<PromptArgument>
        {
            new()
            {
                Name = "thinker",
                Description = "Thinker name (e.g., 'sivers', 'karpathy') or URL to study directly.",
                Required = true
            },
            new()
            {
                Name = "scope",
                Description = "Study scope: 'recent' (last 5-10 posts), 'deep' (full archive), or 'topic' (focused theme). Default: recent.",
                Required = false
            },
            new()
            {
                Name = "route",
                Description = "Vault route for captured entries (default: research/{thinkerId}).",
                Required = false
            }
        }
    };

    public Task<PromptGetResult> GetAsync(Dictionary<string, string>? arguments, CancellationToken ct)
    {
        var thinker = arguments != null && arguments.TryGetValue("thinker", out var t) ? t : "unknown";
        var scope = arguments != null && arguments.TryGetValue("scope", out var s) ? s : "recent";
        var route = arguments != null && arguments.TryGetValue("route", out var r) ? r : null;

        var promptText = BuildPrompt(thinker, scope, route);

        var result = new PromptGetResult
        {
            Description = $"Study thinker: {thinker}",
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

    private static string BuildPrompt(string thinker, string scope, string? route)
    {
        var isUrl = ThinkerRegistry.IsUrl(thinker);

        var thinkerInfo = !isUrl ? ThinkerRegistry.Lookup(thinker) : null;
        var thinkerId = thinkerInfo?.Id ?? ThinkerRegistry.SanitizeId(thinker);
        var resolvedRoute = route ?? $"research/{thinkerId}";

        var thinkerSection = BuildThinkerSection(thinker, isUrl, thinkerInfo);
        var scopeSection = BuildScopeSection(scope);

        return $@"# Study Thinker: {(thinkerInfo != null ? thinkerInfo.Name : thinker)}

You are performing a Leonardo-style study of a thinker's work, capturing both explicit and implicit knowledge into a LogMark vault.

{thinkerSection}

## Step 0: Check for Cached Content

Before browsing and synthesizing from scratch, check if pre-synthesized content exists for this thinker on logmark.ink.

Call `import_thinker_space` with the thinker name:
```
import_thinker_space(thinker: ""{thinkerId}"")
```

If it returns imported entries, you already have high-quality synthesized content in the vault. You can then:
- Browse the imported entries to understand what is already captured
- Run a Pass 2 (Leonardo Lens) on the imported content to add questions, gaps, and analogies
- Only study new content that was published after the cached content

If it returns an error (no Space found), proceed with the full study below.

## Route

All entries and pages should be routed to `+{resolvedRoute}`.

{scopeSection}

## Two-Pass Leonardo Methodology

This study follows Leonardo da Vinci's notebook practice: first capture what is said, then probe what is left unsaid.

### PASS 1: Explicit Knowledge

Read the thinker's content and extract explicit knowledge:

| What to capture | Entry type | Example |
|-----------------|------------|---------|
| Key ideas worth preserving | `i:` | `i: software should be joyful to use +{resolvedRoute} #design` |
| Decisions or positions stated | `d:` | `d: chose simplicity over features +{resolvedRoute} #philosophy` |
| Actionable takeaways | `t:` | `t: try their approach to daily reviews +{resolvedRoute}` |
| Longer synthesis or summaries | page via `write_page` | Multi-paragraph distillation |

For each piece of content studied, use `create_entry` to capture entries and `write_page` for longer-form synthesis.

Use `organize_entry` (action: link) to connect summary entries to their detail pages.
Use `save_asset` to preserve any images, diagrams, or media referenced in the content.

### PASS 2: Implicit Knowledge

After completing Pass 1, make a second pass to surface what is beneath the surface:

| What to surface | Entry type | Example |
|-----------------|------------|---------|
| Open questions raised | `q:` | `q: why do they avoid metrics? +{resolvedRoute} #methodology` |
| Knowledge gaps exposed | `g:` | `g: no data on long-term outcomes of this approach +{resolvedRoute}` |
| Cross-domain analogies | `a:` | `a: their content model is like a garden -- growth through cultivation not construction +{resolvedRoute}` |

Questions should be specific and answerable. Gaps should state what is unknown, not what should be done. Analogies should map structure between domains using the pattern ""X is like Y -- Z"".

## Provenance Metadata

Every entry MUST include thinktank provenance metadata in the entry body or metadata block:

```
origin: thinktank
via: {(thinkerInfo != null ? thinkerInfo.Name : thinker)}
via_url: [URL of the specific piece being studied]
crawled: [ISO date when content was read]
rating: 0
```

The `via:` field identifies the thinker. The `via_url:` field points to the specific article, post, or page. The `crawled:` date records when the content was accessed. The `rating:` starts at 0 (unrated) for human review later.

## MCP Tools

Use these tools throughout the study:

| Tool | Purpose |
|------|---------|
| `create_entry` | Capture individual insights, decisions, questions, gaps, analogies |
| `write_page` | Write longer synthesis pages (summaries, comparisons, deep dives) |
| `organize_entry` (action: link) | Connect entry to its detail page |
| `save_asset` | Preserve images, diagrams, or media from the source |

## Diary Assembly

After both passes, check if any single date has 2 or more entries. For dates with multiple entries, assemble a diary page using `write_page` that weaves the day's captures into a narrative summary. Route diary pages to `+{resolvedRoute}` with the date in the title.

## Cross-Cutting Theme Synthesis

After both passes, step back and identify themes that run across multiple pieces of content:

1. **Recurring patterns**: What principles does this thinker apply repeatedly across different topics? Name the pattern and cite 2+ entries that demonstrate it.
2. **Tensions**: Where does the thinker contradict themselves or hold positions in tension? These are often the most interesting findings.
3. **Transferable frameworks**: What mental models or decision frameworks does this thinker use that would apply in completely different domains?

Write a synthesis page using `write_page` titled ""{(thinkerInfo != null ? thinkerInfo.Name : thinker)} -- Cross-Cutting Themes"" that captures these patterns. This page is the highest-value output of the study -- it is what makes this thinker's thinking composable with other thinkers' work.

## Completion

When finished, report:
- Count of entries by type (i, d, t, q, g, a)
- Count of pages written
- Top 3 most significant findings
- Cross-cutting themes identified (the patterns that span multiple pieces)
- Any gaps that warrant follow-up study";
    }

    private static string BuildThinkerSection(string thinker, bool isUrl, ThinkerRegistry.ThinkerInfo? info)
    {
        if (isUrl)
        {
            return $@"## Source

Direct URL to study: {thinker}

Read and study the content at this URL.";
        }

        if (info != null)
        {
            var sources = string.Join("\n", info.Sources.Select(s => $"- {s.Type}: {s.Url}"));
            return $@"## Known thinker: {info.Name}

{info.Summary}

**Sources to study:**
{sources}";
        }

        return $@"## Thinker: {thinker}

No pre-loaded context for this thinker. Search for their primary content sources (blog, newsletter, social media, talks) before beginning the study.";
    }

    private static string BuildScopeSection(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "deep" => @"## Scope: deep

Study the thinker's full archive. Cover their major works, recurring themes, and evolution of thinking over time. This is a comprehensive study -- expect to create many entries and multiple synthesis pages.",

            "topic" => @"## Scope: topic

Focus on a specific theme or topic within the thinker's work. Identify the relevant pieces, trace the thread across their content, and synthesize the focused perspective.",

            _ => @"## Scope: recent

Study the thinker's last 5-10 posts, articles, or pieces of content. Focus on what they are currently thinking about and any shifts from their established positions."
        };
    }

}
