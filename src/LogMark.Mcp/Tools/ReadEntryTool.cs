using System.Text.Json;
using System.Text.RegularExpressions;
using LogMark.Mcp.Protocol;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;

namespace LogMark.Mcp.Tools;

/// <summary>
/// MCP tool to read an entry by ID, by path, or both.
/// </summary>
public class ReadEntryTool : ITool
{
    private static readonly Regex BareIdPattern = new(@"^[0-9a-fA-F]{8}$", RegexOptions.Compiled);

    private readonly VaultServices _services;

    public ReadEntryTool(VaultServices services)
    {
        _services = services;
    }

    public string Name => "read_entry";

    public ToolDefinition Definition => new()
    {
        Name = "read_entry",
        Description = "Get full entry content by entry_id, by vault-relative path, or both. entry_id alone finds the entry anywhere in the vault - use it for ids from search/list results, whose route does not name the file. A path selects a file; with entry_id too, the entry is read from its real file even when the path is stale. Follows wikilink targets directly: the .md extension is optional and a trailing #shortid anchor selects that entry. Files without timestamped entries (pages/ subpages, concept pages) return raw page content plus that page's own resolved links, so references can be followed link by link. Find this tool when: full detail of one note by its id, follow a reference, read a linked subpage.",
        InputSchema = new JsonSchema
        {
            Type = "object",
            Properties = new Dictionary<string, JsonSchemaProperty>
            {
                ["path"] = new()
                {
                    Type = "string",
                    Description = "Optional vault-relative path to the markdown file (e.g., 'logmark/projects/logmark/_decisions/2025.md'). Omit it when you have an entry_id. Wikilink target form works too: extension optional, trailing #shortid anchor selects that entry."
                },
                ["entry_id"] = new()
                {
                    Type = "string",
                    Description = "8-character entry ID. Enough on its own: the entry is looked up across the vault. Required unless path is given."
                },
                ["timestamp"] = new()
                {
                    Type = "string",
                    Description = "Optional ISO 8601 timestamp to find entry by time (alternative to entry_id)."
                },
                ["max_content_length"] = new()
                {
                    Type = "integer",
                    Description = "Optional maximum content length in characters. Content exceeding this limit is truncated with a marker. Omit for full content."
                }
            },
            Required = new List<string>()
        },
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false,
        },
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
    {
        var path = McpToolHelpers.GetStringArg(arguments, "path");
        var entryId = McpToolHelpers.GetStringArg(arguments, "entry_id");
        var timestampStr = McpToolHelpers.GetStringArg(arguments, "timestamp");
        var maxContentLength = McpToolHelpers.GetIntArg(arguments, "max_content_length");

        // Wikilink anchor form: path#shortid selects an entry within the file.
        var hashIdx = path?.IndexOf('#') ?? -1;
        if (hashIdx >= 0)
        {
            var anchorTail = path![(hashIdx + 1)..].Trim();
            if (string.IsNullOrEmpty(entryId) && anchorTail.Length > 0)
                entryId = anchorTail;
            path = path[..hashIdx];
        }

        // A bare id in path (agents holding only an id reach for the field older
        // schemas required) names no file; read it as entry_id.
        if (string.IsNullOrEmpty(entryId) && path != null && BareIdPattern.IsMatch(path)
            && !File.Exists(Path.Combine(_services.VaultPath!, path))
            && !File.Exists(Path.Combine(_services.VaultPath!, path + ".md")))
        {
            entryId = path;
            path = null;
        }

        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(entryId))
        {
            return McpToolHelpers.ErrorResult("Missing required parameter: provide path, entry_id, or both");
        }

        // Set when the entry was found somewhere other than the caller's path.
        // List results carry an id and a route but no file, and a route does not
        // name the file (nested folders read back as their parent route), so a
        // caller-built path is a guess the id can correct.
        string? relocatedFrom = null;
        string fullPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            var located = await LocateEntryFileAsync(entryId!);
            if (located == null)
            {
                return await NotFoundByIdAsync(entryId!);
            }
            fullPath = located;
        }
        else
        {
            fullPath = Path.GetFullPath(
                Path.Combine(_services.VaultPath!, path.Replace('/', Path.DirectorySeparatorChar)));

            // MCP serves vault content only. Vault-external targets are surfaced
            // as link metadata but never read here: harness-level trust (Allow
            // dialogs) gates the agent's own file tools, and serving arbitrary
            // paths through MCP would tunnel under that boundary.
            if (!LinkTargetResolver.IsUnderRoot(fullPath, _services.VaultPath!))
            {
                var existsNote = File.Exists(fullPath)
                    ? "The target exists on disk - read it with your own file tools."
                    : "The target does not exist on disk.";
                return McpToolHelpers.ErrorResult($"Path is outside the vault: {path}. {existsNote}");
            }

            // Wikilink targets omit the .md extension - fall back to the .md
            // sibling when the literal path is absent.
            if (!File.Exists(fullPath)
                && !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath + ".md"))
            {
                fullPath += ".md";
            }

            if (!File.Exists(fullPath))
            {
                var located = string.IsNullOrEmpty(entryId) ? null : await LocateEntryFileAsync(entryId);
                if (located == null)
                {
                    return McpToolHelpers.ErrorResult($"File not found: {path}");
                }
                relocatedFrom = path;
                fullPath = located;
            }
        }

        try
        {
            var entries = await ParseFileAsync(fullPath, ct);

            if (entries.Count == 0)
            {
                // Plain page (paste subpage, concept page, vault.md): serve the
                // raw content plus the page's own resolved links so the agent
                // can keep following references.
                return PageResult(fullPath, await File.ReadAllTextAsync(fullPath, ct), maxContentLength);
            }

            // If looking for specific entry
            ParsedEntry? targetEntry = null;

            if (!string.IsNullOrEmpty(entryId))
            {
                // Find by entry ID
                targetEntry = FindById(entries, entryId);

                if (targetEntry == null && relocatedFrom == null)
                {
                    var located = await LocateEntryFileAsync(entryId);
                    if (located != null && !PathsEqual(located, fullPath))
                    {
                        relocatedFrom = path;
                        fullPath = located;
                        targetEntry = FindById(await ParseFileAsync(fullPath, ct), entryId);
                    }
                }

                if (targetEntry == null)
                {
                    return await NotFoundByIdAsync(entryId);
                }
            }
            else if (!string.IsNullOrEmpty(timestampStr))
            {
                // Find by timestamp
                if (!DateTime.TryParse(timestampStr, out var timestamp))
                {
                    return McpToolHelpers.ErrorResult($"Invalid timestamp format: {timestampStr}");
                }

                targetEntry = entries.FirstOrDefault(e => Math.Abs((e.Timestamp - timestamp).TotalMinutes) < 1);

                if (targetEntry == null)
                {
                    return McpToolHelpers.ErrorResult($"Entry not found at timestamp: {timestampStr}");
                }
            }

            // Return single entry or all entries
            if (targetEntry != null)
            {
                var resolved = _services.EntryResolver!.Resolve(targetEntry, fullPath, ResolveOptions.Full);
                var truncated = false;

                if (maxContentLength.HasValue && resolved.Content.Length > maxContentLength.Value)
                {
                    resolved = TruncateEntry(resolved, maxContentLength.Value);
                    truncated = true;
                }

                return McpToolHelpers.SuccessResult(new ReadEntryResult
                {
                    Success = true,
                    Entry = resolved,
                    Message = relocatedFrom == null
                        ? null
                        : $"Entry {entryId} is not in {relocatedFrom}; read from {resolved.Path}. Omit path to look an entry up by id alone.",
                    Truncated = truncated,
                    TruncationMessage = truncated ? $"Content truncated at {maxContentLength!.Value} chars. Use read_entry without max_content_length for full content." : null
                });
            }
            else
            {
                // Return all entries in file. Use Full (not Query) so each entry
                // carries resolved Links/Backlinks - read_entry is a detail/follow
                // surface, so the path-only shape must resolve links exactly like
                // the single entry_id shape above (bead lmk-f6c3).
                var resolvedEntries = entries.Select(e => _services.EntryResolver!.Resolve(e, fullPath, ResolveOptions.Full)).ToList();

                if (maxContentLength.HasValue)
                {
                    resolvedEntries = resolvedEntries.Select(r =>
                        r.Content.Length > maxContentLength.Value
                            ? TruncateEntry(r, maxContentLength.Value)
                            : r).ToList();
                }

                return McpToolHelpers.SuccessResult(new ReadEntriesResult
                {
                    Success = true,
                    Entries = resolvedEntries,
                    TotalCount = resolvedEntries.Count
                });
            }
        }
        catch (Exception ex)
        {
            return McpToolHelpers.ErrorResult($"Failed to read entry: {ex.Message}");
        }
    }

    private ToolCallResult PageResult(string fullPath, string content, int? maxContentLength)
    {
        var vaultPath = _services.VaultPath!;
        var fileAccessor = new FileAccessor();
        var links = ContentParser.GetWikiLinks(content)
            .Select(link => LinkTargetResolver.ResolveToLink(link, fullPath, vaultPath, fileAccessor))
            .ToList();

        var truncated = maxContentLength.HasValue && content.Length > maxContentLength.Value;

        return McpToolHelpers.SuccessResult(new ReadPageResult
        {
            Success = true,
            Kind = "page",
            Path = EntryResolver.GetRelativePath(fullPath, vaultPath),
            Content = truncated ? content[..maxContentLength!.Value] : content,
            SizeBytes = content.Length,
            Links = links,
            Backlinks = _services.EntryResolver?.ResolveFileBacklinks(fullPath) ?? new(),
            Truncated = truncated,
            TruncationMessage = truncated
                ? $"Content truncated at {maxContentLength!.Value} chars. Use read_entry without max_content_length for full content."
                : null
        });
    }

    private static async Task<List<ParsedEntry>> ParseFileAsync(string fullPath, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(fullPath, ct);
        return EntryParser.Parse(content, McpToolHelpers.GetTypeHintFromPath(fullPath));
    }

    private static ParsedEntry? FindById(IEnumerable<ParsedEntry> entries, string entryId) =>
        entries.FirstOrDefault(e => EntryResolver.GetEntryId(e).Equals(entryId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolve a ShortId to the absolute path of the file holding it, via the
    /// EntryCache ShortId index. Null when the id is unknown or no cache is wired.
    /// </summary>
    private async Task<string?> LocateEntryFileAsync(string entryId)
    {
        var cache = _services.EntryCache;
        if (cache == null)
            return null;

        // The index is empty until warmup completes.
        if (cache.WarmupTask is { } warmup)
            await warmup;

        return cache.ResolveShortId(entryId)?.filePath;
    }

    /// <summary>
    /// The id missed the ShortId index. An unanchored entry is served under a
    /// computed id that index never holds, so before answering "not found",
    /// check whether the id is one of those and say what it is (lmk-gwy6).
    /// Runs only on a miss, so the full scan costs nothing on the read path.
    /// </summary>
    private async Task<ToolCallResult> NotFoundByIdAsync(string entryId)
    {
        var cache = _services.EntryCache;
        if (cache != null)
        {
            if (cache.WarmupTask is { } warmup)
                await warmup;

            var unanchored = cache.GetAllEntries(includeThinkers: true).FirstOrDefault(ie =>
                string.IsNullOrEmpty(ie.Entry.ShortId)
                && EntryResolver.GetEntryId(ie.Entry).Equals(entryId, StringComparison.OrdinalIgnoreCase));
            if (unanchored != null)
                return McpToolHelpers.UnaddressableError(
                    entryId, EntryResolver.GetRelativePath(unanchored.FilePath, _services.VaultPath!));
        }

        return McpToolHelpers.ErrorResult($"Entry not found with ID: {entryId}");
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static ResolvedEntry TruncateEntry(ResolvedEntry entry, int maxLength)
    {
        return new ResolvedEntry
        {
            EntryId = entry.EntryId,
            Timestamp = entry.Timestamp,
            EntryType = entry.EntryType,
            Content = entry.Content[..maxLength],
            Path = entry.Path,
            Route = entry.Route,
            Scope = entry.Scope,
            StartLine = entry.StartLine,
            EndLine = entry.EndLine,
            Tags = entry.Tags,
            IsCompleted = entry.IsCompleted,
            Context = entry.Context,
            Source = entry.Source,
            Session = entry.Session,
            // Carried through truncation: the breadcrumb is metadata, not body,
            // so a truncated CONTENT must not silently cost the provenance.
            Evidence = entry.Evidence,
            ShortId = entry.ShortId,
            Deadline = entry.Deadline,
            PostponedCount = entry.PostponedCount,
            Resolution = entry.Resolution,
            AgeDays = entry.AgeDays,
            IsResolved = entry.IsResolved,
            Links = entry.Links,
            Backlinks = entry.Backlinks
        };
    }

    private class ReadEntryResult
    {
        public bool Success { get; set; }
        public ResolvedEntry? Entry { get; set; }
        public string? Message { get; set; }
        public bool Truncated { get; set; }
        public string? TruncationMessage { get; set; }
    }

    private class ReadEntriesResult
    {
        public bool Success { get; set; }
        public List<ResolvedEntry> Entries { get; set; } = new();
        public int TotalCount { get; set; }
    }

    private class ReadPageResult
    {
        public bool Success { get; set; }
        public string Kind { get; set; } = "page";
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public long SizeBytes { get; set; }
        public List<ResolvedLink> Links { get; set; } = new();
        public List<ResolvedBacklink> Backlinks { get; set; } = new();
        public bool Truncated { get; set; }
        public string? TruncationMessage { get; set; }
    }
}
