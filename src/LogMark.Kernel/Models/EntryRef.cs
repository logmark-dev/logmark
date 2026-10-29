namespace LogMark.Models;

/// <summary>
/// A reference to another entry, used for cross-entry linking.
/// Format: type:id (e.g., "task:b123bbe1", "decision:xyz123")
/// </summary>
public record EntryRef(string Type, string Id)
{
    /// <summary>
    /// Parse a reference string like "task:b123bbe1" into an EntryRef.
    /// Returns null if the format is invalid.
    /// </summary>
    public static EntryRef? Parse(string refString)
    {
        if (string.IsNullOrWhiteSpace(refString))
            return null;

        var parts = refString.Trim().Split(':', 2);
        if (parts.Length != 2)
            return null;

        var type = parts[0].Trim().ToLowerInvariant();
        var id = parts[1].Trim();

        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id))
            return null;

        return new EntryRef(type, id);
    }

    /// <summary>
    /// Format the reference as "type:id".
    /// </summary>
    public override string ToString() => $"{Type}:{Id}";
}
