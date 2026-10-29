using FluentAssertions;
using LogMark.Services;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

public class VaultRoutingE2ETests : IDisposable
{
    private readonly string _tempPath;

    public VaultRoutingE2ETests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"logmark-routing-e2e-{Guid.NewGuid()}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    [Theory]
    [InlineData("operator", 2, "Client", "Operation")]
    [InlineData("research", 3, "Reading", "Idea", "Active")]
    [InlineData("build", 1, "Project")]
    [InlineData("journal", 0)]
    public void Bundle_GeneratesCorrectPromptOptions(string bundle, int expectedCount, params string[] expectedLabels)
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");
        initResult.ExitCode.Should().Be(0, $"Init should succeed: {initResult.StdErr}");

        // Act - Use RouteService to generate options
        var logger = new SystemLogger(_tempPath);
        var routeService = new RouteService(_tempPath, logger);
        routeService.LoadRoutes();

        var options = routeService.GeneratePromptOptions("acme");

        // Assert
        options.Choices.Should().HaveCount(expectedCount);

        foreach (var label in expectedLabels)
        {
            options.Choices.Select(c => c.Label).Should().Contain(label);
        }
    }

    [Theory]
    [InlineData("operator", "acme")]
    // The new bundles start from broad sections without nested seed routes, so
    // InferSuggestedPath has no nested-vs-flat signal and defaults to flat.
    [InlineData("build", "acme")]
    public void Bundle_GeneratesCorrectDefaultPath(string bundle, string expectedDefaultPath)
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");
        initResult.ExitCode.Should().Be(0);

        // Act
        var logger = new SystemLogger(_tempPath);
        var routeService = new RouteService(_tempPath, logger);
        routeService.LoadRoutes();

        var options = routeService.GeneratePromptOptions("acme");

        // Assert
        options.DefaultPath.Should().Be(expectedDefaultPath);
    }

    [Fact]
    public void JournalVault_GeneratesSimplePath()
    {
        // Arrange
        var initResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=journal");
        initResult.ExitCode.Should().Be(0);

        // Act
        var logger = new SystemLogger(_tempPath);
        var routeService = new RouteService(_tempPath, logger);
        routeService.LoadRoutes();

        var options = routeService.GeneratePromptOptions("acme");

        // Assert - journal has no section choices, just route name as path
        options.Choices.Should().BeEmpty();
        options.DefaultPath.Should().Be("acme");
    }

    [Theory]
    [InlineData("operator", "clients/newroute", "logmark/clients/newroute/_tasks")]
    [InlineData("build", "features/newroute", "logmark/features/newroute/_tasks")]
    public async Task CaptureToNewRoute_CreatesFileAtCorrectLocation(string bundle, string routePath, string expectedFolder)
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");
        initResult.ExitCode.Should().Be(0);

        var logger = new SystemLogger(_tempPath);
        var promptService = new MockRoutePromptService();
        // Use "newroute" instead of "acme" to avoid conflict with seeded entries
        promptService.SetCreateResponse("newroute", routePath);

        var routeService = new RouteService(_tempPath, logger, promptService);
        routeService.LoadRoutes();

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_tempPath, notationParser, routeService, logger);

        // Act - Parse and save entry
        var entry = notationParser.Parse("+newroute t: test task", "Test Context");
        await vaultService.SaveEntryAsync(entry);

        // Assert - File created in correct location
        var expectedPath = Path.Combine(_tempPath, expectedFolder);
        Directory.Exists(expectedPath).Should().BeTrue($"Expected folder to exist: {expectedPath}");

        // Find the year file for current year
        var currentYear = DateTime.Now.Year;
        var yearFile = Path.Combine(expectedPath, $"{currentYear}.md");
        File.Exists(yearFile).Should().BeTrue("Year file should be created");

        var content = File.ReadAllText(yearFile);
        content.Should().Contain("test task");
    }

    [Theory]
    [InlineData("operator", "clients/newroute")]
    [InlineData("build", "features/newroute")]
    public async Task CreateRoute_AddsToRoutesFile(string bundle, string routePath)
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, $"--bundle={bundle}");
        initResult.ExitCode.Should().Be(0);

        var logger = new SystemLogger(_tempPath);
        var promptService = new MockRoutePromptService();
        // Use "newroute" to avoid conflict with seeded entries
        promptService.SetCreateResponse("newroute", routePath);

        var routeService = new RouteService(_tempPath, logger, promptService);
        routeService.LoadRoutes();

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_tempPath, notationParser, routeService, logger);

        // Act - Parse and save entry (triggers route creation)
        var entry = notationParser.Parse("+newroute t: test task", "Test Context");
        await vaultService.SaveEntryAsync(entry);

        // Assert - Route added to routes.md
        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        var routesContent = File.ReadAllText(routesPath);

        routesContent.Should().Contain("### newroute");
        routesContent.Should().Contain($"path: {routePath}");
    }

    [Fact]
    public async Task ReferenceOnlyRoute_DoesNotCreateFolder()
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=operator");
        initResult.ExitCode.Should().Be(0);

        var logger = new SystemLogger(_tempPath);
        var promptService = new MockRoutePromptService();
        promptService.SetReferenceOnlyResponse("acme");

        var routeService = new RouteService(_tempPath, logger, promptService);
        routeService.LoadRoutes();

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_tempPath, notationParser, routeService, logger);

        // Act - Parse and save entry
        var entry = notationParser.Parse("+acme t: test task", "Test Context");
        await vaultService.SaveEntryAsync(entry);

        // Assert - No routed folder created
        var projectsPath = Path.Combine(_tempPath, "logmark", "clients", "acme");
        Directory.Exists(projectsPath).Should().BeFalse("Reference-only route should not create folder");

        // Entry goes to inbox instead
        var inboxPath = Path.Combine(_tempPath, "logmark", "_inbox", "_tasks");
        Directory.Exists(inboxPath).Should().BeTrue();
    }

    [Fact]
    public async Task CancelledRoute_DoesNotSaveRouteOrEntry()
    {
        // Arrange - Create vault via CLI
        var initResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=operator");
        initResult.ExitCode.Should().Be(0);

        var logger = new SystemLogger(_tempPath);
        var promptService = new MockRoutePromptService();
        promptService.SetCancelResponse("acme");

        var routeService = new RouteService(_tempPath, logger, promptService);
        routeService.LoadRoutes();

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_tempPath, notationParser, routeService, logger);

        // Act - Parse and save entry
        var entry = notationParser.Parse("+acme t: test task", "Test Context");
        await vaultService.SaveEntryAsync(entry);

        // Assert - Route not added to routes.md
        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        var routesContent = File.ReadAllText(routesPath);
        routesContent.Should().NotContain("### acme");

        // No routed folder created
        var projectsPath = Path.Combine(_tempPath, "logmark", "clients", "acme");
        Directory.Exists(projectsPath).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingRoute_DoesNotPrompt()
    {
        // Arrange - Create vault via CLI with operator bundle
        var initResult = CliRunner.Run("vault", "init", _tempPath, "--bundle=operator");
        initResult.ExitCode.Should().Be(0);

        // Manually add a route to routes.md
        var routesPath = Path.Combine(_tempPath, "logmark", "_system", "routes.md");
        var routesContent = File.ReadAllText(routesPath);
        routesContent += "\n\n### existing\n- path: clients/existing\n";
        File.WriteAllText(routesPath, routesContent);

        var logger = new SystemLogger(_tempPath);
        var promptService = new MockRoutePromptService();
        // Don't set any response - if prompt is called, it will return Cancel by default

        var routeService = new RouteService(_tempPath, logger, promptService);
        routeService.LoadRoutes();

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_tempPath, notationParser, routeService, logger);

        // Act - Parse and save entry to existing route
        var entry = notationParser.Parse("+existing t: test task", "Test Context");
        await vaultService.SaveEntryAsync(entry);

        // Assert - Prompt service was not called
        promptService.PromptedRoutes.Should().BeEmpty("Existing route should not prompt");

        // Entry was saved to the existing route path
        var tasksPath = Path.Combine(_tempPath, "logmark", "clients", "existing", "_tasks");
        Directory.Exists(tasksPath).Should().BeTrue();
    }
}
