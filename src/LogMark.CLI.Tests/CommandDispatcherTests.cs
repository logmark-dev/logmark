using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public void NoArgs_ShowsHelp_ExitZero()
    {
        var result = CliRunner.Run();

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Usage:");
        result.StdOut.Should().Contain("lm");
    }

    [Fact]
    public void HelpFlag_ShowsHelp_ExitZero()
    {
        var result = CliRunner.Run("--help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Usage:");
        result.StdOut.Should().Contain("Commands:");
    }

    [Fact]
    public void HelpCommand_ShowsHelp_ExitZero()
    {
        var result = CliRunner.Run("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Usage:");
    }

    [Fact]
    public void HelpCapture_ShowsCommandHelp()
    {
        var result = CliRunner.Run("help", "capture");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("capture");
        result.StdOut.Should().Contain("Usage:");
    }

    [Fact]
    public void HelpVault_ShowsSubcommands()
    {
        var result = CliRunner.Run("help", "vault");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Subcommands:");
        result.StdOut.Should().Contain("init");
        result.StdOut.Should().Contain("templates");
    }

    [Fact]
    public void UnknownCommand_SuggestsSimilar_ExitOne()
    {
        var result = CliRunner.Run("captur");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown command: captur");
        result.StdErr.Should().Contain("Did you mean 'capture'?");
    }

    [Fact]
    public void UnknownCommand_NoSuggestion_ExitOne()
    {
        var result = CliRunner.Run("zzzzzzz");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("Unknown command: zzzzzzz");
        result.StdErr.Should().NotContain("Did you mean");
    }

    [Fact]
    public void VersionFlag_ShowsVersion()
    {
        var result = CliRunner.Run("--version");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("LogMark CLI v");
    }

    [Fact]
    public void VersionCommand_ShowsVersion()
    {
        var result = CliRunner.Run("version");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("LogMark CLI v");
    }

    [Fact]
    public void GlobalFlags_ParsedCorrectly()
    {
        // --json flag should be parsed before command name
        // This should fail because vault doesn't exist, but the flag should parse
        var result = CliRunner.Run("--vault", "/nonexistent/path", "recent");

        // Should fail because path doesn't exist
        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("does not exist");
    }

    [Fact]
    public void HelpFlagWithCommand_ShowsCommandHelp()
    {
        var result = CliRunner.Run("--help", "recent");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("recent");
    }

    [Fact]
    public void AllCommands_ListedInHelp()
    {
        var result = CliRunner.Run("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("capture");
        result.StdOut.Should().Contain("recent");
        result.StdOut.Should().Contain("search");
        result.StdOut.Should().Contain("tasks");
        result.StdOut.Should().Contain("blocks");
        result.StdOut.Should().Contain("decisions");
        result.StdOut.Should().Contain("routes");
        result.StdOut.Should().Contain("vault");
        result.StdOut.Should().Contain("version");
    }

    [Fact]
    public void GlobalFlags_AfterCommand_AreExtracted()
    {
        var globals = GlobalArgParser.Parse(new[] { "routes", "--vault", @"C:\v", "--json" });

        globals.CommandName.Should().Be("routes");
        globals.VaultPath.Should().Be(@"C:\v");
        globals.JsonOutput.Should().BeTrue();
        globals.RemainingArgs.Should().BeEmpty("global flags must not leak into command args");
    }

    [Fact]
    public void GlobalFlags_BeforeAndAfterCommand_LastVaultWins()
    {
        var globals = GlobalArgParser.Parse(new[] { "--vault", @"C:\a", "routes", "--vault", @"C:\b" });

        globals.CommandName.Should().Be("routes");
        globals.VaultPath.Should().Be(@"C:\b");
        globals.RemainingArgs.Should().BeEmpty();
    }

    [Fact]
    public void CommandSpecificFlags_AfterCommand_StayInRemaining()
    {
        var globals = GlobalArgParser.Parse(new[] { "recent", "--vault", @"C:\v", "--days", "3" });

        globals.RemainingArgs.Should().Equal("--days", "3");
    }
}
