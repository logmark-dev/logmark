using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Parses entry content into renderable segments.
/// Identifies wiki links, tags, projects, and plain text.
/// Designed for use in viewers (NativeViewer, CLI, MCP).
/// </summary>
public static partial class ContentParser
{
    // Wiki link: [[path]] or [[path|display text]]
    // Path capture allows embedded shroud tokens [TYPE:hex] so that partially-tokenized
    // subpage links (e.g. [[.../pages/[AMT:eb49a7f8]-04-20-title|display]]) still parse
    // as a single WikiLinkSegment instead of being torn apart into a TokenSegment plus
    // two orphan text segments -- which would break the hyperlink and its click handler.
    // Display capture keeps the original behavior: any char except ] unless followed by ].
    [GeneratedRegex(@"\[\[((?:\[[A-Z_]+:[0-9A-Fa-f]{4,16}\]|[^\]|])+)(?:\|((?:[^\]]|\](?!\]))+))?\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkPattern();

    // Tag: #tagname, or a faceted #key/value (#key:value) with any number of
    // segments. The (?=...) lookahead enforces Obsidian's rule that a tag must
    // contain at least one non-numeric character, so a literal "#2" renders as
    // plain text rather than a tag chip. Kept in step with
    // NotationParser.TagPattern (the capture-time extractor) -- character class
    // AND separator handling. Before facets, this pattern stopped at the first
    // '/', so "#aspect/light" rendered as a "#aspect" tag followed by a plain
    // "/light" text run and navigated by the key alone.
    [GeneratedRegex(@"#(?=[a-zA-Z0-9_/:-]*[a-zA-Z])([a-zA-Z0-9_-]+(?:[/:][a-zA-Z0-9_-]+)*)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    // Project: +route, including nested paths (+agents/claude-code,
    // +domains/ai-llm/architecture). Requires whitespace/SOL before the +, and an
    // alpha start on every segment.
    //
    // This is the DISPLAY half of the rule whose canonical form is
    // NotationParser.ProjectPattern; the two must stay identical. They had
    // drifted: this pattern lacked nested segments and carried a two-character
    // floor the canonical one deliberately does not have. The effect was routes
    // that routed correctly but rendered wrong - "+agents/claude-code"
    // highlighted only its first segment, and a one-character route like "+X"
    // did not highlight at all. As NotationParser records, false positives
    // (1+2, beans+meat) are prevented by the lookbehind, not by a length floor.
    [GeneratedRegex(@"(?<=\s|^)\+([a-zA-Z][a-zA-Z0-9_-]*(?:/[a-zA-Z][a-zA-Z0-9_-]*)*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ProjectPattern();

    // Quick check for page links (used for hasPageLink without full parsing)
    [GeneratedRegex(@"\[\[[^\]]*pages/[^\]]+\]\]", RegexOptions.Compiled)]
    private static partial Regex PageLinkQuickPattern();

    // Image: ![alt](path) -- markdown image syntax
    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImagePattern();

    // Shroud token: [TYPE:ID] where TYPE is uppercase letters/underscores and ID is 4-16 hex chars.
    // Single-bracket outer delimiters -- does NOT collide with wikilinks ([[...]]).
    [GeneratedRegex(@"\[([A-Z_]+):([0-9A-Fa-f]{4,16})\]", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    // URL: allowlisted schemes only. Trailing sentence punctuation is trimmed in code.
    [GeneratedRegex(@"(?:https?://|obsidian://|mailto:)[^\s<>""\]]+", RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    // Local path: quoted "C:\a b" / angle <C:\a b> (capture spaces), else bare
    // C:\path or file:///C:/path terminated by whitespace or forbidden filename chars.
    // The bare branch carries a token-boundary lookbehind so it cannot start mid-word
    // (e.g. the "l:" inside "evil://payload") - unknown-scheme URIs must stay plain
    // text, since custom URI handlers are an execution vector. The quoted/angle
    // branches are already delimited by their wrapper chars and need no lookbehind.
    [GeneratedRegex(@"""(?<q>[A-Za-z]:[\\/][^""]+)""|<(?<a>[A-Za-z]:[\\/][^>]+)>|(?<b>(?<![A-Za-z0-9_])(?:file:///)?[A-Za-z]:[\\/][^\s<>|""*?]+)", RegexOptions.Compiled)]
    private static partial Regex LocalPathPattern();

    /// <summary>
    /// Parse content into a list of segments.
    /// Segments are returned in order of appearance in the content.
    /// </summary>
    public static List<ContentSegment> Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        // Collect all matches with their positions
        var matches = new List<(int start, int length, ContentSegment segment)>();

        // Find wiki links
        foreach (Match match in WikiLinkPattern().Matches(content))
        {
            var path = match.Groups[1].Value;
            var displayText = match.Groups[2].Success ? match.Groups[2].Value : null;
            var isPageLink = path.Contains("pages/", StringComparison.OrdinalIgnoreCase);

            matches.Add((
                match.Index,
                match.Length,
                new WikiLinkSegment(match.Index, match.Length, match.Value, path, displayText, isPageLink)));
        }

        // Find tags
        foreach (Match match in TagPattern().Matches(content))
        {
            // Skip if inside a wiki link
            if (IsInsideMatch(match.Index, matches))
                continue;

            var tag = match.Groups[1].Value;
            matches.Add((
                match.Index,
                match.Length,
                new TagSegment(match.Index, match.Length, match.Value, tag)));
        }

        // Find projects
        foreach (Match match in ProjectPattern().Matches(content))
        {
            // Skip if inside a wiki link
            if (IsInsideMatch(match.Index, matches))
                continue;

            var project = match.Groups[1].Value;
            matches.Add((
                match.Index,
                match.Length,
                new ProjectSegment(match.Index, match.Length, match.Value, project)));
        }

        // Find images
        foreach (Match match in ImagePattern().Matches(content))
        {
            // Skip if inside a wiki link or overlapping another match
            if (IsInsideMatch(match.Index, matches))
                continue;

            var altText = match.Groups[1].Value;
            var path = match.Groups[2].Value;
            matches.Add((
                match.Index,
                match.Length,
                new ImageSegment(match.Index, match.Length, match.Value, altText, path)));
        }

        // Find URLs (skip if inside wikilink/image)
        foreach (Match match in UrlPattern().Matches(content))
        {
            if (IsInsideMatch(match.Index, matches)) continue;
            var url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
            if (url.Length == 0) continue;
            // A URL's fragment/query can contain characters that TagPattern or
            // ProjectPattern already matched earlier as a standalone tag/route
            // (e.g. the "#sec.tion" fragment of https://x.com/a#sec.tion looks
            // like "#sec" to TagPattern). Those earlier matches are wrong once
            // the URL is known to span them - evict before adding, or assembly
            // below emits a spurious chip plus duplicate/rewound text.
            RemoveContainedMatches(matches, match.Index, url.Length);
            matches.Add((match.Index, url.Length,
                new UrlSegment(match.Index, url.Length, url, url)));
        }

        // Find local paths (skip if inside wikilink/image/url)
        foreach (Match match in LocalPathPattern().Matches(content))
        {
            if (IsInsideMatch(match.Index, matches)) continue;
            var g = match.Groups["q"].Success ? match.Groups["q"]
                  : match.Groups["a"].Success ? match.Groups["a"]
                  : match.Groups["b"];
            var localPath = match.Groups["b"].Success
                ? g.Value.TrimEnd('.', ',', ';', ':')
                : g.Value;
            var rawLength = match.Groups["b"].Success
                ? localPath.Length
                : match.Length; // quoted/angle: raw includes the wrappers
            RemoveContainedMatches(matches, match.Index, rawLength);
            matches.Add((match.Index, rawLength,
                new PathSegment(match.Index, rawLength, content.Substring(match.Index, rawLength), localPath)));
        }

        // Find shroud tokens: [TYPE:ID] -- must come after wikilinks so IsInsideMatch skips overlap
        foreach (Match match in TokenPattern().Matches(content))
        {
            // Skip if overlapping an already-matched segment (e.g., part of a wikilink)
            if (IsInsideMatch(match.Index, matches))
                continue;

            var entityType = match.Groups[1].Value;
            var tokenId = match.Groups[2].Value;
            matches.Add((
                match.Index,
                match.Length,
                new TokenSegment(match.Index, match.Length, match.Value, entityType, tokenId)));
        }

        // Sort by position
        matches.Sort((a, b) => a.start.CompareTo(b.start));

        // Build segment list with text segments filling gaps
        var segments = new List<ContentSegment>();
        var currentPos = 0;

        foreach (var (start, length, segment) in matches)
        {
            // Add text segment for gap before this match
            if (start > currentPos)
            {
                var text = content.Substring(currentPos, start - currentPos);
                segments.Add(new TextSegment(currentPos, text.Length, text));
            }

            segments.Add(segment);
            currentPos = start + length;
        }

        // Add trailing text
        if (currentPos < content.Length)
        {
            var text = content.Substring(currentPos);
            segments.Add(new TextSegment(currentPos, text.Length, text));
        }

        return segments;
    }

    /// <summary>
    /// Quick check if content contains any page links (links to pages/ subpages).
    /// </summary>
    public static bool HasPageLinks(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        return PageLinkQuickPattern().IsMatch(content);
    }

    /// <summary>
    /// Get the path of the first page link in content.
    /// Returns null if no page link found.
    /// </summary>
    public static string? GetFirstPageLinkPath(string content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        foreach (Match match in WikiLinkPattern().Matches(content))
        {
            var path = match.Groups[1].Value;
            if (path.Contains("pages/", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Get all wiki link segments from content.
    /// </summary>
    public static List<WikiLinkSegment> GetWikiLinks(string content)
    {
        return Parse(content)
            .OfType<WikiLinkSegment>()
            .ToList();
    }

    /// <summary>
    /// Get all page link segments from content.
    /// </summary>
    public static List<WikiLinkSegment> GetPageLinks(string content)
    {
        return Parse(content)
            .OfType<WikiLinkSegment>()
            .Where(s => s.IsPageLink)
            .ToList();
    }

    /// <summary>
    /// Replaces every [[wiki link]] with the text a reader should see: the
    /// alias when the link has one, otherwise the target's display name.
    /// Everything else in the string is left untouched.
    ///
    /// This is for surfaces that show prose as plain text - list previews,
    /// confirmation modals, command output - where a clickable link is either
    /// impossible (no navigation host) or wrong (a modal, the trash). Surfaces
    /// that can navigate render through RichContentBehavior instead and get
    /// real hyperlinks; the two paths agree on the display text because both
    /// resolve it here.
    ///
    /// Flatten BEFORE capping a preview. Capping first can slice a link in
    /// half, and the leftover "[[logmark/proj" fragment matches nothing and
    /// survives into the UI as raw brackets.
    /// </summary>
    public static string FlattenWikiLinks(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains("[["))
            return content;

        return WikiLinkPattern().Replace(content, m =>
            m.Groups[2].Success ? m.Groups[2].Value : GetLinkDisplayName(m.Groups[1].Value));
    }

    /// <summary>
    /// The reader-facing name for an alias-less wiki link target: the file name
    /// without extension, without the "#^id" anchor a canonical entry reference
    /// carries, and without a leading ISO date prefix (2026-01-15-title ->
    /// title). Shared with the WPF hyperlink renderer so a link reads the same
    /// whether or not the surface can navigate.
    /// </summary>
    public static string GetLinkDisplayName(string path)
    {
        var hashIndex = path.IndexOf('#');
        if (hashIndex >= 0) path = path[..hashIndex];

        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        if (fileName.Length > 11 && fileName[4] == '-' && fileName[7] == '-' && fileName[10] == '-')
            fileName = fileName[11..];

        return fileName;
    }

    /// <summary>
    /// Truncates text to maxLength without breaking wiki links.
    /// If truncation point falls inside [[...]], truncates before the opening [[.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        var truncateAt = maxLength;

        // Check if we're inside a wiki link at the truncation point
        var lastOpen = text.LastIndexOf("[[", truncateAt);
        var lastClose = text.LastIndexOf("]]", truncateAt);

        if (lastOpen >= 0 && lastOpen > lastClose)
        {
            // Truncation falls inside [[ ]], cut before the opening [[
            truncateAt = lastOpen;
        }

        return text[..truncateAt].TrimEnd() + "...";
    }

    private static bool IsInsideMatch(int position, List<(int start, int length, ContentSegment segment)> matches)
    {
        foreach (var (start, length, _) in matches)
        {
            if (position >= start && position < start + length)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Evicts any already-collected match whose StartIndex falls within
    /// [start, start+length) - the span of a new Url/Path match. Called
    /// before adding a Url or Path match so an earlier, narrower match (tag,
    /// project) that turned out to be a substring of the wider URL/path does
    /// not survive alongside it. Eviction, not loop reordering, is the fix:
    /// reordering would let URLs eat legitimate tags that merely FOLLOW a
    /// URL rather than sit inside one.
    /// </summary>
    private static void RemoveContainedMatches(List<(int start, int length, ContentSegment segment)> matches, int start, int length)
    {
        matches.RemoveAll(m => m.start >= start && m.start < start + length);
    }
}
