using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class CaptureStdinRuleTests : IDisposable
{
    private readonly TestVaultBuilder _vault = new TestVaultBuilder().WithStandardStructure();

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Inline_text_with_a_redirected_stdin_does_not_read_stdin()
    {
        var saved = CaptureCommand.IsInputRedirected;
        var savedIn = Console.In;
        try
        {
            // A reader that throws stands in for a pipe that never closes:
            // any read means capture would have blocked.
            Console.SetIn(new ThrowingReader());
            CaptureCommand.IsInputRedirected = () => true;
            var context = new CliContext(_vault.VaultPath, jsonOutput: false, quiet: true);

            new CaptureCommand().Execute(context, new[] { "t: probe from an agent" }).Should().Be(0);
        }
        finally
        {
            CaptureCommand.IsInputRedirected = saved;
            Console.SetIn(savedIn);
        }
    }

    [Fact]
    public void Dash_reads_the_piped_body_under_an_inline_title()
    {
        var result = CliRunner.RunWithStdin("one\ntwo\nthree\nfour", "--vault", _vault.VaultPath, "capture", "d: notes", "-");
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("with linked page");
    }

    private sealed class ThrowingReader : TextReader
    {
        public override string ReadToEnd() => throw new InvalidOperationException("stdin was read");
        public override int Read() => throw new InvalidOperationException("stdin was read");
    }
}
