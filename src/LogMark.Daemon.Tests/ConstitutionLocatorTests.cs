using LogMark.Services.Hosting.Http;
using Xunit;

namespace LogMark.Daemon.Tests;

public class ConstitutionLocatorTests
{
    [Fact]
    public void PrefersVaultMdInsideLogmarkContentRoot()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-constitution").FullName;
        try
        {
            var content = Path.Combine(vault, "logmark");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "vault.md"), "# Constitution");
            Assert.Equal(Path.Combine(content, "vault.md"), ConstitutionLocator.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void PrefersVaultMdOverConstitutionMdWhenBothExist()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-constitution").FullName;
        try
        {
            var content = Path.Combine(vault, "logmark");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "vault.md"), "# Constitution");
            File.WriteAllText(Path.Combine(content, "constitution.md"), "# Legacy");
            Assert.Equal(Path.Combine(content, "vault.md"), ConstitutionLocator.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void FallsBackToLegacyConstitutionMd()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-constitution").FullName;
        try
        {
            File.WriteAllText(Path.Combine(vault, "constitution.md"), "rules");
            Assert.Equal(Path.Combine(vault, "constitution.md"), ConstitutionLocator.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void DefaultsToVaultMdWhenNeitherExists()
    {
        var vault = Directory.CreateTempSubdirectory("lmk-constitution").FullName;
        try
        {
            Assert.Equal(Path.Combine(vault, "vault.md"), ConstitutionLocator.Resolve(vault));
        }
        finally { Directory.Delete(vault, recursive: true); }
    }
}
