namespace LogMark.Models;

/// <summary>
/// DTO for event query results
/// Separates query layer from database records
/// </summary>
public class EventQueryResult
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public EventType EventType { get; set; }
    public EventData Data { get; set; } = new();
}

/// <summary>
/// DTO for task postpone pattern analysis
/// </summary>
public class PostponePattern
{
    public string TaskId { get; set; } = string.Empty;
    public int PostponeCount { get; set; }
    public int TotalDaysAdded { get; set; }
    public DateTime LastPostpone { get; set; }
}
