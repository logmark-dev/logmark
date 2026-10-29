using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Writes GitHub Copilot CLI's flat-shell hook schema (.github/hooks/logmark.json
/// or ~/.copilot/hooks/logmark.json):
///   { "version": 1, "hooks": { "sessionEnd": [ { "type":"command", "bash":"...", "powershell":"..." } ] } }
/// The command goes into both bash and powershell so the hook fires on either
/// shell.
/// </summary>
public sealed class CopilotJsonHookWriter : IHookWriter
{
    public WiringResult Write(string hookPath, HookProfile profile, IReadOnlyList<HookEntry> entries)
    {
        JsonObject root;
        try { root = HookConfigJson.ReadOrCreateRoot(hookPath); }
        catch (JsonException ex)
        {
            return new WiringResult(false, $"Existing hook file is not valid JSON; refusing to modify it: {ex.Message}");
        }

        root["version"] = 1;
        foreach (var entry in entries)
            HookConfigJson.UpsertItem(root, entry.Event, new JsonObject
            {
                ["type"] = "command",
                ["bash"] = entry.Command,
                ["powershell"] = entry.Command,
            });

        return HookConfigJson.WriteValidated(hookPath, root);
    }

    public WiringResult Remove(string hookPath) => HookConfigJson.RemoveMarked(hookPath);

    public bool IsInstalled(string hookPath) => HookConfigJson.FileContainsMarker(hookPath);
}
