using FluentAssertions;
using LogMark.Models;
using LogMark.Services;
using LogMark.Services.EntryCache;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Private-route rejection (capturing to a route marked <c>private: true</c>
/// through a headless surface) is a closed-sensitivity-service behavior: the
/// open build has no gate at all when sensitivity is null (see
/// VaultHostBootstrap), so it cannot exercise "returns exit code 2" here. That
/// coverage lives in LogMark.CLI.Producers.Tests.CaptureCommandPrivateRouteTests.
/// </summary>
[Collection("EnvironmentTests")]
public class CaptureCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    public CaptureCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
        // Save and clear license tier to avoid pollution from parallel tests
        _originalTier = Environment.GetEnvironmentVariable("LOGMARK_LICENSE_TIER");
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", _originalTier);
        _vault.Dispose();
    }

    [Fact]
    public void Capture_ImplicitLog_SavesEntry()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "deployed v2.3 to production");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");
    }

    [Fact]
    public void Capture_Task_SavesEntry()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: review PR by friday");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
    }

    [Fact]
    public void Capture_Idea_SavesEntry()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "i: cache layer for API");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved idea");
    }

    [Fact]
    public void Capture_Block_SavesEntry()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: rate limiting still broken");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved block");
    }

    [Fact]
    public void Capture_Decision_SavesEntry()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "d: using SQLite for local storage");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved decision");
    }

    [Fact]
    public void Capture_ExplicitTypeFlag_SetsType()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "--type", "task", "review PR by friday");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
    }

    [Fact]
    public void Capture_RoutedEntry_SavesCorrectly()
    {
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n");

        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "+backend t: fix API endpoint");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved task");
    }

    // A +word in prose is not a route unless it is declared: it stays in the
    // text, nothing is created, and the CLI says so on stderr
    // (lmk-headless-inline-route-tokens-cli-backfill-b184).
    [Fact]
    public void Capture_UndeclaredPlusWord_IsKeptAsTextAndCreatesNoRoute()
    {
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n\n## Domains\n");

        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture",
            "+backend landed cost priced at +USD 5,999 street");

        result.ExitCode.Should().Be(0);
        var routed = Path.Combine(_vault.VaultPath,
            PathConstants.GetRoutedFilePath("projects/backend", "diary", DateTime.Now.Year));
        File.ReadAllText(routed).Should().Contain("priced at +USD 5,999 street");
        File.ReadAllText(Path.Combine(_vault.VaultPath, PathConstants.GetRoutesPath()))
            .Should().NotContain("### USD");
        Directory.Exists(Path.Combine(_vault.VaultPath, "logmark", "USD")).Should().BeFalse();
        result.StdErr.Should().Contain("+USD");
    }

    [Fact]
    public void Capture_ToNonPrivateRoute_Succeeds()
    {
        // Baseline: a non-private routed entry still saves normally.
        _vault.WithStandardStructure();
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n");

        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "test entry +backend");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");
    }

    [Fact]
    public void Capture_JsonOutput_ReturnsJson()
    {
        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "capture", "t: test json output");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"path\":");
        result.StdOut.Should().Contain("\"type\":");
        result.StdOut.Should().Contain("task");
    }

    [Fact]
    public void Capture_NoText_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("No content");
    }

    [Fact]
    public void Capture_NoVault_ShowsError()
    {
        var result = CliRunner.Run("--vault", "/nonexistent/vault/path", "capture", "test");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("does not exist");
    }

    [Fact]
    public async Task Capture_VerifyThroughCache()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: review PR by friday");
        result.ExitCode.Should().Be(0);

        // Verify through Core: read back from vault
        var cache = new EntryCache(_vault.VaultPath);
        cache.StartWarmup();
        if (cache.WarmupTask != null) await cache.WarmupTask;
        var searchService = new SearchService(cache);
        var searchResult = searchService.Search(new SearchQuery { Types = new[] { EntryType.Task } });
        searchResult.Entries.Should().ContainSingle()
            .Which.Entry.Content.Should().Contain("review PR by friday");
    }

    [Fact]
    public async Task Capture_MultipleWords_JoinedAsContent()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "this", "is", "a", "multi-word", "entry");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Saved log");

        // Verify content was joined
        var cache = new EntryCache(_vault.VaultPath);
        cache.StartWarmup();
        if (cache.WarmupTask != null) await cache.WarmupTask;
        var searchResult = new SearchService(cache).Search(new SearchQuery { Text = "multi-word" });
        searchResult.Entries.Should().NotBeEmpty();
    }
}
