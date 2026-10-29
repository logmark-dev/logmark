using FluentAssertions;
using LogMark.Mcp;
using LogMark.Services;
using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Mcp.Tests;

public class HeadlessRoutePromptServiceTests
{
    private static readonly HeadlessRoutePromptService Svc = new();

    [Fact]
    public async Task SingleSectionVault_UsesThatSectionsSuggestedPath()
    {
        var options = new RoutePromptOptions(
            new[] { new RouteOption("Project", "projects/autogen") },
            DefaultPath: "projects/autogen");

        var result = await Svc.PromptForRouteCreationAsync("autogen", options);

        result.Outcome.Should().Be(RoutePromptOutcome.Create);
        result.Path.Should().Be("projects/autogen");
    }

    // Filing flat at the vault root put +USD and +Security beside projects/ and
    // domains/ in a multi-section vault. Without a human to pick a section the
    // headless surface declines; the caller declares the route with a path.
    [Fact]
    public async Task MultiSectionVault_DeclinesRatherThanFilingAtVaultRoot()
    {
        var options = new RoutePromptOptions(
            new[]
            {
                new RouteOption("Project", "projects/autogen"),
                new RouteOption("Domain", "domains/autogen"),
            },
            DefaultPath: "projects/autogen");

        var result = await Svc.PromptForRouteCreationAsync("autogen", options);

        result.Outcome.Should().Be(RoutePromptOutcome.Cancel);
    }

    [Fact]
    public async Task SectionlessVault_FilesFlat()
    {
        var options = new RoutePromptOptions(System.Array.Empty<RouteOption>(), DefaultPath: "autogen");

        var result = await Svc.PromptForRouteCreationAsync("autogen", options);

        result.Outcome.Should().Be(RoutePromptOutcome.Create);
        result.Path.Should().Be("autogen");
    }
}
