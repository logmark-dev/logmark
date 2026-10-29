using Xunit;

namespace LogMark.Daemon.Tests;

public class SelfManifestTests
{
    [Fact]
    public void Engine_manifest_carries_the_display_version_including_prerelease()
    {
        var manifest = Program.SelfManifest();
        Assert.Equal(LogMark.BuildInfo.DisplayVersion(typeof(Program).Assembly), manifest.BinVersion);
        Assert.Equal("oss-cli", manifest.ProviderId);
    }
}
