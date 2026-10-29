using LogMark.Services.EntryCache;
using LogMark.Services.Routes;

namespace LogMark.Services.Search;

public class SubpageDocumentSource : MarkdownDocumentSourceBase
{
    private readonly Func<RouteLocator>? _routes;

    /// <param name="routes">
    /// Route resolution for page paths. Without it, the projects|domains|clients
    /// convention applies, which misnames any route whose name differs from its
    /// folder or that nests inside another route's folder.
    /// </param>
    public SubpageDocumentSource(IFileAccessor files, string logmarkPath, Func<RouteLocator>? routes = null)
        : base(files, logmarkPath)
    {
        _routes = routes;
    }

    public override SearchFamily Family => SearchFamily.Subpage;

    protected override SearchOption Recursion => SearchOption.AllDirectories;

    protected override bool IncludeFile(string path) => HasPagesSegment(path);

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        var relativePath = Path.GetRelativePath(Dir, file).Replace('\\', '/');

        var body = content;
        string? yaml = null;
        if (FrontmatterReader.TrySplit(content, out var y, out var b))
        {
            body = b;
            yaml = y;
        }

        var title = (yaml != null ? FrontmatterReader.Scalar(yaml, "title") : null)
            ?? FirstHeading(body)
            ?? fileName;

        var ts = Files.GetLastWriteTimeUtc(file);
        if (yaml != null)
        {
            var dateStr = FrontmatterReader.Scalar(yaml, "date") ?? FrontmatterReader.Scalar(yaml, "created");
            if (TryParseDate(dateStr, out var parsed)) ts = parsed;
        }

        var route = RouteFromPath(relativePath);
        var tags = NotationParser.ExtractTags(body);

        return new SearchIndexRecord(
            $"page:{relativePath}", SearchFamily.Subpage, title, file, route,
            tags, ts, title + "\n" + body, null);
    }

    private static bool HasPagesSegment(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        // Check directory segments only; the last part is the file name.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("pages", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private string? RouteFromPath(string relativePath)
    {
        if (_routes != null)
            return _routes().Locate(relativePath).Route;

        var parts = relativePath.Split('/');
        if (parts.Length >= 2 && parts[0] is "projects" or "domains" or "clients")
            return parts[1];
        return null;
    }
}
