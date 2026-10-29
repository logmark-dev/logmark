using Xunit;

namespace LogMark.Daemon.Tests;

public class ServeOptionsTests
{
    [Fact]
    public void Defaults_PublishActiveVault()
    {
        var o = Program.ParseServeOptions(new[] { "serve", "--vault", @"C:\v" });
        Assert.NotNull(o);
        Assert.True(o!.PublishActiveVault);
        Assert.Equal(4747, o.Port);
    }

    [Fact]
    public void NoActiveVault_Flag_DisablesPublish()
    {
        var o = Program.ParseServeOptions(new[] { "serve", "--vault", @"C:\v", "--no-activevault", "--port", "4848" });
        Assert.NotNull(o);
        Assert.False(o!.PublishActiveVault);
        Assert.Equal(4848, o.Port);
    }

    [Fact]
    public void MissingVault_ReturnsNull()
    {
        Assert.Null(Program.ParseServeOptions(new[] { "serve", "--port", "4848" }));
    }
}
