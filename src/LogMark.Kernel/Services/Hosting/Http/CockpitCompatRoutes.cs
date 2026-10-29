using System.Net;
using LogMark.Services.Hosting.Cockpit;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Cockpit-compatibility route surface (cockpit embedded hosting design,
/// lmk-nhjr Task 1): aliases the LogMark.Knowledge SPA's `/api/*` contract
/// (LogMark.Knowledge/src/ui/api.ts, the `export const api` block) onto the
/// daemon's existing `/api/v0/*` handlers, so the same Node-served cockpit
/// UI can run unmodified against the daemon. Every alias below delegates to
/// <see cref="V0Routes.TryDispatch"/> with a remapped path - no handler body
/// is duplicated. Two shapes are NOT plain aliases:
///
/// - GET /api/settings layers an extra `inference: { active }` field over
///   the v0 settings payload (the SPA's KnowledgeSettings view model expects
///   it; the daemon has no live local-inference detection of its own, so
///   `active` is always reported as "none" here).
/// - GET /api/pages is the subpage resolver (lmk-nhjr Task 2): a port of
///   LogMark.Knowledge/src/server/routes/pages.ts, not a v0 alias (the
///   daemon's v0 surface has no page-resolution concept), so it is handled
///   by <see cref="CockpitPageResolver"/> directly rather than through
///   <see cref="V0Routes.TryDispatch"/>. See that class for the full
///   candidate-path and traversal-guard semantics.
/// - /api/distill, /api/health, /api/health/refresh remain Node-bound
///   cockpit features with no daemon equivalent yet; they return an honest
///   501 "not-implemented" problem.
/// </summary>
public sealed class CockpitCompatRoutes
{
    private readonly V0Routes _v0;
    private readonly SettingsStore _settings;

    public CockpitCompatRoutes(V0Routes v0, SettingsStore settings)
    {
        _v0 = v0;
        _settings = settings;
    }

    /// <summary>
    /// Returns true when the request was handled. Only claims paths under
    /// /api/ - non-API paths return false unhandled so DaemonHttpServer can
    /// offer the request to static serving next (Task 3). A path under
    /// /api/ that matches no known compat or v0 route gets a 404 problem
    /// response.
    /// </summary>
    public bool Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        var method = req.HttpMethod.ToUpperInvariant();

        if (!path.StartsWith("/api/", StringComparison.Ordinal)) return false;

        try
        {
            switch (method, path)
            {
                case ("GET", "/api/capabilities"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/capabilities");

                case ("GET", "/api/vault-summary"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/vault/summary");

                case ("GET", "/api/entries"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/entries");

                case ("GET", "/api/search"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/search");

                case ("GET", "/api/concepts"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/concepts");

                case ("GET", "/api/constitution"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/constitution");

                case ("PUT", "/api/constitution"):
                    return _v0.TryDispatch(ctx, "PUT", "/api/v0/constitution");

                case ("GET", "/api/settings"):
                    return GetSettingsWithInference(ctx);

                case ("PUT", "/api/settings"):
                    return _v0.TryDispatch(ctx, "PUT", "/api/v0/settings");

                case ("GET", "/api/events"):
                    return _v0.TryDispatch(ctx, "GET", "/api/v0/events");

                case ("GET", "/api/pages"):
                    return CockpitPageResolver.Handle(ctx, _v0.VaultPath);

                case ("POST", "/api/distill"):
                    return NotImplemented(ctx, "Distillation");

                case ("GET", "/api/health"):
                    return NotImplemented(ctx, "The health lint scan");

                case ("POST", "/api/health/refresh"):
                    return NotImplemented(ctx, "The health lint scan");
            }

            if (method == "GET" && path.StartsWith("/api/entries/", StringComparison.Ordinal))
            {
                var id = path["/api/entries/".Length..];
                return _v0.TryDispatch(ctx, "GET", "/api/v0/entries/" + id);
            }

            if (path.StartsWith("/api/concepts/", StringComparison.Ordinal))
            {
                var slug = path["/api/concepts/".Length..];
                if (method is "GET" or "PUT")
                    return _v0.TryDispatch(ctx, method, "/api/v0/concepts/" + slug);
            }

            // Any other unmatched /api/* path gets a 404 here.
            return V0Routes.WriteProblem(ctx, 404, "not-found", "Not found",
                $"No compat route for {method} {path}.");
        }
        catch (Exception ex)
        {
            return V0Routes.WriteProblem(ctx, 500, "internal", "Internal error", ex.Message);
        }
    }

    // GET /api/settings is the one compat endpoint that is not a pure alias:
    // the SPA's settings view model reads `inference.active` alongside the
    // settings payload (LogMark.Knowledge/src/server/routes/settings.ts
    // resolves it from a live local-inference probe). The daemon does not
    // run that detection, so "none" is reported honestly rather than
    // guessing from providerOrder[0].
    private bool GetSettingsWithInference(HttpListenerContext ctx)
    {
        var settings = _settings.Read();
        return V0Routes.WriteJson(ctx, new { settings, inference = new { active = "none" } });
    }

    private static bool NotImplemented(HttpListenerContext ctx, string feature)
    {
        return V0Routes.WriteProblem(ctx, 501, "not-implemented", "Not implemented",
            $"{feature} is not served by this host.");
    }
}
