// src/LogMark.Core/Services/Export/RouteArchiveExporter.cs
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace LogMark.Services.Export;

/// <summary>
/// Composes a route-scoped .logmark archive. Includes:
/// - Entries and subpages under logmark/{route}/
/// - Only assets referenced by exported entries. References are scanned via
///   markdown image syntax `![alt](path)` and `[[path|title]]` wiki links;
///   a reference is treated as asset-shaped when its path leaf has a known
///   asset extension (jpg/png/pdf/mp4/zip/...) or when the path explicitly
///   traverses _system/assets/. URLs (http/https) are skipped.
/// - Minimal _system/config.md (so the archive validates as a vault source)
/// - _system/routes.md filtered to only the exported route's H2 section
/// - _system/tags.md (full file; the engine's EntityPoolService dedups
///   on import and target casing wins anyway)
///
/// Excludes: _system/log.md, _indexes/, telemetry/vector DBs, _system/protocols/.
///
/// Missing-asset scanning is best-effort: an asset referenced but not
/// present on disk is omitted from the archive and reported in the
/// result's MissingAssets list. The entry's reference is left unchanged.
/// </summary>
public sealed class RouteArchiveExporter
{
    private static readonly Regex MarkdownImage =
        new(@"!\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex WikiLink =
        new(@"\[\[([^\]|#]+)(?:\|[^\]]*)?\]\]", RegexOptions.Compiled);

    public RouteArchiveResult Export(string vaultPath, string route, string archivePath)
    {
        if (!Directory.Exists(vaultPath))
            throw new DirectoryNotFoundException($"Vault folder not found: {vaultPath}");

        var vaultFull = Path.GetFullPath(vaultPath);
        var archiveFull = Path.GetFullPath(archivePath);
        if (string.Equals(archiveFull, vaultFull, StringComparison.OrdinalIgnoreCase) ||
            archiveFull.StartsWith(vaultFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot export into the vault being exported");

        var logmarkRoot = Path.Combine(vaultFull, "logmark");
        var routeRoot = Path.Combine(logmarkRoot, route);
        if (!Directory.Exists(routeRoot))
            throw new InvalidOperationException($"Route has no entries: +{route}");

        // 1. Enumerate route content files.
        var contentFiles = Directory.EnumerateFiles(routeRoot, "*.md", SearchOption.AllDirectories).ToList();
        if (contentFiles.Count == 0)
            throw new InvalidOperationException($"Route has no entries: +{route}");

        // 2. Scan for asset references.
        var (referencedAssets, missingAssets) = ScanForAssets(contentFiles, logmarkRoot);

        // 3. Compose archive.
        try
        {
            using var fs = File.Create(archiveFull);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // 3a. Content files (entries + subpages).
            foreach (var file in contentFiles)
            {
                var rel = Path.GetRelativePath(vaultFull, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, rel);
            }

            // 3b. Referenced assets only.
            foreach (var asset in referencedAssets)
            {
                var assetFull = Path.Combine(logmarkRoot, "_system", "assets", asset);
                if (File.Exists(assetFull))
                {
                    archive.CreateEntryFromFile(assetFull, $"logmark/_system/assets/{asset}");
                }
            }

            // 3c. Minimal config.md.
            var configFull = Path.Combine(logmarkRoot, "_system", "config.md");
            if (File.Exists(configFull))
                archive.CreateEntryFromFile(configFull, "logmark/_system/config.md");

            // 3d. Filtered routes.md.
            var routesFull = Path.Combine(logmarkRoot, "_system", "routes.md");
            if (File.Exists(routesFull))
            {
                var filtered = FilterRoutesMarkdown(File.ReadAllText(routesFull), route);
                var entry = archive.CreateEntry("logmark/_system/routes.md");
                using var s = entry.Open();
                using var w = new StreamWriter(s);
                w.Write(filtered);
            }

            // 3e. Tags.md (full file - target's EntityPoolService dedups on import).
            var tagsFull = Path.Combine(logmarkRoot, "_system", "tags.md");
            if (File.Exists(tagsFull))
                archive.CreateEntryFromFile(tagsFull, "logmark/_system/tags.md");
        }
        catch
        {
            try { File.Delete(archiveFull); } catch { }
            throw;
        }

        // 4. Append log marker via SystemLogger.
        try
        {
            var logger = new SystemLogger(vaultFull);
            logger.Info($"Exported route +{route} to {Path.GetFileName(archiveFull)}");
        }
        catch
        {
            // Best-effort, by SystemLogger design.
        }

        var entriesCount = CountEntryHeaders(contentFiles);
        return new RouteArchiveResult(archiveFull, entriesCount, missingAssets);
    }

    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp", ".tiff",
        ".pdf", ".mp4", ".mov", ".mp3", ".wav", ".zip"
    };

    private static (List<string> Referenced, List<string> Missing) ScanForAssets(
        List<string> contentFiles, string logmarkRoot)
    {
        var assetsDir = Path.Combine(logmarkRoot, "_system", "assets");
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in contentFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in MarkdownImage.Matches(text))
                CheckRef(m.Groups[1].Value);
            foreach (Match m in WikiLink.Matches(text))
                CheckRef(m.Groups[1].Value);
        }

        return (referenced.ToList(), missing.ToList());

        void CheckRef(string raw)
        {
            var trimmed = raw.Trim();
            // Skip external URLs.
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;

            // Take only the leaf name.
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) return;

            var ext = Path.GetExtension(name);
            var looksLikeAsset = AssetExtensions.Contains(ext) ||
                                 trimmed.Contains("_system/assets/", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.Contains("_system\\assets\\", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeAsset) return;

            var direct = Path.Combine(assetsDir, name);
            if (File.Exists(direct))
                referenced.Add(name);
            else
                missing.Add(name); // asset-shaped reference but not on disk
        }
    }

    private static string FilterRoutesMarkdown(string fullRoutesMd, string targetRoute)
    {
        // routes.md is structured as H2 sections per route ("## +travel\n...").
        // Keep the file header (everything before the first H2) plus any H2
        // section whose name matches the target route. Other H2 sections are
        // dropped. Lines inside fenced code blocks are treated literally and do
        // not toggle section state, so a "## " line inside a ``` block
        // cannot accidentally end the kept section.
        var lines = fullRoutesMd.Replace("\r\n", "\n").Split('\n');
        var result = new System.Text.StringBuilder();
        var inSection = false;
        var keepSection = true; // header lines are kept
        var inFence = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;

            if (!inFence && line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = true;
                var name = line.Substring(3).TrimStart('+').Trim();
                keepSection = string.Equals(name, targetRoute, StringComparison.OrdinalIgnoreCase);
            }
            if (!inSection || keepSection)
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    private static int CountEntryHeaders(List<string> contentFiles)
    {
        var count = 0;
        var headerStart = "## 20";
        foreach (var file in contentFiles)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith(headerStart, StringComparison.Ordinal) &&
                    line.Length >= "## YYYY-MM-DD HH:mm".Length)
                    count++;
            }
        }
        return count;
    }
}
