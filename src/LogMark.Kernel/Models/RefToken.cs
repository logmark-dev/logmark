using System.Text.RegularExpressions;

namespace LogMark.Models;

/// <summary>
/// A single token in an entry's `refs:` field. Three forms:
///   Entry: "type:id" (existing shape, parsed via EntryRef)
///   WikiLink: "[[path#^blockid]]" (Obsidian style)
///   Url: bare URL starting with http:// or https://
/// </summary>
public abstract record RefToken
{
    public sealed record Entry(EntryRef Ref) : RefToken;
    public sealed record WikiLink(string Path, string BlockId) : RefToken;
    public sealed record Url(string Value) : RefToken;

    private static readonly Regex WikiLinkRegex =
        new(@"^\[\[([^#\]|]+)#\^([a-zA-Z0-9-]+)(?:\|[^\]]*)?\]\]$", RegexOptions.Compiled);

    public static RefToken? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var token = raw.Trim();

        var m = WikiLinkRegex.Match(token);
        if (m.Success)
            return new WikiLink(m.Groups[1].Value, m.Groups[2].Value);

        if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new Url(token);

        var entryRef = EntryRef.Parse(token);
        if (entryRef != null) return new Entry(entryRef);

        return null;
    }

    public override string ToString() => this switch
    {
        Entry e => e.Ref.ToString(),
        WikiLink w => $"[[{w.Path}#^{w.BlockId}]]",
        Url u => u.Value,
        _ => base.ToString() ?? string.Empty
    };
}
