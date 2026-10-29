using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogMark.Services.Hosting.Cockpit;
using LogMark.Models;
using LogMark.Services.EntryCache;
using LogMark.Services.Hosting;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// The daemon HTTP API v0 route table (umbrella
/// docs/reference/daemon-http-api-spec.md). Response shapes are normatively
/// the cockpit's TS types in LogMark.Knowledge/src/types/. Missing-resource
/// reads return 200 with a null payload; errors are RFC 9457
/// application/problem+json (see WriteProblem).
/// </summary>
public sealed class V0Routes
{
    // No global WhenWritingNull: the contract requires explicit
    // { "entry": null } / { "page": null } for missing resources. Optional
    // DTO fields opt out individually via [JsonIgnore(WhenWritingNull)].
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // RFC 9457 problem type base. Dereferenceable stable URIs let a client map
    // an error to a fix without string-matching the human-readable detail.
    private const string ProblemBase = "https://logmark.dev/errors/";

    // The API contract version is the /v0 path segment; envelope spec version
    // mirrors VIEWBLOCK_SPEC_VERSION in packages/lblocks (no C# const yet).
    private const string ApiVersion = "v0";
    private const int EnvelopeSpecVersion = 1;

    private static readonly string ServerVersion =
        BuildInfo.DisplayVersion(typeof(V0Routes).Assembly);

    private static readonly DateTime StartedUtc = DateTime.UtcNow;

    // Embedded copy of the umbrella-authored contract doc
    // (schemas/openapi/daemon-v0.json), served byte-for-byte at
    // /api/v0/openapi.json. Loaded once and cached; null when the resource
    // was not bundled (a standalone LogMark clone built outside the
    // umbrella workspace).
    private const string OpenApiResourceName = "LogMark.Resources.openapi.daemon-v0.json";

    private static readonly Lazy<byte[]?> OpenApiDocument = new(LoadOpenApiDocument);

    private static byte[]? LoadOpenApiDocument()
    {
        using var stream = typeof(V0Routes).Assembly.GetManifestResourceStream(OpenApiResourceName);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private readonly VaultHostServices _services;
    private readonly ConceptPageStore _concepts;
    private readonly SettingsStore _settings;
    private readonly SseChannel _sse;
    private readonly HostIdentity? _host;

    public V0Routes(VaultHostServices services, ConceptPageStore concepts, SettingsStore settings, SseChannel sse, HostIdentity? host = null)
    {
        _host = host;
        _services = services;
        _concepts = concepts;
        _settings = settings;
        _sse = sse;
    }

    // Exposes the hosted vault path for CockpitCompatRoutes: the /api/pages
    // subpage resolver (CockpitPageResolver) needs it to compute the vault
    // content root, but unlike other compat routes it is not a pure v0
    // alias, so it cannot reach the vault path through TryDispatch.
    internal string VaultPath => _services.VaultPath;

    /// <summary>
    /// Returns true when the request was handled (SSE keeps the response
    /// open). Only claims paths under /api/v0/ - anything else returns false
    /// unhandled so DaemonHttpServer can offer the request to the cockpit
    /// compat routes (and, eventually, static serving) next. A path under
    /// /api/v0/ that matches no known route still gets the 404 problem
    /// response here (unchanged from before the compat split).
    /// </summary>
    public bool Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        var method = req.HttpMethod.ToUpperInvariant();

        if (!path.StartsWith("/api/v0/", StringComparison.Ordinal)) return false;

        try
        {
            if (TryDispatch(ctx, method, path)) return true;

            return WriteProblem(ctx, 404, "not-found", "Not found",
                $"No v0 route for {method} {path}.");
        }
        catch (Exception ex)
        {
            return WriteProblem(ctx, 500, "internal", "Internal error", ex.Message);
        }
    }

    /// <summary>
    /// The v0 route table, keyed on explicit (method, path) rather than
    /// re-deriving them from <paramref name="ctx"/>. Internal so
    /// CockpitCompatRoutes can alias a compat path onto its v0 counterpart
    /// (e.g. "/api/capabilities" -> "/api/v0/capabilities") and reuse the
    /// exact same handler body instead of duplicating it. Returns false
    /// (rather than writing a 404) when nothing matches, so callers can
    /// decide how to handle a miss - Handle() above writes the v0 404;
    /// CockpitCompatRoutes writes its own compat 404 for a miss under
    /// /api/*.
    /// </summary>
    internal bool TryDispatch(HttpListenerContext ctx, string method, string path)
    {
        var req = ctx.Request;

        switch (method, path)
        {
            case ("GET", "/api/v0/openapi.json"):
                return ServeOpenApiDocument(ctx);

            case ("GET", "/api/v0/health"):
                return WriteJson(ctx, new
                {
                    status = "ok",
                    vaultPath = _services.VaultPath,
                    uptimeSeconds = (long)(DateTime.UtcNow - StartedUtc).TotalSeconds,
                });

            case ("GET", "/api/v0/capabilities"):
            {
                var payload = new Dictionary<string, object?>
                {
                    ["adapter"] = "kernel-http",
                    ["capabilities"] = new
                    {
                        hasNotation = true,
                        hasFolderSemantics = true,
                        hasBehaviorCatalog = false,
                        hasFeedSystem = false,
                        hasSessions = true,
                        hasConceptProducer = _host?.HasConceptProducer ?? false,
                    },
                    ["vaultPath"] = _services.VaultPath,
                    ["versions"] = new { api = ApiVersion, server = ServerVersion, envelopeSpec = EnvelopeSpecVersion },
                };
                if (_host is not null)
                    payload["host"] = new { providerId = _host.ProviderId, product = _host.Product };
                return WriteJson(ctx, payload);
            }

            case ("GET", "/api/v0/vault/summary"):
                return WriteJson(ctx, new { summary = BuildSummary() });

            case ("GET", "/api/v0/entries"):
                return WriteJson(ctx, new { entries = ListEntries(req) });

            case ("GET", "/api/v0/search"):
                return WriteJson(ctx, new { results = Search(req) });

            case ("GET", "/api/v0/concepts"):
                return WriteJson(ctx, new { pages = _concepts.List() });

            case ("GET", "/api/v0/constitution"):
                return WriteJson(ctx, new { body = ReadConstitution() });

            case ("PUT", "/api/v0/constitution"):
                return PutConstitution(ctx);

            case ("GET", "/api/v0/settings"):
                return WriteJson(ctx, new { settings = _settings.Read() });

            case ("PUT", "/api/v0/settings"):
                return PutSettings(ctx);

            case ("GET", "/api/v0/events"):
                _sse.Attach(ctx.Response);
                return true;
        }

        if (method == "GET" && path.StartsWith("/api/v0/entries/", StringComparison.Ordinal))
        {
            var id = WebUtility.UrlDecode(path["/api/v0/entries/".Length..]);
            return WriteJson(ctx, new { entry = ReadEntry(id) });
        }

        if (path.StartsWith("/api/v0/concepts/", StringComparison.Ordinal))
        {
            var slug = WebUtility.UrlDecode(path["/api/v0/concepts/".Length..]);
            if (method == "GET")
                return WriteJson(ctx, new { page = _concepts.Read(slug) });
            if (method == "PUT")
                return PutConcept(ctx, slug);
        }

        return false;
    }

    // ---- Entries ----

    private List<EntrySummaryDto> ListEntries(HttpListenerRequest req)
    {
        var q = req.QueryString;
        var query = new SearchQuery
        {
            Types = ParseType(q["type"]),
            Route = string.IsNullOrEmpty(q["route"]) ? null : q["route"]!.TrimStart('+'),
            RequiredTags = string.IsNullOrEmpty(q["tag"]) ? null : new[] { q["tag"]! },
            After = DateTime.TryParse(q["since"], out var since) ? since : null,
            Limit = int.TryParse(q["limit"], out var limit) ? limit : 200,
        };
        // "Recent" over a wide window: the cockpit paginates client-side.
        var result = _services.SearchService.GetRecent(days: 3650, query);
        return Resolve(result.Entries);
    }

    private List<EntrySummaryDto> Search(HttpListenerRequest req)
    {
        var q = req.QueryString;
        var query = new SearchQuery
        {
            Text = q["q"] ?? "",
            Limit = int.TryParse(q["limit"], out var limit) ? limit : 50,
        };
        var result = _services.SearchService.Search(query);
        return Resolve(result.Entries);
    }

    private EntryDto? ReadEntry(string id)
    {
        var match = _services.EntryCache.GetAllEntries()
            .FirstOrDefault(e => EntryResolver.GetEntryId(e.Entry).Equals(id, StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;

        var resolved = _services.EntryResolver.Resolve(match, ResolveOptions.Full);
        return new EntryDto
        {
            Id = resolved.EntryId,
            Title = FirstLine(resolved.Content),
            Type = NormalizeType(resolved.EntryType),
            Route = resolved.Route,
            Tags = resolved.Tags,
            CreatedAt = Iso(resolved.Timestamp),
            UpdatedAt = Iso(resolved.Timestamp),
            Body = resolved.Content,
            Frontmatter = new Dictionary<string, object?>(),
            Path = resolved.Path,
            Scope = resolved.Scope,
            Source = resolved.Source,
            Backlinks = resolved.Backlinks.Select(b => new BacklinkDto
            {
                Id = b.EntryId,
                Title = b.ContentPreview,
                Kind = b.EntryType,
                Preview = b.ContentPreview,
                SourcePath = b.SourcePath,
            }).ToList(),
        };
    }

    private List<EntrySummaryDto> Resolve(IReadOnlyList<IndexedEntry> entries)
    {
        return _services.EntryResolver
            .ResolveMany(entries, ResolveOptions.Preview(200))
            .Select(r => new EntrySummaryDto
            {
                Id = r.EntryId,
                Title = FirstLine(r.Content),
                Type = NormalizeType(r.EntryType),
                Route = r.Route,
                Tags = r.Tags,
                CreatedAt = Iso(r.Timestamp),
                UpdatedAt = Iso(r.Timestamp),
            })
            .ToList();
    }

    private object BuildSummary()
    {
        var resolved = _services.EntryResolver.ResolveMany(
            _services.EntryCache.GetAllEntries(), ResolveOptions.Preview(200));
        var byType = resolved
            .GroupBy(r => NormalizeType(r.EntryType))
            .ToDictionary(g => g.Key, g => g.Count());
        // routes.md descriptions, so the cockpit's route page can say what a
        // route is for instead of generic text (bead lmk-jsnz item 2).
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in _services.RouteService.GetRoutes())
            if (!string.IsNullOrWhiteSpace(route.Description))
                descriptions.TryAdd(route.Name, route.Description);
        var allRoutes = resolved
            .Where(r => !string.IsNullOrEmpty(r.Route))
            .GroupBy(r => r.Route!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var item = new Dictionary<string, object?> { ["path"] = g.Key, ["count"] = g.Count(), ["name"] = "+" + g.Key };
                if (descriptions.TryGetValue(g.Key, out var description)) item["description"] = description;
                return item;
            })
            .ToList();
        var topRoutes = allRoutes.Take(10).ToList();
        var topTags = resolved
            .SelectMany(r => r.Tags)
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { tag = g.Key, count = g.Count() })
            .ToList();
        var recent = resolved
            .OrderByDescending(r => r.Timestamp)
            .Take(10)
            .Select(r => new
            {
                id = r.EntryId,
                title = FirstLine(r.Content),
                updatedAt = Iso(r.Timestamp),
                route = r.Route,
                type = NormalizeType(r.EntryType),
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["total_entries"] = resolved.Count,
            ["by_type"] = byType,
            ["recent_entries"] = recent,
            ["top_routes"] = topRoutes,
            ["all_routes"] = allRoutes,
            ["top_tags"] = topTags,
            ["total_concept_pages"] = _concepts.List().Count,
            ["route_count"] = allRoutes.Count,
            ["recent_activity_total"] = recent.Count,
        };
    }

    // ---- OpenAPI document ----

    private static bool ServeOpenApiDocument(HttpListenerContext ctx)
    {
        var bytes = OpenApiDocument.Value;
        if (bytes is null)
        {
            return WriteProblem(ctx, 404, "not-found", "Not found",
                "OpenAPI document was not bundled into this build (built outside the umbrella workspace).");
        }
        return WriteRawJson(ctx, bytes);
    }

    // ---- Constitution ----

    private string? ReadConstitution()
    {
        var path = ConstitutionLocator.Resolve(_services.VaultPath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private bool PutConstitution(HttpListenerContext ctx)
    {
        var payload = ReadBody<JsonElement>(ctx);
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("body", out var body) ||
            body.ValueKind != JsonValueKind.String)
        {
            return WriteProblem(ctx, 400, "invalid-request", "Invalid request",
                "body must be a string");
        }
        File.WriteAllText(ConstitutionLocator.Resolve(_services.VaultPath), body.GetString()!);
        return WriteJson(ctx, new { ok = true });
    }

    // ---- Concepts / settings ----

    private bool PutConcept(HttpListenerContext ctx, string slug)
    {
        var payload = ReadBody<ConceptPutDto>(ctx);
        if (payload?.Page is null || string.IsNullOrEmpty(payload.Page.Title))
        {
            return WriteProblem(ctx, 400, "invalid-request", "Invalid request", "invalid page");
        }
        payload.Page.Slug = slug;
        _concepts.Write(payload.Page);
        return WriteJson(ctx, new { ok = true });
    }

    private bool PutSettings(HttpListenerContext ctx)
    {
        var payload = ReadBody<SettingsPutDto>(ctx);
        if (payload?.Settings is null || !SettingsStore.IsValid(payload.Settings))
        {
            return WriteProblem(ctx, 400, "invalid-request", "Invalid request", "invalid settings");
        }
        _settings.Write(payload.Settings);
        return WriteJson(ctx, new { ok = true });
    }

    // ---- Helpers ----

    private static IReadOnlyList<EntryType>? ParseType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        return Enum.TryParse<EntryType>(type, ignoreCase: true, out var parsed)
            ? new[] { parsed }
            : Array.Empty<EntryType>();
    }

    private static string NormalizeType(string entryType) => entryType.ToLowerInvariant();

    private static string FirstLine(string content)
    {
        var line = content.AsSpan();
        var nl = line.IndexOf('\n');
        return (nl >= 0 ? line[..nl] : line).Trim().ToString();
    }

    // Vault timestamps are naive local wall-clock values (## yyyy-MM-dd HH:mm
    // headers). Only a genuinely-UTC DateTime may carry the Z suffix; naive
    // values serialize unqualified so clients interpret them as local time.
    private static string Iso(DateTime timestamp) =>
        timestamp.Kind == DateTimeKind.Utc
            ? timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            : timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss");

    private static T? ReadBody<T>(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        var raw = reader.ReadToEnd();
        if (string.IsNullOrEmpty(raw)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, Json);
        }
        catch (JsonException)
        {
            // Malformed or mistyped JSON is an invalid request, not a fault:
            // returning default sends every PUT down its 400 problem+json
            // branch (bead lmk-vbhl).
            return default;
        }
    }

    // internal (not private): CockpitCompatRoutes reuses this writer directly
    // (e.g. for the /api/settings response, which layers an extra
    // `inference` field over the v0 settings shape) instead of duplicating
    // JSON-serialization plumbing.
    internal static bool WriteJson(HttpListenerContext ctx, object payload, int status = 200)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }

    // Raw-bytes passthrough for pre-serialized JSON documents (the embedded
    // OpenAPI doc). Deliberately separate from WriteJson: that method always
    // serializes an object through JsonSerializerOptions, whereas this one
    // must reproduce the source bytes exactly with no re-encoding.
    private static bool WriteRawJson(HttpListenerContext ctx, byte[] bytes, int status = 200)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }

    // RFC 9457 (application/problem+json). `code` is the stable machine key a
    // client branches on; `type` is its dereferenceable URI form; `detail` is
    // the human-readable message. `status` mirrors the HTTP status.
    // internal (not private): CockpitCompatRoutes reuses this writer directly
    // for its own 501 stubs and compat-surface 404s, so the RFC 9457 problem
    // shape stays in exactly one place.
    internal static bool WriteProblem(HttpListenerContext ctx, int status, string code, string title, string detail)
    {
        var problem = new Dictionary<string, object?>
        {
            ["type"] = ProblemBase + code,
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["code"] = code,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(problem, Json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }

    private sealed class ConceptPutDto
    {
        [JsonPropertyName("page")] public ConceptPageDto? Page { get; set; }
    }

    private sealed class SettingsPutDto
    {
        [JsonPropertyName("settings")] public CockpitSettings? Settings { get; set; }
    }
}

public class EntrySummaryDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("type")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Type { get; set; }
    [JsonPropertyName("route")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Route { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
}

public sealed class EntryDto : EntrySummaryDto
{
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("frontmatter")] public Dictionary<string, object?> Frontmatter { get; set; } = new();
    [JsonPropertyName("path")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Path { get; set; }
    [JsonPropertyName("scope")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Scope { get; set; }
    [JsonPropertyName("source")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; set; }
    [JsonPropertyName("backlinks")] public List<BacklinkDto> Backlinks { get; set; } = new();
}

/// <summary>An entry that links to this one (schema Entry.backlinks, bead lmk-nziz).</summary>
public sealed class BacklinkDto
{
    [JsonPropertyName("id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("preview")] public string Preview { get; set; } = "";
    [JsonPropertyName("sourcePath")] public string SourcePath { get; set; } = "";
}
