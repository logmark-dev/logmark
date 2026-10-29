using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

public class McpProgramArgsTests
{
    [Fact]
    public void ParseVaultOverride_AcceptsVaultAlias()
    {
        Program.ParseVaultOverride(new[] { "--agent", "x", "--vault", @"C:\v" })
            .Should().Be(@"C:\v");
    }

    [Fact]
    public void ParseVaultOverride_LastWins()
    {
        Program.ParseVaultOverride(new[] { "--vault-override", @"C:\a", "--vault", @"C:\b" })
            .Should().Be(@"C:\b");
    }

    [Fact]
    public void ParseVaultOverride_AbsentIsNull()
    {
        Program.ParseVaultOverride(new[] { "--agent", "x" }).Should().BeNull();
    }
}
