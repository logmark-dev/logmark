using LogMark.Services.Hosting;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Single content-root resolution rule shared by ConstitutionLocator and
/// ConceptPageStore (see VaultContentRoot.cs): `&lt;vault&gt;/logmark` if that
/// directory exists, else the vault root itself.
/// </summary>
public class VaultContentRootTests
{
    [Fact]
    public void WrappedVault_ResolvesToLogmarkSubdir()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-vcr").FullName;
        try
        {
            var content = Path.Combine(vault, "logmark");
            Directory.CreateDirectory(content);
            Assert.Equal(content, VaultContentRoot.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void FlatVault_ResolvesToVaultRoot()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-vcr").FullName;
        try
        {
            Assert.Equal(vault, VaultContentRoot.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void LogmarkAsFileNotDirectory_ResolvesToVaultRoot()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-vcr").FullName;
        try
        {
            File.WriteAllText(Path.Combine(vault, "logmark"), "not a directory");
            Assert.Equal(vault, VaultContentRoot.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }
}
