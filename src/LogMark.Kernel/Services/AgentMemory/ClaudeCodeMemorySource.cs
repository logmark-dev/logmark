using LogMark.Models.AgentMemory;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Reads Claude Code auto-memory: ~/.claude/projects/&lt;encoded&gt;/memory/*.md,
/// one markdown file per fact, MEMORY.md index skipped (derivable).
/// </summary>
public class ClaudeCodeMemorySource : IAgentMemorySource
{
    private readonly string _root;
    private readonly List<string> _malformed = new();

    public ClaudeCodeMemorySource(string? rootOverride = null)
    {
        _root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    public string SourceId => "claude-code";
    public IReadOnlyList<string> MalformedFiles => _malformed;

    public AgentSourceInfo Discover()
    {
        if (!Directory.Exists(_root))
            return new AgentSourceInfo(SourceId, false, null, Array.Empty<AgentProjectInfo>(), null);

        var projects = new List<AgentProjectInfo>();
        foreach (var projectDir in Directory.EnumerateDirectories(_root).OrderBy(d => d))
        {
            var memoryDir = Path.Combine(projectDir, "memory");
            if (!Directory.Exists(memoryDir)) continue;
            var count = EnumerateFactFiles(memoryDir).Count();
            if (count == 0) continue;
            projects.Add(new AgentProjectInfo(
                DecodeProjectName(Path.GetFileName(projectDir)), memoryDir, count));
        }
        return new AgentSourceInfo(SourceId, true, _root, projects, null);
    }

    public IEnumerable<AgentMemoryFact> Enumerate()
    {
        _malformed.Clear();
        var info = Discover();
        if (!info.Found) yield break;

        foreach (var project in info.Projects)
        {
            foreach (var file in EnumerateFactFiles(project.Directory).OrderBy(f => f))
            {
                AgentMemoryFact? fact = null;
                try
                {
                    var raw = File.ReadAllText(file);
                    if (string.IsNullOrWhiteSpace(raw)) { _malformed.Add(file); continue; }

                    AgentMemoryFrontmatter.ParseSourceFact(raw,
                        out var name, out var description, out var type, out var body);

                    var factId = Path.GetFileNameWithoutExtension(file);
                    var firstBodyLine = body.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                    fact = new AgentMemoryFact(
                        SourceId, project.Project, factId,
                        Title: string.IsNullOrWhiteSpace(name) ? factId : name!,
                        Description: string.IsNullOrWhiteSpace(description) ? firstBodyLine : description!,
                        Body: body,
                        SourceType: string.IsNullOrWhiteSpace(type) ? "unknown" : type!,
                        SourcePath: file,
                        ContentHash: AgentMemoryFrontmatter.ComputeHash(raw),
                        SourceModifiedAt: File.GetLastWriteTime(file));
                }
                catch (IOException) { _malformed.Add(file); }
                catch (UnauthorizedAccessException) { _malformed.Add(file); }
                if (fact != null) yield return fact;
            }
        }
    }

    /// <summary>Encoded dir names flatten the repo path with '-'; the project name is the last segment that starts with uppercase.</summary>
    public static string DecodeProjectName(string encodedDirName)
    {
        // Strip drive prefix up to and including the first "--"
        var s = encodedDirName;
        var drive = s.IndexOf("--", StringComparison.Ordinal);
        if (drive >= 0) s = s[(drive + 2)..];

        var segments = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return encodedDirName;

        // Project dir names conventionally start with an uppercase letter
        // (PhotoMind, Logmark-project, Temp); path parents are lowercase
        // (projects, sync) or CamelCase system dirs that also start uppercase,
        // in which case the LAST uppercase-start segment is still the leaf.
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (segments[i].Length > 0 && char.IsUpper(segments[i][0]))
                return string.Join('-', segments[i..]);
        }
        return segments[^1];
    }

    /// <summary>SourcePath is &lt;root&gt;/&lt;encodedDir&gt;/memory/&lt;factId&gt;.md; the encoded workdir is the memory dir's parent name.</summary>
    public string? EncodedWorkdirFolder(AgentMemoryFact fact)
    {
        var memoryDir = Path.GetDirectoryName(fact.SourcePath);
        var projectDir = memoryDir == null ? null : Path.GetDirectoryName(memoryDir);
        return projectDir == null ? null : Path.GetFileName(projectDir);
    }

    private static IEnumerable<string> EnumerateFactFiles(string memoryDir)
        => Directory.EnumerateFiles(memoryDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase));
}
