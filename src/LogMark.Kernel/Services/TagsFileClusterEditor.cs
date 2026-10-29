using System.Text.RegularExpressions;

namespace LogMark.Services;

/// <summary>
/// Pure editor for cluster blocks in tags.md. A cluster block runs from its
/// "### name" header line up to (exclusive) the next "###" or "##" header or EOF.
/// Removing a block also consumes the blank lines immediately following it so the
/// file does not accumulate blank runs. Everything else is preserved.
/// </summary>
public static class TagsFileClusterEditor
{
    public static string RemoveCluster(string content, string name)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrWhiteSpace(name))
            return content;

        var newline = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var m = Regex.Match(lines[i], @"^###\s+([\w-]+)\s*$");
            if (m.Success && string.Equals(m.Groups[1].Value, name, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }
        if (start < 0) return content;

        int end = lines.Count; // exclusive
        for (int i = start + 1; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], @"^###\s") || Regex.IsMatch(lines[i], @"^##\s"))
            {
                end = i;
                break;
            }
        }

        // Consume trailing blank lines that belonged to the removed block, but stop
        // before a following header so its preceding blank separator is retained.
        while (end < lines.Count && lines[end].Trim().Length == 0
               && !(end + 1 < lines.Count && (Regex.IsMatch(lines[end + 1], @"^###\s") || Regex.IsMatch(lines[end + 1], @"^##\s"))))
        {
            end++;
        }

        lines.RemoveRange(start, end - start);
        return string.Join(newline, lines);
    }
}
