using System.Net;
using System.Reflection;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Embedded static server for the LogMark.Knowledge cockpit SPA
/// (embedded-hosting design, lmk-nhjr Task 3). Serves the client bundle
/// baked into LogMark.Kernel.csproj at compile time (see that file's
/// comment for the Exists-guarded glob) for every non-/api path
/// <see cref="DaemonHttpServer"/> offers it - <see cref="CockpitCompatRoutes"/>
/// already claims and terminates every /api/* request (200, its own 404, or
/// 501), so this class never sees one.
///
/// Naming: the glob uses <c>Link</c> + <c>%(RecursiveDir)</c> only, no
/// explicit <c>LogicalName</c> - the same shape as the pre-existing bundles
/// glob in the csproj. This was verified empirically (a scratch glob built
/// both ways, resource names enumerated via
/// <see cref="Assembly.GetManifestResourceNames"/>):
/// - Link-derived default naming auto-prefixes with the assembly's
///   RootNamespace and joins path segments with '.', sanitizing
///   intermediate directory segments to valid identifiers (a hyphenated
///   folder name gets its hyphens turned to underscores - see
///   BundleService's "code-context" -&gt; "code_context") but leaves the
///   trailing filename segment untouched, so a Vite asset hash like
///   "index-Bl0hKN5f.js" keeps its hyphens.
/// - An explicit <c>LogicalName</c> with <c>%(RecursiveDir)</c> DOES expand
///   per item (no collisions across recursive files), but the value is used
///   completely verbatim: no RootNamespace prefix is added, and the
///   RecursiveDir separator stays a literal backslash instead of a dot
///   (e.g. "cockpitScratchLogical.assets\index-Ba-4qbS6.css").
///
/// Because the cockpit's own directories ("cockpit", "assets") contain no
/// hyphens, the request-path -&gt; resource-name mapping below (prefix +
/// slash-to-dot join, filename kept literal) is exact with no extra
/// sanitization required.
/// </summary>
public sealed class CockpitStaticServer
{
    internal const string ResourcePrefix = "LogMark.cockpit.";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
        [".ico"] = "image/x-icon",
    };

    /// <summary>The marker segment every cockpit resource name carries.</summary>
    private const string CockpitMarker = ".cockpit.";

    /// <summary>
    /// One embedded-cockpit source: an assembly and the resource-name prefix
    /// its cockpit resources share. Two coexist in the shipped daemon - the
    /// Kernel-baked set (<see cref="ResourcePrefix"/>, from the sibling
    /// LogMark.Knowledge/dist/ui glob, private umbrella layout) and the entry
    /// assembly's set (from LogMark.Daemon's own cockpit-dist/ glob, prefix
    /// auto-detected; the layout-independent drop the OSS publish uses).
    ///
    /// Internal (not private) so <c>CockpitStaticServerMergeTests</c> can
    /// build synthetic sources directly - via <see cref="BuildSource"/> - and
    /// exercise the multi-source merge logic in <see cref="Handle"/>
    /// (precedence, per-source index.html fallback, stream routing)
    /// deterministically, without depending on two real embedded cockpit
    /// builds existing side by side.
    /// </summary>
    internal sealed record Source(Assembly Assembly, string Prefix, HashSet<string> Names);

    private readonly List<Source> _sources;

    public CockpitStaticServer() : this(DefaultSources())
    {
    }

    // Internal ctor so tests can force a single-assembly probe deterministically
    // (pass an assembly with no *.cockpit.* resources to hit the "no cockpit
    // baked in" branch) rather than depending on whether LogMark.Knowledge/
    // dist/ui happened to exist when THIS assembly was last built.
    internal CockpitStaticServer(Assembly assembly)
        : this(new List<Source> { BuildSource(assembly, ExpectedKnownPrefix(assembly)) })
    {
    }

    // Internal so tests can construct a server over an arbitrary, ordered
    // list of sources - e.g. two synthetic in-memory sources built from this
    // test assembly's own embedded fixtures - to exercise the multi-source
    // merge behavior in Handle() (exact-match precedence by list order,
    // per-source index.html fallback, correct stream-per-source routing)
    // directly, rather than only indirectly via DefaultSources().
    internal CockpitStaticServer(List<Source> sources)
    {
        _sources = sources;
    }

    // The shipped composition scans two assemblies: the Kernel (this type's
    // own assembly, the umbrella-layout bake) and the entry assembly (the
    // `logmark` binary, the LogMark.Daemon cockpit-dist/ bake). Either, both,
    // or neither may carry a cockpit; the sources merge and a request resolves
    // against whichever holds the asset.
    private static List<Source> DefaultSources()
    {
        var kernel = typeof(CockpitStaticServer).Assembly;
        var sources = new List<Source> { BuildSource(kernel, ResourcePrefix) };

        var entry = Assembly.GetEntryAssembly();
        if (entry is not null && entry != kernel)
        {
            sources.Add(BuildSource(entry, knownPrefix: null));
        }
        return sources;
    }

    // Kernel's cockpit resources always carry the fixed ResourcePrefix
    // ("LogMark.<RootNamespace-is-LogMark>.cockpit." collapses to
    // "LogMark.cockpit."), so pass it explicitly there; any other assembly
    // (entry binary, a test assembly) has its prefix auto-detected from the
    // marker so a different RootNamespace (e.g. "LogMark.Daemon.cockpit.")
    // still resolves.
    private static string? ExpectedKnownPrefix(Assembly assembly) =>
        assembly == typeof(CockpitStaticServer).Assembly ? ResourcePrefix : null;

    // Internal (not private) so tests can build synthetic Source instances
    // from an arbitrary explicit prefix - see the Source doc comment above.
    internal static Source BuildSource(Assembly assembly, string? knownPrefix)
    {
        var all = assembly.GetManifestResourceNames();
        var prefix = knownPrefix ?? DetectCockpitPrefix(all);
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (prefix is not null)
        {
            foreach (var name in all)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    names.Add(name);
                }
            }
        }
        return new Source(assembly, prefix ?? string.Empty, names);
    }

    /// <summary>
    /// Derives a cockpit resource prefix from a set of manifest resource names:
    /// the substring up to and including the first <c>.cockpit.</c> marker
    /// (e.g. "LogMark.Daemon.cockpit.index.html" -&gt; "LogMark.Daemon.cockpit.").
    /// Null when no name carries the marker. Internal for direct unit coverage.
    /// </summary>
    internal static string? DetectCockpitPrefix(IEnumerable<string> resourceNames)
    {
        foreach (var name in resourceNames)
        {
            var idx = name.IndexOf(CockpitMarker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return name[..(idx + CockpitMarker.Length)];
            }
        }
        return null;
    }

    /// <summary>
    /// True when this binary (Kernel bake or entry-assembly bake) was built
    /// with a cockpit SPA embedded. Tests use this as the probe to skip
    /// cockpit-serving assertions gracefully on a build that never ran
    /// `npm run build` in LogMark.Knowledge/ nor populated cockpit-dist/.
    /// </summary>
    public bool HasCockpit => _sources.Any(s => s.Names.Count > 0);

    /// <summary>
    /// Handles a non-/api request. Always writes a response and returns
    /// true: an exact resource match, the SPA's index.html for any
    /// unmatched client-side route, or (when the build carries no cockpit
    /// at all) a small inline "API only" notice. Never a 500 - there is
    /// nothing here that should fail short of a response already sent.
    /// </summary>
    public bool Handle(HttpListenerContext ctx)
    {
        // Static assets and the SPA shell are GET-only; anything else falls
        // through to the server's defensive 404 like the sibling routers.
        if (!string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!HasCockpit)
        {
            return WriteApiOnlyNotice(ctx);
        }

        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var relative = path.TrimStart('/');
        if (relative.Length == 0) relative = "index.html";
        var suffix = relative.Replace('/', '.');

        // Exact asset match, tried against each source in registration order
        // (Kernel bake first, then the entry-assembly bake).
        foreach (var source in _sources)
        {
            var candidate = source.Prefix + suffix;
            if (source.Names.Contains(candidate) && TryWriteResource(ctx, source, candidate))
            {
                return true;
            }
        }

        // SPA fallback: an unmatched non-/api path (a client-side route
        // like /timeline) serves index.html so the SPA's own router takes
        // over instead of 404ing.
        foreach (var source in _sources)
        {
            var index = source.Prefix + "index.html";
            if (source.Names.Contains(index) && TryWriteResource(ctx, source, index))
            {
                return true;
            }
        }

        // Defensive: HasCockpit was true (some resource exists) but no source
        // holds index.html. Should not happen from a real Vite build, but this
        // must never fall through to a 500.
        return WriteApiOnlyNotice(ctx);
    }

    // Content type is derived from the RESOURCE name's own trailing
    // extension, not the request path: the request path for the root ("/")
    // and the SPA fallback (any unmatched client route) carries no
    // extension at all, but the resource it resolves to always does
    // (the glob's Link-derived naming keeps the real filename+extension
    // verbatim as the trailing segment - see the class doc).
    private static bool TryWriteResource(HttpListenerContext ctx, Source source, string resourceName)
    {
        using var stream = source.Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return false;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = ContentTypeFor(resourceName);
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }

    private static string ContentTypeFor(string path)
    {
        var ext = Path.GetExtension(path);
        return ContentTypes.TryGetValue(ext, out var type) ? type : "application/octet-stream";
    }

    private static bool WriteApiOnlyNotice(HttpListenerContext ctx)
    {
        const string html = """
            <!doctype html>
            <html>
            <head><meta charset="utf-8"><title>LogMark daemon</title></head>
            <body>
            <h1>LogMark daemon - API only</h1>
            <p>This build of the daemon does not carry an embedded cockpit UI.</p>
            <p>To get the UI, build the cockpit SPA first, then rebuild the daemon
            so it is baked in: from the repository root run
            <code>npm install</code>, <code>npm run build</code>, then
            <code>dotnet build src/LogMark.Daemon</code>. Release archives from
            the project's download channels already include the cockpit.</p>
            <p>The API surface is still live: <code>/api/v0/*</code> and the
            cockpit compat surface at <code>/api/*</code>.</p>
            </body>
            </html>
            """;
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
        return true;
    }
}
