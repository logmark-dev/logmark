using System.Net;
using System.Reflection;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Unit coverage for the multi-source merge logic in
/// <see cref="CockpitStaticServer.Handle"/> - previously untested (chunk A
/// review, IMPORTANT finding 3). Uses the internal sources-list constructor
/// and <see cref="CockpitStaticServer.BuildSource"/> to build the server over
/// two or three synthetic "cockpit builds" (A/B/C) embedded into this test
/// assembly (<c>TestFixtures/CockpitMerge/</c>), so the three merge behaviors
/// - exact-match precedence by source order, per-source index.html fallback,
/// and correct stream-per-source routing - are exercised deterministically,
/// without depending on two real LogMark.Knowledge builds coexisting.
///
/// Does not touch <see cref="CockpitStaticServerNoCockpitTests"/> or any
/// other existing test class - purely additive.
/// </summary>
public class CockpitStaticServerMergeTests
{
    private static readonly Assembly TestAssembly = typeof(CockpitStaticServerMergeTests).Assembly;

    /// <summary>
    /// Derives the actual embedded-resource prefix for one of the synthetic
    /// fixture folders ("cockpitA", "cockpitB", "cockpitC") by locating any
    /// resource name containing that folder marker, rather than hardcoding
    /// the RootNamespace - robust to a project rename.
    /// </summary>
    private static string PrefixFor(string folderMarker)
    {
        var marker = $".{folderMarker}.";
        var name = TestAssembly.GetManifestResourceNames()
            .First(n => n.Contains(marker, StringComparison.Ordinal));
        var idx = name.IndexOf(marker, StringComparison.Ordinal);
        return name[..(idx + marker.Length)];
    }

    private static CockpitStaticServer.Source SourceA() =>
        CockpitStaticServer.BuildSource(TestAssembly, PrefixFor("cockpitA"));

    private static CockpitStaticServer.Source SourceB() =>
        CockpitStaticServer.BuildSource(TestAssembly, PrefixFor("cockpitB"));

    /// <summary>Carries no index.html - only <c>onlyC.txt</c>.</summary>
    private static CockpitStaticServer.Source SourceC() =>
        CockpitStaticServer.BuildSource(TestAssembly, PrefixFor("cockpitC"));

    private static async Task<string> GetBody(CockpitStaticServer server, string path)
    {
        using var fixture = new DirectHandlerFixture(server);
        var res = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadAsStringAsync()).Trim();
    }

    [Fact]
    public void Fixture_sanity_each_synthetic_source_actually_carries_resources()
    {
        Assert.NotEmpty(SourceA().Names);
        Assert.NotEmpty(SourceB().Names);
        Assert.NotEmpty(SourceC().Names);
    }

    [Fact]
    public async Task Exact_match_precedence_follows_source_list_order()
    {
        // Both A and B carry "shared.txt" with distinct content. Handle()
        // tries the exact-match candidate against each source IN LIST ORDER
        // and returns on the first hit - so whichever source is registered
        // first wins, regardless of any other property.
        var aFirst = new CockpitStaticServer(new List<CockpitStaticServer.Source> { SourceA(), SourceB() });
        Assert.Equal("SHARED-FROM-A", await GetBody(aFirst, "/shared.txt"));

        var bFirst = new CockpitStaticServer(new List<CockpitStaticServer.Source> { SourceB(), SourceA() });
        Assert.Equal("SHARED-FROM-B", await GetBody(bFirst, "/shared.txt"));
    }

    [Fact]
    public async Task Spa_fallback_finds_index_html_on_the_first_source_that_carries_one()
    {
        // C carries no index.html at all. Registering C first must not break
        // the SPA fallback for an unmatched client-side route - Handle()'s
        // fallback loop must walk PAST a source with no index.html and land
        // on the next source that has one, rather than stopping (or
        // API-only-noticing) at the first source in the list.
        var server = new CockpitStaticServer(new List<CockpitStaticServer.Source> { SourceC(), SourceB() });

        var body = await GetBody(server, "/some/unmatched/client/route");

        Assert.Equal("INDEX-B", body);
    }

    [Fact]
    public async Task Exact_asset_requests_route_to_the_owning_sources_own_stream()
    {
        // A and B each carry an asset the other does not (onlyA.txt /
        // onlyB.txt). Requesting each must return THAT source's content, not
        // the other's and not a mix - proving TryWriteResource reads the
        // stream from the Source that actually owns the matched resource
        // name, not just whichever source happens to be first in the list.
        var server = new CockpitStaticServer(new List<CockpitStaticServer.Source> { SourceA(), SourceB() });

        Assert.Equal("ONLY-A", await GetBody(server, "/onlyA.txt"));
        Assert.Equal("ONLY-B", await GetBody(server, "/onlyB.txt"));
    }

    [Fact]
    public void HasCockpit_is_true_when_any_source_in_the_merged_list_carries_resources()
    {
        // C alone (no index.html, but it does carry onlyC.txt) is still
        // enough for HasCockpit - it is a per-source resource-count check,
        // not an index.html check.
        var server = new CockpitStaticServer(new List<CockpitStaticServer.Source> { SourceC() });

        Assert.True(server.HasCockpit);
    }
}
