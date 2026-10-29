namespace LogMark.Services.Routes;

/// <summary>
/// Validates that a vault-relative route path is safe to materialize as a
/// folder on disk (logmark/{path}/_{type}/...). Route paths come from free-text
/// user input in the routes editor and the rename dialog, so every persistence
/// path that can create or move a route folder must run its target through this
/// before touching routes.md or the filesystem.
///
/// The single Core choke point: RouteMutationService (Promote/Reparent) and the
/// Settings routes editor's Save both call <see cref="Validate"/>. Centralizing
/// here means the editor's lossy serialize-on-save path cannot bypass the
/// guard the mutation primitives enforce.
/// </summary>
public static class RouteName
{
    /// <summary>Max length of the vault-relative path. Conservative cap that
    /// leaves headroom under the Windows MAX_PATH (260) once the vault root,
    /// "logmark/", a "_type/" folder, and a "YYYY.md" file are appended.</summary>
    public const int MaxPathLength = 200;

    // Windows reserved device names. Reserved with or without an extension
    // ("CON", "CON.txt" both fail), case-insensitive.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Characters illegal in a Windows path segment. '/' is the segment
    // separator (handled by the split) and is intentionally NOT in this set;
    // '\\' IS, so a backslash inside a segment is rejected rather than treated
    // as a separator.
    private static readonly char[] IllegalChars = { '<', '>', ':', '"', '|', '?', '*', '\\' };

    /// <summary>
    /// Validate a full vault-relative route path (one or more '/'-joined
    /// segments). Returns null when valid, otherwise a human-readable reason
    /// suitable for a dialog or status line.
    /// </summary>
    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Route path cannot be empty.";

        // Do NOT Trim() here: a leading/trailing space is a per-segment defect
        // we want to reject, and trimming would mask it. Do NOT normalize '\\'
        // to '/' either: a backslash is never a legal segment separator in a
        // route path, so it must be REJECTED (by ValidateSegment's illegal-char
        // check), not silently reinterpreted as a separator.
        if (path.StartsWith("/"))
            return "Route path cannot start with a slash or drive letter.";

        if (path.Length > MaxPathLength)
            return $"Route path is too long (max {MaxPathLength} characters).";

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            var reason = ValidateSegment(segment);
            if (reason != null)
                return reason;
        }

        return null;
    }

    /// <summary>
    /// Validate a single path segment (a folder name). Returns null when valid,
    /// otherwise a human-readable reason.
    /// </summary>
    public static string? ValidateSegment(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
            return "Route path cannot contain an empty segment (no leading, trailing, or doubled slashes).";

        if (segment == "." || segment == "..")
            return "Route path cannot contain '.' or '..' segments.";

        foreach (var c in segment)
        {
            if (c < 32)
                return "Route name cannot contain control characters.";
            if (System.Array.IndexOf(IllegalChars, c) >= 0)
                return $"Route name cannot contain the character '{c}'.";
        }

        if (segment.Trim() != segment)
            return "Route name cannot start or end with spaces.";

        if (segment.EndsWith("."))
            return "Route name cannot end with a dot.";

        // Reserved name check: the part before any extension dot.
        var baseName = segment;
        var dot = baseName.IndexOf('.');
        if (dot >= 0)
            baseName = baseName.Substring(0, dot);
        if (ReservedNames.Contains(baseName))
            return $"'{segment}' is a reserved Windows name and cannot be used.";

        return null;
    }
}
