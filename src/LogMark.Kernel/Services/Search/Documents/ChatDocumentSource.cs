using System.Text;
using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

public class ChatDocumentSource : MarkdownDocumentSourceBase
{
    public ChatDocumentSource(IFileAccessor files, string logmarkPath)
        : base(files, Path.Combine(logmarkPath, "_chats"))
    {
    }

    public override SearchFamily Family => SearchFamily.Chat;

    protected override SearchIndexRecord? BuildRecord(string file, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);

        var body = content;
        var ts = Files.GetLastWriteTimeUtc(file);
        if (FrontmatterReader.TrySplit(content, out var yaml, out var b))
        {
            body = b;
            var startedStr = FrontmatterReader.Scalar(yaml, "started");
            if (TryParseDate(startedStr, out var started)) ts = started;
        }

        var title = FirstUserTurnText(body) ?? fileName;
        var searchText = StripTurnHeaders(body);

        return new SearchIndexRecord(
            $"chat:{fileName}", SearchFamily.Chat, title, file, null,
            Array.Empty<string>(), ts, searchText, null);
    }

    private static string StripTurnHeaders(string body)
    {
        var sb = new StringBuilder();
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("### ")) continue;
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static string? FirstUserTurnText(string body)
    {
        var inUserTurn = false;
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("## user", StringComparison.OrdinalIgnoreCase))
            {
                inUserTurn = true;
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                if (inUserTurn) break; // left the user turn before finding text
                continue;
            }
            if (!inUserTurn) continue;
            if (trimmed.StartsWith("### ")) continue;
            if (trimmed.Length == 0) continue;
            return trimmed;
        }
        return null;
    }
}
