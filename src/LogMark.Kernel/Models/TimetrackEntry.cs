namespace LogMark.Models;

public class TimetrackEntry
{
    public string Description { get; set; } = "";
    public string? ProjectRoute { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime StoppedAt { get; set; }
    public List<TimeSegment> Timeline { get; set; } = [];
    public TimeSpan TotalDuration { get; set; }
}
