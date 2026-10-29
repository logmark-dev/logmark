using System.Text.Json;
using System.Text.Json.Serialization;
using LogMark.Services.EntryCache;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Append-only record of memory-review decisions (promoted/dismissed counts per
/// session). Latent acceptance signal for the feedback-aware salience scorecard;
/// nothing consumes it yet. Reads fresh and writes the whole file per Record - low
/// frequency, no caching needed.
/// </summary>
public record MemoryReviewOutcome(
    DateTime DecidedAt,
    int PromotedCount,
    int DismissedCount,
    string? RoutesSummary);

public class MemoryReviewOutcomeStore
{
    private readonly string _path;
    private readonly IFileAccessor _fileAccessor;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MemoryReviewOutcomeStore(string vaultPath, IFileAccessor fileAccessor)
    {
        _fileAccessor = fileAccessor;
        _path = Path.Combine(vaultPath, "logmark", "feed", "_memory-review-outcomes.json");
    }

    public void Record(MemoryReviewOutcome outcome)
    {
        lock (_lock)
        {
            var all = Load();
            all.Add(outcome);
            Save(all);
        }
    }

    public IReadOnlyList<MemoryReviewOutcome> GetAll()
    {
        lock (_lock) return Load();
    }

    private List<MemoryReviewOutcome> Load()
    {
        try
        {
            if (!_fileAccessor.Exists(_path)) return new();
            var json = _fileAccessor.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<MemoryReviewOutcome>>(json, JsonOptions) ?? new();
        }
        catch { return new(); }
    }

    private void Save(List<MemoryReviewOutcome> data)
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (!_fileAccessor.DirectoryExists(dir)) _fileAccessor.CreateDirectory(dir);
        _fileAccessor.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOptions));
    }
}
