using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

public class DigestDocumentSource : MarkdownDocumentSourceBase
{
    public DigestDocumentSource(IFileAccessor files, string logmarkPath)
        : base(files, Path.Combine(logmarkPath, "_digests"))
    {
    }

    public override SearchFamily Family => SearchFamily.Digest;

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);

        var title = FirstHeading(content) ?? fileName;
        var ts = Files.GetLastWriteTimeUtc(file);

        return new SearchIndexRecord(
            $"digest:{fileName}", SearchFamily.Digest, title, file, null,
            Array.Empty<string>(), ts, content, null);
    }
}
