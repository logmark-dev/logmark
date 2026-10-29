namespace LogMark.Models;

public class Deadline
{
    /// <summary>
    /// Full datetime with time component. Defaults to 00:00 if no time specified.
    /// </summary>
    public DateTime DateTime { get; set; }

    public string OriginalText { get; set; } = string.Empty;
}

// Keep enum for backward compatibility during transition, but always use TimeSpecific
public enum DeadlineGranularity
{
    DateOnly,      // Deprecated - treat as TimeSpecific with 00:00
    TimeSpecific   // All deadlines use this
}
