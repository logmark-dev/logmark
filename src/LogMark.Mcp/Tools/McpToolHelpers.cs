using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Models;
using LogMark.Services;

namespace LogMark.Mcp.Tools;

/// <summary>
/// Shared helper methods for MCP tools.
/// </summary>
public static class McpToolHelpers
{
    public const string LogMarkRoot = "logmark";

    #region Argument Extraction

    public static string? GetStringArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null) return null;
        if (args.TryGetValue(key, out var value) && value is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }
        return null;
    }

    public static int? GetIntArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null) return null;
        if (args.TryGetValue(key, out var value) && value is JsonElement element && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt32();
        }
        return null;
    }

    public static bool? GetBoolArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null) return null;
        if (args.TryGetValue(key, out var value) && value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    public static List<string>? GetStringArrayArg(Dictionary<string, object?>? args, string key)
    {
        if (args == null) return null;
        if (args.TryGetValue(key, out var value) && value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var str = item.GetString();
                    if (!string.IsNullOrEmpty(str))
                        result.Add(str);
                }
            }
            return result;
        }
        return null;
    }

    #endregion

    #region Path Helpers

    public static EntryType? GetTypeHintFromPath(string path)
    {
        if (path.Contains("_tasks")) return EntryType.Task;
        if (path.Contains("_ideas")) return EntryType.Idea;
        if (path.Contains("_blocks")) return EntryType.Block;
        if (path.Contains("_decisions")) return EntryType.Decision;
        if (path.Contains("_timetrack")) return EntryType.Timetrack;
        if (path.Contains("_diary")) return EntryType.Log;
        return null;
    }

    /// <summary>
    /// Route of a vault-relative file path through the cache's RouteLocator (the
    /// same answer search gives), falling back to the path convention only when
    /// no cache is wired.
    /// </summary>
    public static string? RouteOf(VaultServices services, string vaultRelativePath)
    {
        if (services.EntryCache != null && services.VaultPath != null)
            return services.EntryCache.LocatePath(Path.Combine(services.VaultPath, vaultRelativePath)).Route;
        return GetRouteFromPath(vaultRelativePath);
    }

    public static string? GetRouteFromPath(string relativePath)
    {
        var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "logmark")
            return null;

        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith('_'))
            {
                if (i >= 2 && (parts[1] == "projects" || parts[1] == "domains" || parts[1] == "clients"))
                {
                    return parts[2];
                }
                return null;
            }
        }
        return null;
    }

    public static string? GetScopeFromPath(string relativePath, string? route)
    {
        if (route == null) return null;

        var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        // Skip "logmark" prefix in vault-relative paths to avoid matching route name
        // against the logmark root directory (e.g., route "logmark" matching parts[0])
        int startIndex = (parts.Length > 0 && parts[0].Equals("logmark", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        var routeIndex = Array.FindIndex(parts, startIndex, parts.Length - startIndex,
            p => p.Equals(route, StringComparison.OrdinalIgnoreCase));

        if (routeIndex >= 0 && routeIndex + 1 < parts.Length)
        {
            var nextPart = parts[routeIndex + 1];
            if (!nextPart.StartsWith('_'))
            {
                return nextPart;
            }
        }
        return null;
    }

    /// <summary>
    /// Get markdown files from the vault, optionally filtered by route/scope.
    /// </summary>
    public static List<string> GetMarkdownFiles(string vaultPath, string? route = null, string? scope = null, EntryType? typeFilter = null)
    {
        var logmarkRoot = Path.Combine(vaultPath, LogMarkRoot);
        if (!Directory.Exists(logmarkRoot))
            return new List<string>();

        var mdFiles = Directory.GetFiles(logmarkRoot, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_system" + Path.DirectorySeparatorChar))
            .ToList();

        // Filter by route
        if (!string.IsNullOrEmpty(route))
        {
            var matchingFiles = new List<string>();

            // Try to get route path from routes.md
            var parser = new RouteParser(vaultPath);
            var config = parser.Parse();
            var routeObj = config.Routes.FirstOrDefault(r =>
                r.Name.Equals(route, StringComparison.OrdinalIgnoreCase));

            if (routeObj?.Path != null)
            {
                // Use configured path (normalize separators for cross-platform compatibility)
                var normalizedPath = routeObj.Path.Replace('/', Path.DirectorySeparatorChar);
                var routePath = Path.Combine(logmarkRoot, normalizedPath);
                matchingFiles.AddRange(mdFiles.Where(f => f.StartsWith(routePath, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                // Fallback: check common route locations
                var routePatterns = new[]
                {
                    Path.Combine(logmarkRoot, "projects", route),
                    Path.Combine(logmarkRoot, "domains", route),
                    Path.Combine(logmarkRoot, "clients", route)
                };

                foreach (var pattern in routePatterns)
                {
                    matchingFiles.AddRange(mdFiles.Where(f => f.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)));
                }
            }
            mdFiles = matchingFiles.Distinct().ToList();
        }

        // Filter by scope (subfolder within route)
        if (!string.IsNullOrEmpty(scope))
        {
            mdFiles = mdFiles.Where(f =>
            {
                var relativePath = Path.GetRelativePath(logmarkRoot, f);
                var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

                // Look for scope after route name
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Equals(route, StringComparison.OrdinalIgnoreCase))
                    {
                        return i + 1 < parts.Length && parts[i + 1].Equals(scope, StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            }).ToList();
        }

        // Filter by entry type (folder name)
        if (typeFilter.HasValue)
        {
            var typeFolderName = "_" + EntryTypeMapper.GetFolderName(typeFilter.Value);
            mdFiles = mdFiles.Where(f => f.Contains(typeFolderName)).ToList();
        }

        return mdFiles;
    }

    #endregion

    #region Result Helpers

    /// <summary>
    /// Serializes a tool result compactly. WriteIndented was true until bead
    /// lmk-1zld: pretty-printing cost 10_843 bytes of pure whitespace on a
    /// measured 51KB get_project_context response, 21% of the payload, which
    /// helped push it past grok's 20_000-byte MCP tool-result cap. Nothing
    /// human reads these - every consumer parses the JSON - and both
    /// McpServer and ProducerServerHooks already serialize compactly, so this
    /// helper was the outlier rather than the convention.
    /// </summary>
    public static ToolCallResult SuccessResult(object result)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        return new ToolCallResult
        {
            Content = new List<ToolContent>
            {
                new() { Type = "text", Text = JsonSerializer.Serialize(result, options) }
            }
        };
    }

    public static ToolCallResult ErrorResult(string message)
    {
        return new ToolCallResult
        {
            IsError = true,
            Content = new List<ToolContent>
            {
                new() { Type = "text", Text = message }
            }
        };
    }

    /// <summary>
    /// The error for an id served with Unaddressable: true. Such an entry has no
    /// persisted anchor, so the id index and every writer miss it, and a bare
    /// "not found" gave an MCP-only agent nothing to act on (lmk-gwy6). Names
    /// the file it can still be read from and the one command that fixes it.
    /// </summary>
    public static ToolCallResult UnaddressableError(string entryId, string relativePath) =>
        ErrorResult(
            $"Entry {entryId} is Unaddressable: it has no persisted id anchor, so it cannot be found by id " +
            $"or changed. Read it with read_entry(path: \"{relativePath.Replace('\\', '/')}\"). To fix it, ask the user to run " +
            $"`{Program.CliName} entry mint-anchors --apply`, which anchors the id already served here.");

    #endregion
}
