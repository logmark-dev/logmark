namespace LogMark.Models;

/// <summary>
/// Data payload for an event log entry
/// Maps to columns in events table
/// </summary>
public class EventData
{
    // Entry-related dimensions (nullable for non-entry events)
    public string? EntryId { get; set; }
    public string? EntryType { get; set; }
    public string? Route { get; set; }
    public string? TagsJson { get; set; }
    public string? ProjectsJson { get; set; }

    // Entry characteristics
    public bool HasPaste { get; set; }
    public string? PasteType { get; set; }
    public bool HasDeadline { get; set; }
    public int ContentLength { get; set; }

    // Command/interaction context (nullable for entry events)
    public string? CommandName { get; set; }
    public string? ModalName { get; set; }

    // Flexible extension
    public string? DataJson { get; set; }
}
