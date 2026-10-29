using System.IO;
using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Parses routes from _logmark/routes.md
/// </summary>
public class RouteParser
{
    private readonly string _routesFilePath;

    // Regex to parse markdown list items: - key: value (value can be empty for multi-line properties)
    private static readonly Regex PropertyRegex = new(
        @"^\s*-\s*(\w+):\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public RouteParser(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new ArgumentNullException(nameof(vaultPath));

        _routesFilePath = Path.Combine(vaultPath, PathConstants.GetRoutesPath());
    }

    /// <summary>
    /// Get the path to routes.md file
    /// </summary>
    public string GetRoutesFilePath() => _routesFilePath;

    /// <summary>
    /// Parse all routes from routes.md
    /// </summary>
    public RouteConfig Parse()
    {
        var config = new RouteConfig();

        if (!File.Exists(_routesFilePath))
            return config;

        try
        {
            var content = File.ReadAllText(_routesFilePath);
            ParseRoutes(content, config);
        }
        catch (Exception)
        {
            // Silently return empty config if parsing fails
        }

        return config;
    }

    /// <summary>
    /// Parse routes from markdown content without file I/O.
    /// Enables validation of content before writing to disk.
    /// </summary>
    public static RouteConfig ParseContent(string content)
    {
        var config = new RouteConfig();
        if (string.IsNullOrWhiteSpace(content))
            return config;

        ParseRoutes(content, config);
        return config;
    }

    private static void ParseRoutes(string content, RouteConfig config)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string? currentSection = null;
        string? currentSectionNotes = null;
        Route? currentRoute = null;
        var proseLines = new List<string>();
        bool seenFirstSection = false;
        bool seenFirstH1 = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Skip # Routes header
            if (!seenFirstH1 && trimmed.StartsWith("# ") && trimmed.Length > 2)
            {
                seenFirstH1 = true;
                continue;
            }

            // Match any ## header as a section
            if (trimmed.StartsWith("## ") && trimmed.Length > 3)
            {
                // Flush pending route before switching sections
                if (currentRoute != null)
                {
                    config.Routes.Add(currentRoute);
                    currentRoute = null;
                }

                // Flush collected prose
                if (!seenFirstSection)
                {
                    // Prose before first ## = vault notes
                    config.VaultNotes = FlushProse(proseLines);
                }
                else
                {
                    // Prose after previous ## = section notes for that section
                    currentSectionNotes = FlushProse(proseLines);
                    ApplySectionNotes(config, currentSection, currentSectionNotes);
                }

                currentSection = trimmed.Substring(3).Trim();
                currentSectionNotes = null;
                seenFirstSection = true;
                proseLines.Clear();
                continue;
            }

            // Route heading (### name)
            if (trimmed.StartsWith("### "))
            {
                // Flush section prose if we haven't yet (first route in section)
                if (proseLines.Count > 0 && seenFirstSection)
                {
                    currentSectionNotes = FlushProse(proseLines);
                    ApplySectionNotes(config, currentSection, currentSectionNotes);
                }

                if (currentRoute != null)
                    config.Routes.Add(currentRoute);

                var name = trimmed.Substring(4).Trim();
                currentRoute = new Route
                {
                    Name = name,
                    Section = currentSection,
                    SectionNotes = currentSectionNotes
                };
                continue;
            }

            // Property lines (- key: value)
            if (currentRoute != null && trimmed.StartsWith("- "))
            {
                var match = PropertyRegex.Match(trimmed);
                if (match.Success)
                {
                    var key = match.Groups[1].Value.ToLowerInvariant();
                    var value = match.Groups[2].Value.Trim();

                    switch (key)
                    {
                        case "path":
                            currentRoute.Path = value;
                            break;
                        case "description":
                            currentRoute.Description = value;
                            break;
                        case "status":
                            currentRoute.Status = value;
                            break;
                        case "tags":
                            // Parse tags: #tag1 #tag2
                            currentRoute.Tags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Where(t => t.StartsWith("#"))
                                .Select(t => t.TrimStart('#'))
                                .ToList();
                            break;
                        case "scopes":
                            // If value is empty, parse multi-line scopes
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                ParseScopesMultiLine(lines, ref i, currentRoute);
                            }
                            break;
                        case "private":
                            currentRoute.Private = ParsePrivatePreset(value);
                            break;
                        case "workdir":
                            if (!string.IsNullOrWhiteSpace(value))
                                currentRoute.Workdirs.Add(value.Trim());
                            break;
                    }
                }
                continue;
            }

            // Vault-level workdir: a "- workdir: <path>" line in the file header,
            // before any "## " section and before any route. Filesystem binding for
            // a project-dedicated vault.
            if (currentRoute == null && !seenFirstSection && trimmed.StartsWith("- "))
            {
                var headerMatch = PropertyRegex.Match(trimmed);
                if (headerMatch.Success
                    && headerMatch.Groups[1].Value.Equals("workdir", StringComparison.OrdinalIgnoreCase))
                {
                    var headerValue = headerMatch.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(headerValue))
                        config.VaultWorkdirs.Add(headerValue);
                    continue;
                }
            }

            // Collect prose lines (non-structural, non-empty lines before routes)
            if (currentRoute == null && !string.IsNullOrWhiteSpace(trimmed)
                && !trimmed.StartsWith("---"))
            {
                proseLines.Add(trimmed);
            }
        }

        // Flush any trailing prose (e.g., vault notes when no sections exist)
        if (!seenFirstSection && proseLines.Count > 0)
        {
            config.VaultNotes = FlushProse(proseLines);
        }

        // Add last route
        if (currentRoute != null)
            config.Routes.Add(currentRoute);
    }

    /// <summary>
    /// Join collected prose lines into a single string, trimmed.
    /// Returns null if no meaningful prose was collected.
    /// </summary>
    private static string? FlushProse(List<string> lines)
    {
        if (lines.Count == 0)
            return null;

        var result = string.Join(" ", lines).Trim();
        lines.Clear();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <summary>
    /// Apply section notes to all routes already parsed that belong to this section
    /// but don't yet have SectionNotes set.
    /// </summary>
    private static void ApplySectionNotes(RouteConfig config, string? section, string? notes)
    {
        if (section == null || notes == null)
            return;

        foreach (var route in config.Routes)
        {
            if (string.Equals(route.Section, section, StringComparison.OrdinalIgnoreCase)
                && route.SectionNotes == null)
            {
                route.SectionNotes = notes;
            }
        }
    }

    /// <summary>
    /// Parse multi-line scopes format:
    /// - scopes:
    ///   - architecture: System design decisions
    ///   - ux: User experience patterns
    /// </summary>
    private static void ParseScopesMultiLine(string[] lines, ref int index, Route route)
    {
        // Regex for scope lines: indented "  - name: description". Scope names allow
        // hyphens (e.g. "trade-ideas", "ai-capex-cycle"); without them, the parser
        // silently dropped hyphenated scopes from route.Scopes, which made every routed
        // write think the scope was new and re-append it to routes.md.
        // Slashes too: a nested subpath (+quant/research/vol) auto-registers as
        // scope "research/vol", and dropping it here re-appended it on every write.
        var scopeRegex = new Regex(@"^\s+-\s*([A-Za-z0-9_/-]+):\s*(.*)$", RegexOptions.Compiled);

        while (index + 1 < lines.Length)
        {
            var nextLine = lines[index + 1];

            // Check if line is indented (part of scopes block)
            if (!nextLine.StartsWith("  ") && !nextLine.StartsWith("\t"))
                break;

            // Skip empty lines within scopes block
            var trimmed = nextLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                index++;
                continue;
            }

            var match = scopeRegex.Match(nextLine);
            if (match.Success)
            {
                var scopeName = match.Groups[1].Value;
                var scopeDesc = match.Groups[2].Value.Trim();
                route.Scopes[scopeName] = scopeDesc;
            }

            index++;
        }
    }

    private static PrivatePreset? ParsePrivatePreset(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            // "default" is what RouteSerializer emits for PrivatePreset.Default;
            // "true" is kept as a back-compat alias. Both must map to Default or
            // a route set to the default preset silently de-shrouds on reload.
            "default" => PrivatePreset.Default,
            "true" => PrivatePreset.Default,
            "paranoid" => PrivatePreset.Paranoid,
            "financial" => PrivatePreset.Financial,
            "dev" => PrivatePreset.Dev,
            "false" => null,
            _ => null
        };
    }

    /// <summary>
    /// Find route by name (case-insensitive). Strict-name lookup.
    /// </summary>
    public Route? FindRoute(RouteConfig config, string name)
    {
        return config.Routes
            .FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolve a user-typed +token to a route, accepting either the route name
    /// or the first segment of the route path. Used for user-facing lookups so
    /// that `+infrastructure` resolves to the route named `infra` whose path is
    /// `infrastructure` (and similar).
    ///
    /// Precedence: exact name match wins. If no name matches, try a unique
    /// path-head match. If multiple routes share the path-head, returns null
    /// (ambiguous -- caller falls back to discovery popup / prompt).
    /// </summary>
    public Route? FindRouteByToken(RouteConfig config, string token)
    {
        var byName = config.Routes
            .FirstOrDefault(r => r.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;

        var byPathHead = config.Routes
            .Where(r => !string.IsNullOrEmpty(r.Path) &&
                string.Equals(
                    r.Path!.Split('/').FirstOrDefault(),
                    token,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byPathHead.Count == 1 ? byPathHead[0] : null;
    }

    /// <summary>
    /// Get section headers from routes.md (## Headers).
    /// Excludes "Archived" section as it's for reference-only routes.
    /// </summary>
    public IReadOnlyList<string> GetSections()
    {
        if (!File.Exists(_routesFilePath))
            return Array.Empty<string>();

        try
        {
            var content = File.ReadAllText(_routesFilePath);
            return ParseSections(content);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParseSections(string content)
    {
        var sections = new List<string>();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Match ## Header (level 2 headers only)
            if (trimmed.StartsWith("## ") && trimmed.Length > 3)
            {
                var sectionName = trimmed.Substring(3).Trim();

                // Skip Archived section - it's for reference-only routes
                if (!sectionName.Equals("Archived", StringComparison.OrdinalIgnoreCase))
                {
                    sections.Add(sectionName);
                }
            }
        }

        return sections;
    }
}
