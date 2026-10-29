using System.Linq;
using LogMark.Services.Hosting.Cockpit;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Cockpit ConceptPageStore (daemon HTTP API v0 /concepts) canonical-dir +
/// legacy-fallback behavior, mirroring ConstitutionLocatorTests. Distinct
/// from LogMark.Core.Tests/Services/Insights/ConceptPageStoreTests.cs, which
/// covers the unrelated LogMark.Services.Insights.ConceptPageStore.
/// </summary>
public class ConceptPageStoreTests
{
    private static ConceptPageDto MakePage(string slug, string title = "Title") => new()
    {
        Slug = slug,
        Title = title,
        Body = "# " + title,
        Status = "draft",
        Created = "2026-07-18T00:00:00Z",
        LastDistilled = "2026-07-18T00:00:00Z",
    };

    [Fact]
    public void Write_LandsUnderCanonicalConceptsDirForWrappedVault()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-cps").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(vault, "logmark"));
            var store = new ConceptPageStore(vault);

            store.Write(MakePage("alpha"));

            var expected = Path.Combine(vault, "logmark", "_concepts", "alpha.md");
            Assert.True(File.Exists(expected));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void Write_LandsUnderContentRootConceptsDirForUnwrappedVault()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-cps").FullName;
        try
        {
            var store = new ConceptPageStore(vault);

            store.Write(MakePage("alpha"));

            var expected = Path.Combine(vault, "_concepts", "alpha.md");
            Assert.True(File.Exists(expected));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void LegacyOnlySlug_IsReadableAndListable()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-cps").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(vault, "logmark"));
            var legacyDir = Path.Combine(vault, "concepts");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllText(Path.Combine(legacyDir, "legacy-only.md"),
                "---\n" +
                "slug: legacy-only\n" +
                "title: Legacy Only\n" +
                "created: 2026-01-01T00:00:00Z\n" +
                "last_distilled: 2026-01-01T00:00:00Z\n" +
                "distillation_version: 1\n" +
                "status: draft\n" +
                "tags: []\n" +
                "sources: []\n" +
                "related_concepts: []\n" +
                "---\n\nBody.\n");

            var store = new ConceptPageStore(vault);

            var read = store.Read("legacy-only");
            Assert.NotNull(read);
            Assert.Equal("Legacy Only", read!.Title);

            var list = store.List();
            Assert.Contains(list, p => p.Slug == "legacy-only");
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void SlugCollision_CanonicalWinsOverLegacy()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-cps").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(vault, "logmark"));
            var legacyDir = Path.Combine(vault, "concepts");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllText(Path.Combine(legacyDir, "shared.md"),
                "---\n" +
                "slug: shared\n" +
                "title: Legacy Title\n" +
                "created: 2026-01-01T00:00:00Z\n" +
                "last_distilled: 2026-01-01T00:00:00Z\n" +
                "distillation_version: 1\n" +
                "status: draft\n" +
                "tags: []\n" +
                "sources: []\n" +
                "related_concepts: []\n" +
                "---\n\nLegacy body.\n");

            var store = new ConceptPageStore(vault);
            store.Write(MakePage("shared", "Canonical Title"));

            var read = store.Read("shared");
            Assert.Equal("Canonical Title", read!.Title);

            var list = store.List();
            Assert.Single(list, p => p.Slug == "shared");
            Assert.Equal("Canonical Title", list.Single(p => p.Slug == "shared").Title);
        }
        finally { Directory.Delete(vault, recursive: true); }
    }
}
