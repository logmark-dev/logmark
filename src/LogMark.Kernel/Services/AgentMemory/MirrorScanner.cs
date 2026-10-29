using LogMark.Models.AgentMemory;
using LogMark.Services;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Reads mirror-page frontmatter vault-wide (the ledger follows the page after
/// promotion). No second bookkeeping store (design decision, see design doc).
/// Read-only; reading files directly is fine here (the write guard covers writes,
/// and EntryCache is for display reads, not service-internal ledger scans).
/// </summary>
public class MirrorScanner
{
    private readonly string _vaultPath;
    private readonly List<string> _staleStagingCopies = new();

    public MirrorScanner(string vaultPath)
    {
        _vaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
    }

    /// <summary>Paths of losing staging duplicates from the last Scan.</summary>
    public IReadOnlyList<string> StaleStagingCopies => _staleStagingCopies;

    public static string GetMirrorRoute(string sourceId) => "agents/" + sourceId;

    public static string GetMirrorPagesDir(string vaultPath, string sourceId)
    {
        var route = GetMirrorRoute(sourceId);
        var canonicalType = EntryTypeMapper.ResolveTypeName("diary");
        if (canonicalType == null)
            throw new ArgumentException("Unable to resolve 'diary' type name");

        return Path.Combine(vaultPath,
            PathConstants.GetRoutedPagesFolder(route, canonicalType));
    }

    public IReadOnlyList<MirrorRecord> Scan(string sourceId)
    {
        _staleStagingCopies.Clear();
        var stagingDir = GetMirrorPagesDir(_vaultPath, sourceId);
        var byPath = new Dictionary<string, MirrorRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in EnumeratePagesDirs())
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f))
            {
                string content;
                try { content = File.ReadAllText(file); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                if (!AgentMemoryFrontmatter.TryParse(content, out var fm, out var body)) continue;
                if (!string.Equals(fm.Source, sourceId, StringComparison.OrdinalIgnoreCase)) continue;
                // A hand-corrupted or malformed page can carry an empty
                // source_path. It must never become a ledger record (SourcePath
                // is the dedup key) and never qualify as a StaleStagingCopy --
                // the heal path deletes files, and deleting on an empty key
                // could target the wrong thing.
                if (string.IsNullOrWhiteSpace(fm.SourcePath)) continue;

                var record = new MirrorRecord(file, fm, AgentMemoryFrontmatter.ComputeHash(body));
                if (byPath.TryGetValue(fm.SourcePath, out var existing))
                {
                    // Crash-case duplicate: the copy under the staging dir loses.
                    var existingIsStaging = IsUnder(existing.PagePath, stagingDir);
                    var newIsStaging = IsUnder(file, stagingDir);
                    if (existingIsStaging && !newIsStaging)
                    {
                        _staleStagingCopies.Add(existing.PagePath);
                        byPath[fm.SourcePath] = record;
                    }
                    else if (!existingIsStaging && newIsStaging)
                    {
                        _staleStagingCopies.Add(file);
                    }
                    else if (existingIsStaging && newIsStaging)
                    {
                        // Both under staging: keep first, but still record the
                        // loser so it can be cleaned up like any other stale copy.
                        _staleStagingCopies.Add(file);
                    }
                    // two non-staging copies: keep first, deterministic (enumeration is sorted)
                    continue;
                }
                byPath[fm.SourcePath] = record;
            }
        }
        return byPath.Values.OrderBy(r => r.PagePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Every <route>/_diary/pages dir under logmark/, staging included;
    // the _system tree (trash) is never a ledger location.
    private IEnumerable<string> EnumeratePagesDirs()
    {
        var root = Path.Combine(_vaultPath, "logmark");
        if (!Directory.Exists(root)) yield break;
        List<string> diaryDirs;
        try
        {
            // Materialize and sort: filesystem enumeration order is unspecified,
            // and the duplicate tie-break below ("keep first") depends on a
            // deterministic traversal order to be deterministic itself.
            diaryDirs = Directory.EnumerateDirectories(root, "_diary", SearchOption.AllDirectories)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }
        foreach (var diary in diaryDirs)
        {
            if (diary.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Any(s => s.Equals("_system", StringComparison.OrdinalIgnoreCase))) continue;
            var pages = Path.Combine(diary, "pages");
            if (Directory.Exists(pages)) yield return pages;
        }
    }

    private static bool IsUnder(string path, string dir)
        => Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Route path for a page under &lt;route&gt;/_diary/pages/, else null.</summary>
    public static string? RouteOfPagePath(string vaultPath, string pagePath)
    {
        var root = Path.GetFullPath(Path.Combine(vaultPath, "logmark"));
        var full = Path.GetFullPath(pagePath);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        var relative = full[(root.Length + 1)..];
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // shape: <route segments...>/_diary/pages/<file>
        if (segments.Length < 4) return null;
        if (!segments[^2].Equals("pages", StringComparison.OrdinalIgnoreCase)) return null;
        if (!segments[^3].Equals("_diary", StringComparison.OrdinalIgnoreCase)) return null;
        return string.Join('/', segments[..^3]);
    }
}
