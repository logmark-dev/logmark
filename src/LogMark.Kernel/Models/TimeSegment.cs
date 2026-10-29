namespace LogMark.Models;

public class TimeSegment
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsOpen { get; set; }
    public TimeSpan Duration => IsOpen ? TimeSpan.Zero : End - Start;
}
