using System.Text.RegularExpressions;

namespace LogMark.Services.Search;

public static class FrontmatterReader
{
    public static bool TrySplit(string content, out string yaml, out string body)
    {
        yaml = string.Empty;
        body = content ?? string.Empty;
        if (content == null) return false;
        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n")) return false;
        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return false;
        yaml = normalized.Substring(4, end - 4);
        var after = normalized.IndexOf('\n', end + 1);
        body = after >= 0 ? normalized[(after + 1)..] : string.Empty;
        return true;
    }

    public static string? Scalar(string yaml, string key)
    {
        var m = Regex.Match(yaml, $@"(?m)^{Regex.Escape(key)}:[ \t]*(.+?)[ \t]*$");
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        if (v.StartsWith('[')) return null; // it is a list, not a scalar
        return v.Trim('"', '\'');
    }

    public static IReadOnlyList<string> List(string yaml, string key)
    {
        var inline = Regex.Match(yaml, $@"(?m)^{Regex.Escape(key)}:\s*\[(.*?)\]\s*$");
        if (inline.Success)
            return inline.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim('"', '\'')).Where(s => s.Length > 0).ToList();

        var block = new List<string>();
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var inBlock = false;
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, $@"^{Regex.Escape(key)}:\s*$")) { inBlock = true; continue; }
            if (inBlock)
            {
                var mm = Regex.Match(line, @"^\s*-\s*(.+?)\s*$");
                if (mm.Success) block.Add(mm.Groups[1].Value.Trim('"', '\''));
                else if (line.Trim().Length > 0 && !line.StartsWith(' ')) break;
            }
        }
        return block;
    }
}
