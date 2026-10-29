using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;
using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Bead lmk-fjcj: a tools/call answered before EntryCache warmup finished read
/// a partial cache, so vault_summary on a freshly opened vault reported 0 of
/// 68 entries. Cache-backed tools must not answer until warmup is done.
/// </summary>
public class CacheWarmupGateTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), $"lm-warmup-{Guid.NewGuid():N}");

    public CacheWarmupGateTests()
    {
        var tasks = Path.Combine(_vault, "logmark", "_tasks");
        Directory.CreateDirectory(tasks);
        File.WriteAllText(Path.Combine(tasks, "2026.md"),
            "---\n\n## 2026-01-05 10:00\n\nt: first\n\n---\n\n## 2026-01-06 11:00\n\nt: second\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_vault)) Directory.Delete(_vault, true);
    }

    [Fact]
    public async Task ToolsCall_RunsOnlyAfterTheReadinessGateCompletes()
    {
        var gateDone = false;
        var sawGateDone = (bool?)null;
        var registry = new ToolRegistry();
        registry.Register(new ProbeTool(() => sawGateDone = gateDone));
        var input = string.Join("\n",
            Request("initialize", new { protocolVersion = "2024-11-05" }, 1),
            Request("tools/call", new { name = "probe", arguments = new { } }, 2)) + "\n";
        var server = new McpServer(registry, null, null,
            new MemoryStream(Encoding.UTF8.GetBytes(input)), new MemoryStream());
        server.BeforeToolCall = async ct =>
        {
            await Task.Delay(150, ct);
            gateDone = true;
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        sawGateDone.Should().BeTrue("the tool must not run before the gate completes");
    }

    [Fact]
    public async Task AwaitCacheWarmup_WaitsForWarmupThenSeesEveryEntry()
    {
        using var release = new ManualResetEventSlim(false);
        var cache = new EntryCache(_vault, new GatedAccessor(release));
        cache.StartWarmup();
        var services = new VaultServices { EntryCache = cache };

        var wait = McpServerBootstrap.AwaitCacheWarmupAsync(services, TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.Delay(150);
        wait.IsCompleted.Should().BeFalse("warmup is still blocked");

        release.Set();
        await wait;
        cache.GetAllEntries().Should().HaveCount(2);
    }

    [Fact]
    public async Task AwaitCacheWarmup_GivesUpAfterTheBound()
    {
        using var release = new ManualResetEventSlim(false);
        var cache = new EntryCache(_vault, new GatedAccessor(release));
        cache.StartWarmup();
        var services = new VaultServices { EntryCache = cache };

        var wait = McpServerBootstrap.AwaitCacheWarmupAsync(services, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        var finished = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().BeSameAs(wait, "a stuck warmup must not hang the tool call");
        await wait; // and must not throw
        release.Set();
    }

    [Fact]
    public async Task AwaitCacheWarmup_NoCache_CompletesImmediately()
    {
        await McpServerBootstrap.AwaitCacheWarmupAsync(new VaultServices(), TimeSpan.FromSeconds(1), CancellationToken.None);
    }

    private static string Request(string method, object parameters, int id) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

    private sealed class ProbeTool : ITool
    {
        private readonly Action _onRun;
        public ProbeTool(Action onRun) => _onRun = onRun;
        public string Name => "probe";
        public ToolDefinition Definition => new()
        {
            Name = "probe",
            Description = "probe",
            InputSchema = new JsonSchema { Type = "object", Properties = new() },
        };
        public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
        {
            _onRun();
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ToolContent> { new() { Text = "ok" } },
            });
        }
    }

    /// <summary>Real file access whose directory scan blocks until released.</summary>
    private sealed class GatedAccessor : IFileAccessor
    {
        private readonly ManualResetEventSlim _release;
        private readonly FileAccessor _inner = new();
        public GatedAccessor(ManualResetEventSlim release) => _release = release;
        public string ReadAllText(string path) => _inner.ReadAllText(path);
        public void WriteAllText(string path, string content) => _inner.WriteAllText(path, content);
        public bool Exists(string path) => _inner.Exists(path);
        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        {
            _release.Wait(TimeSpan.FromSeconds(30));
            return _inner.EnumerateFiles(path, pattern, option);
        }
        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public long GetFileSize(string path) => _inner.GetFileSize(path);
    }
}
