using System.IO;
using System.Text.RegularExpressions;

namespace LogMark.Services;

/// <summary>
/// Loads entity pools from vault's _logmark/ folder.
/// Provides lists of projects and tags for autocomplete.
/// </summary>
public partial class EntityPoolService
{
    private readonly string _logmarkPath;
    private Func<string, IEnumerable<string>, IEnumerable<string>>? _orderEntities;

    // Matches ### heading-name
    [GeneratedRegex(@"^###\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    // Matches #tag, #key/value, or #key:value faceted tags in tags.md entries
    [GeneratedRegex(@"#([a-zA-Z0-9_-]+(?:[/:][a-zA-Z0-9_-]+)*)", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    public List<string> Projects { get; private set; } = [];
    public List<string> Tags { get; private set; } = [];

    public EntityPoolService(string vaultPath)
    {
        _logmarkPath = Path.Combine(vaultPath, PathConstants.GetSystemFolder());
    }

    /// <summary>
    /// Loads all entity pools from vault config files.
    /// Projects are loaded from routes.md (not projects.md).
    /// </summary>
    public void Load()
    {
        Projects = LoadHeadings("routes.md");
        Tags = LoadTags("tags.md");
    }

    /// <summary>
    /// Sets an optional ordering function for entity lists.
    /// The function receives (category, entities) and returns ordered entities.
    /// Category is "+" for routes and "#" for tags.
    /// </summary>
    public void SetEntityOrderer(Func<string, IEnumerable<string>, IEnumerable<string>> orderer)
    {
        _orderEntities = orderer;
    }

    /// <summary>
    /// Gets all entities matching a sigil prefix.
    /// If an orderer is set, entities are returned in order of usage frequency.
    /// </summary>
    public List<string> GetEntities(char sigil)
    {
        var entities = sigil switch
        {
            '+' => Projects,
            '#' => Tags,
            _ => (List<string>)[]
        };

        if (_orderEntities != null && entities.Count > 0)
            return _orderEntities(sigil.ToString(), entities).ToList();

        return entities;
    }

    /// <summary>
    /// Extracts ### headings from a pool file.
    /// </summary>
    private List<string> LoadHeadings(string fileName)
    {
        var path = Path.Combine(_logmarkPath, fileName);
        if (!File.Exists(path))
            return [];

        var content = File.ReadAllText(path);
        return HeadingPattern()
            .Matches(content)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts #tags from tags.md file.
    /// </summary>
    private List<string> LoadTags(string fileName)
    {
        var path = Path.Combine(_logmarkPath, fileName);
        if (!File.Exists(path))
            return [];

        var content = File.ReadAllText(path);
        return TagPattern()
            .Matches(content)
            .Select(m => NotationParser.LeafTag(m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds unknown tags to tags.md file.
    /// Only adds tags that don't already exist in the file.
    /// </summary>
    public void AddUnknownTags(List<string> tagsToAdd)
    {
        if (tagsToAdd == null || tagsToAdd.Count == 0)
            return;

        var tagsPath = Path.Combine(_logmarkPath, "tags.md");

        // Ensure _logmark directory exists
        Directory.CreateDirectory(_logmarkPath);

        // Get existing tags (case-insensitive)
        var existingTags = Tags.Select(t => t.ToLowerInvariant()).ToHashSet();

        // Find new tags
        var newTags = tagsToAdd
            .Where(t => !existingTags.Contains(t.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newTags.Count == 0)
            return;

        // Create or append to tags.md
        if (!File.Exists(tagsPath))
        {
            // Create new tags.md with header
            var initialContent = "# Tags\n\n## Auto-discovered Tags\n\n";
            File.WriteAllText(tagsPath, initialContent);
        }

        // Append new tags (normalize ':' -> '/' so disk prefers Obsidian-portable form)
        var tagsToAppend = string.Join(", ", newTags.Select(t => $"#{t.TrimStart('#').Replace(':', '/')}"));
        File.AppendAllText(tagsPath, $"- {tagsToAppend}\n");

        // Reload tags list
        Tags = LoadTags("tags.md");
    }
}
