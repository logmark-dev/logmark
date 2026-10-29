using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.Services.EntryParser;
using Xunit;

namespace LogMark.Mcp.Tests;

public class UpdateEntryToolTests : IDisposable
{
    private readonly string _vault;
    private readonly UpdateEntryTool _tool;
    private readonly CreateEntryTool _create;

    public UpdateEntryToolTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), $"logmark-mcp-update-entry-{Guid.NewGuid()}");
        Directory.CreateDirectory(_vault);
        Directory.CreateDirectory(Path.Combine(_vault, PathConstants.GetSystemFolder()));
        File.WriteAllText(Path.Combine(_vault, PathConstants.GetRoutesPath()),
            "# Routes\n\n## Projects\n\n### logmark\n- path: projects/logmark\n\n## Domains\n");

        var notationParser = new NotationParser();
        var logger = new SystemLogger(_vault);
        var routeService = new RouteService(_vault, logger, new TestRoutePromptService());
        routeService.LoadRoutes();
        var sessionService = new SessionService(_vault, logger);
        var sessionContext = new McpSessionContext(sessionService, logger, "test-agent", "general");
        var fileAccessor = new FileAccessor();

        var services = new VaultServices
        {
            VaultPath = _vault,
            VaultService = new VaultService(_vault, notationParser),
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = logger,
            SessionService = sessionService,
            SessionContext = sessionContext,
            TaskTracker = new TaskTracker(_vault, logger),
            EntryCache = new EntryCache(_vault, fileAccessor),
            VaultWriter = new VaultWriter(),
        };
        _tool = new UpdateEntryTool(services);
        _create = new CreateEntryTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vault)) Directory.Delete(_vault, true);
    }

    private async Task<string> CreateAndGetId(string content)
    {
        var res = await _create.ExecuteAsync(Args(new { content }), CancellationToken.None);
        res.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(res.Content[0].Text!);
        return doc.RootElement.GetProperty("EntryId").GetString()!;
    }

    private static string FileTextContaining(string vault, string token)
        => Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "_sessions" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "_system" + Path.DirectorySeparatorChar))
            .Select(File.ReadAllText).First(t => t.Contains(token));

    [Fact]
    public async Task UnaddressableId_NamesTheMintRemedy()
    {
        // An entry with no "_id:" anchor is served under a computed id that no
        // write can find. The error must say why and how to fix it (lmk-gwy6).
        var rel = Path.Combine("logmark", "projects", "logmark", "_tasks", "2026.md");
        var full = Path.Combine(_vault, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "## 2026-05-15 08:00\ntask: wire the guide vault\n");
        var servedId = EntryResolver.GetEntryId(EntryParser.Parse(File.ReadAllText(full)).Single());

        var res = await _tool.ExecuteAsync(Args(new { entry_id = servedId, complete = true }), CancellationToken.None);

        res.IsError.Should().BeTrue();
        res.Content[0].Text.Should().Contain("Unaddressable").And.Contain("logmark entry mint-anchors --apply");
    }

    [Fact]
    public async Task FreezeDecision_AddsFrozenTag()
    {
        var id = await CreateAndGetId("+logmark d: ship the consolidation");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "frozen" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Action", "\"frozen\"");
        FileTextContaining(_vault, id).Should().Contain("#frozen");
    }

    [Fact]
    public async Task RejectDecision_AddsRejectedTag()
    {
        var id = await CreateAndGetId("+logmark d: maybe drop telegram");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "rejected" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        FileTextContaining(_vault, id).Should().Contain("#rejected");
    }

    [Fact]
    public async Task ResolveBlock_AddsResolvedTag()
    {
        var id = await CreateAndGetId("+logmark b: waiting on api key");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "resolved" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Action", "\"resolved\"");
        FileTextContaining(_vault, id).Should().Contain("#resolved");
    }

    [Fact]
    public async Task DismissBlock_AddsDismissedTag()
    {
        var id = await CreateAndGetId("+logmark b: flaky network");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "dismissed" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        FileTextContaining(_vault, id).Should().Contain("#dismissed");
    }

    [Fact]
    public async Task ResolveBlock_WithResolution_StoresNoteAndEchoesInResponse()
    {
        var id = await CreateAndGetId("+logmark b: waiting on vendor");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "resolved", resolution = "vendor shipped the fix" }),
            CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Resolution", "\"vendor shipped the fix\"");
        FileTextContaining(_vault, id).Should().Contain("resolved: vendor shipped the fix");
    }

    [Fact]
    public async Task DismissBlock_WithResolution_StoresNote()
    {
        var id = await CreateAndGetId("+logmark b: flaky network again");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "dismissed", resolution = "not reproducible" }),
            CancellationToken.None);

        res.IsError.Should().BeFalse();
        FileTextContaining(_vault, id).Should().Contain("resolved: not reproducible");
        FileTextContaining(_vault, id).Should().Contain("#dismissed");
    }

    [Fact]
    public async Task FreezeDecision_WithResolutionArg_IgnoresIt()
    {
        // Decisions have no resolution note; passing one should not error and
        // should not be echoed (Resolution stays scoped to blocks/tasks).
        var id = await CreateAndGetId("+logmark d: ship the consolidation take two");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "frozen", resolution = "irrelevant" }),
            CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.Should().NotContain("\"Resolution\"");
    }

    [Fact]
    public async Task FreezeTwice_IsIdempotentSuccess()
    {
        var id = await CreateAndGetId("+logmark d: keep wpf");
        await _tool.ExecuteAsync(Args(new { entry_id = id, status = "frozen" }), CancellationToken.None);
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "frozen" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Action", "\"frozen\"");
    }

    [Fact]
    public async Task StatusOnDecision_WithBlockValue_ErrorsNamingAllowed()
    {
        var id = await CreateAndGetId("+logmark d: a decision");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "resolved" }), CancellationToken.None);

        res.IsError.Should().BeTrue();
        res.Content[0].Text!.Should().Contain("frozen");
        res.Content[0].Text!.Should().Contain("rejected");
    }

    [Fact]
    public async Task StatusOnLogEntry_Errors()
    {
        var id = await CreateAndGetId("+logmark just a plain log line");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, status = "frozen" }), CancellationToken.None);

        res.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownEntryId_ReturnsNotFound()
    {
        var res = await _tool.ExecuteAsync(Args(new { entry_id = "deadbeef", status = "frozen" }), CancellationToken.None);
        res.IsError.Should().BeTrue();
        res.Content[0].Text!.Should().Contain("not found");
    }

    [Fact]
    public async Task NoActionableArg_Errors()
    {
        var id = await CreateAndGetId("+logmark d: a decision");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id }), CancellationToken.None);
        res.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteTask_MarksDoneAndReturnsCompleted()
    {
        var id = await CreateAndGetId("+logmark t: write the plan");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, complete = true }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        var json = res.Content[0].Text!;
        json.ShouldHaveJsonProperty("Action", "\"completed\"");
        json.Should().Contain("\"CompletedAt\":");
        json.Should().Contain("\"ActiveSession\":");
    }

    [Fact]
    public async Task CompleteTask_WithResolution_EchoesResolution()
    {
        var id = await CreateAndGetId("+logmark t: refactor finder");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, complete = true, resolution = "done in pr 12" }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Resolution", "\"done in pr 12\"");
    }

    [Fact]
    public async Task CompleteTask_Twice_ReportsAlreadyCompleted()
    {
        var id = await CreateAndGetId("+logmark t: idempotent complete");
        await _tool.ExecuteAsync(Args(new { entry_id = id, complete = true }), CancellationToken.None);
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, complete = true }), CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("AlreadyCompleted", "true");
    }

    [Fact]
    public async Task Complete_OnNonTask_Errors()
    {
        var id = await CreateAndGetId("+logmark i: an idea, not a task");
        var res = await _tool.ExecuteAsync(Args(new { entry_id = id, complete = true }), CancellationToken.None);

        res.IsError.Should().BeTrue();
    }

    /// <summary>
    /// The task's second exit (layer 3b, 2026-07-27). Before it existed the
    /// only way to close a task overtaken by other work was complete: true,
    /// which records in the vault that work happened when it did not.
    /// </summary>
    [Fact]
    public async Task DismissTask_TagsDismissed_AndDoesNotStrikeItThrough()
    {
        var id = await CreateAndGetId("+logmark t: start phase 4 cockpit");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "dismissed", resolution = "obsoleted by cockpit v1" }),
            CancellationToken.None);

        res.IsError.Should().BeFalse();
        res.Content[0].Text!.ShouldHaveJsonProperty("Action", "\"dismissed\"");
        res.Content[0].Text!.ShouldHaveJsonProperty("Resolution", "\"obsoleted by cockpit v1\"");
        var text = FileTextContaining(_vault, id);
        text.Should().Contain("#dismissed");
        text.Should().Contain("resolved: obsoleted by cockpit v1");
        text.Should().NotContain("~~");
    }

    [Fact]
    public async Task DismissTask_WithoutResolution_Errors()
    {
        var id = await CreateAndGetId("+logmark t: no reason given");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "dismissed" }), CancellationToken.None);

        res.IsError.Should().BeTrue();
        FileTextContaining(_vault, id).Should().NotContain("#dismissed");
    }

    [Fact]
    public async Task Status_OnTask_OnlyAcceptsDismissed()
    {
        var id = await CreateAndGetId("+logmark t: wrong status");
        var res = await _tool.ExecuteAsync(
            Args(new { entry_id = id, status = "resolved", resolution = "x" }), CancellationToken.None);

        res.IsError.Should().BeTrue();
        res.Content[0].Text!.Should().Contain("dismissed");
    }

    private static Dictionary<string, object?> Args(object obj)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        var d = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.Clone();
        return d;
    }

    private class TestRoutePromptService : IRoutePromptService
    {
        public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
            => Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, options.DefaultPath ?? $"projects/{routeName}"));
    }
}
