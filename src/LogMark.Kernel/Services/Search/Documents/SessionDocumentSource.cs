using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

public class SessionDocumentSource : MarkdownDocumentSourceBase
{
    public SessionDocumentSource(IFileAccessor files, string logmarkPath)
        : base(files, Path.Combine(logmarkPath, "_sessions"))
    {
    }

    public override SearchFamily Family => SearchFamily.Session;

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);

        var title = FirstHeading(content) ?? fileName;
        var route = PlainLine(content, "route")?.TrimStart('+');
        var ts = TryParseDate(PlainLine(content, "started"), out var started)
            ? started
            : Files.GetLastWriteTimeUtc(file);

        return new SearchIndexRecord(
            $"session:{fileName}", SearchFamily.Session, title, file, route,
            Array.Empty<string>(), ts, content, null);
    }

    private static string? PlainLine(string content, string key)
    {
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.TrimStart();
            if (t.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                return t[(key.Length + 1)..].Trim();
        }
        return null;
    }
}
