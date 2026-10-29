using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services.EntryCache;

/// <summary>
/// Projects IndexedEntry/ParsedEntry into a unified ResolvedEntry model.
/// Centralizes ID generation, path normalization, tag extraction, link resolution,
/// and backlink fetching that was previously duplicated across MCP tools and App views.
/// </summary>
public partial class EntryResolver
{
    private readonly EntryCache _cache;
    private readonly string _vaultPath;

    public EntryResolver(EntryCache cache, string vaultPath)
    {
        _cache = cache;
        _vaultPath = vaultPath;
    }

    /// <summary>
    /// Resolve an IndexedEntry into a ResolvedEntry.
    /// </summary>
    public ResolvedEntry Resolve(IndexedEntry ie, ResolveOptions? options = null)
    {
        options ??= ResolveOptions.Query;
        var entry = ie.Entry;
        var relativePath = GetRelativePath(ie.FilePath, _vaultPath);
        var tags = NotationParser.ExtractTags(entry.Content);
        var entryId = GetEntryId(entry);
        var now = DateTime.Now;

        var resolved = new ResolvedEntry
        {
            EntryId = entryId,
            Timestamp = entry.Timestamp,
            EntryType = entry.Type.ToString().ToLowerInvariant(),
            Content = options.ContentMaxLength.HasValue
                ? TruncateContent(entry.Content, options.ContentMaxLength.Value)
                : entry.Content,
            ContentPreview = options.ContentPreviewLength.HasValue
                ? TruncateContent(entry.Content, options.ContentPreviewLength.Value)
                : null,
            Path = relativePath,
            Route = ie.Route,
            Scope = ie.Scope,
            StartLine = entry.StartLine,
            EndLine = entry.EndLine,
            Tags = tags,
            IsCompleted = entry.IsCompleted,
            Context = entry.Context,
            Source = entry.Source,
            Session = entry.Session,
            Evidence = entry.Evidence,
            ShortId = entry.ShortId,
            // Null, not false, when the entry is fine: the field is a warning,
            // not a status, and only earns payload space when it is true.
            Unaddressable = string.IsNullOrEmpty(entry.ShortId) ? true : null,

            // Task-specific
            Deadline = entry.TaskMeta?.Deadline?.ToString("O"),
            PostponedCount = entry.TaskMeta != null ? entry.TaskMeta.PostponedCount : null,
            // Tasks read the note from TaskMeta (unchanged); blocks have no TaskMeta,
            // so they read the same "resolved: ..." trailer via ParsedEntry.Resolution
            // (see ParsedEntry.Resolution doc comment).
            Resolution = entry.Type == EntryType.Block ? entry.Resolution : entry.TaskMeta?.Resolution,

            // Block-specific
            AgeDays = entry.Type == EntryType.Block
                ? (int)(now - entry.Timestamp).TotalDays
                : null,
            IsResolved = entry.Type == EntryType.Block
                ? entry.IsCompleted || tags.Contains("resolved", StringComparer.OrdinalIgnoreCase)
                : null,
            // The "resolved" branch is scoped to blocks for the same reason
            // SearchService.PassesFilters scopes LifecycleFilter.Open's
            // #resolved closure to blocks: on a task or a decision, #resolved
            // is not a closure tag. Unscoped, this field reported "resolved"
            // on an entry every reader still lists as open - a projection
            // contradicting the predicate is the defect shape lmk-nnd5 filed.
            // The two must be changed together if this ever widens.
            Lifecycle = entry.IsCompleted ? "completed"
                : tags.Contains("dismissed", StringComparer.OrdinalIgnoreCase) ? "dismissed"
                : entry.Type == EntryType.Block
                  && tags.Contains("resolved", StringComparer.OrdinalIgnoreCase) ? "resolved"
                : "open",

            // Relationships
            Links = options.IncludeLinks ? ResolveLinks(entry, ie.FilePath) : [],
            Backlinks = options.IncludeBacklinks ? ResolveBacklinks(entry, ie.FilePath) : []
        };

        return resolved;
    }

    /// <summary>
    /// Resolve multiple IndexedEntries.
    /// </summary>
    public List<ResolvedEntry> ResolveMany(IEnumerable<IndexedEntry> entries, ResolveOptions? options = null)
    {
        return entries.Select(ie => Resolve(ie, options)).ToList();
    }

    /// <summary>
    /// Convenience overload for callers that have a ParsedEntry + filePath (ReadEntryTool, detail panel).
    /// Constructs route/scope from the path.
    /// </summary>
    public ResolvedEntry Resolve(ParsedEntry entry, string filePath, ResolveOptions? options = null)
    {
        // Same resolver as the cache, so read_entry reports the route search does.
        var location = _cache.LocatePath(filePath);
        var ie = new IndexedEntry(entry, filePath, location.Route, location.Scope);
        return Resolve(ie, options);
    }

    #region ID Generation

    /// <summary>
    /// Get the entry ID. Uses stored ShortId when available,
    /// falls back to hash-based ID for legacy entries.
    /// </summary>
    public static string GetEntryId(ParsedEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ShortId))
            return entry.ShortId;

        // A logmark.ink publish carries its id as "short_id: <8 hex>", which
        // AnchorMintSweep adopts as the anchor: serve it now, so the id an
        // agent learns before minting is the id minting writes.
        if (entry.ExtendedMetadata.TryGetValue("short_id", out var published)
            && PublishedShortId().IsMatch(published))
            return published;

        return GenerateHashId(entry.Timestamp, entry.Content);
    }

    [GeneratedRegex("^[a-f0-9]{8}$")]
    private static partial Regex PublishedShortId();

    /// <summary>
    /// Generate a hash-based entry ID from timestamp and content.
    /// </summary>
    public static string GenerateHashId(DateTime timestamp, string content)
    {
        var hashInput = $"{timestamp:O}{content}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hash[..4]).ToLowerInvariant();
    }

    #endregion

    #region Path Helpers

    /// <summary>
    /// Convert a file path to a vault-relative path starting from "logmark/...".
    ///
    /// When <paramref name="vaultPath"/> is supplied (and both paths are rooted),
    /// the result is anchored to the actual vault root via Path.GetRelativePath.
    /// Prefer passing it: the "/logmark/" fallback below greedily matches the FIRST
    /// occurrence, which is wrong when an ancestor directory is itself named
    /// "logmark"/"LogMark" (case-insensitive) - e.g. the dogfooding vault under
    /// .../LogMark/testData/vault/, where the repo's LogMark/ folder steals the
    /// match and leaves a "LogMark/testData/vault/" prefix baked into wikilink
    /// targets (broken Sources links in concept pages). A project route literally
    /// named "logmark" breaks the heuristic the same way. Only anchoring to the
    /// vault root is correct in those cases.
    /// </summary>
    public static string GetRelativePath(string filePath, string? vaultPath = null)
    {
        if (!string.IsNullOrEmpty(vaultPath)
            && Path.IsPathRooted(vaultPath) && Path.IsPathRooted(filePath))
        {
            try
            {
                var rel = Path.GetRelativePath(vaultPath, filePath);
                // Outside the vault: GetRelativePath returns ".." or a rooted path.
                if (!rel.StartsWith("..") && !Path.IsPathRooted(rel))
                    return rel.Replace('\\', '/');
            }
            catch
            {
                // Fall through to the heuristic.
            }
        }

        var normalized = filePath.Replace('\\', '/');
        var logmarkIndex = normalized.IndexOf("/logmark/", StringComparison.OrdinalIgnoreCase);
        if (logmarkIndex >= 0)
            return normalized[(logmarkIndex + 1)..];

        return Path.GetFileName(filePath);
    }

    /// <summary>
    /// Extract route name from a vault-relative path.
    /// </summary>
    public static string? GetRouteFromPath(string relativePath)
    {
        var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "logmark")
            return null;

        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith('_'))
            {
                if (i >= 2 && (parts[1] == "projects" || parts[1] == "domains" || parts[1] == "clients"))
                    return parts[2];
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Extract the FULL route folder an entry file lives in - every segment
    /// between "logmark/" and the entry-type folder ("_tasks", "_diary", ...).
    /// Returns null when the entry is unrouted (type folder directly under
    /// logmark/) or the path holds no entry-type folder at all.
    /// </summary>
    /// <remarks>
    /// This is the display/navigation counterpart to <see cref="GetRouteFromPath"/>,
    /// not a replacement for it. That method answers the search index's question -
    /// "which single route facet does this belong to" - and deliberately returns
    /// only one segment, and only under projects/domains/clients. This one answers
    /// "where does this entry actually live", so it keeps nested segments
    /// ("agents/claude-code", "domains/ai-llm/architecture") and treats "_inbox"
    /// as the route folder it is. Callers that render a route to the user or
    /// navigate to its files want this one.
    /// </remarks>
    public static string? GetEntryRouteFolder(string relativePath)
    {
        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "logmark", StringComparison.OrdinalIgnoreCase))
            return null;

        // Walk forward to the first entry-type folder; everything between
        // logmark/ and it is the route. EntryTypeMapper (not a bare '_' prefix
        // test) decides what counts as a type folder, so route folders that
        // legitimately start with an underscore - "_inbox" - are not mistaken
        // for one.
        for (var i = 1; i < parts.Length - 1; i++)
        {
            if (EntryTypeMapper.ResolveTypeName(parts[i]) is null) continue;
            return i == 1 ? null : string.Join('/', parts[1..i]);
        }

        return null;
    }

    /// <summary>
    /// Extract scope from a vault-relative path given a route.
    /// </summary>
    public static string? GetScopeFromPath(string relativePath, string? route)
    {
        if (route == null) return null;

        var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Skip "logmark" prefix in vault-relative paths to avoid matching route name
        // against the logmark root directory (e.g., route "logmark" matching parts[0])
        int startIndex = (parts.Length > 0 && parts[0].Equals("logmark", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        var routeIndex = Array.FindIndex(parts, startIndex, parts.Length - startIndex,
            p => p.Equals(route, StringComparison.OrdinalIgnoreCase));

        if (routeIndex >= 0 && routeIndex + 1 < parts.Length)
        {
            var nextPart = parts[routeIndex + 1];
            if (!nextPart.StartsWith('_'))
                return nextPart;
        }
        return null;
    }

    #endregion

    #region Content Helpers

    /// <summary>
    /// Truncate content to maxLength, appending "..." if truncated.
    /// </summary>
    public static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content))
            return "";
        if (content.Length <= maxLength)
            return content;
        return content[..maxLength] + "...";
    }

    #endregion

    #region Link Resolution

    private List<ResolvedLink> ResolveLinks(ParsedEntry entry, string filePath)
    {
        var links = new List<ResolvedLink>();

        // Wiki links from content, resolved into light file objects so agents
        // can follow them (viewer click parity). Only runs under Full resolve
        // options - list/search surfaces skip link resolution entirely.
        var wikiLinks = ContentParser.GetWikiLinks(entry.Content);
        foreach (var link in wikiLinks)
        {
            links.Add(LinkTargetResolver.ResolveToLink(link, filePath, _vaultPath, _cache.FileAccessor));
        }

        // Metadata refs
        foreach (var entryRef in entry.Refs)
        {
            string label = $"{entryRef.Type}:{entryRef.Id}";
            bool isResolved = false;

            var resolved = _cache.ResolveShortId(entryRef.Id);
            if (resolved != null)
            {
                label = BacklinkIndex.GetContentPreview(resolved.Value.entry.Content);
                isResolved = true;
            }

            links.Add(new ResolvedLink
            {
                Label = label,
                LinkType = entryRef.Type,
                TargetShortId = entryRef.Id,
                IsResolved = isResolved
            });
        }

        return links;
    }

    /// <summary>
    /// Entries whose wiki links point at <paramref name="filePath"/> - the
    /// backlinks of a page, which has no entries of its own to resolve
    /// (bead lmk-gr8f). Newest first, same shape as an entry's Backlinks.
    /// </summary>
    public List<ResolvedBacklink> ResolveFileBacklinks(string filePath) =>
        _cache.GetBacklinks(filePath)
            .OrderByDescending(b => b.Timestamp)
            .Select(bl => new ResolvedBacklink
            {
                SourcePath = GetRelativePath(bl.SourceFilePath, _vaultPath),
                Timestamp = bl.Timestamp,
                EntryType = bl.Type.ToString().ToLowerInvariant(),
                ContentPreview = bl.ContentPreview,
                EntryId = string.IsNullOrEmpty(bl.ShortId) ? null : bl.ShortId
            })
            .ToList();

    private List<ResolvedBacklink> ResolveBacklinks(ParsedEntry entry, string filePath)
    {
        var backlinks = new List<BacklinkEntry>();

        // ShortId backlinks (from refs: metadata)
        if (!string.IsNullOrEmpty(entry.ShortId))
        {
            backlinks.AddRange(_cache.GetBacklinksForEntry(entry.ShortId));
        }

        // File-level backlinks (from wiki links)
        var fileBacklinks = _cache.GetBacklinks(filePath);
        foreach (var bl in fileBacklinks)
        {
            if (!backlinks.Any(b => b.SourceFilePath == bl.SourceFilePath && b.Timestamp == bl.Timestamp))
                backlinks.Add(bl);
        }

        // Sort newest first, project to ResolvedBacklink
        return backlinks
            .OrderByDescending(b => b.Timestamp)
            .Select(bl => new ResolvedBacklink
            {
                SourcePath = GetRelativePath(bl.SourceFilePath, _vaultPath),
                Timestamp = bl.Timestamp,
                EntryType = bl.Type.ToString().ToLowerInvariant(),
                ContentPreview = bl.ContentPreview,
                EntryId = string.IsNullOrEmpty(bl.ShortId) ? null : bl.ShortId
            })
            .ToList();
    }

    #endregion
}
