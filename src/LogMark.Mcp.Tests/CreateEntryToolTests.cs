using System.Text.Json;
using FluentAssertions;
using LogMark.Exceptions;
using LogMark.Interfaces;
using LogMark.Mcp.Tools;
using LogMark.Models;
using LogMark.Models.Sensitivity;
using LogMark.Services;
using Xunit;

namespace LogMark.Mcp.Tests;

public class CreateEntryToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly CreateEntryTool _tool;

    public CreateEntryToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Create minimal routes file
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath, "# Routes\n\n## Projects\n\n## Domains\n");

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_testVaultPath, notationParser);
        var systemLogger = new SystemLogger(_testVaultPath);
        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService());
        routeService.LoadRoutes();
        var sessionService = new SessionService(_testVaultPath, systemLogger);
        var sessionContext = new McpSessionContext(sessionService, systemLogger, "test-agent", "general");

        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultService = vaultService,
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = sessionService,
            SessionContext = sessionContext
        };
        _tool = new CreateEntryTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    [Fact]
    public async Task Execute_SimpleLogEntry_CreatesEntry()
    {
        // Arrange
        var args = CreateArgs("content", "Test log entry");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(1);

        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Success", "true");
        json.ShouldHaveJsonProperty("ParsedType", "\"log\"");
    }

    [Fact]
    public async Task Execute_TaskEntry_ParsesTypeFromNotation()
    {
        // Arrange
        var args = CreateArgs("content", "t: Fix the bug by friday");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"task\"");
    }

    [Fact]
    public async Task Execute_IdeaEntry_ParsesTypeFromNotation()
    {
        // Arrange
        var args = CreateArgs("content", "i: Consider caching approach");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"idea\"");
    }

    [Fact]
    public async Task Execute_DecisionEntry_ParsesTypeFromNotation()
    {
        // Arrange
        var args = CreateArgs("content", "d: Using SQLite for local storage");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"decision\"");
    }

    [Fact]
    public async Task Execute_BlockEntry_ParsesTypeFromNotation()
    {
        // Arrange
        var args = CreateArgs("content", "b: Stuck on rate limiting");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"block\"");
    }

    [Fact]
    public async Task Execute_ExplicitType_OverridesDefault()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"Some content\"").RootElement,
            ["entry_type"] = JsonDocument.Parse("\"idea\"").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"idea\"");
    }

    [Fact]
    public async Task Execute_NotationPrefixOverridesExplicitType()
    {
        // Arrange - Notation prefix (t:) should override explicit type (idea)
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"t: Fix this bug\"").RootElement,
            ["entry_type"] = JsonDocument.Parse("\"idea\"").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("ParsedType", "\"task\"");
    }

    [Fact]
    public async Task Execute_MissingContent_ReturnsError()
    {
        // Arrange
        var args = new Dictionary<string, object?>();

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("content");
    }

    [Fact]
    public async Task Execute_EmptyContent_ReturnsError()
    {
        // Arrange
        var args = CreateArgs("content", "");

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("empty");
    }

    /// <summary>
    /// Session attribution used to be assigned after the entry was already written,
    /// so it never reached disk - every MCP-created entry read back with no session
    /// line, and the viewer's session metadata row had nothing to link. Pin that the
    /// stamped id both persists and names a real file under _sessions/.
    /// </summary>
    [Fact]
    public async Task Execute_PersistsSessionAttribution_PointingAtARealSessionFile()
    {
        var result = await _tool.ExecuteAsync(CreateArgs("content", "Test entry"), CancellationToken.None);
        result.IsError.Should().BeFalse();

        // Path comes back vault-relative.
        var savedPath = Path.Combine(_testVaultPath, JsonDocument.Parse(result.Content[0].Text!)
            .RootElement.GetProperty("Path").GetString()!);
        var sessionLine = (await File.ReadAllLinesAsync(savedPath))
            .FirstOrDefault(l => l.StartsWith("session:", StringComparison.OrdinalIgnoreCase));

        sessionLine.Should().NotBeNull("the entry on disk carries its session attribution");
        var sessionId = sessionLine!.Substring("session:".Length).Trim();
        SessionService.ResolveSessionFilePath(_testVaultPath, sessionId).Should().NotBeNull(
            "a stamped session id must name a session file, not a reference that dead-ends");
    }

    // lmk-jwy9: connect launches the server with --agent <framework>, so an
    // entry created without source_agent must carry that identity, not the
    // bare "mcp" fallback that lost which agent wrote it.
    [Fact]
    public async Task Execute_WithoutSourceAgent_FallsBackToTheServerAgentIdentity()
    {
        var args = CreateArgs("content", "Test entry");

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        (await ReadSourceLineAsync(result)).Should().Be("source: agent:test-agent",
            "the session context was built with agent id test-agent and no source_agent was passed");
    }

    [Fact]
    public async Task Execute_WithSourceAgent_ExplicitNameWinsOverTheServerIdentity()
    {
        var args = CreateArgs("content", "Test entry");
        args["source_agent"] = JsonDocument.Parse("\"custom-bot\"").RootElement;

        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        (await ReadSourceLineAsync(result)).Should().Be("source: agent:custom-bot");
    }

    [Fact]
    public async Task Execute_WithoutAnyAgentIdentity_FallsBackToMcp()
    {
        var tool = new CreateEntryTool(ServicesWithAgent(""));
        var args = CreateArgs("content", "Test entry");

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        (await ReadSourceLineAsync(result)).Should().Be("source: agent:mcp",
            "a server started without --agent still marks the entry as agent-written");
    }

    private async Task<string?> ReadSourceLineAsync(LogMark.Mcp.Protocol.ToolCallResult result)
    {
        var savedPath = Path.Combine(_testVaultPath, JsonDocument.Parse(result.Content[0].Text!)
            .RootElement.GetProperty("Path").GetString()!);
        return (await File.ReadAllLinesAsync(savedPath))
            .FirstOrDefault(l => l.StartsWith("source:", StringComparison.OrdinalIgnoreCase));
    }

    private VaultServices ServicesWithAgent(string agentId)
    {
        var notationParser = new NotationParser();
        var systemLogger = new SystemLogger(_testVaultPath);
        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService());
        routeService.LoadRoutes();
        var sessionService = new SessionService(_testVaultPath, systemLogger);
        return new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultService = new VaultService(_testVaultPath, notationParser),
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = sessionService,
            SessionContext = new McpSessionContext(sessionService, systemLogger, agentId, "general")
        };
    }

    [Fact]
    public async Task Execute_WithTags_AddsTags()
    {
        // Arrange
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"Test entry\"").RootElement,
            ["tags"] = JsonDocument.Parse("[\"urgent\", \"bug\"]").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.Should().Contain("urgent");
        json.Should().Contain("bug");
    }

    // Regression: facet tag present both inline in content AND in the tags array must not
    // be appended a second time. Under the leaf rule, ExtractTags returns "4090" (the leaf
    // of "hardware:4090"), and the dedup compares that leaf against the tags array element
    // after the same leaf transformation, so both sides collapse to "4090".
    //
    // Old code compared full "hardware/4090" against leaf "4090" -- always unequal, so the
    // tag was always appended, producing "#hardware/4090 #hardware/4090" on disk.
    [Fact]
    public async Task Execute_FacetTagInBothContentAndTagsArray_NoduplicateOnDisk()
    {
        // Arrange: content has inline #hardware:4090; tags array carries the same tag as "hardware:4090"
        var args = new Dictionary<string, object?>
        {
            ["content"] = JsonDocument.Parse("\"Test GPU entry #hardware:4090\"").RootElement,
            ["tags"] = JsonDocument.Parse("[\"hardware:4090\"]").RootElement
        };

        // Act
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);

        // Assert: no error and "4090" (the leaf) appears exactly once in the Tags array
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text ?? "";
        json.ShouldHaveJsonProperty("Success", "true");

        // Count occurrences: "4090" must appear exactly once in the serialized Tags array
        var occurrences = 0;
        var search = "4090";
        var idx = 0;
        while ((idx = json.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            occurrences++;
            idx += search.Length;
        }
        occurrences.Should().Be(1, because: "facet tag present inline should not be duplicated by the tags array");

        // Also verify the on-disk file body: the normalized form "#hardware/4090" must appear
        // exactly once. Extract the relative path from the result JSON, combine with the test
        // vault root, and count occurrences in the raw file content.
        var resultDoc = JsonDocument.Parse(json);
        var relativePath = resultDoc.RootElement.GetProperty("Path").GetString() ?? "";
        var diskPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_testVaultPath, relativePath);
        var diskContent = await File.ReadAllTextAsync(diskPath);

        var diskOccurrences = 0;
        var diskSearch = "hardware/4090";
        var diskIdx = 0;
        while ((diskIdx = diskContent.IndexOf(diskSearch, diskIdx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            diskOccurrences++;
            diskIdx += diskSearch.Length;
        }
        diskOccurrences.Should().Be(1, because: "on-disk entry body must contain #hardware/4090 exactly once, not duplicated by the tags array dedup bug");
    }

    // Private-route gate tests.
    // These use a separate vault fixture with one private route and one public route,
    // wired with a stub ISensitivityService that throws RoutePrivateDesktopOnlyException.
    // The stub bypasses the real closed sensitivity stack so tests don't require
    // the local embedding model registry.

    [Fact]
    public async Task Execute_ToPrivateRoute_ReturnsStructuredError()
    {
        using var fixture = new PrivateRouteVaultFixture();

        var result = await fixture.Tool.ExecuteAsync(
            CreateArgs("content", "+secretroute i: idea for private route"),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("ROUTE_PRIVATE_DESKTOP_ONLY");
        result.Content[0].Text.Should().Contain("private");
        result.Content[0].Text.Should().Contain("desktop app");
    }

    [Fact]
    public async Task Execute_ToNonPrivateRoute_Succeeds()
    {
        using var fixture = new PrivateRouteVaultFixture();

        var result = await fixture.Tool.ExecuteAsync(
            CreateArgs("content", "+publicroute i: safe idea for public route"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.ShouldHaveJsonProperty("Success", "true");
    }

    private static Dictionary<string, object?> CreateArgs(string key, string value)
    {
        return new Dictionary<string, object?>
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement
        };
    }

    private class TestRoutePromptService : IRoutePromptService
    {
        public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
        {
            var path = options.DefaultPath ?? $"projects/{routeName}";
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
        }
    }

    /// <summary>
    /// Test fixture with one private route (+secretroute) and one public route (+publicroute).
    /// Uses a stub ISensitivityService so tests don't need a real Shroud vault.shroud registry.
    /// The stub throws RoutePrivateDesktopOnlyException on MaskAndRegisterAsync -- matching the
    /// real SensitivityService behavior when prompt is null (headless/MCP mode).
    /// </summary>
    private class PrivateRouteVaultFixture : IDisposable
    {
        public readonly string VaultPath;
        public readonly CreateEntryTool Tool;

        public PrivateRouteVaultFixture()
        {
            VaultPath = Path.Combine(Path.GetTempPath(), $"logmark-priv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(VaultPath);

            var logmarkDir = Path.Combine(VaultPath, PathConstants.GetSystemFolder());
            Directory.CreateDirectory(logmarkDir);

            // Routes file: one private route, one public route.
            var routesPath = Path.Combine(VaultPath, PathConstants.GetRoutesPath());
            File.WriteAllText(routesPath,
                "# Routes\n\n## Projects\n\n" +
                "### secretroute\n- path: projects/secretroute\n- private: financial\n\n" +
                "### publicroute\n- path: projects/publicroute\n\n");

            var notationParser = new NotationParser();
            var vaultService = new VaultService(VaultPath, notationParser);
            var systemLogger = new SystemLogger(VaultPath);

            var sensitivity = new PrivateRouteRejectingSensitivity();
            var routeService = new RouteService(VaultPath, systemLogger, new TestRoutePromptService(), sensitivity: sensitivity);
            routeService.LoadRoutes();

            var sessionService = new SessionService(VaultPath, systemLogger);
            var sessionContext = new McpSessionContext(sessionService, systemLogger, "test-agent", null);
            var vaultWriter = new VaultWriter();

            var services = new VaultServices
            {
                VaultPath = VaultPath,
                VaultWriter = vaultWriter,
                VaultService = vaultService,
                NotationParser = notationParser,
                RouteService = routeService,
                Logger = systemLogger,
                SessionService = sessionService,
                SessionContext = sessionContext
            };

            Tool = new CreateEntryTool(services);
        }

        public void Dispose()
        {
            if (Directory.Exists(VaultPath))
                Directory.Delete(VaultPath, true);
        }

        private class TestRoutePromptService : IRoutePromptService
        {
            public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
            {
                var path = options.DefaultPath ?? $"projects/{routeName}";
                return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
            }
        }
    }

    /// <summary>
    /// Stub ISensitivityService that rejects all private-route writes by throwing
    /// RoutePrivateDesktopOnlyException. Mirrors the real SensitivityService behavior
    /// when constructed with prompt: null (headless/MCP mode).
    /// </summary>
    private class PrivateRouteRejectingSensitivity : ISensitivityService
    {
        public bool IsUnlocked => false;

        public Task<MaskResult> MaskAndRegisterAsync(string text, Route route)
        {
            throw new RoutePrivateDesktopOnlyException(route.Name);
        }

        public string Reveal(string text) => text;

        public ScanResult Scan(string text, PrivatePreset routePreset) =>
            new ScanResult([]);

        public Task<bool> EnsureUnlockedAsync() => Task.FromResult(false);
    }
}
