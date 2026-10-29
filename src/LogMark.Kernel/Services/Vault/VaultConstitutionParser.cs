using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LogMark.Services.Vault;

using LogMark.Models;

public sealed class VaultConstitutionParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    public VaultConstitution Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return VaultConstitution.Empty;

        var doc = Markdown.Parse(markdown, Pipeline);
        var sections = SplitByH2(doc, markdown!);

        string? scopeMd = sections.GetValueOrDefault("Scope");
        var scope = scopeMd is null ? null : ParseScope(scopeMd);

        return new VaultConstitution
        {
            VaultName = ExtractFirstH1(doc),
            Purpose = ExtractFirstParagraphAfterH1(doc) ?? ExtractFirstParagraphUnderH2(doc, "Purpose"),
            Scope = scope,
            AgentsHelpBy = sections.GetValueOrDefault("How agents should help")?.Trim(),
            FrozenDecisions = sections.GetValueOrDefault("Frozen vault-level decisions")?.Trim(),
            DontSurface = sections.GetValueOrDefault("Don't surface")?.Trim(),
            Conventions = sections.GetValueOrDefault("Conventions")?.Trim(),
            RawMarkdown = markdown,
            Schema = VaultManifestFrontmatter.ReadSchema(markdown),
        };
    }

    private static string? ExtractFirstH1(MarkdownDocument doc)
    {
        foreach (var block in doc)
        {
            if (block is HeadingBlock h && h.Level == 1)
                return InlineToString(h.Inline)?.Trim();
        }
        return null;
    }

    private static string? ExtractFirstParagraphAfterH1(MarkdownDocument doc)
    {
        bool seenH1 = false;
        foreach (var block in doc)
        {
            if (!seenH1)
            {
                if (block is HeadingBlock h && h.Level == 1) seenH1 = true;
                continue;
            }
            if (block is HeadingBlock) return null;
            if (block is ParagraphBlock p) return InlineToString(p.Inline)?.Trim();
        }
        return null;
    }

    // Fallback for manifests that put the purpose under a "## Purpose" heading
    // instead of directly below the H1 (the shape seven vault init bundles shipped).
    private static string? ExtractFirstParagraphUnderH2(MarkdownDocument doc, string title)
    {
        bool inSection = false;
        foreach (var block in doc)
        {
            if (block is HeadingBlock h)
            {
                if (inSection) return null;
                inSection = h.Level == 2
                    && string.Equals(InlineToString(h.Inline)?.Trim(), title, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inSection && block is ParagraphBlock p) return InlineToString(p.Inline)?.Trim();
        }
        return null;
    }

    private static Dictionary<string, string> SplitByH2(MarkdownDocument doc, string source)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentTitle = null;
        int currentStart = -1;
        var lines = source.Split('\n');

        foreach (var block in doc)
        {
            if (block is HeadingBlock h && h.Level == 2)
            {
                if (currentTitle is not null)
                {
                    int endLineExclusive = h.Line;
                    sections[currentTitle] = JoinLines(lines, currentStart, endLineExclusive);
                }
                currentTitle = InlineToString(h.Inline)?.Trim();
                currentStart = h.Line + 1;
            }
        }

        if (currentTitle is not null && currentStart >= 0)
            sections[currentTitle] = JoinLines(lines, currentStart, lines.Length);

        return sections;
    }

    private static string JoinLines(string[] lines, int startInclusive, int endExclusive)
    {
        if (startInclusive < 0 || startInclusive >= lines.Length) return "";
        int actualEnd = Math.Min(endExclusive, lines.Length);
        return string.Join('\n', lines[startInclusive..actualEnd]).Trim();
    }

    private static readonly System.Text.RegularExpressions.Regex RouteRegex =
        new(@"\+([A-Za-z][\w\-]*)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex TagRegex =
        new(@"#([A-Za-z][\w\-]*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static ConstitutionScope ParseScope(string scopeMarkdown)
    {
        string? include = null, exclude = null;
        foreach (var rawLine in scopeMarkdown.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("INCLUDE:", StringComparison.OrdinalIgnoreCase))
                include = line.Substring("INCLUDE:".Length).Trim();
            else if (line.StartsWith("EXCLUDE:", StringComparison.OrdinalIgnoreCase))
                exclude = line.Substring("EXCLUDE:".Length).Trim();
        }

        return new ConstitutionScope
        {
            IncludeProse = include,
            ExcludeProse = exclude,
            IncludedRoutes = ExtractMatches(include, RouteRegex),
            ExcludedRoutes = ExtractMatches(exclude, RouteRegex),
            IncludedTags = ExtractMatches(include, TagRegex),
            ExcludedTags = ExtractMatches(exclude, TagRegex),
        };
    }

    private static IReadOnlyList<string> ExtractMatches(string? text, System.Text.RegularExpressions.Regex regex)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
        {
            var v = m.Groups[1].Value;
            if (seen.Add(v)) result.Add(v);
        }
        return result;
    }

    private static string? InlineToString(ContainerInline? container)
    {
        if (container is null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case LineBreakInline: sb.Append(' '); break;
                case CodeInline code: sb.Append(code.Content); break;
                case EmphasisInline em: sb.Append(InlineToString(em)); break;
                case LinkInline link: sb.Append(InlineToString(link)); break;
                default: break;
            }
        }
        return sb.ToString();
    }
}
