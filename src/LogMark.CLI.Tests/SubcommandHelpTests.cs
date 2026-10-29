using System.Security.Cryptography;
using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Bead lmk-r2hs: --help after a verb or subverb was passed to the command as
/// an argument, so `entry mint-anchors --help` ran the scan, `lint fix --help`
/// wrote lint state, `vault init --help` initialized a vault, and
/// `capture --help` / `page write --help` failed on missing input. A help
/// flag must never act.
/// </summary>
[Collection("EnvironmentTests")]
public class SubcommandHelpTests : IDisposable
{
    private readonly TestVaultBuilder _vault = new TestVaultBuilder().WithStandardStructure();

    public void Dispose() => _vault.Dispose();

    private Dictionary<string, string> Snapshot(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
                f => Path.GetRelativePath(root, f),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
            : new Dictionary<string, string>();

    [Theory]
    [InlineData("entry", "mint-anchors", "--help")]
    [InlineData("entry", "mint-anchors", "-h")]
    [InlineData("lint", "fix", "--help")]
    [InlineData("lint", "scan", "--help")]
    [InlineData("page", "write", "--help")]
    [InlineData("capture", "--help")]
    [InlineData("capture", "t: some text", "--help")]
    [InlineData("recent", "--help")]
    public void Help_flag_prints_usage_and_leaves_the_vault_untouched(params string[] verbArgs)
    {
        var before = Snapshot(_vault.VaultPath);

        var result = CliRunner.Run(new[] { "--vault", _vault.VaultPath }.Concat(verbArgs).ToArray());

        result.ExitCode.Should().Be(0, result.StdErr);
        result.StdOut.Should().Contain("Usage:");
        Snapshot(_vault.VaultPath).Should().BeEquivalentTo(before, "a help flag must never write");
    }

    [Fact]
    public void Subcommand_help_shows_the_subcommand_not_the_group()
    {
        var result = CliRunner.Run("--vault", _vault.VaultPath, "page", "write", "--help");

        result.StdOut.Should().Contain("page write");
        result.StdOut.Should().NotContain("Subcommands:");
    }

    [Fact]
    public void Help_verb_resolves_a_subcommand()
    {
        var result = CliRunner.Run("help", "page", "write");

        result.ExitCode.Should().Be(0, result.StdErr);
        result.StdOut.Should().Contain("page write");
        result.StdOut.Should().NotContain("Subcommands:");
    }

    [Fact]
    public void Vault_init_help_creates_nothing()
    {
        var target = Path.Combine(Path.GetTempPath(), $"lm-help-init-{Guid.NewGuid():N}");
        try
        {
            var result = CliRunner.Run("vault", "init", target, "--help");

            result.ExitCode.Should().Be(0, result.StdErr);
            result.StdOut.Should().Contain("vault init");
            Directory.Exists(target).Should().BeFalse("help must not create the folder");
        }
        finally
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    [Fact]
    public void Usage_lines_name_logmark_on_the_open_binary()
    {
        var prior = ConnectCommand.OwnerId;
        ConnectCommand.OwnerId = "oss-cli";
        try
        {
            var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "--help");
            result.StdOut.Should().Contain("Usage: logmark capture");
            result.StdOut.Should().NotContain("Usage: lm ");
        }
        finally { ConnectCommand.OwnerId = prior; }
    }

    /// <summary>lmk-bdpz: a group run without a subcommand printed a hardcoded
    /// "Usage: lm ..." line on the open binary, whose command is logmark.</summary>
    [Theory]
    [InlineData("entry")]
    [InlineData("lint")]
    [InlineData("page")]
    [InlineData("vault")]
    public void Group_usage_names_logmark_on_the_open_binary(string group)
    {
        var prior = ConnectCommand.OwnerId;
        ConnectCommand.OwnerId = "oss-cli";
        try
        {
            var result = CliRunner.Run("--vault", _vault.VaultPath, group);
            (result.StdOut + result.StdErr).Should().Contain($"Usage: logmark {group}")
                .And.NotContain("Usage: lm ");
        }
        finally { ConnectCommand.OwnerId = prior; }
    }

    /// <summary>lmk-bdpz: hints that quote a command to run must name the
    /// binary the user has. Both quoting styles the CLI uses are covered.</summary>
    [Fact]
    public void No_hint_quotes_a_hardcoded_lm_command()
    {
        var cliDir = Path.Combine(RepoRoot(), "src", "LogMark.CLI");
        var offenders = Directory.GetFiles(cliDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => x.line.Contains("`lm ") || x.line.Contains("'lm "))
            .Where(x => !x.line.TrimStart().StartsWith("//"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("use CommandRunner.CliName so the open binary says logmark");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "LogMark.CLI")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>Final review Minor 1: the top-level help header names the
    /// product the same way --version does.</summary>
    [Theory]
    [InlineData("oss-cli", "LogMark Engine ")]
    [InlineData("desktop", "LogMark CLI v")]
    public void Help_header_names_the_product_like_version(string ownerId, string header)
    {
        var prior = ConnectCommand.OwnerId;
        ConnectCommand.OwnerId = ownerId;
        try
        {
            var help = CliRunner.Run("help");
            var version = CliRunner.Run("--version");
            help.StdOut.Should().StartWith(header);
            version.StdOut.Should().StartWith(header);
            if (ownerId == "oss-cli") help.StdOut.Should().NotContain("LogMark CLI v");
        }
        finally { ConnectCommand.OwnerId = prior; }
    }
}
