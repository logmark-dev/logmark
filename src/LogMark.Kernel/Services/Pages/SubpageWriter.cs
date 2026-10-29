using System.IO;
using LogMark.Interfaces;
using LogMark.Services;

namespace LogMark.Services.Pages;

/// <summary>
/// Writes markdown pages to correct vault locations.
/// Generalizes BackfillEngine.AddPage for any vault subdirectory:
/// diary pages, insight outputs, article drafts, etc.
/// </summary>
public class SubpageWriter
{
    private readonly string _vaultPath;
    private readonly IVaultFileWriter? _vaultWriter;
    private readonly Func<string?, string?>? _routePathResolver;

    /// <param name="routePathResolver">
    /// Maps a route token to the vault-relative route FOLDER path (normally
    /// RouteService.ResolveRouteFolderPath). PathConstants.GetRoutedPagesFolder
    /// takes a route PATH, so without this a caller holding an alias ("quant")
    /// silently creates logmark/quant/ beside the real
    /// logmark/projects/quant-trading-platform/. Optional and identity by
    /// default, because callers that already hold a resolved path are correct
    /// as-is; the resolver is required to be idempotent for the same reason.
    /// </param>
    public SubpageWriter(
        string vaultPath,
        IVaultFileWriter? vaultWriter = null,
        Func<string?, string?>? routePathResolver = null)
    {
        _vaultPath = vaultPath;
        _vaultWriter = vaultWriter;
        _routePathResolver = routePathResolver;
    }

    /// <summary>
    /// Route token to route folder path, via the injected resolver. Identity
    /// when no resolver was supplied.
    /// </summary>
    public string? ResolveRoutePath(string? route)
        => _routePathResolver == null ? route : _routePathResolver(route);

    /// <summary>
    /// Vault-relative wiki path for a page, resolving the route token the same
    /// way <see cref="WritePage"/> does. Prefer this over the static
    /// <see cref="GetPageWikiPath"/> whenever the route may be an alias - the
    /// link and the file must not disagree.
    /// </summary>
    public string GetPageWikiPathFor(string? route, string typeName, string fileNameWithoutExtension)
        => GetPageWikiPath(ResolveRoutePath(route), typeName, fileNameWithoutExtension);

    /// <summary>
    /// Write a page to routed or unrouted pages directory.
    /// Equivalent to BackfillEngine.AddPage but standalone.
    /// </summary>
    public void WritePage(string? route, string typeName, string filename, string content)
    {
        var canonical = ResolveOrThrow(typeName);
        route = ResolveRoutePath(route);
        var folder = string.IsNullOrEmpty(route)
            ? PathConstants.GetUnroutedPagesFolder(canonical)
            : PathConstants.GetRoutedPagesFolder(route, canonical);

        var normalized = HouseStyleNormalizer.Normalize(content) ?? "";
        var fullPath = Path.Combine(_vaultPath, folder, filename);
        if (_vaultWriter != null)
            _vaultWriter.EnsureAndWrite(fullPath, normalized);
        else
        {
            EnsureDirectory(fullPath);
            File.WriteAllText(fullPath, normalized);
        }
    }

    /// <summary>
    /// Write an insight output file.
    /// Cross-route: logmark/_insights/{category}/{filename}
    /// Route-specific: logmark/{route}/_insights/{category}/{filename}
    ///
    /// Production callers use category="clusters". The "digest" category is
    /// retained in tests for back-compat with legacy fixtures but is NOT a
    /// current writer destination -- digests live under logmark/_digests/.
    /// See <see cref="WriteDigest"/> for the digest writer.
    /// </summary>
    public void WriteInsight(string category, string filename, string content, string? route = null)
    {
        route = ResolveRoutePath(route);
        var folder = string.IsNullOrEmpty(route)
            ? Path.Combine("logmark", "_insights", category)
            : Path.Combine("logmark", route, "_insights", category);

        var fullPath = Path.Combine(_vaultPath, folder, filename);
        if (_vaultWriter != null)
            _vaultWriter.EnsureAndWrite(fullPath, content);
        else
        {
            EnsureDirectory(fullPath);
            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// Get vault-relative wiki path for a page (forward slashes for wiki links).
    /// </summary>
    public static string GetPageWikiPath(string? route, string typeName, string fileNameWithoutExtension)
    {
        var canonical = ResolveOrThrow(typeName);
        var folder = string.IsNullOrEmpty(route)
            ? PathConstants.GetUnroutedPagesFolder(canonical)
            : PathConstants.GetRoutedPagesFolder(route, canonical);

        return $"{folder.Replace('\\', '/')}/{fileNameWithoutExtension}";
    }

    /// <summary>
    /// Write a digest synthetic file.
    /// Path: logmark/_digests/{filename}
    /// Digests are pure historical artifacts; no per-route variant.
    /// </summary>
    public void WriteDigest(string filename, string content)
    {
        var fullPath = Path.Combine(_vaultPath, "logmark", "_digests", filename);
        if (_vaultWriter != null)
            _vaultWriter.EnsureAndWrite(fullPath, content);
        else
        {
            EnsureDirectory(fullPath);
            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// Get vault-relative wiki path for a digest file (forward slashes).
    /// </summary>
    public static string GetDigestWikiPath(string fileNameWithoutExtension)
        => $"logmark/_digests/{fileNameWithoutExtension}";

    /// <summary>
    /// Get vault-relative wiki path for an insight output.
    /// </summary>
    public static string GetInsightWikiPath(string category, string fileNameWithoutExtension, string? route = null)
    {
        var folder = string.IsNullOrEmpty(route)
            ? $"logmark/_insights/{category}"
            : $"logmark/{route}/_insights/{category}";

        return $"{folder}/{fileNameWithoutExtension}";
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private static string ResolveOrThrow(string typeName)
    {
        var resolved = EntryTypeMapper.ResolveTypeName(typeName);
        if (resolved is null)
            throw new ArgumentException(
                $"Unknown entry type '{typeName}'. Valid types: tasks, ideas, blocks, decisions, diary, questions, gaps, analogies, workflow, timetrack. (Use 'diary' for log entries.)",
                nameof(typeName));
        return resolved;
    }
}
