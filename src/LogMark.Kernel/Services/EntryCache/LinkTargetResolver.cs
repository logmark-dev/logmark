using System.IO;
using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services.EntryCache;

/// <summary>
/// A wikilink target resolved into a "light file object": validated path,
/// existence, classification, and size. Gives agents the same click-through
/// affordance the desktop viewer's MarkdownLinkRouter gives the user.
/// </summary>
public sealed record ResolvedLinkTarget(
    string AbsolutePath,
    string TargetPath,
    string? Anchor,
    bool Exists,
    string Kind,
    long SizeBytes);

/// <summary>
/// Resolves wikilink hrefs the way the viewer does (MarkdownLinkRouter
/// semantics, ported not shared: the App router can adopt this later):
/// split #shortid anchor, append .md, vault-relative for logmark/... targets,
/// file-relative otherwise. Adds the classification/metadata the viewer
/// computes implicitly on click.
/// </summary>
public static class LinkTargetResolver
{
    public const string KindEntriesFile = "entries-file";
    public const string KindPage = "page";
    public const string KindAsset = "asset";
    public const string KindExternal = "external";
    public const string KindMissing = "missing";

    private static readonly Regex YearFilePattern = new(@"^\d{4}\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ResolvedLinkTarget Resolve(
        string href,
        string sourceFilePath,
        string vaultPath,
        IFileAccessor fileAccessor)
    {
        string mdPath;
        string rawPath;
        string? anchor;
        try
        {
            (mdPath, rawPath, anchor) = ResolvePath(href, sourceFilePath, vaultPath);
        }
        catch
        {
            return new ResolvedLinkTarget(href, href, null, false, KindMissing, 0);
        }

        // When the .md candidate is absent but the raw path exists, the link
        // points at a non-markdown file (asset) - keep the raw path.
        var candidate = mdPath;
        if (!fileAccessor.FileExists(candidate) && candidate != rawPath && fileAccessor.FileExists(rawPath))
            candidate = rawPath;
        if (!fileAccessor.FileExists(candidate) && BareName(href) is { } name)
            candidate = FindBareTarget(name, sourceFilePath, vaultPath, fileAccessor) ?? candidate;

        var exists = fileAccessor.FileExists(candidate);
        var isExternal = !IsUnderRoot(candidate, vaultPath);
        var kind = Classify(candidate, exists, isExternal);
        var size = exists ? fileAccessor.GetFileSize(candidate) : 0;
        var targetPath = isExternal
            ? candidate
            : EntryResolver.GetRelativePath(candidate, vaultPath);

        return new ResolvedLinkTarget(candidate, targetPath, anchor, exists, kind, size);
    }

    /// <summary>
    /// Pure path resolution shared with the viewer's MarkdownLinkRouter: split
    /// the #anchor, pick vault-relative (logmark/... against
    /// <paramref name="vaultRoot"/>) vs file-relative (against the source
    /// file's directory), and produce both the .md-appended candidate and the
    /// raw combined path. Vault-relative hrefs fall back to file-relative when
    /// <paramref name="vaultRoot"/> is null (router semantics when no vault
    /// root can be derived). No filesystem access; Path.GetFullPath may throw
    /// on invalid input.
    /// </summary>
    public static (string mdPath, string rawPath, string? anchor) ResolvePath(
        string href,
        string sourceFilePath,
        string? vaultRoot)
    {
        string? anchor = null;
        var hashIdx = href.IndexOf('#');
        if (hashIdx >= 0)
        {
            var tail = href[(hashIdx + 1)..];
            if (tail.Length > 0) anchor = tail;
            href = href[..hashIdx];
        }

        var hrefForward = href.Replace('\\', '/');
        var isVaultRelative =
            hrefForward.StartsWith("logmark/", StringComparison.OrdinalIgnoreCase) ||
            hrefForward.Equals("logmark", StringComparison.OrdinalIgnoreCase);

        string combined;
        if (isVaultRelative && vaultRoot is not null)
        {
            combined = Path.GetFullPath(Path.Combine(vaultRoot, href));
        }
        else
        {
            var dir = Path.GetDirectoryName(sourceFilePath) ?? "";
            combined = Path.GetFullPath(Path.Combine(dir, href));
        }

        // Wikilink targets are markdown files; append .md unless already
        // present (foo.transcript -> foo.transcript.md).
        var mdPath = combined.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? combined
            : combined + ".md";
        return (mdPath, combined, anchor);
    }

    /// <summary>
    /// The target name of a bare wikilink (no folder in it, anchor dropped),
    /// or null when the href names a path.
    /// </summary>
    public static string? BareName(string href)
    {
        var hashIdx = href.IndexOf('#');
        var name = (hashIdx >= 0 ? href[..hashIdx] : href).Trim();
        return name.Length == 0 || name.IndexOfAny(['/', '\\', '*', '?']) >= 0 ? null : name;
    }

    /// <summary>
    /// The page a bare name points at when the source's own folder has no such
    /// file: the sibling pages/ folder first (where page write puts pages),
    /// then a unique basename anywhere in the vault, as Obsidian resolves it.
    /// An ambiguous basename resolves to nothing (bead lmk-m39p).
    /// </summary>
    public static string? FindBareTarget(string name, string sourceFilePath, string vaultPath, IFileAccessor fileAccessor)
    {
        var fileName = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name : name + ".md";
        var sibling = SiblingPagesPath(fileName, sourceFilePath);
        if (fileAccessor.FileExists(sibling)) return sibling;
        try
        {
            var matches = fileAccessor.EnumerateFiles(vaultPath, fileName, SearchOption.AllDirectories).Take(2).ToList();
            return matches.Count == 1 ? Path.GetFullPath(matches[0]) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The pages/ folder beside the source file, holding <paramref name="fileName"/>.</summary>
    public static string SiblingPagesPath(string fileName, string sourceFilePath) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? "", "pages", fileName));

    /// <summary>
    /// Resolve a wikilink segment straight into the ResolvedLink light object
    /// shape shared by entry and page results.
    /// </summary>
    public static ResolvedLink ResolveToLink(
        WikiLinkSegment link,
        string sourceFilePath,
        string vaultPath,
        IFileAccessor fileAccessor)
    {
        var target = Resolve(link.Path, sourceFilePath, vaultPath, fileAccessor);
        return new ResolvedLink
        {
            Label = link.DisplayText ?? link.Path,
            LinkType = "wiki",
            TargetPath = target.TargetPath,
            IsResolved = target.Exists,
            Kind = target.Kind,
            Exists = target.Exists,
            Anchor = target.Anchor,
            SizeBytes = target.Exists ? target.SizeBytes : null
        };
    }

    private static string Classify(string absolutePath, bool exists, bool isExternal)
    {
        if (isExternal) return KindExternal;
        if (!exists) return KindMissing;
        if (!absolutePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return KindAsset;

        // Yearly entry files live directly under a _type/ folder (_tasks/2026.md);
        // everything else markdown (pages/, concept pages, vault.md) is a page.
        var fileName = Path.GetFileName(absolutePath);
        var parent = Path.GetFileName(Path.GetDirectoryName(absolutePath) ?? "");
        return parent.StartsWith('_') && YearFilePattern.IsMatch(fileName)
            ? KindEntriesFile
            : KindPage;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> sits at or under <paramref name="root"/>.
    /// Same containment semantics as the viewer's trust boundary.
    /// </summary>
    public static bool IsUnderRoot(string candidate, string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, '/');
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, '/');
            return c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || c.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, r, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
