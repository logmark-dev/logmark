using System.Text.RegularExpressions;
using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class UnknownFlagTests : IDisposable
{
    private readonly TestVaultBuilder _vault = new TestVaultBuilder().WithStandardStructure();

    public void Dispose() => _vault.Dispose();

    public static IEnumerable<object[]> WritingVerbs => new[]
    {
        new object[] { new[] { "capture" } }, new object[] { new[] { "vault", "init" } },
        new object[] { new[] { "routes-normalize" } }, new object[] { new[] { "page", "write" } },
        new object[] { new[] { "page", "link" } }, new object[] { new[] { "entry", "rate" } },
        new object[] { new[] { "entry", "flag" } }, new object[] { new[] { "entry", "mint-anchors" } },
        new object[] { new[] { "lint", "fix" } }, new object[] { new[] { "hooks", "install" } },
        new object[] { new[] { "hooks", "uninstall" } }, new object[] { new[] { "connect" } },
        new object[] { new[] { "disconnect" } }, new object[] { new[] { "link" } },
        new object[] { new[] { "agent-capture" } },
    };

    private static ICommand Leaf(string[] path)
    {
        var registry = Program.BuildRegistry();
        ICommand command = registry.Find(path[0])!;
        foreach (var token in path.Skip(1))
            command = ((IGroupCommand)command).Subcommands[token];
        return command;
    }

    [Theory]
    [MemberData(nameof(WritingVerbs))]
    public void Writing_verb_declares_exactly_the_flags_its_help_documents(string[] path)
    {
        var leaf = Leaf(path);
        leaf.KnownFlags.Should().NotBeNull($"{string.Join(" ", path)} writes, so it must reject unknown flags");
        // Global flags are stripped by GlobalArgParser before any command sees
        // its args, so help text may mention them without the command declaring them.
        var globals = new HashSet<string> { "--vault", "--json", "--quiet", "--help" };
        var documented = Regex.Matches(leaf.Usage + "\n" + (leaf.DetailedHelp ?? ""), @"--[a-z][a-z0-9-]*")
            .Select(m => m.Value).Where(f => !globals.Contains(f)).ToHashSet();
        leaf.KnownFlags!.Keys.Should().BeEquivalentTo(documented);
    }

    [Fact]
    public void Capture_with_an_unknown_flag_exits_64_and_writes_nothing()
    {
        var before = Directory.GetFiles(_vault.VaultPath, "*.md", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText);

        var result = CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: probe", "--dry-run");

        result.ExitCode.Should().Be(64);
        result.StdErr.Should().Contain("--dry-run");
        Directory.GetFiles(_vault.VaultPath, "*.md", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllText).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void Mint_anchors_with_a_typo_flag_exits_64()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "entry", "mint-anchors", "--aply").ExitCode.Should().Be(64);
    }

    [Fact]
    public void Value_of_a_value_flag_is_not_checked_as_a_flag()
    {
        CommandRunner.FindUnknownFlag(Leaf(new[] { "capture" }), new[] { "--source", "--odd-name", "hello" })
            .Should().BeNull();
    }

    [Fact]
    public void Equals_form_is_accepted()
    {
        CommandRunner.FindUnknownFlag(Leaf(new[] { "vault", "init" }), new[] { "init", "x", "--bundle=open" })
            .Should().BeNull();
    }

    [Fact]
    public void Bare_dash_stdin_marker_is_not_a_flag()
    {
        CommandRunner.FindUnknownFlag(Leaf(new[] { "capture" }), new[] { "-" }).Should().BeNull();
    }
}
