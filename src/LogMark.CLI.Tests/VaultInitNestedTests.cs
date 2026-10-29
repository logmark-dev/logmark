using FluentAssertions;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Bead lmk-zj5c: `vault init <outer>/nested` wrote a second vault inside an
/// existing one; the outer vault then walked nested/logmark as a route and
/// double-read its entries.
/// </summary>
public class VaultInitNestedTests : IDisposable
{
    private readonly string _outer = Path.Combine(Path.GetTempPath(), $"lm-nested-{Guid.NewGuid():N}");

    public VaultInitNestedTests()
    {
        CliRunner.Run("vault", "init", _outer, "--no-seed").ExitCode.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outer)) Directory.Delete(_outer, true);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("deeper/still")]
    [InlineData("logmark/projects/x")]
    public void Init_InsideAnExistingVault_Refuses(string relative)
    {
        var inner = Path.Combine(_outer, relative);

        var result = CliRunner.Run("vault", "init", inner, "--no-seed");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("inside an existing LogMark vault").And.Contain(_outer);
        Directory.Exists(Path.Combine(inner, "logmark")).Should().BeFalse();
        File.Exists(Path.Combine(inner, "vault.md")).Should().BeFalse();
    }

    [Fact]
    public void Init_InsideAnExistingVault_WithForce_Proceeds()
    {
        var inner = Path.Combine(_outer, "nested");

        var result = CliRunner.Run("vault", "init", inner, "--no-seed", "--force");

        result.ExitCode.Should().Be(0, result.StdErr);
        Directory.Exists(Path.Combine(inner, "logmark")).Should().BeTrue();
    }

    [Fact]
    public void Init_BesideAVault_IsNotNested()
    {
        var sibling = Path.Combine(Path.GetDirectoryName(_outer)!, Path.GetFileName(_outer) + "-sibling");
        try
        {
            CliRunner.Run("vault", "init", sibling, "--no-seed").ExitCode.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(sibling)) Directory.Delete(sibling, true);
        }
    }
}
