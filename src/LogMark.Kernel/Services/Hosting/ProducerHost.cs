namespace LogMark.Services.Hosting;

/// <summary>
/// The shared producer host both composition roots run (design 4.3, amended
/// 2026-07-13): the desktop app registers its closed producer zoo through
/// AddProducers; the standalone daemon registers nothing. Owns background-loop
/// lifecycle; registration freezes at StartAsync.
/// </summary>
public sealed class ProducerHost : IProducerRegistrar, IAsyncDisposable
{
    private readonly List<IBackgroundLoop> _loops = new();
    private readonly Dictionary<Type, List<object>> _contributions = new();
    private readonly List<IBackgroundLoop> _started = new();
    private bool _running;

    public IReadOnlyList<IBackgroundLoop> Loops => _loops;

    public void AddBackgroundLoop(IBackgroundLoop loop)
    {
        ThrowIfRunning();
        _loops.Add(loop);
    }

    public void AddContribution<T>(T contribution) where T : notnull
    {
        ThrowIfRunning();
        if (!_contributions.TryGetValue(typeof(T), out var list))
        {
            list = new List<object>();
            _contributions[typeof(T)] = list;
        }
        list.Add(contribution);
    }

    /// <summary>All contributions registered under contract type T, in registration order.</summary>
    public IReadOnlyList<T> Contributions<T>() where T : notnull
    {
        return _contributions.TryGetValue(typeof(T), out var list)
            ? list.Cast<T>().ToList()
            : Array.Empty<T>();
    }

    /// <summary>
    /// Starts loops in registration order. If a loop fails to start, loops
    /// already started are stopped in reverse order and the failure rethrows.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_running) throw new InvalidOperationException("ProducerHost is already started.");
        _running = true;
        foreach (var loop in _loops)
        {
            try
            {
                await loop.StartAsync(ct).ConfigureAwait(false);
                _started.Add(loop);
            }
            catch
            {
                await StopStartedAsync().ConfigureAwait(false);
                _running = false;
                throw;
            }
        }
    }

    /// <summary>Stops started loops in reverse order. No-op when not started.</summary>
    public async Task StopAsync()
    {
        if (!_running && _started.Count == 0) return;
        await StopStartedAsync().ConfigureAwait(false);
        _running = false;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task StopStartedAsync()
    {
        for (var i = _started.Count - 1; i >= 0; i--)
        {
            await _started[i].StopAsync().ConfigureAwait(false);
        }
        _started.Clear();
    }

    private void ThrowIfRunning()
    {
        if (_running) throw new InvalidOperationException("ProducerHost registration is frozen after StartAsync.");
    }
}
