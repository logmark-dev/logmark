using System.Net;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Unit coverage for the entry-assembly cockpit prefix auto-detection added so
/// the LogMark.Daemon `cockpit-dist/` embedding (resource names
/// `LogMark.Daemon.cockpit.*`) is a live serving source, not just the Kernel
/// bake (`LogMark.cockpit.*`).
/// </summary>
public class CockpitPrefixDetectionTests
{
    [Theory]
    [InlineData("LogMark.Daemon.cockpit.index.html", "LogMark.Daemon.cockpit.")]
    [InlineData("LogMark.cockpit.assets.app-abc123.js", "LogMark.cockpit.")]
    [InlineData("LogMark.Daemon.cockpit.assets.index-Bl0hKN5f.js", "LogMark.Daemon.cockpit.")]
    public void Detects_prefix_up_to_and_including_the_cockpit_marker(string resource, string expected)
    {
        Assert.Equal(expected, CockpitStaticServer.DetectCockpitPrefix(new[] { resource }));
    }

    [Fact]
    public void Returns_null_when_no_resource_carries_the_marker()
    {
        Assert.Null(CockpitStaticServer.DetectCockpitPrefix(
            new[] { "LogMark.Resources.openapi.daemon-v0.json", "bundles.foo.txt" }));
    }
}

/// <summary>
/// Full-chain serving-seam assertions over the same DaemonHttpServer
/// composition production uses (via <see cref="CockpitStaticServerFixture"/>):
/// the v0 API answers, static serving at <c>/</c> never 500s (it serves the
/// API-only notice when no cockpit is embedded), and /api/* keeps priority.
/// </summary>
public class DaemonServingSeamTests : IClassFixture<CockpitStaticServerFixture>
{
    private readonly CockpitStaticServerFixture _f;

    public DaemonServingSeamTests(CockpitStaticServerFixture fixture)
    {
        _f = fixture;
    }

    [Fact]
    public async Task Health_responds_and_root_returns_a_page_never_500()
    {
        var health = await _f.Client.GetAsync("/api/v0/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var root = await _f.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.StartsWith("text/html", root.Content.Headers.ContentType?.MediaType);

        // With no cockpit baked in, `/` must serve the inline API-only notice,
        // not a 500 and not an empty body.
        if (!_f.HasCockpit)
        {
            var body = await root.Content.ReadAsStringAsync();
            Assert.Contains("API only", body);
        }
    }

    [Fact]
    public async Task Api_routes_keep_priority_over_static_serving()
    {
        var res = await _f.Client.GetAsync("/api/nope");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }
}
