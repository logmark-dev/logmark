using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.Services.EntryCache;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Open capture-only OpenAgentCaptureCommand (lmk-tfv4): the closed
/// LogMark.CLI.Producers.Tests.AgentCaptureCommandTests exercises the full
/// distill-aware closed command; this suite exercises the OPEN capture-only
/// subset shipped in LogMark.CLI - a deterministic stub entry, no transcript
/// stats, no distill enqueue, and a deliberate --checkpoint no-op. The shared
/// CliRunner does not toggle this command's stdin detector (mirrors the
/// closed suite's own precedent), so this suite manages
/// OpenAgentCaptureCommand.IsInputRedirected itself.
/// </summary>
[Collection("EnvironmentTests")]
public class OpenAgentCaptureCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly Func<bool> _originalRedirected;

    public OpenAgentCaptureCommandTests()
    {
        _originalRedirected = OpenAgentCaptureCommand.IsInputRedirected;
        OpenAgentCaptureCommand.IsInputRedirected = () => true;
        _vault = new TestVaultBuilder().WithStandardStructure();
    }

    public void Dispose()
    {
        OpenAgentCaptureCommand.IsInputRedirected = _originalRedirected;
        _vault.Dispose();
    }

    private static string Payload(string sessionId = "sess-1", string cwd = "/p") =>
        $$"""{"session_id":"{{sessionId}}","transcript_path":"x.jsonl","cwd":"{{cwd}}","hook_event_name":"SessionEnd"}""";

    private async Task<IReadOnlyList<IndexedEntry>> AllEntriesAsync()
    {
        var cache = new EntryCache(_vault.VaultPath);
        cache.StartWarmup();
        if (cache.WarmupTask != null) await cache.WarmupTask;
        return cache.GetAllEntries();
    }

    [Fact]
    public async Task FinalMode_WritesOneSessionEndEntry_WithSourceAndSession()
    {
        var result = CliRunner.RunWithStdin(Payload("sess-42"),
            "--vault", _vault.VaultPath, "agent-capture");

        result.ExitCode.Should().Be(0);

        var all = await AllEntriesAsync();
        var entry = all.Should().ContainSingle(e => e.Entry.Source == "agent:claude-code").Subject;
        entry.Entry.Session.Should().Be("sess-42");
        entry.Entry.Content.Should().Contain("Agent session ended: claude-code");
    }

    [Fact]
    public async Task NoSessionId_WritesNothing_ExitsZero()
    {
        var result = CliRunner.RunWithStdin(
            """{"transcript_path":"x.jsonl","cwd":"/p","hook_event_name":"SessionEnd"}""",
            "--vault", _vault.VaultPath, "agent-capture");

        result.ExitCode.Should().Be(0);
        (await AllEntriesAsync()).Should().NotContain(e => e.Entry.Source == "agent:claude-code");
    }

    [Fact]
    public async Task Checkpoint_IsNoOp_WritesNothing_ExitsZero()
    {
        var result = CliRunner.RunWithStdin(Payload("sess-chk"),
            "--vault", _vault.VaultPath, "agent-capture", "--checkpoint");

        result.ExitCode.Should().Be(0);
        (await AllEntriesAsync()).Should().NotContain(e => e.Entry.Session == "sess-chk");
    }

    [Fact]
    public async Task BakedRoute_TagsTheEntry()
    {
        var result = CliRunner.RunWithStdin(Payload("sess-route"),
            "--vault", _vault.VaultPath, "agent-capture", "--route", "foo");

        result.ExitCode.Should().Be(0);

        var all = await AllEntriesAsync();
        var entry = all.Should().ContainSingle(e => e.Entry.Session == "sess-route").Subject;
        entry.Route.Should().Be("foo");
    }

    [Fact]
    public async Task MalformedStdin_NoThrow_ExitsZero_WritesNothing()
    {
        var result = CliRunner.RunWithStdin("not json at all",
            "--vault", _vault.VaultPath, "agent-capture");

        result.ExitCode.Should().Be(0);
        (await AllEntriesAsync()).Should().BeEmpty();
    }

    [Fact]
    public void NoStdin_ExitsZero_WritesNothing()
    {
        OpenAgentCaptureCommand.IsInputRedirected = () => false;
        var result = CliRunner.Run("--vault", _vault.VaultPath, "agent-capture");

        result.ExitCode.Should().Be(0);
    }
}
