namespace LogMark.Models;

public class RouteUpdateResult
{
    public int RouteCount { get; set; }
    public List<string> Sections { get; set; } = new();
    public List<string> RouteNames { get; set; } = new();
    public string? VaultNotes { get; set; }
    public List<string> Warnings { get; set; } = new();
}
