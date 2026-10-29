using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Minimal frontmatter codec for agent-memory source facts and mirror subpages.
/// Deliberately not a YAML library: the two shapes handled here are fixed and flat
/// (plus one nested "metadata: / type:" case in Claude Code source facts).
/// </summary>
public static partial class AgentMemoryFrontmatter
{
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonKebab();

    public static string Serialize(MirrorFrontmatter fm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"source: {fm.Source}");
        sb.AppendLine($"source_project: {fm.SourceProject}");
        sb.AppendLine($"source_path: {fm.SourcePath}");
        sb.AppendLine($"source_type: {fm.SourceType}");
        sb.AppendLine($"content_hash: {fm.ContentHash}");
        sb.AppendLine($"landed_hash: {fm.LandedHash}");
        sb.AppendLine($"imported_at: {fm.ImportedAt}");
        sb.AppendLine($"status: {fm.Status}");
        sb.AppendLine($"suggested_route: {fm.SuggestedRoute}");
        sb.AppendLine($"route_confidence: {fm.RouteConfidence}");
        sb.AppendLine($"overlap_prior: {fm.OverlapPrior}");
        sb.AppendLine($"promoted_at: {fm.PromotedAt}");
        sb.AppendLine("---");
        sb.AppendLine();
        return sb.ToString();
    }

    public static bool TryParse(string pageContent, out MirrorFrontmatter frontmatter, out string body)
    {
        frontmatter = null!;
        body = pageContent;
        if (!SplitFrontmatter(pageContent, out var block, out body)) return false;

        var map = ParseFlat(block);
        if (!map.ContainsKey("source") || !map.ContainsKey("source_path")) return false;

        frontmatter = new MirrorFrontmatter(
            map.GetValueOrDefault("source", ""),
            map.GetValueOrDefault("source_project", ""),
            map.GetValueOrDefault("source_path", ""),
            map.GetValueOrDefault("source_type", "unknown"),
            map.GetValueOrDefault("content_hash", ""),
            map.GetValueOrDefault("landed_hash", ""),
            map.GetValueOrDefault("imported_at", ""),
            map.GetValueOrDefault("status", "live"),
            map.GetValueOrDefault("suggested_route", ""),
            map.GetValueOrDefault("route_confidence", "none"),
            map.GetValueOrDefault("overlap_prior", "low"),
            map.GetValueOrDefault("promoted_at", ""));
        return true;
    }

    public static void ParseSourceFact(string fileContent,
        out string? name, out string? description, out string? metadataType, out string body)
    {
        name = null; description = null; metadataType = null;
        if (!SplitFrontmatter(fileContent, out var block, out body))
        {
            body = fileContent.Trim();
            return;
        }

        string? pendingParent = null;
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var indented = line.StartsWith("  ") || line.StartsWith("\t");
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            if (!indented)
            {
                pendingParent = value.Length == 0 ? key : null;
                switch (key)
                {
                    case "name": name = value; break;
                    case "description": description = value; break;
                }
            }
            else if (pendingParent == "metadata" && key == "type")
            {
                metadataType = value;
            }
        }
    }

    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToKebab(string value)
        => NonKebab().Replace(value.ToLowerInvariant(), "-").Trim('-');

    private static bool SplitFrontmatter(string content, out string block, out string body)
    {
        block = ""; body = content.Trim();
        var text = content.TrimStart('﻿');
        if (!text.StartsWith("---")) return false;
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return false;
        block = text[3..end].Trim('\r', '\n');
        var after = text.IndexOf('\n', end + 1);
        body = after < 0 ? "" : text[(after + 1)..].Trim();
        return true;
    }

    private static Dictionary<string, string> ParseFlat(string block)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            map[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return map;
    }
}
