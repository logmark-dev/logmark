using System.IO;
using LogMark.Interfaces;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using EntryParserClass = LogMark.Services.EntryParser.EntryParser;

namespace LogMark.Services.Pages;

/// <summary>
/// Result of an EntryPageLinker.WritePageAndLink call.
/// PagePath is the vault-relative wiki path (no extension, forward slashes) of the page that was written.
/// EntryFilePath is the vault-relative path of the markdown file that was updated (or would have been, if a link was inserted).
/// LinkInserted is false when the entry already contained a wiki link to the same page (scoped idempotency).
/// </summary>
public record EntryPageLinkResult(string PagePath, string EntryFilePath, bool LinkInserted);

/// <summary>
/// Thrown when WritePageAndLink cannot locate an entry by ShortId.
/// No page is written when this is thrown.
/// </summary>
public class EntryNotFoundException : Exception
{
    public EntryNotFoundException(string shortId)
        : base($"No entry found with shortId '{shortId}'") { }
}

/// <summary>
/// Atomically writes a subpage and wiki-links it from an existing entry.
/// Order of operations: locate entry first, write page, then insert link.
/// If the entry cannot be found, no page is written.
///
/// Atomicity caveat: true atomicity (page write + entry update either both succeed
/// or both fail) is not achievable without a transactional file system. The current
/// design is "find first, write page, insert link" -- so if the page write succeeds
/// but the link insert fails, the page exists orphaned. Acceptable for the current
/// MCP use case; future work may add rollback.
/// </summary>
public class EntryPageLinker
{
    private readonly string _vaultPath;
    private readonly SubpageWriter _subpageWriter;
    private readonly IVaultFileWriter? _vaultWriter;

    public EntryPageLinker(string vaultPath, SubpageWriter subpageWriter, IVaultFileWriter? vaultWriter = null)
    {
        _vaultPath = vaultPath;
        _subpageWriter = subpageWriter;
        _vaultWriter = vaultWriter;
    }

    /// <summary>
    /// Write a subpage and insert a wiki link to it into the entry identified by entryShortId.
    /// Throws EntryNotFoundException (without writing a page) if the entry cannot be located.
    /// Idempotent: if the target entry already contains a wiki link to the same page,
    /// the page is still written but the entry file is left unchanged and LinkInserted is false.
    /// </summary>
    public EntryPageLinkResult WritePageAndLink(
        string? route,
        string typeName,
        string filename,
        string content,
        string entryShortId,
        string displayText)
    {
        // Step 1: locate entry first (do not write a page we cannot link)
        var (entryFile, entry) = FindEntryByShortId(entryShortId);
        if (entryFile is null || entry is null)
            throw new EntryNotFoundException(entryShortId);

        // Step 2: write the page (validates type via SubpageWriter)
        _subpageWriter.WritePage(route, typeName, filename, content);

        // Step 3: insert wiki link into the entry
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        // Resolving variant, so the link cannot point somewhere other than where
        // step 2 just wrote when `route` is an alias rather than a route path.
        var wikiPath = _subpageWriter.GetPageWikiPathFor(route, typeName, fileNameWithoutExt);

        var entryFileAbs = Path.Combine(_vaultPath, entryFile);
        var markdown = File.ReadAllText(entryFileAbs);
        var updated = EntrySerializer.InsertWikiLink(
            markdown, entry.Timestamp, wikiPath, displayText, entryShortId);
        var linkInserted = updated != markdown;
        if (linkInserted)
        {
            if (_vaultWriter != null)
                _vaultWriter.EnsureAndWrite(entryFileAbs, updated);
            else
                File.WriteAllText(entryFileAbs, updated);
        }

        return new EntryPageLinkResult(wikiPath, entryFile, linkInserted);
    }

    private (string? path, ParsedEntry? entry) FindEntryByShortId(string shortId)
    {
        // Walk all .md files under the vault's logmark namespace.
        var root = Path.Combine(_vaultPath, "logmark");
        if (!Directory.Exists(root)) return (null, null);

        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var content = File.ReadAllText(file);
                var typeHint = EntryTypeMapper.InferTypeFromPath(file);
                var entries = EntryParserClass.Parse(content, typeHint);
                foreach (var entry in entries)
                {
                    var id = EntryResolver.GetEntryId(entry);
                    if (string.Equals(id, shortId, StringComparison.OrdinalIgnoreCase))
                        return (Path.GetRelativePath(_vaultPath, file), entry);
                }
            }
            catch (Exception)
            {
                // Skip files that fail to parse; they cannot host the target entry anyway.
                continue;
            }
        }
        return (null, null);
    }
}
