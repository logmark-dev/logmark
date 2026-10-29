namespace LogMark.Services.Vault;

using LogMark.Models;
using System.IO;

public sealed class VaultConstitutionService : IDisposable
{
    private readonly string _vaultMdPath;
    private readonly VaultConstitutionParser _parser;
    private FileSystemWatcher? _watcher;
    private VaultConstitution _cached = VaultConstitution.Empty;
    private readonly object _lock = new();

    public event EventHandler<VaultConstitution>? Changed;

    public VaultConstitutionService(string vaultRootPath, VaultConstitutionParser parser)
    {
        _vaultMdPath = Path.Combine(vaultRootPath, "vault.md");
        _parser = parser;
    }

    public VaultConstitution Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_vaultMdPath))
            {
                _cached = VaultConstitution.Empty;
                return _cached;
            }
            try
            {
                var md = File.ReadAllText(_vaultMdPath);
                _cached = _parser.Parse(md);
                return _cached;
            }
            catch (IOException)
            {
                return _cached;
            }
        }
    }

    public VaultConstitution Current
    {
        get { lock (_lock) return _cached; }
    }

    private System.Threading.Timer? _debounceTimer;
    private const int DebounceMs = 500;

    public void StartWatching()
    {
        if (_watcher is not null) return;
        var dir = Path.GetDirectoryName(_vaultMdPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, "vault.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Deleted += OnChanged;
    }

    private void OnChanged(object _, FileSystemEventArgs __) => Schedule();

    private void Schedule()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            var prev = _cached;
            var next = Load();
            if (!ReferenceEquals(prev, next))
                Changed?.Invoke(this, next);
        }, null, DebounceMs, System.Threading.Timeout.Infinite);
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
        _watcher = null;
    }
}
