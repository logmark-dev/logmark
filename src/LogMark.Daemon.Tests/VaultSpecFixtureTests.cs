using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryParser;
using LogMark.Services.Hosting.Http;
using LogMark.Services.Vault;
using Xunit;
using Xunit.Abstractions;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Conformance harness for the lm-vault spec. Runs the open kernel
/// parser over the spec's sample vaults (docs/spec-drafts/lm-vault/fixtures/)
/// and diffs the result against each fixture's committed expected.json, so the
/// reference implementation is conformance-bound to its own published format.
///
/// The expected.json files ARE the ground truth: they are generated from this
/// same parser (run this test class once with LOGMARK_WRITE_EXPECTED=1 to
/// regenerate them), never hand-authored.
///
/// Fixture location is a dual-path probe up the directory tree:
///   - private layout: &lt;umbrella&gt;/docs/spec-drafts/lm-vault/fixtures
///   - public layout:  &lt;tree-root&gt;/spec-fixtures  (the OSS mirror rebases
///     the fixtures/ subtree to spec-fixtures/ - see manifest.logmark.json)
/// The same probe mirrors the Kernel csproj's dual-location openapi resource.
/// If neither path exists the test skips with an explicit message rather than
/// failing (matching the skip convention used elsewhere in this project).
/// </summary>
public class VaultSpecFixtureTests
{
    private readonly ITestOutputHelper _output;

    public VaultSpecFixtureTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("valid-minimal")]
    [InlineData("valid-full")]
    [InlineData("invalid-frontmatter")]
    [InlineData("invalid-notation")]
    public void Fixture_ParsesToExpectedJson(string fixtureName)
    {
        var fixturesRoot = LocateFixturesRoot();
        if (fixturesRoot is null)
        {
            _output.WriteLine(
                "SKIP: no lm-vault spec fixtures found. Probed every ancestor of " +
                $"'{AppContext.BaseDirectory}' for 'docs/spec-drafts/lm-vault/fixtures' " +
                "(private layout) and 'spec-fixtures' (public mirror layout); neither exists.");
            return;
        }

        var fixtureDir = Path.Combine(fixturesRoot, fixtureName);
        if (!Directory.Exists(fixtureDir))
        {
            _output.WriteLine($"SKIP: fixture directory not present: {fixtureDir}");
            return;
        }

        var actualJson = ComputeFixtureJson(fixtureDir, fixtureName);
        var expectedPath = Path.Combine(fixtureDir, "expected.json");

        if (Environment.GetEnvironmentVariable("LOGMARK_WRITE_EXPECTED") == "1")
        {
            File.WriteAllText(expectedPath, actualJson + "\n");
            _output.WriteLine($"WROTE ground-truth expected.json: {expectedPath}");
            Assert.Fail("expected.json regenerated for this fixture - re-run without LOGMARK_WRITE_EXPECTED to verify conformance");
        }

        Assert.True(
            File.Exists(expectedPath),
            $"expected.json missing for fixture '{fixtureName}'. Regenerate with " +
            "LOGMARK_WRITE_EXPECTED=1 dotnet test --filter VaultSpecFixtureTests");

        var expectedJson = Normalize(File.ReadAllText(expectedPath));
        Assert.Equal(expectedJson, Normalize(actualJson));
    }

    // ---- fixture location (dual-path probe) --------------------------------

    private static string? LocateFixturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var priv = Path.Combine(dir.FullName, "docs", "spec-drafts", "lm-vault", "fixtures");
            if (Directory.Exists(priv)) return priv;
            var pub = Path.Combine(dir.FullName, "spec-fixtures");
            if (Directory.Exists(pub)) return pub;
            dir = dir.Parent;
        }
        return null;
    }

    // ---- conformance model (what the kernel parser emits over a vault) ------

    private static string ComputeFixtureJson(string vaultDir, string fixtureName)
    {
        var model = new FixtureDto(
            Vault: fixtureName,
            EntryFiles: BuildEntryFiles(vaultDir),
            Constitution: BuildConstitution(vaultDir),
            Routes: BuildRoutes(vaultDir));

        return JsonSerializer.Serialize(model, JsonOpts);
    }

    private static IReadOnlyList<EntryFileDto> BuildEntryFiles(string vaultDir)
    {
        var files = new List<EntryFileDto>();

        foreach (var abs in Directory.EnumerateFiles(vaultDir, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(vaultDir, abs).Replace('\\', '/');
            var parsed = PathConstants.ParsePath(rel);
            if (parsed is null) continue; // not an entry file (manifest, routes.md, tags.md, ...)

            var typeHint = EntryTypeMapper.InferTypeFromPath(rel);
            var content = File.ReadAllText(abs);
            var entries = EntryParser.Parse(content, typeHint)
                .Select(ToDto)
                .ToList();

            files.Add(new EntryFileDto(rel, parsed.Route, parsed.TypeName, entries));
        }

        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }

    private static EntryDto ToDto(ParsedEntry e) => new(
        Type: e.Type.ToString(),
        Timestamp: e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        Context: e.Context,
        Body: e.Content,
        InlineRoutes: NotationParser.ExtractProjects(e.Content),
        Tags: NotationParser.ExtractTags(e.Content),
        Source: e.Source,
        Session: e.Session,
        Refs: e.Refs.Select(r => r.ToString()).ToList(),
        ShortId: e.ShortId,
        IsCompleted: e.IsCompleted,
        Resolution: e.Resolution,
        TaskDeadline: e.TaskMeta?.Deadline?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        PostponedCount: e.TaskMeta?.PostponedCount,
        ExtendedMetadata: new SortedDictionary<string, string>(
            e.ExtendedMetadata.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal));

    private static ConstitutionDto BuildConstitution(string vaultDir)
    {
        var path = ConstitutionLocator.Resolve(vaultDir);
        if (!File.Exists(path))
            return new ConstitutionDto(null, null, false, false,
                Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<string>());

        var c = new VaultConstitutionParser().Parse(File.ReadAllText(path));
        var scope = c.Scope;
        return new ConstitutionDto(
            VaultName: c.VaultName,
            Purpose: c.Purpose,
            HasConventions: c.Conventions != null,
            HasScope: scope != null,
            ScopeIncludedRoutes: scope?.IncludedRoutes ?? Array.Empty<string>(),
            ScopeExcludedRoutes: scope?.ExcludedRoutes ?? Array.Empty<string>(),
            ScopeIncludedTags: scope?.IncludedTags ?? Array.Empty<string>(),
            ScopeExcludedTags: scope?.ExcludedTags ?? Array.Empty<string>());
    }

    private static IReadOnlyList<RouteDto> BuildRoutes(string vaultDir)
    {
        var config = new RouteParser(vaultDir).Parse();
        return config.Routes
            .Select(r => new RouteDto(r.Name, r.Section, r.Path, r.Status, r.Tags ?? new List<string>()))
            .ToList();
    }

    // ---- serialization -----------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();

    // ---- DTOs --------------------------------------------------------------

    private sealed record FixtureDto(
        string Vault,
        IReadOnlyList<EntryFileDto> EntryFiles,
        ConstitutionDto Constitution,
        IReadOnlyList<RouteDto> Routes);

    private sealed record EntryFileDto(
        string Path,
        string? Route,
        string TypeName,
        IReadOnlyList<EntryDto> Entries);

    private sealed record EntryDto(
        string Type,
        string Timestamp,
        string? Context,
        string Body,
        IReadOnlyList<string> InlineRoutes,
        IReadOnlyList<string> Tags,
        string? Source,
        string? Session,
        IReadOnlyList<string> Refs,
        string? ShortId,
        bool IsCompleted,
        string? Resolution,
        string? TaskDeadline,
        int? PostponedCount,
        IReadOnlyDictionary<string, string> ExtendedMetadata);

    private sealed record ConstitutionDto(
        string? VaultName,
        string? Purpose,
        bool HasConventions,
        bool HasScope,
        IReadOnlyList<string> ScopeIncludedRoutes,
        IReadOnlyList<string> ScopeExcludedRoutes,
        IReadOnlyList<string> ScopeIncludedTags,
        IReadOnlyList<string> ScopeExcludedTags);

    private sealed record RouteDto(
        string Name,
        string? Section,
        string? Path,
        string? Status,
        IReadOnlyList<string> Tags);
}
