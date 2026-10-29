using System.Net;
using System.Text.Json;
using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

public sealed class CockpitPageSlugTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lm-slug-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Plant(params string[] relativePaths)
    {
        foreach (var rel in relativePaths)
        {
            var abs = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, "# " + rel);
        }
    }

    [Fact]
    public void Newer_diary_pages_layout_is_found()
    {
        Plant("projects/a/_diary/pages/x.md");
        Assert.Equal("projects/a/_diary/pages/x.md", CockpitPageResolver.FindBySlug(_root, "x.md"));
    }

    [Fact]
    public void Older_pages_diary_layout_is_found()
    {
        Plant("projects/a/_pages/diary/x.md");
        Assert.Equal("projects/a/_pages/diary/x.md", CockpitPageResolver.FindBySlug(_root, "x.md"));
    }

    [Fact]
    public void A_pages_folder_beats_a_plain_note()
    {
        Plant("notes/x.md", "projects/a/_diary/pages/x.md");
        Assert.Equal("projects/a/_diary/pages/x.md", CockpitPageResolver.FindBySlug(_root, "x.md"));
    }

    [Fact]
    public void Shallower_beats_deeper_then_ordinal()
    {
        Plant("b/c/x.md", "b/x.md", "a/x.md");
        Assert.Equal("a/x.md", CockpitPageResolver.FindBySlug(_root, "x.md"));
    }

    [Fact]
    public void System_and_dot_folders_are_never_searched()
    {
        Plant("_system/x.md", ".obsidian/x.md");
        Assert.Null(CockpitPageResolver.FindBySlug(_root, "x.md"));
    }

    [Fact]
    public void Match_is_case_insensitive_and_absent_is_null()
    {
        Plant("a/_diary/pages/My-Page.md");
        Assert.Equal("a/_diary/pages/My-Page.md", CockpitPageResolver.FindBySlug(_root, "my-page.md"));
        Assert.Null(CockpitPageResolver.FindBySlug(_root, "nope.md"));
    }

    [Fact]
    public void A_file_symlink_is_never_a_match()
    {
        var outside = Path.Combine(Path.GetTempPath(), "lm-slug-out-" + Guid.NewGuid().ToString("N")[..8] + ".md");
        File.WriteAllText(outside, "# outside");
        try
        {
            Directory.CreateDirectory(Path.Combine(_root, "a"));
            try
            {
                File.CreateSymbolicLink(Path.Combine(_root, "a", "link.md"), outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Symlink creation is not permitted on this host (Windows without privilege); CI on ubuntu runs it.
                return;
            }

            Assert.Null(CockpitPageResolver.FindBySlug(_root, "link.md"));
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }
}

public class CockpitPageSlugHttpTests : IClassFixture<CockpitPageResolverFixture>
{
    private readonly CockpitPageResolverFixture _f;

    public CockpitPageSlugHttpTests(CockpitPageResolverFixture f) => _f = f;

    [Fact]
    public async Task Bare_slug_resolves_through_the_pages_route()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=2026-01-18-x");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("projects/foo/_blocks/pages/2026-01-18-x.md", doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Traversal_is_still_rejected()
    {
        var res = await _f.Client.GetAsync("/api/pages?path=..%2Fsecrets");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
