using System.Text.Json;
using System.Text.Json.Serialization;
using LogMark.Services.EntryCache;

namespace LogMark.Services.AgentMemory;

/// <summary>
/// Remembers which offered source projects the user declined, so the
/// AgentMemoryScope offer card does not re-offer them on every sync.
///
/// This is deliberately the ONLY persisted selection state. The set of synced
/// projects is derived from the mirror pages (MirrorScanner is the ledger, see
/// its header) rather than stored, so there is no second bookkeeping store for
/// what is already on disk. Declines are the exception: a project the user
/// said no to leaves no mirror page, so nothing else records the decision.
///
/// Reads fresh and writes the whole file per Decline - low frequency, no
/// caching needed. Mirrors MemoryReviewOutcomeStore's shape and location.
/// </summary>
public class DeclinedProjectStore
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

    public DeclinedProjectStore(string vaultPath, IFileAccessor fileAccessor)
    {
        _fileAccessor = fileAccessor;
        _path = Path.Combine(vaultPath, "logmark", "feed", "_memory-declined-projects.json");
    }

    public void Decline(string sourceId, string project)
    {
        lock (_lock)
        {
            var all = Load();
            if (!all.TryGetValue(sourceId, out var list))
            {
                list = new List<string>();
                all[sourceId] = list;
            }
            if (!list.Contains(project, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(project);
                Save(all);
            }
        }
    }

    public bool IsDeclined(string sourceId, string project)
    {
        lock (_lock)
        {
            return Load().TryGetValue(sourceId, out var list)
                && list.Contains(project, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<string> GetDeclined(string sourceId)
    {
        lock (_lock)
        {
            return Load().TryGetValue(sourceId, out var list)
                ? list.ToList()
                : new List<string>();
        }
    }

    private Dictionary<string, List<string>> Load()
    {
        try
        {
            if (!_fileAccessor.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            var json = _fileAccessor.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, JsonOptions)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt file must not wedge the sync. Treat as empty: the worst
            // case is one repeated offer, not a broken behavior row.
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, List<string>> all)
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (!_fileAccessor.DirectoryExists(dir)) _fileAccessor.CreateDirectory(dir);
        _fileAccessor.WriteAllText(_path, JsonSerializer.Serialize(all, JsonOptions));
    }
}
