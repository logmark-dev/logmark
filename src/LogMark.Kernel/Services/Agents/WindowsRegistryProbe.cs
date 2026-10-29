using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LogMark.Services.Agents;

[SupportedOSPlatform("windows")]
public class WindowsRegistryProbe : IRegistryProbe
{
    public IEnumerable<string> EnumerateSubKeys(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}
