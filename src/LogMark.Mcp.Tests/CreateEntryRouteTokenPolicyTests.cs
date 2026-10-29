using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;
using LogMark.Services;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Which +tokens in a create_entry body are routing directives. Agents quote
/// prose that carries a '+' (ASX forms mark defined terms as "+Security"), and
/// every whitespace-preceded +word used to be routed - an undeclared one was
/// auto-created flat at the vault root and the entry was double-written there.
/// Incidents 2026-08-23 (+USD) and 2026-09-07 (+Security) in the dogfooding vault.
/// Rules: an explicit route argument is the only destination; with no route
/// argument, only declared routes route; an inline +word never creates a route
/// and stays in the text verbatim. Uses the production headless prompt service
/// on a multi-section vault, the shape that produced the strays.
/// </summary>
public class CreateEntryRouteTokenPolicyTests : IDisposable
{
    private readonly string _vaultPath =
        Path.Combine(Path.GetTempPath(), $"logmark-mcp-tokens-{Guid.NewGuid()}");
    private readonly CreateEntryTool _tool;

    public CreateEntryRouteTokenPolicyTests()
    {
        var routesPath = Path.Combine(_vaultPath, PathConstants.GetRoutesPath());
        Directory.CreateDirectory(Path.GetDirectoryName(routesPath)!);
        File.WriteAllText(routesPath,
            "# Routes\n\n## Projects\n\n" +
            "### iasx\n- path: projects/asx-infra\n\n" +
            "### logmark\n- path: projects/LogMark\n\n" +
            "## Domains\n\n" +
            "### llm\n- path: domains/ai-llm\n\n");

        var notationParser = new NotationParser();
        var logger = new SystemLogger(_vaultPath);
        var routeService = new RouteService(_vaultPath, logger, new HeadlessRoutePromptService());
        routeService.LoadRoutes();
        var sessionService = new SessionService(_vaultPath, logger);

        _tool = new CreateEntryTool(new VaultServices
        {
            VaultPath = _vaultPath,
            VaultService = new VaultService(_vaultPath, notationParser),
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = logger,
            SessionService = sessionService,
            SessionContext = new McpSessionContext(sessionService, logger, "test-agent", null)
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultPath))
            Directory.Delete(_vaultPath, true);
    }

    [Fact]
    public async Task ExplicitRoute_InlineUndeclaredToken_IsTextNotARoute()
    {
        var result = await Run(new()
        {
            ["content"] = "Batch 31 closed: FWD's printed 'ASX +Security Description' field is the discriminator.",
            ["route"] = "+iasx",
        });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        RoutesFile().Should().NotContain("### Security");
        Directory.Exists(Path.Combine(_vaultPath, "logmark", "Security")).Should().BeFalse();

        var routed = ReadYearFile("projects/asx-infra", "diary");
        routed.Should().Contain("'ASX +Security Description'");
        Json(result).GetProperty("IgnoredRouteTokens").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("Security");
    }

    [Fact]
    public async Task ExplicitRoute_InlineDeclaredOtherRoute_WritesOnlyToExplicitRoute()
    {
        var result = await Run(new()
        {
            ["content"] = "Mirrors what +logmark does for capture.",
            ["route"] = "+iasx",
        });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        ReadYearFile("projects/asx-infra", "diary").Should().Contain("Mirrors what +logmark does");
        File.Exists(YearFilePath("projects/LogMark", "diary")).Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitRoute_InlineScopeOfSameRoute_StillRefinesTheRoute()
    {
        var result = await Run(new()
        {
            ["content"] = "+iasx/ops runbook drafted",
            ["route"] = "+iasx",
        });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        File.Exists(YearFilePath("projects/asx-infra/ops", "diary")).Should().BeTrue();
    }

    // 2026-10-20: route llm + scope local-deployment, text "bakeoff variant +schema
    // (vLLM ...)". A pre-10-10 MCP build created route "schema" and wrote a second
    // copy there with the word stripped from both.
    [Fact]
    public async Task ExplicitRouteWithScope_InlineUndeclaredToken_IsTextNotARoute()
    {
        var result = await Run(new()
        {
            ["content"] = "New since this morning: bakeoff variant +schema (vLLM structured outputs, 1b83ad3c).",
            ["route"] = "+llm",
            ["scope"] = "local-deployment",
        });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        RoutesFile().Should().NotContain("### schema");
        Directory.Exists(Path.Combine(_vaultPath, "logmark", "schema")).Should().BeFalse();
        ReadYearFile("domains/ai-llm/local-deployment", "diary")
            .Should().Contain("bakeoff variant +schema (vLLM structured outputs");
    }

    [Fact]
    public async Task NoRouteArg_InlineUndeclaredToken_GoesToInboxVerbatim()
    {
        var result = await Run(new()
        {
            ["content"] = "Landed cost for a 4TB unit, priced at +USD 5,999 street.",
        });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        RoutesFile().Should().NotContain("### USD");
        Directory.Exists(Path.Combine(_vaultPath, "logmark", "USD")).Should().BeFalse();

        var inbox = File.ReadAllText(Path.Combine(_vaultPath,
            PathConstants.GetUnroutedFilePath("diary", DateTime.Now.Year)));
        inbox.Should().Contain("priced at +USD 5,999");
    }

    [Fact]
    public async Task NoRouteArg_InlineDeclaredToken_StillRoutes()
    {
        var result = await Run(new() { ["content"] = "+llm i: prefill dominates on the GB10" });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        ReadYearFile("domains/ai-llm", "ideas").Should().Contain("prefill dominates on the GB10");
    }

    [Fact]
    public async Task NoRouteArg_TwoDeclaredRoutes_ReturnedIdIsInTheReturnedPath()
    {
        var result = await Run(new() { ["content"] = "+llm +logmark i: shared between two routes" });

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var json = Json(result);
        var path = json.GetProperty("Path").GetString()!;
        var id = json.GetProperty("EntryId").GetString()!;
        File.ReadAllText(Path.Combine(_vaultPath, path)).Should().Contain($"^{id}");
    }

    [Fact]
    public async Task ExplicitUndeclaredRoute_InMultiSectionVault_IsRejectedWithoutWriting()
    {
        var result = await Run(new()
        {
            ["content"] = "first note for a brand new project",
            ["route"] = "+brandnew",
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("update_routes");
        RoutesFile().Should().NotContain("### brandnew");
        Directory.Exists(Path.Combine(_vaultPath, "logmark", "brandnew")).Should().BeFalse();
        File.Exists(Path.Combine(_vaultPath, PathConstants.GetUnroutedFilePath("diary", DateTime.Now.Year)))
            .Should().BeFalse();
    }

    private Task<ToolCallResult> Run(Dictionary<string, string> args) =>
        _tool.ExecuteAsync(
            args.ToDictionary(kv => kv.Key, kv => (object?)JsonDocument.Parse(JsonSerializer.Serialize(kv.Value)).RootElement),
            CancellationToken.None);

    private static JsonElement Json(ToolCallResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement;

    private string RoutesFile() =>
        File.ReadAllText(Path.Combine(_vaultPath, PathConstants.GetRoutesPath()));

    private string YearFilePath(string routePath, string typeName) =>
        Path.Combine(_vaultPath, PathConstants.GetRoutedFilePath(routePath, typeName, DateTime.Now.Year));

    private string ReadYearFile(string routePath, string typeName) =>
        File.ReadAllText(YearFilePath(routePath, typeName));
}
