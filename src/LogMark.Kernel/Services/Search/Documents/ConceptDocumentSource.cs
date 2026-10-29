using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

public class ConceptDocumentSource : MarkdownDocumentSourceBase
{
    public ConceptDocumentSource(IFileAccessor files, string logmarkPath)
        : base(files, Path.Combine(logmarkPath, "_concepts"))
    {
    }

    public override SearchFamily Family => SearchFamily.Concept;

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var slug = Path.GetFileNameWithoutExtension(file);
        string title = slug;
        string? route = null;
        IReadOnlyList<string> tags = Array.Empty<string>();
        DateTime ts = Files.GetLastWriteTimeUtc(file);
        var body = content;

        if (FrontmatterReader.TrySplit(content, out var yaml, out var b))
        {
            body = b;
            title = FrontmatterReader.Scalar(yaml, "title") ?? slug;
            route = FrontmatterReader.List(yaml, "routes").FirstOrDefault();
            tags = FrontmatterReader.List(yaml, "tags");
            var dateStr = FrontmatterReader.Scalar(yaml, "updated") ?? FrontmatterReader.Scalar(yaml, "created");
            if (TryParseDate(dateStr, out var parsed)) ts = parsed;
        }

        return new SearchIndexRecord(
            $"concept:{slug}", SearchFamily.Concept, title, file, route,
            tags, ts, title + "\n" + body, null);
    }
}
