using System.Reflection;

namespace LogMark;

public static class BuildInfo
{
    /// <summary>Human-facing: informational version (keeps a prerelease suffix), build metadata stripped.</summary>
    public static string DisplayVersion(Assembly asm)
    {
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return StripMetadata(info ?? ManifestVersion(asm));
    }

    /// <summary>Machine-facing (provider manifests, vault stamps): always Major.Minor.Build so System.Version parses it.</summary>
    public static string ManifestVersion(Assembly asm) =>
        asm.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string StripMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
