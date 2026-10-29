using System.Text.Json;
using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

public class HelpTests
{
    [Fact]
    public void HelpJson_OutputsValidJson()
    {
        var result = CliRunner.Run("--help-json");
        result.ExitCode.Should().Be(0);
        var action = () => JsonDocument.Parse(result.StdOut);
        action.Should().NotThrow();
    }

    [Fact]
    public void HelpJson_ListsAllCommands()
    {
        var result = CliRunner.Run("--help-json");
        result.ExitCode.Should().Be(0);
        var doc = JsonDocument.Parse(result.StdOut);
        var commands = doc.RootElement.GetProperty("commands");
        commands.GetArrayLength().Should().BeGreaterThan(5);
        var names = commands.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("page");
        names.Should().Contain("entry");
        names.Should().Contain("capture");
    }

    [Fact]
    public void HelpJson_GroupCommandsHaveSubcommands()
    {
        var result = CliRunner.Run("--help-json");
        var doc = JsonDocument.Parse(result.StdOut);
        var commands = doc.RootElement.GetProperty("commands");
        var pageCmd = commands.EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "page");
        pageCmd.TryGetProperty("subcommands", out var subs).Should().BeTrue();
        subs.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void HelpJson_IncludesVersion()
    {
        var result = CliRunner.Run("--help-json");
        var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HelpJson_CommandsHaveRequiredFields()
    {
        var result = CliRunner.Run("--help-json");
        var doc = JsonDocument.Parse(result.StdOut);
        var commands = doc.RootElement.GetProperty("commands");

        foreach (var cmd in commands.EnumerateArray())
        {
            cmd.TryGetProperty("name", out _).Should().BeTrue();
            cmd.TryGetProperty("description", out _).Should().BeTrue();
            cmd.TryGetProperty("usage", out _).Should().BeTrue();
            cmd.TryGetProperty("requires_vault", out _).Should().BeTrue();
        }
    }

    [Fact]
    public void HelpJson_CaptureHasExamples()
    {
        var result = CliRunner.Run("--help-json");
        var doc = JsonDocument.Parse(result.StdOut);
        var commands = doc.RootElement.GetProperty("commands");
        var captureCmd = commands.EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "capture");
        captureCmd.TryGetProperty("examples", out var examples).Should().BeTrue();
        examples.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void HelpCapture_ShowsExamples()
    {
        var result = CliRunner.Run("help", "capture");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpCapture_ShowsDetailedHelp()
    {
        var result = CliRunner.Run("help", "capture");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("--type");
        result.StdOut.Should().Contain("--file");
    }

    [Fact]
    public void HelpRecent_ShowsExamples()
    {
        var result = CliRunner.Run("help", "recent");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpSearch_ShowsExamples()
    {
        var result = CliRunner.Run("help", "search");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpTasks_ShowsExamples()
    {
        var result = CliRunner.Run("help", "tasks");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpBlocks_ShowsExamples()
    {
        var result = CliRunner.Run("help", "blocks");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpDecisions_ShowsExamples()
    {
        var result = CliRunner.Run("help", "decisions");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }

    [Fact]
    public void HelpRoutes_ShowsExamples()
    {
        var result = CliRunner.Run("help", "routes");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Examples:");
    }
}
