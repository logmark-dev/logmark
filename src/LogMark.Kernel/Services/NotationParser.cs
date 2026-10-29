using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Parses notation from entry content.
/// Extracts: entry types, tags, projects.
/// </summary>
public partial class NotationParser
{
    private readonly TemporalParser _temporalParser;

    // Entry type prefixes - must be at start of entry or line
    // Short aliases map to canonical forms for faster capture
    private static readonly Dictionary<string, EntryType> TypePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["task:"] = EntryType.Task,
        ["idea:"] = EntryType.Idea,
        ["block:"] = EntryType.Block,
        ["decision:"] = EntryType.Decision,
        ["timetrack:"] = EntryType.Timetrack,
        ["question:"] = EntryType.Question,
        ["gap:"] = EntryType.Gap,
        ["analogy:"] = EntryType.Analogy,
        ["t:"] = EntryType.Task,
        ["i:"] = EntryType.Idea,
        ["b:"] = EntryType.Block,
        ["d:"] = EntryType.Decision,
        ["tt:"] = EntryType.Timetrack,
        ["q:"] = EntryType.Question,
        ["g:"] = EntryType.Gap,
        ["a:"] = EntryType.Analogy
    };

    // Tags may be flat (#tag) or hierarchical (#key/value, #a/b/c). ':' is accepted
    // as a separator (see LeafTag) and the tag is collapsed to its final segment at
    // extraction, so #surface/mcp, #hardware:4090, and #mcp all resolve to the same
    // leaf. Each separator must be followed by a segment, so trailing/double separators
    // degrade gracefully rather than producing a malformed tag.
    // The leading (?=...) lookahead enforces Obsidian's rule: a tag must contain at
    // least one non-numeric character. Without it, a literal reference like "#2" or
    // "backlog item #1234" parses as the tag "2"/"1234" and pollutes the tag index.
    // Digit-leading tags that also carry a letter (#2fa, #4090ti) still match.
    [GeneratedRegex(@"#(?=[a-zA-Z0-9_/:-]*[a-zA-Z])([a-zA-Z0-9_-]+(?:[/:][a-zA-Z0-9_-]+)*)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    // Obsidian wiki-links: [[target]], [[target|alias]], [[target#heading]],
    // [[target#^block]]. The path/heading/block-id inside a [[...]] span is link
    // syntax, NOT notation -- its leading '#' (heading/block fragment) and any '+'
    // must never be parsed as a #tag or +project. Back-links written by the
    // synthesis/feed/rollup pipeline embed block-id fragments like
    // [[_tasks/2026#b5568360]]; without this strip, "b5568360" leaks into the tag
    // index and pollutes co-occurrence and cluster detection. We remove the whole
    // span before entity extraction. Non-greedy, single-line so it never swallows
    // across a line break.
    [GeneratedRegex(@"\[\[[^\]\r\n]*\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkPattern();

    // Remove wiki-link spans before #tag / +project extraction. Replaces each span
    // with a single space so adjacent real notation keeps its boundary (the
    // ProjectPattern lookbehind requires whitespace before '+').
    private static string StripWikiLinks(string text)
        => string.IsNullOrEmpty(text) ? text : WikiLinkPattern().Replace(text, " ");

    /// <summary>
    /// Canonical tag-leaf rule. Normalizes ':' -> '/' then returns the final segment
    /// after the last '/'. Leading key/ segments are categorical prefixes that LogMark
    /// ignores, so #surface/mcp and #mcp resolve to the same leaf 'mcp'.
    /// This is the single source of truth reused by all tag consumers (TagService,
    /// SearchService, and ExtractTags itself).
    /// </summary>
    public static string LeafTag(string tag)
    {
        var norm = tag.Replace(':', '/');
        var slash = norm.LastIndexOf('/');
        return slash < 0 ? norm : norm[(slash + 1)..];
    }

    // Matches a wikilink span (group 1) OR a tag (group 2). Used to rewrite facet-tag
    // separators in free text while leaving wikilink internals untouched. The tag
    // alternative carries the same "at least one letter" lookahead as TagPattern so a
    // pure-numeric "#2:3" is left as literal text rather than rewritten to "#2/3".
    [GeneratedRegex(@"(\[\[[^\]\r\n]*\]\])|#(?=[a-zA-Z0-9_/:-]*[a-zA-Z])([a-zA-Z0-9_-]+(?:[/:][a-zA-Z0-9_-]+)*)", RegexOptions.Compiled)]
    private static partial Regex WikiLinkOrTagPattern();

    // Rewrite #key:value -> #key/value inside hashtag facets only, leaving wikilink
    // spans and prose colons (note:, re:) untouched. Used at the MCP write boundary so
    // new agent writes land Obsidian-portable on disk.
    public static string NormalizeFacetTagsInText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return WikiLinkOrTagPattern().Replace(text, m =>
            m.Groups[1].Success ? m.Value : "#" + m.Groups[2].Value.Replace(':', '/'));
    }

    // Supports two-level routing: +project or +project/scope
    // Requires whitespace/SOL before + (the lookbehind), alpha start. Single-char
    // base names are allowed (`+r`) so a route literally named `r` is referenceable;
    // false positives like `1+2` and `beans+meat` are prevented by the (?<=\s|^)
    // lookbehind, not by a length floor. The capture-time route-discovery popup keeps
    // its own 2-char floor (IsDiscoverableRouteToken) so typed `+flag` text
    // (chmod +x, rotate +z) never triggers a "create route?" modal.
    // Shared validation rule lives in IsValidProjectToken below; keep both in sync.
    [GeneratedRegex(@"(?<=\s|^)\+([a-zA-Z][a-zA-Z0-9_-]*(?:/[a-zA-Z][a-zA-Z0-9_-]*)*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ProjectPattern();

    // Canonical "is this a valid +project token?" check. Mirrors the body of
    // ProjectPattern, anchored for whole-token validation. Callers that only
    // have the token text (no surrounding context) should use this instead of
    // re-implementing the rule. Keep in sync with ProjectPattern: if the
    // shape of a valid project token changes, change both.
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_-]*(?:/[a-zA-Z][a-zA-Z0-9_-]*)*$", RegexOptions.Compiled)]
    private static partial Regex ProjectTokenRegex();

    // Match deadline after "by", "at", or "on" - stop at colon to avoid capturing URLs
    // Captures: "by friday", "at 2pm", "on jan 15", "by next week"
    // Stops at: ":", ".", ",", ";", "[", "(" to avoid URLs and other punctuation
    [GeneratedRegex(@"\b(?:by|at|on)\s+([\w\s,-]+?)(?=\s*:|\.|\s*$|,|;|\[|\()", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DeadlinePattern();

    public NotationParser()
    {
        _temporalParser = new TemporalParser();
    }

    public Entry Parse(string content, string? context = null)
    {
        var entry = new Entry
        {
            RawContent = content,
            Context = context
        };

        // Parse entry type from first line
        (entry.Type, entry.CustomTypePrefix) = ParseEntryType(content);

        // Create expanded content (replace shorthand with canonical forms)
        entry.ExpandedContent = ExpandNotation(content, entry.Type, entry.CustomTypePrefix);

        // Parse entity references. Both strip wikilink spans first (so block-id fragments
        // never become tags); ExtractTags additionally normalizes tag separators to '/'.
        entry.Tags = ExtractTags(content);
        entry.Projects = ExtractProjects(content);

        // Parse deadline (only for tasks)
        if (entry.Type == EntryType.Task)
        {
            entry.Deadline = ParseDeadline(content);
        }

        return entry;
    }

    private string ExpandNotation(string content, EntryType? type, string? customPrefix)
    {
        if (type == null)
            return content;

        // Map entry type to canonical prefix
        var canonicalPrefix = type switch
        {
            EntryType.Task => "task:",
            EntryType.Idea => "idea:",
            EntryType.Block => "block:",
            EntryType.Decision => "decision:",
            EntryType.Timetrack => "timetrack:",
            EntryType.Question => "question:",
            EntryType.Gap => "gap:",
            EntryType.Analogy => "analogy:",
            EntryType.Log => null, // No prefix for log entries
            _ => null
        };

        if (canonicalPrefix == null)
            return content;

        // Find which prefix was used in content (could be shorthand or canonical)
        var firstLine = content.Split('\n')[0];
        string? usedPrefix = null;
        int prefixIndex = -1;

        foreach (var (prefix, prefixType) in TypePrefixes)
        {
            if (prefixType == type)
            {
                var index = firstLine.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (index == 0 || char.IsWhiteSpace(firstLine[index - 1])))
                {
                    usedPrefix = prefix;
                    prefixIndex = index;
                    break;
                }
            }
        }

        // Replace shorthand with canonical if found
        if (usedPrefix != null && prefixIndex >= 0 && !usedPrefix.Equals(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var expanded = content.Remove(prefixIndex, usedPrefix.Length);
            expanded = expanded.Insert(prefixIndex, canonicalPrefix);
            return expanded;
        }

        return content;
    }

    private Deadline? ParseDeadline(string content)
    {
        // Strategy 1: Extract temporal expression after "by", "at", or "on" (most explicit)
        var match = DeadlinePattern().Match(content);
        if (match.Success)
        {
            var temporalText = match.Groups[1].Value.Trim();
            // requireFullSpan: the capture after by/at/on can run across sentence noise
            // (e.g. "EOD The manual LinkedIn page publish"). Only trust it if the whole
            // capture is temporal, otherwise fall through to Strategy 2/3. See DESK112.
            var deadline = _temporalParser.ParseDeadline(temporalText, requireFullSpan: true);
            if (deadline != null)
                return deadline;
        }

        // Strategy 2: Pass full content to MS Recognizers (handles multi-word expressions)
        // This allows MS Recognizers to extract temporal phrases from natural language
        // Examples: "ready in 4 days", "meeting in 2 hours", etc.
        var fullContentDeadline = _temporalParser.ParseDeadline(content);
        if (fullContentDeadline != null)
            return fullContentDeadline;

        // Strategy 3: Fallback to token-by-token parsing
        // Handles single-word temporal expressions: "tomorrow", "friday", "2pm", "EOD"
        var tokens = content.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Try single tokens first
        foreach (var token in tokens)
        {
            var deadline = _temporalParser.ParseDeadline(token);
            if (deadline != null)
                return deadline;
        }

        // Try two-word combinations (handles "2:15 pm" with space)
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var combined = tokens[i] + tokens[i + 1]; // Remove space
            var deadline = _temporalParser.ParseDeadline(combined);
            if (deadline != null)
                return deadline;
        }

        return null;
    }

    /// <summary>
    /// Parse deadline with LLM fallback for fuzzy/typo input.
    /// Single LLM call strategy: regex first (instant), then one LLM call with full content.
    /// </summary>
    public async Task<Deadline?> ParseDeadlineWithFallbackAsync(
        string content,
        LlmClient? llm,
        SystemLogger? logger,
        CancellationToken ct = default,
        TimeSpan? llmTimeout = null)
    {
        // Strategy 1: Extract temporal expression after "by", "at", or "on" (most explicit)
        var match = DeadlinePattern().Match(content);
        if (match.Success)
        {
            var temporalText = match.Groups[1].Value.Trim();
            // requireFullSpan: see ParseDeadline Strategy 1. Reject prose-laden captures
            // so the buried-fragment misparse falls through to Strategy 2/3. See DESK112.
            var deadline = _temporalParser.ParseDeadline(temporalText, requireFullSpan: true);
            if (deadline != null)
                return deadline;
        }

        // Strategy 2: Pass full content to MS Recognizers (handles multi-word expressions)
        var fullContentDeadline = _temporalParser.ParseDeadline(content);
        if (fullContentDeadline != null)
            return fullContentDeadline;

        // Strategy 3: Fallback to token-by-token parsing
        var tokens = content.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var deadline = _temporalParser.ParseDeadline(token);
            if (deadline != null)
                return deadline;
        }

        // Try two-word combinations
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var combined = tokens[i] + tokens[i + 1];
            var deadline = _temporalParser.ParseDeadline(combined);
            if (deadline != null)
                return deadline;
        }

        // Strategy 4: Single LLM call with full content (max 1 call, not per-token)
        if (llm == null)
            return null;

        return await ExtractDeadlineWithLlmAsync(content, llm, logger, ct, llmTimeout);
    }

    private const string LlmExtractPrompt =
        "Extract the deadline from the task. Output only ISO 8601 datetime (YYYY-MM-DDTHH:mm:ss), or 'none' if no deadline.";

    private async Task<Deadline?> ExtractDeadlineWithLlmAsync(
        string content,
        LlmClient llm,
        SystemLogger? logger,
        CancellationToken ct,
        TimeSpan? llmTimeout = null)
    {
        var now = DateTime.Now;
        var userPrompt = $"Current: {now:yyyy-MM-ddTHH:mm:ss} ({now:dddd})\n\nTask: {content}";

        // Default 3s; the capture path passes AppSettings.LlmTimeoutMs so a slow
        // local model can be given more headroom (lmk-e4yi).
        var response = await llm.ChatAsync(
            LlmExtractPrompt,
            userPrompt,
            timeout: llmTimeout ?? TimeSpan.FromSeconds(3),
            ct);

        Deadline? parsed = null;
        if (response.Response != null &&
            !response.Response.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(response.Response, out var dt))
        {
            // All deadlines have full datetime (00:00 if no time in LLM response)
            parsed = new Deadline
            {
                DateTime = dt,
                OriginalText = content
            };
        }

        logger?.Info(
            $"LLM deadline: \"{content}\" -> " +
            $"{parsed?.DateTime.ToString("yyyy-MM-dd HH:mm") ?? "none"} " +
            $"({response.ElapsedMs}ms{(response.TimedOut ? ", timeout" : "")})");

        return parsed;
    }

    /// <summary>
    /// Returns the entry type implied by a leading prefix on the first line,
    /// or null if no canonical prefix is present at the body start. This is
    /// the SINGLE SOURCE OF TRUTH for type detection: capture-panel preview
    /// (header chip), save path, and any other consumer must call this so
    /// the visible chip and the persisted entry agree.
    ///
    /// Anchored at the body start. Allows one optional leading +route token
    /// (e.g. "+project i: foo") since routes precede the type prefix in the
    /// notation. Trailing prefixes ("synthesize note ... t:") and inline
    /// references ("i: foo. Refs gap:abc") do NOT match -- previously such
    /// content misclassified, and a permissive preview parser convinced the
    /// user the entry would save with the prefix's type when in fact it
    /// landed as a default log.
    /// </summary>
    public static EntryType? TryGetLeadingEntryType(string content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var firstLine = content.Split('\n')[0];

        var pos = SkipWhitespace(firstLine, 0);
        pos = TryConsumeRouteToken(firstLine, pos);
        pos = SkipWhitespace(firstLine, pos);

        // At the body start: pick the longest matching canonical prefix so
        // "idea:" wins over "i:" when both could match at this position.
        EntryType? bestType = null;
        var bestLength = 0;
        foreach (var (prefix, type) in TypePrefixes)
        {
            if (firstLine.Length - pos >= prefix.Length &&
                string.Compare(firstLine, pos, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                prefix.Length > bestLength)
            {
                bestLength = prefix.Length;
                bestType = type;
            }
        }

        return bestType;
    }

    private (EntryType?, string?) ParseEntryType(string content)
    {
        var type = TryGetLeadingEntryType(content);
        return type is not null ? (type, null) : (EntryType.Log, null);
    }

    private static int SkipWhitespace(string s, int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
        return pos;
    }

    // Optionally consume a single "+route" or "+route/scope" token. Returns
    // the position after the token (and its trailing whitespace gate) on
    // success, or the original pos if the input does not start with a valid
    // route token followed by whitespace. Mirrors the project regex shape:
    // letter, then optional alnum/_-, optional /letter+ segments. A single-letter
    // base ("+r") IS a valid token (matches ProjectPattern); trailing whitespace
    // is still required so "+18Ti" (digit-first) does not consume.
    private static int TryConsumeRouteToken(string s, int pos)
    {
        if (pos >= s.Length || s[pos] != '+')
            return pos;

        var p = pos + 1;
        if (p >= s.Length || !char.IsLetter(s[p]))
            return pos;
        p++;

        // [a-zA-Z0-9_-]* for the rest of the base segment. Zero or more: the
        // leading letter alone is a valid single-char base ("+r"), matching
        // ProjectPattern. No length floor here.
        while (p < s.Length && (char.IsLetterOrDigit(s[p]) || s[p] == '_' || s[p] == '-'))
        {
            p++;
        }

        // Optional /scope segments: /[a-zA-Z][a-zA-Z0-9_-]*
        while (p < s.Length && s[p] == '/' && p + 1 < s.Length && char.IsLetter(s[p + 1]))
        {
            p += 2;
            while (p < s.Length && (char.IsLetterOrDigit(s[p]) || s[p] == '_' || s[p] == '-'))
                p++;
        }

        // Must be terminated by whitespace (or end of line) for this to be a route token.
        if (p < s.Length && !char.IsWhiteSpace(s[p]))
            return pos;

        return p;
    }

    private static List<string> ParseMatches(Regex pattern, string content)
    {
        return pattern.Matches(content)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extract all #tag references from text, deduplicated case-insensitively.
    /// Each tag is reduced to its leaf: the final segment after the last '/' (or ':',
    /// which normalizes to '/'). So #surface/mcp, #hardware:4090, and flat #mcp all
    /// collapse to a single leaf token ('mcp', '4090', 'mcp'). The full slash-path is
    /// kept on disk; only the read index collapses.
    /// </summary>
    public static List<string> ExtractTags(string text)
    {
        return TagPattern().Matches(StripWikiLinks(text))
            .Select(m => LeafTag(m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extract faceted tags: any #tag containing a separator ('/' or ':',
    /// normalized to '/'). Key = first segment, Value = remainder. Flat tags
    /// yield no facet. Deduplicated case-insensitively by FullPath.
    /// </summary>
    public static IReadOnlyList<TagFacet> ExtractFacets(string text)
    {
        var result = new List<TagFacet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TagPattern().Matches(StripWikiLinks(text)))
        {
            var full = m.Groups[1].Value.Replace(':', '/');
            var idx = full.IndexOf('/');
            if (idx <= 0 || idx == full.Length - 1) continue;
            if (!seen.Add(full)) continue;
            result.Add(new TagFacet(full[..idx], full[(idx + 1)..], full));
        }
        return result;
    }

    /// <summary>
    /// Extract full tag paths without leaf collapse (':' normalized to '/').
    /// Used by view source selectors, where #aspect/light must stay a path
    /// filter rather than collapsing to 'light'.
    /// </summary>
    public static IReadOnlyList<string> ExtractTagPaths(string text)
    {
        return TagPattern().Matches(StripWikiLinks(text))
            .Select(m => m.Groups[1].Value.Replace(':', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Filters a set of leaf tags (typically <see cref="ExtractTags"/> output)
    /// down to the subset eligible as CO-OCCURRENCE / CLUSTERING evidence for
    /// one entry. Facets (#key/value) encode DECLARED structure; co-occurrence
    /// and cluster detection are EMERGENT groupings, so a leaf whose only
    /// source on this entry is a facet must not manufacture evidence and is
    /// removed. A leaf that ALSO has a genuine flat occurrence on the same
    /// entry (both #mcp and #surface/mcp present) is kept -- the flat tag is
    /// independent evidence, unaffected by a facet sharing its leaf.
    ///
    /// Scoped strictly to <paramref name="content"/>: a flat tag on a
    /// DIFFERENT entry that happens to share a leaf with a facet here is
    /// untouched, because this method only ever sees one entry's content.
    /// Callers (TagService.GetCoOccurringTagsAsync, ClusterDetectorSource)
    /// must invoke this per entry, never on a vault-wide aggregate.
    /// </summary>
    public static IReadOnlyList<string> ExcludeFacetOnlyLeaves(IEnumerable<string> leafTags, string content)
    {
        var leafList = leafTags as IReadOnlyList<string> ?? leafTags.ToList();

        var facets = ExtractFacets(content);
        if (facets.Count == 0)
            return leafList;

        var facetLeaves = facets
            .Select(f => LeafTag(f.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var flatLeaves = ExtractTagPaths(content)
            .Where(p => !p.Contains('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return leafList
            .Where(leaf => !facetLeaves.Contains(leaf) || flatLeaves.Contains(leaf))
            .ToList();
    }

    /// <summary>
    /// Extract all #tag references from text without case-insensitive deduplication.
    /// Each distinct spelling (e.g. #ml, #ML, #Ml) is returned as a separate entry.
    /// Tags are collapsed to their leaf (see LeafTag).
    /// Duplicate occurrences of the exact same spelling within the text are still deduplicated.
    /// </summary>
    public static List<string> ExtractTagsPreservingCase(string text)
    {
        return TagPattern().Matches(StripWikiLinks(text))
            .Select(m => LeafTag(m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Extract all +project references from text.
    /// </summary>
    public static List<string> ExtractProjects(string text)
    {
        return ParseMatches(ProjectPattern(), StripWikiLinks(text));
    }

    /// <summary>
    /// True if <paramref name="token"/> (without leading +) matches the
    /// canonical project-token shape: letter-first, one or more chars, optional
    /// /scope segments. False for digit-leading, empty, or punctuation-containing
    /// inputs. Single-letter tokens ARE valid (`r`) so an explicitly-defined
    /// route named `r` resolves via +r. Use this anywhere ProjectPattern can't
    /// run because the surrounding context (whitespace boundary) is absent.
    /// For the capture-time "create route?" discovery popup, use
    /// <see cref="IsDiscoverableRouteToken"/> instead (keeps a 2-char floor so
    /// typed +flag text never offers to create a route).
    /// </summary>
    public static bool IsValidProjectToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        return ProjectTokenRegex().IsMatch(token);
    }

    /// <summary>
    /// True if <paramref name="token"/> is eligible to TRIGGER the unknown-route
    /// discovery popup. Stricter than <see cref="IsValidProjectToken"/>: the base
    /// segment (before any /scope) must be at least 2 chars. Deliberate decoupling
    /// -- resolution accepts 1-char names (a route you explicitly defined), but the
    /// "did you mean to create a route?" affordance stays conservative so common
    /// typed notation like `chmod +x`, `rotate +z`, or `+w` does not pop a modal.
    /// Single-char unknown tokens are simply ignored by discovery; you create a
    /// 1-char route via the editor instead.
    /// </summary>
    public static bool IsDiscoverableRouteToken(string token)
    {
        if (!IsValidProjectToken(token)) return false;
        var slash = token.IndexOf('/');
        var baseLen = slash < 0 ? token.Length : slash;
        return baseLen >= 2;
    }

    // Per-segment character class TagPattern enforces ([a-zA-Z0-9_-]+),
    // anchored for whole-value validation. Keep in sync with TagPattern.
    // \A...\z (not ^...$): .NET's $ also matches immediately before a
    // trailing \n, which would let "todo\n" pass here while TagPattern
    // (which cannot match a newline inside a tag) would reject it.
    [GeneratedRegex(@"\A[a-zA-Z0-9_-]+\z", RegexOptions.Compiled)]
    private static partial Regex FacetValueRegex();

    /// <summary>
    /// True if <paramref name="value"/> is a legal facet value: it can be
    /// written as <c>#key/value</c> and read back by <see cref="ExtractFacets"/>
    /// as the exact same value, with no extra segment, no flat-tag fallback,
    /// and no drift. Anything containing a space, '#', ':', '/', or other
    /// punctuation outside the tag character class fails this check.
    /// Used to gate declared board columns (view <c>columns:</c>) before they
    /// are seeded as Move targets: a value that is not a legal facet value
    /// cannot round-trip through <c>SwapFacetTag</c>/<c>AppendFacetTag</c>
    /// without corrupting the entry (see ViewDefinitionParser,
    /// ViewSectionGrouper).
    /// </summary>
    public static bool IsValidFacetValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return FacetValueRegex().IsMatch(value);
    }

    /// <summary>
    /// Strip all #tag references from text.
    /// </summary>
    public static string StripTags(string text)
    {
        return TagPattern().Replace(text, "").Trim();
    }

    /// <summary>
    /// Strip all +project references from text.
    /// </summary>
    public static string StripProjects(string text)
    {
        return ProjectPattern().Replace(text, "").Trim();
    }

    /// <summary>
    /// Strip +project references except those in <paramref name="proseTokens"/>
    /// (case-insensitive), which are prose - a quoted "+Security" field name,
    /// "+USD 5,999" - and stay in the text verbatim.
    /// </summary>
    public static string StripProjects(string text, IReadOnlyCollection<string> proseTokens)
    {
        if (proseTokens.Count == 0)
            return StripProjects(text);

        return ProjectPattern().Replace(text, m =>
            proseTokens.Contains(m.Groups[1].Value, StringComparer.OrdinalIgnoreCase) ? m.Value : "").Trim();
    }

    /// <summary>
    /// Checks if the input is a command (starts with /)
    /// </summary>
    public static bool IsCommand(string input)
    {
        return input.TrimStart().StartsWith('/');
    }
}
