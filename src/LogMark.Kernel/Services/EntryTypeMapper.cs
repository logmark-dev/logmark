using LogMark.Models;

namespace LogMark.Services;

/// <summary>
/// Maps entry types to folder/file structure
/// Phase 1: Hardcoded mappings
/// Future: Load from _logmark/config.md
/// </summary>
public class EntryTypeMapper
{
    private static readonly Dictionary<EntryType, EntryTypeMapping> Mappings = new()
    {
        [EntryType.Idea] = new EntryTypeMapping
        {
            Prefix = "idea",
            TypeName = "ideas"
        },
        [EntryType.Task] = new EntryTypeMapping
        {
            Prefix = "task",
            TypeName = "tasks"
        },
        [EntryType.Block] = new EntryTypeMapping
        {
            Prefix = "block",
            TypeName = "blocks"
        },
        [EntryType.Decision] = new EntryTypeMapping
        {
            Prefix = "decision",
            TypeName = "decisions"
        },
        [EntryType.Log] = new EntryTypeMapping
        {
            Prefix = "log",
            TypeName = "diary"
        },
        [EntryType.Timetrack] = new EntryTypeMapping
        {
            Prefix = "timetrack",
            TypeName = "timetrack"
        },
        [EntryType.Question] = new EntryTypeMapping
        {
            Prefix = "question",
            TypeName = "questions"
        },
        [EntryType.Gap] = new EntryTypeMapping
        {
            Prefix = "gap",
            TypeName = "gaps"
        },
        [EntryType.Analogy] = new EntryTypeMapping
        {
            Prefix = "analogy",
            TypeName = "analogies"
        }
    };

    // Reverse lookup: folder name (with or without underscore) -> EntryType
    private static readonly Dictionary<string, EntryType> FolderToType;

    static EntryTypeMapper()
    {
        FolderToType = new Dictionary<string, EntryType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, mapping) in Mappings)
        {
            FolderToType[mapping.TypeName] = type;
            FolderToType[$"_{mapping.TypeName}"] = type;
        }
    }

    /// <summary>
    /// Get mapping for entry type, or null if no mapping
    /// </summary>
    public EntryTypeMapping? GetMapping(EntryType? type)
    {
        if (type == null || !Mappings.ContainsKey(type.Value))
            return null;

        return Mappings[type.Value];
    }

    /// <summary>
    /// Get folder name for entry type (e.g., "tasks", "ideas", "diary")
    /// </summary>
    public static string GetFolderName(EntryType? type)
    {
        if (type != null && Mappings.TryGetValue(type.Value, out var mapping))
            return mapping.TypeName;
        return "diary";  // Default for null/Log
    }

    /// <summary>
    /// Get folder name with underscore prefix (e.g., "_tasks", "_ideas", "_diary")
    /// </summary>
    public static string GetFolderNameWithPrefix(EntryType type)
    {
        return $"_{GetFolderName(type)}";
    }

    /// <summary>
    /// Get entry type from folder name (with or without underscore prefix).
    /// Returns null if folder name is not recognized.
    /// </summary>
    public static EntryType? GetEntryTypeFromFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return null;

        return FolderToType.TryGetValue(folderName, out var type) ? type : null;
    }

    /// <summary>
    /// Get display name for entry type (e.g., "Tasks", "Ideas", "Diary")
    /// </summary>
    public static string GetDisplayName(EntryType type)
    {
        return type switch
        {
            EntryType.Task => "Tasks",
            EntryType.Idea => "Ideas",
            EntryType.Block => "Blocks",
            EntryType.Decision => "Decisions",
            EntryType.Log => "Diary",
            EntryType.Timetrack => "Timetrack",
            EntryType.Question => "Questions",
            EntryType.Gap => "Gaps",
            EntryType.Analogy => "Analogies",
            _ => "Entries"
        };
    }

    /// <summary>
    /// Get all entry types that have folder mappings.
    /// </summary>
    public static IEnumerable<EntryType> GetAllTypes() => Mappings.Keys;

    /// <summary>
    /// Infer entry type from a file path by looking for type folder patterns.
    /// Returns null if no type folder pattern is found.
    /// </summary>
    public static EntryType? InferTypeFromPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();

        foreach (var (type, mapping) in Mappings)
        {
            var pattern = $"/_{mapping.TypeName}/";
            if (normalized.Contains(pattern))
                return type;
        }

        return null;
    }

    /// <summary>
    /// Accept any case-insensitive alias (singular, plural, folder-name
    /// with underscore prefix) and return the canonical folder name as used
    /// by PathConstants (e.g., "log" -> "diary", "tasks" -> "tasks", "_tasks" -> "tasks").
    /// Rejects invalid folder prefixes like "_log" (should be "log" or "diary" or "_diary").
    /// Returns null for unknown input.
    /// </summary>
    public static string? ResolveTypeName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim().ToLowerInvariant();
        var hasUnderscore = normalized.StartsWith("_");

        // Strip underscore if present to check the base name
        var baseName = hasUnderscore ? normalized[1..] : normalized;

        // Resolve the base name to canonical folder name
        var canonical = baseName switch
        {
            "task" or "tasks" => "tasks",
            "idea" or "ideas" => "ideas",
            "block" or "blocks" => "blocks",
            "decision" or "decisions" => "decisions",
            "log" or "logs" or "diary" => "diary",
            "question" or "questions" => "questions",
            "gap" or "gaps" => "gaps",
            "analogy" or "analogies" => "analogies",
            "workflow" => "workflow",
            "timetrack" => "timetrack",
            _ => null
        };

        // If underscore was provided, validate it matches the canonical folder name
        // (e.g., "_diary" is valid, but "_log" is invalid because canonical is "diary")
        if (hasUnderscore && canonical != null)
        {
            var withUnderscore = $"_{canonical}";
            return withUnderscore == normalized ? canonical : null;
        }

        return canonical;
    }
}
