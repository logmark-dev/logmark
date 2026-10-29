using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class QueryCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;
    private readonly string? _originalTier;

    public QueryCommandTests()
    {
        _originalTier = Environment.GetEnvironmentVariable("LOGMARK_LICENSE_TIER");
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", null);
        _vault = new TestVaultBuilder()
            .WithStandardStructure()
            .WithEntry("_tasks", DateTime.Now.Year,
                "review PR by friday", DateTime.Now.AddHours(-1))
            .WithEntry("_ideas", DateTime.Now.Year,
                "cache layer for API #perf", DateTime.Now.AddHours(-2))
            .WithEntry("_blocks", DateTime.Now.Year,
                "rate limiting still broken", DateTime.Now.AddHours(-3))
            .WithEntry("_decisions", DateTime.Now.Year,
                "using SQLite for local storage", DateTime.Now.AddHours(-4))
            .WithEntry("_diary", DateTime.Now.Year,
                "deployed v2.3 to production", DateTime.Now.AddHours(-5));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOGMARK_LICENSE_TIER", _originalTier);
        _vault.Dispose();
    }

    [Fact]
    public void Recent_ReturnsEntries()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "recent");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("review PR");
        result.StdOut.Should().Contain("cache layer");
    }

    [Fact]
    public void Recent_WithCount_LimitsResults()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "recent", "2");

        result.ExitCode.Should().Be(0);
        // Should have at most 2 lines of entry output
        var lines = result.StdOut.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Recent_WithDays_AdjustsWindow()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "recent", "--days", "30");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("review PR");
    }

    [Fact]
    public void Recent_JsonOutput_ProducesValidJson()
    {
        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "recent");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"entries\":");
        result.StdOut.Should().Contain("\"total\":");
    }

    [Fact]
    public void Search_FindsMatchingEntries()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "search", "cache");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("cache layer");
    }

    [Fact]
    public void Search_NoQuery_ShowsUsage()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "search");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("No search query");
    }

    [Fact]
    public void Search_JsonOutput_ProducesValidJson()
    {
        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "search", "PR");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"entries\":");
    }

    [Fact]
    public void Search_RouteToken_ScopesToRoute()
    {
        // Regression (lmk-k20h): a +route token in the query must scope to that
        // route, not be matched as literal text. Route tokens are stripped from
        // stored content, so a substring text match for "+photonotes" never hits;
        // before the fix this fell through to unrelated results / semantic noise.
        _vault.WithEntry("projects/photonotes/_ideas", DateTime.Now.Year,
            "caption pipeline experiment", DateTime.Now.AddHours(-1));

        var result = CliRunner.Run("--vault", _vault.VaultPath, "search", "+photonotes");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("caption pipeline");
    }

    [Fact]
    public void Tasks_ReturnsTasks()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "tasks");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("review PR");
        // Should not contain non-task entries
        result.StdOut.Should().NotContain("deployed v2.3");
    }

    [Fact]
    public void Tasks_JsonOutput()
    {
        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "tasks");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"entries\":");
        result.StdOut.Should().Contain("\"type\": \"task\"");
    }

    [Fact]
    public void Blocks_ReturnsBlocks()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "blocks");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("rate limiting");
        result.StdOut.Should().NotContain("review PR");
    }

    [Fact]
    public void Tasks_ExcludesDismissedTasks()
    {
        // A dismissed task is closed work: it carries the #dismissed tag and no
        // strikethrough, so a reader filtering on IsCompleted alone lists it as
        // pending (lmk-cli-tasks-lists-dismissed-gdcc).
        using var vault = new TestVaultBuilder()
            .WithStandardStructure()
            .WithEntry("_tasks", DateTime.Now.Year, "ship the open one", DateTime.Now.AddHours(-1))
            .WithEntry("_tasks", DateTime.Now.Year, "drop the stale one\n\n#dismissed", DateTime.Now.AddHours(-2));

        var result = CliRunner.Run("--vault", vault.VaultPath, "tasks");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("ship the open one");
        result.StdOut.Should().NotContain("drop the stale one");
    }

    [Fact]
    public void Blocks_ExcludesResolvedAndDismissedBlocks()
    {
        using var vault = new TestVaultBuilder()
            .WithStandardStructure()
            .WithEntry("_blocks", DateTime.Now.Year, "still stuck on signing", DateTime.Now.AddHours(-1))
            .WithEntry("_blocks", DateTime.Now.Year, "token was broken #resolved", DateTime.Now.AddHours(-2))
            .WithEntry("_blocks", DateTime.Now.Year, "moot obstacle\n\n#dismissed", DateTime.Now.AddHours(-3));

        var result = CliRunner.Run("--vault", vault.VaultPath, "blocks");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("still stuck on signing");
        result.StdOut.Should().NotContain("token was broken");
        result.StdOut.Should().NotContain("moot obstacle");
    }

    [Theory]
    [InlineData("tasks", "_tasks")]
    [InlineData("blocks", "_blocks")]
    public void OpenWorkLists_LimitFlag_RaisesTheDefaultCap(string command, string folder)
    {
        var builder = new TestVaultBuilder().WithStandardStructure();
        for (int i = 0; i < 55; i++)
            builder.WithEntry(folder, DateTime.Now.Year, $"open item {i:D2}", DateTime.Now.AddMinutes(-i - 1));
        using var vault = builder;

        var capped = CliRunner.Run("--vault", vault.VaultPath, command);
        var raised = CliRunner.Run("--vault", vault.VaultPath, command, "--limit", "100");

        CountLines(capped.StdOut).Should().Be(50);
        raised.ExitCode.Should().Be(0);
        CountLines(raised.StdOut).Should().Be(55);
    }

    private static int CountLines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.Contains("open item"));

    [Fact]
    public void Decisions_ReturnsDecisions()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "decisions");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("SQLite");
        result.StdOut.Should().NotContain("review PR");
    }

    [Fact]
    public void Decisions_WithDays()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "decisions", "--days", "30");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("SQLite");
    }

    [Fact]
    public void Routes_ListsRoutes()
    {
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n\n### frontend\n- path: projects/frontend\n");

        var result = CliRunner.Run("--vault", _vault.VaultPath, "routes");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("backend");
        result.StdOut.Should().Contain("frontend");
    }

    [Fact]
    public void Routes_EmptyVault_ShowsNoRoutes()
    {
        var emptyVault = new TestVaultBuilder().WithStandardStructure();
        try
        {
            var result = CliRunner.Run("--vault", emptyVault.VaultPath, "routes");

            result.ExitCode.Should().Be(0);
            result.StdOut.Should().Contain("No routes");
        }
        finally
        {
            emptyVault.Dispose();
        }
    }

    [Fact]
    public void Routes_JsonOutput()
    {
        _vault.WithFile("_system/routes.md",
            "# Routes\n\n## Projects\n\n### backend\n- path: projects/backend\n");

        var result = CliRunner.Run("--json", "--vault", _vault.VaultPath, "routes");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"routes\":");
        result.StdOut.Should().Contain("\"name\":");
    }

    [Fact]
    public void EmptyVault_ReturnsEmptyResultsGracefully()
    {
        var emptyVault = new TestVaultBuilder().WithStandardStructure();
        try
        {
            var result = CliRunner.Run("--vault", emptyVault.VaultPath, "recent");
            result.ExitCode.Should().Be(0);

            result = CliRunner.Run("--vault", emptyVault.VaultPath, "tasks");
            result.ExitCode.Should().Be(0);

            result = CliRunner.Run("--vault", emptyVault.VaultPath, "blocks");
            result.ExitCode.Should().Be(0);

            result = CliRunner.Run("--vault", emptyVault.VaultPath, "decisions");
            result.ExitCode.Should().Be(0);
        }
        finally
        {
            emptyVault.Dispose();
        }
    }

    // Bead lmk-9hkn: the open build has no embedding model, so --semantic
    // always failed with "T1 embeddings model not found". The open build no
    // longer offers it; the closed build keeps it (CLI.Producers.Tests).
    [Fact]
    public void Search_semantic_flag_is_unknown_in_the_open_build()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "search", "--semantic", "caching");

        result.ExitCode.Should().Be(64);
        result.StdErr.Should().Contain("unknown flag '--semantic'");
    }

    [Fact]
    public void Search_help_does_not_mention_semantic_search_in_the_open_build()
    {
        var result = CliRunner.Run("help", "search");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().NotContainEquivalentOf("semantic");
        result.StdOut.Should().NotContain("embeddings");
    }

    [Fact]
    public void Search_still_accepts_its_open_flags()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "search", "cache", "--type", "idea", "--limit", "5");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("cache layer");
    }
}
