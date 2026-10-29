using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

public class FeedCardDocumentSource : MarkdownDocumentSourceBase
{
    public static readonly HashSet<string> AllowedTypes = new()
    {
        "nightshiftreport",
        "conceptdraft",
        "crossprojectinsight",
    };

    public FeedCardDocumentSource(IFileAccessor files, string logmarkPath)
        : base(files, Path.Combine(logmarkPath, "feed"))
    {
    }

    public override SearchFamily Family => SearchFamily.FeedCard;

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);

        if (!FrontmatterReader.TrySplit(content, out var yaml, out var body)) return null;

        var type = FrontmatterReader.Scalar(yaml, "type");
        if (type == null || !AllowedTypes.Contains(type.Replace(" ", "").ToLowerInvariant())) return null;

        var title = FrontmatterReader.Scalar(yaml, "action_label") ?? FirstHeading(body) ?? fileName;

        var ts = Files.GetLastWriteTimeUtc(file);
        var createdStr = FrontmatterReader.Scalar(yaml, "created");
        if (createdStr != null && TryParseDate(createdStr, out var created)) ts = created;

        return new SearchIndexRecord(
            $"feed:{fileName}", SearchFamily.FeedCard, title, file, null,
            Array.Empty<string>(), ts, body, null);
    }
}
