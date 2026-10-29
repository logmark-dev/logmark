using LogMark.Models;

namespace LogMark.Services.EntryParser;

/// <summary>
/// Shared parser for leading entry-type prefixes (e.g. "g:", "gap:"). Single
/// source of the prefix vocabulary, consumed by EntryParser.DetermineType and
/// the EntryTypePrefixLint rule so the table is not duplicated.
/// </summary>
public static class EntryPrefix
{
    // (short form, long form, type). Order within a tuple does not matter: the
    // colon + required-whitespace guard makes "t:" and "task:" unambiguous.
    private static readonly (string Short, string Long, EntryType Type)[] Prefixes =
    {
        ("t", "task", EntryType.Task),
        ("i", "idea", EntryType.Idea),
        ("b", "block", EntryType.Block),
        ("d", "decision", EntryType.Decision),
        ("q", "question", EntryType.Question),
        ("g", "gap", EntryType.Gap),
        ("a", "analogy", EntryType.Analogy),
    };

    /// <summary>
    /// Parse a leading type prefix from entry content. Returns the type and the
    /// matched token including the colon (e.g. "g:"), or null when the first
    /// non-empty line does not start with a known prefix followed by whitespace.
    /// </summary>
    public static (EntryType Type, string Token)? TryParseLeading(string content)
    {
        var firstLine = FirstNonEmptyLine(content);
        if (firstLine == null) return null;
        var trimmed = firstLine.TrimStart();
        foreach (var (s, l, type) in Prefixes)
        {
            if (MatchesPrefix(trimmed, l)) return (type, l + ":");
            if (MatchesPrefix(trimmed, s)) return (type, s + ":");
        }
        return null;
    }

    /// <summary>
    /// If the line starts with a known type prefix (+ whitespace), return the line
    /// with the prefix and the following whitespace removed, preserving any leading
    /// indentation. Returns null when there is no leading prefix.
    /// </summary>
    public static string? StripLeading(string line)
    {
        var trimmed = line.TrimStart();
        var leadingWs = line.Substring(0, line.Length - trimmed.Length);
        foreach (var (s, l, _) in Prefixes)
        {
            foreach (var form in new[] { l, s })
            {
                if (MatchesPrefix(trimmed, form))
                {
                    var rest = trimmed.Substring(form.Length + 1).TrimStart();
                    return leadingWs + rest;
                }
            }
        }
        return null;
    }

    /// <summary>Singular display label for a type, e.g. "Gap", "Question".</summary>
    public static string TypeLabel(EntryType type) => type switch
    {
        EntryType.Task => "Task",
        EntryType.Idea => "Idea",
        EntryType.Block => "Block",
        EntryType.Decision => "Decision",
        EntryType.Question => "Question",
        EntryType.Gap => "Gap",
        EntryType.Analogy => "Analogy",
        EntryType.Log => "Log",
        _ => type.ToString()
    };

    private static bool MatchesPrefix(string trimmedLine, string form)
    {
        // Need at least: form + ':' + one whitespace char.
        if (trimmedLine.Length < form.Length + 2) return false;
        if (!trimmedLine.StartsWith(form + ":", StringComparison.OrdinalIgnoreCase)) return false;
        return char.IsWhiteSpace(trimmedLine[form.Length + 1]);
    }

    private static string? FirstNonEmptyLine(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
        return null;
    }
}
