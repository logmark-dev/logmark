namespace LogMark.Models.AgentMemory;

/// <summary>One normalized memory fact from any agent source.</summary>
public record AgentMemoryFact(
    string SourceId,            // "claude-code", "codex"
    string Project,             // decoded display name, e.g. "PhotoMind"
    string FactId,              // source filename stem, e.g. "feedback_camera_timezone_drift"
    string Title,               // frontmatter name, fallback FactId
    string Description,         // frontmatter description, fallback first body line
    string Body,                // full body below frontmatter, verbatim
    string SourceType,          // user|feedback|project|reference|unknown
    string SourcePath,          // absolute path of the source file
    string ContentHash,         // sha256:... of the raw source file text
    DateTime SourceModifiedAt);

public record AgentProjectInfo(string Project, string Directory, int FactCount);

public record AgentSourceInfo(
    string SourceId,
    bool Found,
    string? RootPath,
    IReadOnlyList<AgentProjectInfo> Projects,
    string? Note);              // e.g. "found, no memories yet" for Codex

public enum FactClass { New, Unchanged, Changed, Conflict, RemovedAtSource }

/// <summary>A fact with its diff classification. MirrorPagePath is null for New.</summary>
public record PlannedFact(AgentMemoryFact? Fact, FactClass Class, string? MirrorPagePath,
    string? SourcePath, string? Project = null, RouteResolution? Resolution = null)
{
    public string EffectiveSourcePath => SourcePath ?? Fact!.SourcePath;
}

public record ImportPlan(
    string SourceId,
    IReadOnlyList<PlannedFact> Facts,
    IReadOnlyList<string> MalformedFiles)
{
    public int Count(FactClass c) { var n = 0; foreach (var f in Facts) if (f.Class == c) n++; return n; }
}

public record ImportFailure(string SourcePath, string Reason);

public record ImportReport(
    string SourceId,
    int NewCount, int UnchangedCount, int ChangedCount, int ConflictCount, int RemovedCount,
    IReadOnlyList<string> MalformedFiles,
    IReadOnlyList<ImportFailure> Failures,
    IReadOnlyList<string> ConflictPaths,
    PlanGroups? Groups = null);

/// <summary>Provenance frontmatter of one mirror subpage.</summary>
public record MirrorFrontmatter(
    string Source,              // "claude-code"
    string SourceProject,
    string SourcePath,
    string SourceType,
    string ContentHash,         // hash of source file at import time
    string LandedHash,          // hash of the body we wrote (human-edit detection)
    string ImportedAt,          // yyyy-MM-dd
    string Status,              // "live" | "removed-at-source"
    string SuggestedRoute = "",       // resolved destination route path, or empty (design 4c)
    string RouteConfidence = "none",  // exact | ancestry | sibling | none
    string OverlapPrior = "low",      // low | candidate
    string PromotedAt = "");          // yyyy-MM-dd when promoted out of staging, else empty

/// <summary>Scanner output: one mirror page plus its current body hash.</summary>
public record MirrorRecord(string PagePath, MirrorFrontmatter Frontmatter, string CurrentBodyHash);
