using System.Diagnostics;
using System.Globalization;
using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

/// <summary>
/// Shared scan/read skeleton for the file-scanning document sources (concept, session,
/// digest, chat, feed card, subpage). Handles directory-existence checks, the mtime scan,
/// safe file reads (unreadable files are skipped with a debug log rather than silently
/// swallowed), and the common <c>FirstHeading</c>/date-parse helpers. Each source supplies
/// its own record shape via <see cref="BuildRecord"/>.
/// </summary>
public abstract class MarkdownDocumentSourceBase : IDocumentSource
{
    protected IFileAccessor Files { get; }
    protected string Dir { get; }

    protected MarkdownDocumentSourceBase(IFileAccessor files, string dir)
    {
        Files = files;
        Dir = dir;
    }

    public abstract SearchFamily Family { get; }

    // TopDirectoryOnly by default; Subpage overrides to AllDirectories.
    protected virtual SearchOption Recursion => SearchOption.TopDirectoryOnly;

    // Default: include every *.md; Subpage overrides to require a pages/ segment.
    protected virtual bool IncludeFile(string path) => true;

    protected abstract SearchIndexRecord? BuildRecord(string file, string content);

    public virtual DateTime NewestMtime()
    {
        if (!Files.DirectoryExists(Dir)) return DateTime.MinValue;
        var newest = DateTime.MinValue;
        foreach (var f in Files.EnumerateFiles(Dir, "*.md", Recursion))
        {
            if (!IncludeFile(f)) continue;
            var m = Files.GetLastWriteTimeUtc(f);
            if (m > newest) newest = m;
        }
        return newest;
    }

    public IEnumerable<SearchIndexRecord> GetRecords()
    {
        if (!Files.DirectoryExists(Dir)) yield break;
        foreach (var file in Files.EnumerateFiles(Dir, "*.md", Recursion))
        {
            if (!IncludeFile(file)) continue;
            if (!TryRead(file, out var content)) continue;
            var record = BuildRecord(file, content);
            if (record != null) yield return record;
        }
    }

    protected bool TryRead(string file, out string content)
    {
        try { content = Files.ReadAllText(file); return true; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Search] skipped unreadable doc {file}: {ex.Message}");
            content = string.Empty;
            return false;
        }
    }

    protected static string? FirstHeading(string content)
    {
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
            if (line.StartsWith("# ")) return line[2..].Trim();
        return null;
    }

    protected static bool TryParseDate(string? s, out DateTime dt)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt);
}
