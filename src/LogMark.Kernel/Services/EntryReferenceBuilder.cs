using System.IO;
using LogMark.Models;
using LogMark.Services.EntryParser;

namespace LogMark.Services;

/// <summary>
/// Builds the canonical inline entry reference: an Obsidian block-anchor
/// wikilink with a readable alias. Same identity form as refs: tokens
/// (RefToken.WikiLink) so backlinks and Obsidian transclusion work unchanged.
/// </summary>
public static class EntryReferenceBuilder
{
    private const int AliasMaxLength = 40;

    public static string Build(ParsedEntry entry, string filePath, string vaultPath)
    {
        if (string.IsNullOrEmpty(entry.ShortId))
            throw new InvalidOperationException(
                "Entry has no ShortId - mint an anchor (AnchorMintSweep.ApplyToFile) before building a reference.");

        var rel = Path.GetRelativePath(vaultPath, filePath).Replace('\\', '/');
        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            rel = rel[..^3];

        return $"[[{rel}#^{entry.ShortId}|{BuildAlias(entry)}]]";
    }

    public static string BuildAlias(ParsedEntry entry)
    {
        var firstLine = entry.Content.Split('\n')[0].Trim();
        // Strip characters that would terminate the wikilink or alias early.
        var cleaned = firstLine.Replace("]]", " ").Replace("[[", " ").Replace("|", " ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length > AliasMaxLength)
            cleaned = cleaned[..AliasMaxLength].TrimEnd() + "...";

        var prefix = entry.Type switch
        {
            EntryType.Task => "t",
            EntryType.Idea => "i",
            EntryType.Block => "b",
            EntryType.Decision => "d",
            EntryType.Question => "q",
            EntryType.Gap => "g",
            _ => null,
        };
        return prefix is null ? cleaned : $"{prefix}: {cleaned}";
    }
}
