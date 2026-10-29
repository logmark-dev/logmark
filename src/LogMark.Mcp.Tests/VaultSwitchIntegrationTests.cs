using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

public class VaultSwitchIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public VaultSwitchIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logmark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void InitializeVaultServices_PopulatesAllProperties()
    {
        var vaultPath = CreateTestVault("vault1");
        var services = new VaultServices();

        McpServerBootstrap.InitializeOpenVaultServices(services, new McpVaultInit(vaultPath, "mcp", null, 30));

        services.VaultPath.Should().Be(vaultPath);
        services.VaultService.Should().NotBeNull();
        services.NotationParser.Should().NotBeNull();
        services.RouteService.Should().NotBeNull();
        services.Logger.Should().NotBeNull();
        services.EntryCache.Should().NotBeNull();
        services.SearchService.Should().NotBeNull();
        services.EntryResolver.Should().NotBeNull();
        services.SessionService.Should().NotBeNull();
        services.SessionContext.Should().NotBeNull();
        services.TaskTracker.Should().NotBeNull();
    }

    [Fact]
    public void ReinitializeVaultServices_UpdatesVaultPath()
    {
        var vault1 = CreateTestVault("vault1");
        var vault2 = CreateTestVault("vault2");
        var services = new VaultServices();

        McpServerBootstrap.InitializeOpenVaultServices(services, new McpVaultInit(vault1, "mcp", null, 30));
        services.VaultPath.Should().Be(vault1);

        McpServerBootstrap.InitializeOpenVaultServices(services, new McpVaultInit(vault2, "mcp", null, 30));
        services.VaultPath.Should().Be(vault2);
    }

    [Fact]
    public void ReinitializeVaultServices_CreatesNewServiceInstances()
    {
        var vault1 = CreateTestVault("vault1");
        var vault2 = CreateTestVault("vault2");
        var services = new VaultServices();

        McpServerBootstrap.InitializeOpenVaultServices(services, new McpVaultInit(vault1, "mcp", null, 30));
        var firstVaultService = services.VaultService;
        var firstRouteService = services.RouteService;

        McpServerBootstrap.InitializeOpenVaultServices(services, new McpVaultInit(vault2, "mcp", null, 30));
        services.VaultService.Should().NotBeSameAs(firstVaultService);
        services.RouteService.Should().NotBeSameAs(firstRouteService);
    }

    private string CreateTestVault(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "logmark", "_system"));
        return path;
    }
}
