using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogMark.Services.Hosting.Cockpit;

/// <summary>
/// Cockpit settings (daemon HTTP API v0 /settings). Vault-scoped at
/// logmark/_system/cockpit-settings.json so the configuration survives host
/// migration between daemon and app. Shape mirrors KnowledgeSettings in
/// LogMark.Knowledge/src/types/settings.ts.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string[] Providers = ["claude-cli", "ollama", "lm-studio"];
    private static readonly string[] Modes = ["default", "disabled"];

    private readonly string _path;

    public SettingsStore(string vaultPath)
    {
        _path = Path.Combine(vaultPath, "logmark", "_system", "cockpit-settings.json");
    }

    public CockpitSettings Read()
    {
        try
        {
            var raw = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<CockpitSettings>(raw);
            if (parsed is not null && IsValid(parsed)) return parsed;
        }
        catch
        {
            // Missing or corrupt file falls back to defaults.
        }
        return CockpitSettings.Defaults();
    }

    public void Write(CockpitSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsValid(CockpitSettings? s)
    {
        return s is not null
            && s.ProviderOrder is { Count: > 0 }
            && s.ProviderOrder.All(p => Providers.Contains(p))
            && s.Routing is not null
            && Modes.Contains(s.Routing.Summarization)
            && Modes.Contains(s.Routing.Distillation)
            && Modes.Contains(s.Routing.SearchEnrichment);
    }
}

public sealed class CockpitSettings
{
    [JsonPropertyName("providerOrder")]
    public List<string> ProviderOrder { get; set; } = new();

    [JsonPropertyName("routing")]
    public CockpitRouting Routing { get; set; } = new();

    public static CockpitSettings Defaults() => new()
    {
        ProviderOrder = ["claude-cli", "ollama", "lm-studio"],
        Routing = new CockpitRouting
        {
            Summarization = "default",
            Distillation = "default",
            SearchEnrichment = "disabled",
        },
    };
}

public sealed class CockpitRouting
{
    [JsonPropertyName("summarization")]
    public string Summarization { get; set; } = "default";

    [JsonPropertyName("distillation")]
    public string Distillation { get; set; } = "default";

    [JsonPropertyName("searchEnrichment")]
    public string SearchEnrichment { get; set; } = "disabled";
}
