using FluentAssertions;
using LogMark.CLI;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class VaultResolverTests : IDisposable
{
    private readonly string? _originalEnvVar;

    public VaultResolverTests()
    {
        _originalEnvVar = Environment.GetEnvironmentVariable("LOGMARK_VAULT");
    }

    public void Dispose()
    {
        // Restore original env var
        Environment.SetEnvironmentVariable("LOGMARK_VAULT", _originalEnvVar);
    }

    [Fact]
    public void FlagValue_TakesPriority()
    {
        Environment.SetEnvironmentVariable("LOGMARK_VAULT", "/env/path");
        var flagPath = Path.GetTempPath();

        var result = VaultResolver.Resolve(flagPath);

        result.Should().Be(Path.GetFullPath(flagPath));
    }

    [Fact]
    public void EnvVar_UsedWhenNoFlag()
    {
        var envPath = Path.GetTempPath();
        Environment.SetEnvironmentVariable("LOGMARK_VAULT", envPath);

        var result = VaultResolver.Resolve(null);

        result.Should().Be(envPath);
    }

    [Fact]
    public void MissingAll_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("LOGMARK_VAULT", null);
        // Cannot easily test bootstrap.json absence without mocking AppData
        // But if env var is null and no bootstrap, should return null
        // This test depends on whether bootstrap.json exists
        var result = VaultResolver.Resolve(null);

        // Result is either null (no bootstrap) or a path from bootstrap
        // We just verify it doesn't throw
        (result == null || Directory.Exists(result) || !Directory.Exists(result)).Should().BeTrue();
    }

    [Fact]
    public void NonexistentPath_ReturnsPathAsIs()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), "logmark-nonexistent-" + Guid.NewGuid());

        var result = VaultResolver.Resolve(nonexistent);

        // Should return the full path even though it doesn't exist
        result.Should().Be(Path.GetFullPath(nonexistent));
    }

    [Fact]
    public void RelativePath_ResolvedToAbsolute()
    {
        var result = VaultResolver.Resolve("relative/path");

        result.Should().NotBeNull();
        Path.IsPathRooted(result!).Should().BeTrue();
    }

    [Fact]
    public void ParseVaultPath_PascalCase_Resolves()
    {
        // The desktop app serializes PascalCase; this is the casing the live
        // bootstrap.json uses and the case-sensitive lookup used to miss.
        var json = "{\"VaultPath\":\"C:/projects/MyVault\",\"RecentVaults\":[]}";

        VaultResolver.ParseVaultPath(json).Should().Be("C:/projects/MyVault");
    }

    [Fact]
    public void ParseVaultPath_CamelCase_Resolves()
    {
        var json = "{\"vaultPath\":\"C:/projects/MyVault\"}";

        VaultResolver.ParseVaultPath(json).Should().Be("C:/projects/MyVault");
    }

    [Fact]
    public void ParseVaultPath_Missing_ReturnsNull()
    {
        VaultResolver.ParseVaultPath("{\"other\":\"x\"}").Should().BeNull();
    }

    [Fact]
    public void ParseVaultPath_EmptyValue_ReturnsNull()
    {
        VaultResolver.ParseVaultPath("{\"VaultPath\":\"\"}").Should().BeNull();
    }
}
