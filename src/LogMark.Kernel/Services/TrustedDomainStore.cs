using System.IO;
using System.Text.Json;

namespace LogMark.Services;

/// <summary>
/// Per-domain "always open" consent for external links. Backing file:
/// logmark/_system/trusted-domains.json (a plain string array). Read once at
/// construction; Trust() rewrites the file. Corrupt/missing file = empty set.
/// </summary>
public class TrustedDomainStore
{
    private readonly string _filePath;
    private readonly HashSet<string> _domains;

    public TrustedDomainStore(string vaultPath)
    {
        _filePath = Path.Combine(vaultPath, "logmark", "_system", "trusted-domains.json");
        _domains = Load();
    }

    public bool IsTrusted(string? domain) =>
        domain != null && _domains.Contains(domain);

    public void Trust(string domain)
    {
        if (!_domains.Add(domain)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_domains.OrderBy(d => d)));
        }
        catch { /* consent persists for this session only */ }
    }

    private HashSet<string> Load()
    {
        try
        {
            if (File.Exists(_filePath))
                return new HashSet<string>(
                    JsonSerializer.Deserialize<string[]>(File.ReadAllText(_filePath)) ?? [],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
