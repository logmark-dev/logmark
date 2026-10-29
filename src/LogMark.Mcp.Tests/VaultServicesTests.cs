using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

public class VaultServicesTests
{
    [Fact]
    public void VaultPath_IsSettable()
    {
        var services = new VaultServices();
        services.VaultPath = "/test/vault";
        services.VaultPath.Should().Be("/test/vault");
    }

    [Fact]
    public void AllProperties_DefaultToNull()
    {
        var services = new VaultServices();
        services.VaultPath.Should().BeNull();
        services.VaultService.Should().BeNull();
        services.RouteService.Should().BeNull();
        services.Logger.Should().BeNull();
        services.EntryCache.Should().BeNull();
        services.SearchService.Should().BeNull();
        services.EntryResolver.Should().BeNull();
        services.SessionService.Should().BeNull();
        services.SessionContext.Should().BeNull();
        services.TaskTracker.Should().BeNull();
        services.NotationParser.Should().BeNull();
    }
}
