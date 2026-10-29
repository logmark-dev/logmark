using System.Text.Json;
using LogMark.Services;
using LogMark.Services.Agents;

namespace LogMark.CLI;

/// <summary>Resolved hook scope plus whether the workdir feature is in use.</summary>
public sealed record HookScopeContext(HookScopeResult Result, bool FeatureActive);

/// <summary>
/// Shared cwd-to-scope resolution for the agent-context / agent-capture hook
/// executors. Extracts the working directory from the hook stdin payload (the
/// field named by the framework's HookProfile.CwdField; Cursor sends an array,
/// so an array's first element is used) and resolves it against the vault's
/// route bindings. Never throws: any failure resolves to Unbound / inactive.
/// </summary>
public static class AgentHookScope
{
    public static HookScopeContext Resolve(string? vaultPath, string rawStdinJson, string cwdField)
    {
        var cwd = ExtractCwd(rawStdinJson, cwdField);

        if (string.IsNullOrWhiteSpace(vaultPath))
            return new HookScopeContext(new HookScopeResult(HookScopeKind.Unbound, null), false);

        try
        {
            var config = new RouteParser(vaultPath).Parse();
            var featureActive = config.VaultWorkdirs.Count > 0 || config.Routes.Any(r => r.Workdirs.Count > 0);
            var result = HookScopeResolver.Resolve(cwd, config.Routes, config.VaultWorkdirs);
            return new HookScopeContext(result, featureActive);
        }
        catch
        {
            return new HookScopeContext(new HookScopeResult(HookScopeKind.Unbound, null), false);
        }
    }

    private static string? ExtractCwd(string rawStdinJson, string cwdField)
    {
        if (string.IsNullOrWhiteSpace(rawStdinJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawStdinJson);
            if (!doc.RootElement.TryGetProperty(cwdField, out var el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Array => el.GetArrayLength() > 0 && el[0].ValueKind == JsonValueKind.String
                    ? el[0].GetString()
                    : null,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
