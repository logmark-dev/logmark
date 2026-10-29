namespace LogMark.Mcp;

/// <summary>
/// Watches bootstrap.json for vault path changes.
/// Fires OnVaultPathChanged when the vault path changes to a valid directory.
/// </summary>
public class VaultWatcher : IDisposable
{
    private readonly string _bootstrapPath;
    private readonly int _debounceMs;
    private FileSystemWatcher? _watcher;
    private string _currentVaultPath;
    private Timer? _debounceTimer;
    private readonly object _debounceLock = new();

    public event Action<string>? OnVaultPathChanged;

    public VaultWatcher(string bootstrapPath, string currentVaultPath, int debounceMs = 1000)
    {
        _bootstrapPath = bootstrapPath;
        _currentVaultPath = currentVaultPath;
        _debounceMs = debounceMs;
    }

    public void Start()
    {
        var directory = Path.GetDirectoryName(_bootstrapPath)!;
        var fileName = Path.GetFileName(_bootstrapPath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(ProcessChange, null, _debounceMs, Timeout.Infinite);
        }
    }

    private void ProcessChange(object? state)
    {
        var newPath = BootstrapReader.ReadVaultPath(_bootstrapPath);
        if (string.IsNullOrEmpty(newPath))
            return;

        if (string.Equals(newPath, _currentVaultPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!Directory.Exists(newPath))
            return;

        _currentVaultPath = newPath;
        OnVaultPathChanged?.Invoke(newPath);
    }

    public void UpdateCurrentPath(string newPath)
    {
        _currentVaultPath = newPath;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
