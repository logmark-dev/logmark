using System.Text.Json;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Debounced FileSystemWatcher over the vault, classifying changed markdown
/// paths into contract vault-event kinds. Mapping mirrors
/// LogMark.Knowledge/src/services/vault-event-mapping.ts:
/// vault.md or constitution.md -> constitution-changed;
/// _concepts/ (canonical, per ConceptPageStore) or the legacy concepts/ ->
/// concepts-changed; any other .md -> entries-changed; non-markdown emits
/// nothing.
/// </summary>
public sealed class VaultChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _vaultRoot;
    private readonly Action<string, string> _emit; // (eventName, jsonData)
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new();
    private Timer? _flushTimer;

    public VaultChangeWatcher(string vaultRoot, Action<string, string> emit, int debounceMs = 250)
    {
        _vaultRoot = Path.GetFullPath(vaultRoot);
        _emit = emit;
        DebounceMs = debounceMs;
        _watcher = new FileSystemWatcher(_vaultRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        _watcher.Created += (_, e) => Queue(e.FullPath);
        _watcher.Changed += (_, e) => Queue(e.FullPath);
        _watcher.Deleted += (_, e) => Queue(e.FullPath);
        _watcher.Renamed += (_, e) => Queue(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    public int DebounceMs { get; }

    private void Queue(string fullPath)
    {
        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            _pending.Add(fullPath);
            _flushTimer?.Dispose();
            _flushTimer = new Timer(_ => Flush(), null, DebounceMs, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        List<string> batch;
        lock (_gate)
        {
            batch = _pending.ToList();
            _pending.Clear();
        }

        var byKind = new Dictionary<string, List<string>>();
        foreach (var path in batch)
        {
            var kind = Classify(path);
            if (kind is null) continue;
            if (!byKind.TryGetValue(kind, out var list)) byKind[kind] = list = new List<string>();
            list.Add(Relative(path));
        }

        foreach (var (kind, paths) in byKind)
        {
            _emit("vault-event", JsonSerializer.Serialize(new { kind, paths }));
        }
    }

    private string? Classify(string fullPath)
    {
        var rel = Relative(fullPath);
        if (rel == "vault.md" || rel.EndsWith("/vault.md", StringComparison.Ordinal))
            return "constitution-changed";
        if (rel == "constitution.md" || rel.EndsWith("/constitution.md", StringComparison.Ordinal))
            return "constitution-changed";
        if (rel.StartsWith("concepts/", StringComparison.Ordinal) || rel.Contains("/concepts/", StringComparison.Ordinal))
            return "concepts-changed";
        if (rel.StartsWith("_concepts/", StringComparison.Ordinal) || rel.Contains("/_concepts/", StringComparison.Ordinal))
            return "concepts-changed";
        return "entries-changed";
    }

    private string Relative(string fullPath) =>
        Path.GetRelativePath(_vaultRoot, fullPath).Replace('\\', '/');

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_gate)
        {
            _flushTimer?.Dispose();
        }
    }
}
