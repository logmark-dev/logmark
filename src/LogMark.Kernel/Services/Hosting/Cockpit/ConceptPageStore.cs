using System.Text;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LogMark.Services.Hosting.Cockpit;

/// <summary>
/// Concept pages (daemon HTTP API v0 /concepts): frontmatter-bearing markdown
/// under the vault's canonical `&lt;contentRoot&gt;/_concepts/` directory, where
/// contentRoot is `&lt;vault&gt;/logmark` if that directory exists, else
/// `&lt;vault&gt;` itself (mirrors <see cref="Http.ConstitutionLocator"/>).
/// Writes always land canonical (the directory is created on first write).
/// List/Read check canonical first, then fall back to the legacy
/// `&lt;vault&gt;/concepts/` directory (what earlier daemon builds wrote) for
/// slugs not present canonically - canonical wins on a slug collision.
/// There is no automatic file migration: reading the legacy location in
/// place is the migration story for now. Pure file operations - the kernel
/// test is "reads/writes vault files with no judgment". Format and shapes
/// mirror LogMark.Knowledge/src/adapters/vanilla/concept-page-store.ts.
/// </summary>
public sealed class ConceptPageStore
{
    private readonly string _canonicalDir;
    private readonly string _legacyDir;

    public ConceptPageStore(string vaultPath)
    {
        var contentRoot = VaultContentRoot.Resolve(vaultPath);
        _canonicalDir = Path.Combine(contentRoot, "_concepts");
        _legacyDir = Path.Combine(vaultPath, "concepts");
    }

    public List<ConceptPageSummaryDto> List()
    {
        var bySlug = new Dictionary<string, ConceptPageSummaryDto>(StringComparer.Ordinal);
        foreach (var file in EnumerateMd(_legacyDir))
        {
            var page = ReadFile(file);
            if (page is null) continue;
            bySlug[page.Slug] = ToSummary(page);
        }
        foreach (var file in EnumerateMd(_canonicalDir))
        {
            var page = ReadFile(file);
            if (page is null) continue;
            bySlug[page.Slug] = ToSummary(page); // canonical wins on collision
        }
        return bySlug.Values.OrderBy(p => p.Slug, StringComparer.Ordinal).ToList();
    }

    public ConceptPageDto? Read(string slug)
    {
        var canonicalPath = PathFor(_canonicalDir, slug);
        if (File.Exists(canonicalPath)) return ReadFile(canonicalPath);
        var legacyPath = PathFor(_legacyDir, slug);
        return File.Exists(legacyPath) ? ReadFile(legacyPath) : null;
    }

    public void Write(ConceptPageDto page)
    {
        Directory.CreateDirectory(_canonicalDir);
        var front = new ConceptFrontmatter
        {
            Slug = page.Slug,
            Title = page.Title,
            Created = page.Created,
            LastDistilled = page.LastDistilled,
            DistillationVersion = page.DistillationVersion,
            Status = page.Status,
            Density = page.Density,
            Draft = page.Draft,
            Tags = page.Tags,
            Sources = page.Sources.Select(s => new ConceptSourceYaml
            {
                Id = s.Id,
                SnapshotHash = s.SnapshotHash,
                DistilledAt = s.DistilledAt,
            }).ToList(),
            RelatedConcepts = page.RelatedConcepts,
        };
        var yaml = YamlSerializer.Serialize(front).TrimEnd();
        var sb = new StringBuilder();
        sb.Append("---\n").Append(yaml).Append("\n---\n\n").Append(page.Body.TrimStart('\n'));
        File.WriteAllText(PathFor(_canonicalDir, page.Slug), sb.ToString());
    }

    private static IEnumerable<string> EnumerateMd(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private static ConceptPageSummaryDto ToSummary(ConceptPageDto page) => new()
    {
        Slug = page.Slug,
        Title = page.Title,
        Status = page.Status,
        Tags = page.Tags,
        LastDistilled = page.LastDistilled,
        SourceCount = page.Sources.Count,
        RelatedConcepts = page.RelatedConcepts,
        DistillationVersion = page.DistillationVersion,
        Density = page.Density,
        Draft = page.Draft,
    };

    private static string PathFor(string dir, string slug) => Path.Combine(dir, slug + ".md");

    private static ConceptPageDto? ReadFile(string path)
    {
        var raw = File.ReadAllText(path);
        if (!raw.StartsWith("---")) return null;
        var end = raw.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return null;

        var yaml = raw[4..end].Replace("\r\n", "\n");
        var bodyStart = raw.IndexOf('\n', end + 4);
        var body = bodyStart >= 0 ? raw[(bodyStart + 1)..].TrimStart('\n', '\r') : "";

        ConceptFrontmatter front;
        try
        {
            front = YamlDeserializer.Deserialize<ConceptFrontmatter>(yaml);
        }
        catch
        {
            return null;
        }

        return new ConceptPageDto
        {
            Slug = front.Slug ?? Path.GetFileNameWithoutExtension(path),
            Title = front.Title ?? "",
            Body = body,
            Status = front.Status ?? "draft",
            Draft = front.Draft,
            Density = front.Density,
            Tags = front.Tags ?? new List<string>(),
            Sources = (front.Sources ?? new List<ConceptSourceYaml>()).Select(s => new ConceptSourceDto
            {
                Id = s.Id ?? "",
                SnapshotHash = s.SnapshotHash ?? "",
                DistilledAt = s.DistilledAt ?? "",
            }).ToList(),
            RelatedConcepts = front.RelatedConcepts ?? new List<string>(),
            Created = front.Created ?? "",
            LastDistilled = front.LastDistilled ?? "",
            DistillationVersion = front.DistillationVersion,
        };
    }

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private sealed class ConceptFrontmatter
    {
        public string? Slug { get; set; }
        public string? Title { get; set; }
        public string? Created { get; set; }
        public string? LastDistilled { get; set; }
        public int DistillationVersion { get; set; } = 1;
        public string? Status { get; set; }
        public int? Density { get; set; }
        public bool? Draft { get; set; }
        public List<string>? Tags { get; set; }
        public List<ConceptSourceYaml>? Sources { get; set; }
        public List<string>? RelatedConcepts { get; set; }
    }

    private sealed class ConceptSourceYaml
    {
        public string? Id { get; set; }
        public string? SnapshotHash { get; set; }
        public string? DistilledAt { get; set; }
    }
}

public sealed class ConceptPageDto
{
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "draft";
    [JsonPropertyName("draft")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Draft { get; set; }
    [JsonPropertyName("density")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Density { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("sources")] public List<ConceptSourceDto> Sources { get; set; } = new();
    [JsonPropertyName("related_concepts")] public List<string> RelatedConcepts { get; set; } = new();
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("last_distilled")] public string LastDistilled { get; set; } = "";
    [JsonPropertyName("distillation_version")] public int DistillationVersion { get; set; } = 1;
}

public sealed class ConceptSourceDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("snapshot_hash")] public string SnapshotHash { get; set; } = "";
    [JsonPropertyName("distilled_at")] public string DistilledAt { get; set; } = "";
}

public sealed class ConceptPageSummaryDto
{
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "draft";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("last_distilled")] public string LastDistilled { get; set; } = "";
    [JsonPropertyName("source_count")] public int SourceCount { get; set; }
    [JsonPropertyName("related_concepts")] public List<string> RelatedConcepts { get; set; } = new();
    [JsonPropertyName("distillation_version")] public int DistillationVersion { get; set; } = 1;
    [JsonPropertyName("density")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Density { get; set; }
    [JsonPropertyName("draft")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Draft { get; set; }
}
