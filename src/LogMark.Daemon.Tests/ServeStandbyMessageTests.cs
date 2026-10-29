using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// The standby line guessed "(LogMark Desktop?)" whenever the vault host lock
/// was held, though on an Engine-only machine the holder is another
/// `logmark serve` (lmk-oss-cosmetics-batch-0blt item 3). The lock does not
/// say who holds it, so the line must not guess.
/// </summary>
public class ServeStandbyMessageTests
{
    [Fact]
    public void Standby_line_does_not_guess_the_holder()
    {
        Assert.StartsWith("standby: ", Program.StandbyMessage);
        Assert.DoesNotContain("Desktop", Program.StandbyMessage);
        Assert.DoesNotContain("?", Program.StandbyMessage);
    }
}
