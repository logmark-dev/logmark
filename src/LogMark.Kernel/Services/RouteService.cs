using System.IO;
using System.Text.RegularExpressions;
using LogMark.Interfaces;
using LogMark.Models;
using LogMark.Models.Sensitivity;
using LogMark.Services.Routes;
using LogMark.Services.Sensitivity;

namespace LogMark.Services;

/// <summary>
/// Main routing orchestration service
/// Resolves routes, coordinates FileRouter
/// </summary>
public class RouteService
{
    private readonly RouteParser _parser;
    private readonly FileRouter _fileRouter;
    private readonly SystemLogger _logger;
    private readonly IRoutePromptService? _promptService;
    private RouteConfig? _config;
    private (DateTime WriteTimeUtc, long Length) _loadedStamp;

    public RouteService(string vaultPath, SystemLogger logger, IRoutePromptService? promptService = null, IVaultFileWriter? vaultWriter = null, ISensitivityService? sensitivity = null, IVaultWriteGate? writeGate = null)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new ArgumentNullException(nameof(vaultPath));

        _parser = new RouteParser(vaultPath);
        _fileRouter = new FileRouter(vaultPath, logger, vaultWriter ?? new VaultWriter(), sensitivity, writeGate);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _promptService = promptService;
    }

    /// <summary>
    /// Late-inject the write gate after construction.
    /// Eliminates the two-phase construction pattern in App.xaml.cs: construct
    /// RouteService once, build the gate with that instance, then call this to
    /// wire it back in. Hot-reload (LoadRoutes) then updates config for both paths.
    /// </summary>
    public void SetWriteGate(IVaultWriteGate gate)
    {
        _fileRouter.SetWriteGate(gate);
    }

    /// <summary>
    /// Load routes from routes.md
    /// Call this at startup or when routes.md changes
    /// </summary>
    public void LoadRoutes()
    {
        _loadedStamp = RoutesFileStamp();
        _config = _parser.Parse();
        _logger.Debug($"Loaded {_config.Routes.Count} routes");
    }

    /// <summary>
    /// Load routes if never loaded, or reload if routes.md changed on disk since
    /// the last load. A long-lived process (an agent's MCP server) otherwise keeps
    /// resolving a moved route to its old folder and re-creates it on write.
    /// </summary>
    private void EnsureRoutesCurrent()
    {
        if (_config == null || RoutesFileStamp() != _loadedStamp)
            LoadRoutes();
    }

    private (DateTime WriteTimeUtc, long Length) RoutesFileStamp()
    {
        var info = new FileInfo(_parser.GetRoutesFilePath());
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
    }

    /// <summary>
    /// Get the current route configuration (after LoadRoutes has been called).
    /// Returns null if routes have not been loaded yet.
    /// </summary>
    public RouteConfig? CurrentConfig => _config;

    /// <summary>
    /// Resolve a project name to its route path.
    /// Returns the vault-relative path for the route, or null if not found.
    /// </summary>
    public string? ResolveRoutePath(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
            return null;

        EnsureRoutesCurrent();

        // Handle subpaths (domain/subpath syntax)
        var baseName = projectName.Contains('/')
            ? projectName.Split('/', 2)[0]
            : projectName;

        // User-facing token: accept either route name or unique path-head.
        // e.g. +infrastructure resolves to the route named `infra` whose path
        // is `infrastructure`. See v0.18 QA A1 finding.
        var route = _parser.FindRouteByToken(_config!, baseName);
        return route?.Path;
    }

    /// <summary>
    /// Resolve a route token to the vault-relative FOLDER path that page and
    /// entry writers build on, e.g. "quant" -> "projects/quant-trading-platform"
    /// and "quant/research" -> "projects/quant-trading-platform/research".
    ///
    /// Distinct from <see cref="ResolveRoutePath"/>, which answers only for the
    /// base route and drops any /scope suffix. Page writers need the scope back
    /// on, matching how RouteEntryAsync composes effectiveRoute.Path.
    ///
    /// Idempotent by contract: a token that is already a route path (exactly, or
    /// a route path plus a scope segment) is returned unchanged. Callers such as
    /// VaultService pass resolved paths, and both SubpageWriter and
    /// EntryPageLinker resolve on the same write, so double resolution must be a
    /// no-op. An unrecognised token is likewise returned unchanged rather than
    /// throwing or silently becoming unrouted.
    /// </summary>
    public string? ResolveRouteFolderPath(string? routeToken)
    {
        if (string.IsNullOrWhiteSpace(routeToken))
            return null;

        EnsureRoutesCurrent();

        var token = routeToken.Replace('\\', '/').Trim('/');
        if (token.Length == 0)
            return null;

        // Already a route path (with or without a trailing scope segment).
        // Checked before any name lookup so a route whose NAME contains a slash
        // (agents/claude-code) is never mistaken for alias/scope.
        foreach (var r in _config!.Routes)
        {
            if (string.IsNullOrEmpty(r.Path)) continue;
            var p = r.Path!.Replace('\\', '/').Trim('/');
            if (p.Length == 0) continue;
            if (token.Equals(p, StringComparison.OrdinalIgnoreCase)
                || token.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))
                return token;
        }

        // Route name carrying its own slash (agents/claude-code) resolves whole.
        var exact = _parser.FindRouteByToken(_config!, token);
        if (exact != null && !string.IsNullOrEmpty(exact.Path))
            return exact.Path!.Replace('\\', '/').Trim('/');

        // Alias, optionally with a /scope suffix to re-append.
        if (token.Contains('/'))
        {
            var parts = token.Split('/', 2);
            var baseRoute = _parser.FindRouteByToken(_config!, parts[0]);
            if (baseRoute != null && !string.IsNullOrEmpty(baseRoute.Path))
                return baseRoute.Path!.Replace('\\', '/').Trim('/') + "/" + parts[1];
        }

        return token;
    }

    /// <summary>
    /// True if a +token (without the +) names a declared route: by name or
    /// unique path head, either whole (agents/claude-code) or by its base
    /// segment with a /scope suffix (iasx/ops).
    /// </summary>
    public bool IsDeclaredRouteToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        EnsureRoutesCurrent();

        token = token.TrimStart('+');
        return _parser.FindRouteByToken(_config!, token) != null
            || _parser.FindRouteByToken(_config!, token.Split('/', 2)[0]) != null;
    }

    /// <summary>
    /// Headless (MCP/CLI) policy for the +tokens parsed from an entry body: which
    /// are routing directives and which are prose. With an explicit route the
    /// entry goes there only, so just that route and its /scope refinements are
    /// kept; without one, only declared routes are kept. Nothing here creates a
    /// route - an agent quoting "ASX +Security Description" or "+USD 5,999"
    /// once got a route named after the word, filed at the vault root.
    /// </summary>
    public (List<string> Routed, List<string> Ignored) PartitionHeadlessRouteTokens(
        IReadOnlyList<string> tokens, string? explicitRoute)
    {
        var explicitBase = string.IsNullOrWhiteSpace(explicitRoute)
            ? null
            : explicitRoute.TrimStart('+').Split('/', 2)[0];

        var routed = new List<string>();
        var ignored = new List<string>();
        foreach (var token in tokens)
        {
            var keep = explicitBase != null
                ? token.Split('/', 2)[0].Equals(explicitBase, StringComparison.OrdinalIgnoreCase)
                : IsDeclaredRouteToken(token);
            (keep ? routed : ignored).Add(token);
        }
        return (routed, ignored);
    }

    /// <summary>
    /// Check if entry should be routed
    /// Returns true if entry has project reference and entry type
    /// </summary>
    public bool ShouldRoute(Entry entry)
    {
        if (entry.Type == null)
            return false;  // No entry type = no routing

        if (entry.Projects == null || entry.Projects.Count == 0)
            return false;  // No project reference = no routing

        return true;
    }

    /// <summary>
    /// Raised after UpdateRoutes replaces routes.md. EntryCache keeps its own
    /// debounced route locator; the host wires this to EntryCache.InvalidateRoutes
    /// so a route declared mid-session filters reads at once (bead lmk-7t1p).
    /// </summary>
    public event Action? RoutesUpdated;

    /// <summary>
    /// Replace routes.md with new content. Validates, backs up, writes, and reloads.
    /// </summary>
    public RouteUpdateResult UpdateRoutes(string markdownContent)
    {
        // Normalize first: dedupe routes and scope entries so the persisted file is canonical
        // regardless of what content the caller sends. Idempotent.
        markdownContent = RouteSetSerializer.Normalize(markdownContent);

        // 1. Validate by parsing in-memory
        var config = RouteParser.ParseContent(markdownContent);

        // 2. Backup existing routes.md
        var routesFilePath = _parser.GetRoutesFilePath();
        var backupPath = routesFilePath + ".prev";
        if (File.Exists(routesFilePath))
            File.Copy(routesFilePath, backupPath, overwrite: true);

        // 3. Ensure directory exists and write
        var dir = Path.GetDirectoryName(routesFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(routesFilePath, markdownContent);

        // 4. Reload cache, and tell readers holding their own route view
        LoadRoutes();
        RoutesUpdated?.Invoke();

        // 5. Build result summary
        var sections = config.Routes
            .Where(r => r.Section != null)
            .Select(r => r.Section!)
            .Distinct()
            .ToList();

        var warnings = new List<string>();
        foreach (var route in config.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.Path))
                warnings.Add($"Route '{route.Name}' has no path (reference-only)");
        }

        _logger.Info($"Routes updated: {config.Routes.Count} routes in {sections.Count} sections");

        return new RouteUpdateResult
        {
            RouteCount = config.Routes.Count,
            Sections = sections,
            RouteNames = config.Routes.Select(r => r.Name).ToList(),
            VaultNotes = config.VaultNotes,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Get all configured routes.
    /// </summary>
    public IReadOnlyList<Route> GetRoutes()
    {
        EnsureRoutesCurrent();

        return _config?.Routes ?? new List<Route>();
    }

    /// <summary>
    /// Section headings (## H) declared in routes.md, excluding the Archived
    /// section. Empty for section-less (minimalist / project-focus) vaults.
    /// Exposed so headless callers (the create_route verb) can place a new route
    /// under the vault's own sections without assuming a fixed section set.
    /// </summary>
    public IReadOnlyList<string> GetSections() => _parser.GetSections();

    /// <summary>
    /// Append a route to routes.md and reload the cache. Public surface over the
    /// existing private append logic; lets non-prompt callers (lint fixers, scripts)
    /// add routes without going through ResolveAsync's prompt flow.
    /// </summary>
    public void AddRoute(Route route)
    {
        if (route == null) throw new ArgumentNullException(nameof(route));
        if (string.IsNullOrWhiteSpace(route.Name))
            throw new ArgumentException("Route.Name is required", nameof(route));

        AddRouteToFile(route);
        LoadRoutes();
    }

    /// <summary>
    /// Returns true if any of the entry's project references map to a route marked private.
    /// Based on entry intent (Projects list), not resolved destinations, so it works for all
    /// three VaultService log sites including the unrouted-fallback path.
    /// </summary>
    public bool EntryTargetsPrivateRoute(Entry entry)
    {
        if (_config == null || entry.Projects.Count == 0) return false;
        EnsureRoutesCurrent();
        foreach (var projectName in entry.Projects)
        {
            // Strip scope suffix (domain/subpath syntax) to match base token
            var baseName = projectName.Contains('/')
                ? projectName.Split('/', 2)[0]
                : projectName;
            // Accept either route name or unique path-head as the user's token.
            var route = _parser.FindRouteByToken(_config, baseName);
            if (route?.Private is not null) return true;
        }
        return false;
    }

    private static readonly Regex _routePrefixRegex = new(@"\+([A-Za-z0-9_\-/]+)", RegexOptions.Compiled);

    /// <summary>
    /// Returns "(private route)" if the content references any private route via a +token,
    /// otherwise returns the content as a one-line truncation. Used by diagnostic loggers
    /// (e.g. [paste-diag]) to prevent plaintext private-route content from leaking into
    /// _system/log.md. Safe to call before notation parsing — does its own +token scan.
    /// </summary>
    public string ScrubForLog(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return "";
        var projects = _routePrefixRegex.Matches(content)
            .Select(m => m.Groups[1].Value)
            .ToList();
        if (projects.Count > 0 &&
            EntryTargetsPrivateRoute(new Entry { Projects = projects }))
        {
            return "(private route)";
        }
        var oneline = content.Replace('\n', ' ').Replace('\r', ' ');
        return oneline.Length <= maxLength ? oneline : oneline.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Given an absolute or vault-relative file path, returns the configured
    /// Route whose <c>Path</c> is a prefix of the file's directory, or null if no
    /// route matches (unrouted file under _inbox/, a system file, etc.).
    /// Most-specific route wins when multiple routes have nested paths.
    /// </summary>
    public Route? FindRouteForVaultFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        EnsureRoutesCurrent();

        // Normalize to vault-relative under logmark/
        var normalized = filePath.Replace('\\', '/');
        var logmarkMarker = "/logmark/";
        var idx = normalized.LastIndexOf(logmarkMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var relative = normalized.Substring(idx + logmarkMarker.Length);

        // Match against routes, most-specific (longest path) first.
        return _config!.Routes
            .Where(r => !string.IsNullOrEmpty(r.Path))
            .Where(r => relative.StartsWith(r.Path!.Replace('\\', '/') + "/",
                                            StringComparison.OrdinalIgnoreCase)
                     || relative.Equals(r.Path!.Replace('\\', '/'),
                                        StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Path!.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Generate prompt options for a new route based on routes.md sections.
    /// </summary>
    /// <param name="routeName">The route name to generate options for</param>
    /// <returns>Options with choices based on sections, or empty choices for minimalist vaults</returns>
    public RoutePromptOptions GeneratePromptOptions(string routeName)
    {
        EnsureRoutesCurrent();

        var sections = _parser.GetSections()
            .Where(s => !s.Equals("Archived", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sections.Count == 0)
        {
            // No sections (minimalist/project-focus) - just path field
            return new RoutePromptOptions(
                Choices: Array.Empty<RouteOption>(),
                DefaultPath: routeName
            );
        }

        var allRoutes = _config!.Routes;
        var choices = sections.Select(section => new RouteOption(
            Label: SingularizeSection(section),
            SuggestedPath: InferSuggestedPath(section, routeName, allRoutes)
        )).ToList();

        return new RoutePromptOptions(choices, choices[0].SuggestedPath);
    }

    /// <summary>
    /// Create a Route from the prompt result.
    /// </summary>
    /// <param name="routeName">The route name</param>
    /// <param name="result">The user's choice from the prompt</param>
    /// <returns>The created route, or null if cancelled</returns>
    public Route? CreateRouteFromResult(string routeName, RoutePromptResult result)
    {
        if (result.Outcome == RoutePromptOutcome.Cancel)
            return null;

        var route = new Route
        {
            Name = routeName,
            Path = result.Outcome == RoutePromptOutcome.Create ? result.Path : null
        };

        return route;
    }

    /// <summary>
    /// Resolve a route name to a Route, honoring the given creation policy.
    /// Prompt: delegate to IRoutePromptService (returns null if cancelled or no prompt service).
    /// AutoCreate: create the route headlessly under the first section in routes.md.
    /// RejectIfUnknown: throw InvalidOperationException if the route is not declared.
    /// </summary>
    public async Task<Route?> ResolveAsync(string routeName, RouteCreationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            throw new ArgumentException("Route name cannot be empty", nameof(routeName));

        EnsureRoutesCurrent();

        var existing = _parser.FindRoute(_config!, routeName);
        if (existing != null)
            return existing;

        // Unknown single-char tokens are ignored by discovery, never created.
        // NotationParser's ProjectPattern matches any whitespace-preceded +word,
        // so ordinary prose ("stop at +X%", "chmod +x") reaches here as a route
        // token. The desktop capture layer already screens these with
        // IsDiscoverableRouteToken; MCP and CLI writes did not, which is how a
        // route named X (path: X) was created from an entry body on 2026-07-24.
        // Resolution of an explicitly-declared 1-char route is unaffected -
        // this runs only after the lookup above missed.
        if (policy != RouteCreationPolicy.RejectIfUnknown
            && !NotationParser.IsDiscoverableRouteToken(routeName))
        {
            _logger.Info(
                $"Ignoring undeclared route token +{routeName} (below the discovery floor); entry left unrouted for this token");
            return null;
        }

        switch (policy)
        {
            case RouteCreationPolicy.RejectIfUnknown:
                throw new InvalidOperationException(
                    $"Route '{routeName}' is not declared in routes.md and policy is RejectIfUnknown");

            case RouteCreationPolicy.Prompt:
                if (_promptService == null)
                    return null;
                var opts = GeneratePromptOptions(routeName);
                var promptResult = await _promptService.PromptForRouteCreationAsync(routeName, opts);
                var promptRoute = CreateRouteFromResult(routeName, promptResult);
                if (promptRoute == null)
                    return null;
                AddRouteToFile(promptRoute);
                LoadRoutes();
                return _parser.FindRoute(_config!, routeName);

            case RouteCreationPolicy.AutoCreate:
                var options = GeneratePromptOptions(routeName);
                var path = options.DefaultPath;
                var autoRoute = new Route { Name = routeName, Path = path };
                AddRouteToFile(autoRoute);
                LoadRoutes();
                _logger.Info($"AutoCreate route: +{routeName} -> {path}");
                return _parser.FindRoute(_config!, routeName);

            default:
                throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }

    /// <summary>
    /// Route entry to destinations based on project references
    /// Returns list of destination paths for daily log reference
    /// </summary>
    public async Task<List<RouteDestination>> RouteEntryAsync(Entry entry)
    {
        EnsureRoutesCurrent();

        var destinations = new List<RouteDestination>();

        // Keep only most-specific project references. If both "+fin-inv" and
        // "+fin-inv/trade-ideas" appear in the entry, the bare form is a slash-
        // boundary prefix of the scoped form: routing both writes the entry to
        // BOTH the parent and scoped folders, which is never the intent. Drop
        // any project that is a strict slash-delimited prefix of another.
        var projectsToRoute = MostSpecificRoutes(entry.Projects);

        foreach (var projectName in projectsToRoute)
        {
            // Check for subpaths (domain/subpath syntax)
            string baseName = projectName;
            string? subpath = null;

            if (projectName.Contains('/'))
            {
                var parts = projectName.Split('/', 2);
                baseName = parts[0];
                subpath = parts[1];
            }

            // User-facing token: accept either route name or unique path-head.
            var route = _parser.FindRouteByToken(_config!, baseName);

            if (route == null)
            {
                // Below the discovery floor: prose like "stop at +X%" is not a
                // routing directive. Skip rather than create (see ResolveAsync).
                if (!NotationParser.IsDiscoverableRouteToken(baseName))
                {
                    _logger.Info(
                        $"Ignoring undeclared route token +{projectName} (below the discovery floor)");
                    continue;
                }

                // Prompt user to create new route
                if (_promptService != null)
                {
                    var options = GeneratePromptOptions(baseName);
                    var result = await _promptService.PromptForRouteCreationAsync(baseName, options);

                    if (result.Outcome == RoutePromptOutcome.Cancel)
                    {
                        _logger.Info($"Route creation cancelled for +{projectName}");
                        continue;
                    }

                    // Create the route from result
                    route = CreateRouteFromResult(baseName, result);
                    if (route == null)
                    {
                        continue;
                    }

                    AddRouteToFile(route);

                    // Reload routes to include new route
                    LoadRoutes();

                    // Find the newly added route
                    route = _parser.FindRoute(_config!, baseName);
                    if (route == null)
                    {
                        _logger.Error($"Failed to load newly created route: +{projectName}");
                        continue;
                    }
                }
                else
                {
                    _logger.Warn($"Route not found: +{projectName} (entry not routed)");
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(route.Path))
            {
                // Reference-only route
                _logger.Debug($"Reference-only route: +{projectName} (not routed)");
                continue;
            }

            // Handle subpaths - allowed for all routes
            Route effectiveRoute = route;
            string? newScopeName = null;
            if (!string.IsNullOrEmpty(subpath))
            {
                // Check if this scope is already registered
                bool isNewScope = !route.Scopes.ContainsKey(subpath);

                if (isNewScope)
                {
                    // Auto-register the new scope
                    AddScopeToRoute(route.Name, subpath);
                    newScopeName = subpath;

                    // Reload routes to update the cache with the new scope
                    LoadRoutes();

                    _logger.Info($"Auto-registered new scope: +{route.Name}/{subpath}");
                }

                // Create effective route with subpath appended
                effectiveRoute = new Route
                {
                    Name = projectName,  // Full name with subpath
                    Section = route.Section,
                    Path = $"{route.Path}/{subpath}",
                    Tags = route.Tags,
                    Description = route.Description
                };

                _logger.Debug($"Subpath: +{projectName} -> {effectiveRoute.Path}");
            }

            // Route the entry
            try
            {
                var (destPath, writeResult) = await _fileRouter.RouteEntryAsync(entry, effectiveRoute);
                destinations.Add(new RouteDestination
                {
                    RouteName = effectiveRoute.Name,
                    Path = destPath,
                    RoutePath = effectiveRoute.Path,
                    Section = effectiveRoute.Section,
                    NewScopeName = newScopeName,
                    BaseRouteName = !string.IsNullOrEmpty(subpath) ? baseName : null,
                    Write = writeResult,
                    ShortId = entry.ShortId
                });
            }
            catch (LogMark.Exceptions.RoutePrivateDesktopOnlyException)
            {
                // Propagate to surface-layer catch: write tools (MCP, CLI) translate this
                // to a structured error telling the user to use the desktop app instead.
                throw;
            }
            catch (LogMark.Exceptions.LicenseRequiredException)
            {
                // Propagate to surface-layer catch: on free tier, private-route attempt
                // surfaces the same "desktop only" message.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to route to +{projectName}: {ex.Message}");
            }
        }

        // Each write mints its own id, so entry.ShortId is the LAST copy's. Callers
        // report destinations[0].Path; point the id at that same copy.
        if (destinations.Count > 1)
            entry.ShortId = destinations[0].ShortId;

        return destinations;
    }

    /// <summary>
    /// Route entry to destinations (sync version for tests)
    /// Does not support route creation prompts
    /// </summary>
    public List<RouteDestination> RouteEntry(Entry entry)
    {
        return RouteEntryAsync(entry).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Drop any project name that is a strict slash-delimited prefix of another
    /// project name in the same list. So ["fin-inv", "fin-inv/trade-ideas"] -->
    /// ["fin-inv/trade-ideas"]. "fin" is NOT considered a prefix of
    /// "fin-inv/trade-ideas" because the next character is not a slash.
    /// Preserves original order of surviving entries; case-insensitive.
    /// </summary>
    internal static List<string> MostSpecificRoutes(IReadOnlyList<string> projects)
    {
        var result = new List<string>(projects.Count);
        for (int i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            var dropped = false;
            for (int j = 0; j < projects.Count; j++)
            {
                if (i == j) continue;
                var other = projects[j];
                if (other.Length > p.Length
                    && other.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                    && other[p.Length] == '/')
                {
                    dropped = true;
                    break;
                }
            }
            if (!dropped) result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Add route to routes.md file
    /// </summary>
    private void AddRouteToFile(Route route)
    {
        var routesFilePath = _parser.GetRoutesFilePath();

        // Read existing content
        var lines = File.Exists(routesFilePath)
            ? File.ReadAllLines(routesFilePath).ToList()
            : new List<string>();

        // For new interface, we add routes at the end of the file
        // since the path already contains the section prefix (e.g., "projects/acme")
        // We could infer section from path, but simpler to just append

        // Add blank line if needed
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            lines.Add("");

        lines.Add($"### {route.Name}");

        if (!string.IsNullOrWhiteSpace(route.Path))
            lines.Add($"- path: {route.Path}");

        lines.Add("");

        // Write back to file
        File.WriteAllLines(routesFilePath, lines);

        _logger.Info($"Added route to routes.md: +{route.Name} (path: {route.Path ?? "none"})");
    }

    /// <summary>
    /// Add a scope to an existing route in routes.md.
    /// Creates the scopes block if it doesn't exist.
    /// </summary>
    private void AddScopeToRoute(string routeName, string scopeName)
    {
        var routesFilePath = _parser.GetRoutesFilePath();
        if (!File.Exists(routesFilePath))
            return;

        var lines = File.ReadAllLines(routesFilePath).ToList();
        var routeLineIndex = -1;
        var scopesLineIndex = -1;
        var lastPropertyLineIndex = -1;

        // Find the route header and its properties
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            // Found our route header
            if (line.Equals($"### {routeName}", StringComparison.OrdinalIgnoreCase))
            {
                routeLineIndex = i;
                continue;
            }

            // If we've found the route, look for its properties
            if (routeLineIndex >= 0)
            {
                // Another route header means we've passed our route's section
                if (line.StartsWith("### ") || line.StartsWith("## "))
                    break;

                // Track property lines
                if (line.StartsWith("- "))
                {
                    lastPropertyLineIndex = i;

                    // Check if this is the scopes property
                    if (line.StartsWith("- scopes:", StringComparison.OrdinalIgnoreCase))
                    {
                        scopesLineIndex = i;
                    }
                }
            }
        }

        if (routeLineIndex < 0)
        {
            _logger.Warn($"Could not find route '{routeName}' in routes.md to add scope");
            return;
        }

        if (scopesLineIndex >= 0)
        {
            // Scopes block exists - find where to insert the new scope
            // Insert after the last scope entry (or after "- scopes:" if empty)
            int insertIndex = scopesLineIndex + 1;
            while (insertIndex < lines.Count)
            {
                var nextLine = lines[insertIndex];
                // Check if still in scopes block (indented lines)
                if (nextLine.StartsWith("  ") || nextLine.StartsWith("\t"))
                {
                    insertIndex++;
                }
                else
                {
                    break;
                }
            }

            lines.Insert(insertIndex, $"  - {scopeName}:");
        }
        else
        {
            // No scopes block - create one after the last property
            int insertIndex = lastPropertyLineIndex >= 0 ? lastPropertyLineIndex + 1 : routeLineIndex + 1;
            lines.Insert(insertIndex, "- scopes:");
            lines.Insert(insertIndex + 1, $"  - {scopeName}:");
        }

        File.WriteAllLines(routesFilePath, lines);
        _logger.Info($"Added scope '{scopeName}' to route +{routeName} in routes.md");
    }

    /// <summary>
    /// Convert plural section name to singular label.
    /// "Projects" -> "Project", "Areas" -> "Area", "Clients" -> "Client"
    /// </summary>
    private static string SingularizeSection(string section)
    {
        // Simple singularization - just trim trailing 's'
        if (section.EndsWith("s", StringComparison.OrdinalIgnoreCase) && section.Length > 1)
        {
            return section[..^1];
        }
        return section;
    }

    /// <summary>
    /// Infer the suggested path for a new route in the given section. Looks at
    /// existing routes in that section: if the majority have paths starting with
    /// "{section.ToLower()}/" the suggestion follows the nested convention
    /// ("section/routeName"); otherwise the suggestion is flat ("routeName").
    /// Ties default to flat. Empty section (no existing routes) defaults to flat.
    ///
    /// Shared by GeneratePromptOptions (submit-time prompt) and RouteDiscoveryPopup
    /// (inline popup on +name+space).
    /// </summary>
    public static string InferSuggestedPath(string section, string routeName, IReadOnlyList<Route> allRoutes)
    {
        if (string.IsNullOrWhiteSpace(section))
            return routeName;

        var sectionRoutes = allRoutes
            .Where(r => string.Equals(r.Section, section, StringComparison.OrdinalIgnoreCase))
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .ToList();

        if (sectionRoutes.Count == 0)
            return routeName;

        var prefix = section.ToLowerInvariant() + "/";
        var nested = sectionRoutes.Count(r => r.Path!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var flat = sectionRoutes.Count - nested;

        return nested > flat
            ? $"{section.ToLowerInvariant()}/{routeName}"
            : routeName;
    }
}

public class RouteDestination
{
    public required string RouteName { get; set; }
    public required string Path { get; set; }

    /// <summary>The id written for this destination's copy of the entry.</summary>
    public string? ShortId { get; set; }
    public string? Section { get; set; }

    /// <summary>
    /// Resolved vault-relative route directory path (the route's <c>path:</c> from
    /// routes.md, with any subpath appended). Example: route alias "fin-inv" with
    /// path "domains/finance/investing" and scope "trade-ideas" yields
    /// "domains/finance/investing/trade-ideas". Distinct from <see cref="RouteName"/>,
    /// which is the alias plus subpath ("fin-inv/trade-ideas").
    /// </summary>
    public string? RoutePath { get; set; }

    /// <summary>
    /// If a new scope was auto-registered for this destination, contains the scope name.
    /// Null if no new scope was created.
    /// </summary>
    public string? NewScopeName { get; set; }

    /// <summary>
    /// The base route name (without scope) if this destination used a scope.
    /// </summary>
    public string? BaseRouteName { get; set; }

    /// <summary>
    /// The result of the vault write gate for this destination.
    /// Unmasked is true when the entry was written as plain text because Pro is inactive.
    /// </summary>
    public VaultWriteResult Write { get; set; } = VaultWriteResult.Clean;
}
