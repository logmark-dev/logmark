using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace LogMark.Mcp.Tests;

public class VaultWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _bootstrapPath;

    public VaultWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logmark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _bootstrapPath = Path.Combine(_tempDir, "bootstrap.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task OnVaultPathChanged_FiresWhenPathChanges()
    {
        var vault1 = Path.Combine(_tempDir, "vault1");
        var vault2 = Path.Combine(_tempDir, "vault2");
        Directory.CreateDirectory(vault1);
        Directory.CreateDirectory(vault2);

        WriteBootstrap(vault1);

        string? receivedPath = null;
        var changed = new TaskCompletionSource<bool>();

        using var watcher = new VaultWatcher(_bootstrapPath, vault1, debounceMs: 100);
        watcher.OnVaultPathChanged += (newPath) =>
        {
            receivedPath = newPath;
            changed.TrySetResult(true);
        };
        watcher.Start();

        // Simulate desktop app switching vault
        WriteBootstrap(vault2);

        var completed = await Task.WhenAny(changed.Task, Task.Delay(5000));
        completed.Should().Be(changed.Task, "watcher should fire within timeout");
        receivedPath.Should().Be(vault2);
    }

    [Fact]
    public async Task OnVaultPathChanged_DoesNotFireForSamePath()
    {
        var vault1 = Path.Combine(_tempDir, "vault1");
        Directory.CreateDirectory(vault1);
        WriteBootstrap(vault1);

        bool fired = false;

        using var watcher = new VaultWatcher(_bootstrapPath, vault1, debounceMs: 100);
        watcher.OnVaultPathChanged += (_) => fired = true;
        watcher.Start();

        // Rewrite same path
        WriteBootstrap(vault1);

        await Task.Delay(500);
        fired.Should().BeFalse("same path should not trigger change");
    }

    [Fact]
    public async Task OnVaultPathChanged_IgnoresInvalidPath()
    {
        var vault1 = Path.Combine(_tempDir, "vault1");
        Directory.CreateDirectory(vault1);
        WriteBootstrap(vault1);

        bool fired = false;

        using var watcher = new VaultWatcher(_bootstrapPath, vault1, debounceMs: 100);
        watcher.OnVaultPathChanged += (_) => fired = true;
        watcher.Start();

        // Write nonexistent path
        WriteBootstrap(Path.Combine(_tempDir, "nonexistent"));

        await Task.Delay(500);
        fired.Should().BeFalse("invalid path should not trigger change");
    }

    private void WriteBootstrap(string vaultPath)
    {
        var json = JsonSerializer.Serialize(new { VaultPath = vaultPath });
        File.WriteAllText(_bootstrapPath, json);
    }
}
